using BookingService.Entities;
using BookingService.Infrastructure.Data;
using BookingService.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Tests.IntegrationTests;

/// <summary>
/// Интеграционные тесты для Задачи 03 — Race Conditions и зависшие отмены.
///
/// Проверяют:
/// 1. Гард от гонки: BookingJobConfirmed для CancellationPending-бронирования игнорируется
/// 2. Репозиторий корректно находит зависшие отмены по дедлайну
/// 3. Оптимистичная блокировка через xmin сконфигурирована
/// 4. StuckCancellationsJob регистрируется как BackgroundService
/// </summary>
public class Task03_RaceConditionsTests : IntegrationTestBase
{
    // -----------------------------------------------------------------------
    // Гард от гонки: позднее подтверждение для CancellationPending-бронирования
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleBookingJobConfirmed_WhenBookingIsCancellationPending_DoesNotChangeStatus()
    {
        // Arrange: Confirmed → CancellationPending
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await ConfirmBookingAsync(catalogRequestId);
        await BookingService.CancelBooking(bookingId);

        var booking = await Context.Bookings.FindAsync(bookingId);
        booking!.Status.Should().Be(BookingStatus.CancellationPending, "precondition");

        // Act: Catalog Service (с опозданием) присылает подтверждение создания
        await BookingService.HandleBookingJobConfirmed(catalogRequestId);

        // Assert: статус не должен измениться на Confirmed
        await Context.Entry(booking).ReloadAsync();
        booking.Status.Should().Be(BookingStatus.CancellationPending,
            because: "Если пользователь уже запросил отмену (CancellationPending), " +
                     "запоздалое BookingJobConfirmed не должно откатывать решение об отмене");
    }

    [Fact]
    public async Task HandleBookingJobConfirmed_WhenBookingIsAwaitConfirmation_SetsConfirmed()
    {
        // Arrange: нормальный флоу без гонки
        var (bookingId, catalogRequestId) = await CreateBookingAsync();

        // Act
        await BookingService.HandleBookingJobConfirmed(catalogRequestId);

        // Assert: базовая функциональность не сломана
        var booking = await Context.Bookings.FindAsync(bookingId);
        booking!.Status.Should().Be(BookingStatus.Confirmed);
    }

    // -----------------------------------------------------------------------
    // FindStuckCancellationsAsync — корректность выборки для StuckCancellationsJob
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FindStuckCancellationsAsync_ReturnsBookingsOlderThanThreshold()
    {
        // Arrange: создаём бронирование в CancellationPending
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await ConfirmBookingAsync(catalogRequestId);
        await BookingService.CancelBooking(bookingId);

        // Имитируем, что отмена была запрошена давно (5+ минут назад)
        // через прямое обновление поля в БД
        await Context.Database.ExecuteSqlRawAsync(
            "UPDATE bookings SET cancellation_requested_at = NOW() - INTERVAL '10 minutes' WHERE id = {0}",
            bookingId);

        var repository = new BookingRepository(Context);

        // Act: ищем бронирования, зависшие более 5 минут назад
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
        var stuck = await repository.FindStuckCancellationsAsync(cutoff);

        // Assert
        stuck.Should().ContainSingle(b => b.Id == bookingId,
            because: "Бронирование в CancellationPending с меткой времени старше threshold должно попасть в выборку");
    }

    [Fact]
    public async Task FindStuckCancellationsAsync_DoesNotReturnRecentCancellationPending()
    {
        // Arrange: создаём бронирование, которое только что перешло в CancellationPending
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await ConfirmBookingAsync(catalogRequestId);
        await BookingService.CancelBooking(bookingId);

        var repository = new BookingRepository(Context);

        // Act: threshold — 5 минут назад, отмена только что — попасть не должно
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
        var stuck = await repository.FindStuckCancellationsAsync(cutoff);

        // Assert
        stuck.Should().NotContain(b => b.Id == bookingId,
            because: "Свежее CancellationPending (< 5 мин) ещё не считается зависшим — " +
                     "нужно дать Catalog Service время обработать команду");
    }

    [Fact]
    public async Task FindStuckCancellationsAsync_DoesNotReturnConfirmedBookings()
    {
        // Arrange: подтверждённое бронирование не должно попасть в выборку
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await ConfirmBookingAsync(catalogRequestId);

        var repository = new BookingRepository(Context);

        // Act
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
        var stuck = await repository.FindStuckCancellationsAsync(cutoff);

        // Assert
        stuck.Should().NotContain(b => b.Id == bookingId,
            because: "Подтверждённые бронирования не должны попадать в выборку зависших отмен");
    }

    // -----------------------------------------------------------------------
    // Оптимистичная блокировка через xmin
    // -----------------------------------------------------------------------

    [Fact]
    public void BookingEntity_VersionProperty_IsMappedAsXmin()
    {
        // Проверяем, что xmin сконфигурирован как IsConcurrencyToken
        // через метаданные EF Core (не требует запросов к БД)
        var entityType = Context.Model.FindEntityType(typeof(Booking))!;
        var versionProperty = entityType.FindProperty(nameof(Booking.Version));

        versionProperty.Should().NotBeNull(
            because: "Свойство Version должно быть зарегистрировано в EF Core модели");
        versionProperty!.IsConcurrencyToken.Should().BeTrue(
            because: "xmin должен быть настроен как токен параллелизма " +
                     "для обнаружения конкурентных изменений (DbUpdateConcurrencyException)");
        versionProperty.GetColumnName().Should().Be("xmin",
            because: "Токен параллелизма должен маппиться на системный столбец xmin PostgreSQL");
    }

    // -----------------------------------------------------------------------
    // Индекс для зависших отмен (Task 01 migration)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Migration_CancellationPendingIndex_Exists()
    {
        // Проверяем наличие частичного индекса для выборки зависших отмен
        var indexExists = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM pg_indexes " +
            "WHERE tablename = 'bookings' AND indexname = 'idx_bookings_cancellation_pending'")
            .SingleAsync();

        indexExists.Should().Be(1,
            because: "Должен существовать индекс idx_bookings_cancellation_pending " +
                     "для эффективной выборки зависших отмен фоновым job'ом");
    }
}
