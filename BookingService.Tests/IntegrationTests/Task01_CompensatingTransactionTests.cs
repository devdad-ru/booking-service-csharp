using BookingService.Entities;
using BookingService.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Tests.IntegrationTests;

/// <summary>
/// Интеграционные тесты для Задачи 01 — Compensating Transaction.
///
/// Проверяют:
/// 1. Отмена подтверждённого бронирования переходит в CancellationPending (не Cancelled)
/// 2. Поле CancellationRequestedAt устанавливается
/// 3. HandleCancellationError откатывает статус в Confirmed
/// 4. Обработка граничных случаев (бронирование не найдено, статус не CancellationPending)
/// </summary>
public class Task01_CompensatingTransactionTests : IntegrationTestBase
{
    [Fact]
    public async Task CancelBooking_WhenBookingIsConfirmed_StatusMustBeCancellationPending()
    {
        // Arrange: создаём бронирование и подтверждаем его через Catalog Service
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await ConfirmBookingAsync(catalogRequestId);

        // Act
        await BookingService.CancelBooking(bookingId);

        // Assert
        var booking = await Context.Bookings.FindAsync(bookingId);
        booking!.Status.Should().Be(BookingStatus.CancellationPending,
            because: "Confirmed-бронирование при отмене должно переходить в CancellationPending, " +
                     "а не сразу в Cancelled — это обеспечивает возможность отката при ошибке Catalog Service");
    }

    [Fact]
    public async Task CancelBooking_WhenBookingIsConfirmed_SetsNonNullCancellationRequestedAt()
    {
        // Arrange
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await ConfirmBookingAsync(catalogRequestId);

        // Act
        await BookingService.CancelBooking(bookingId);

        // Assert
        var booking = await Context.Bookings.FindAsync(bookingId);
        booking!.CancellationRequestedAt.Should().NotBeNull(
            because: "Метка времени необходима для поиска зависших отмен фоновым job'ом (Задача 03)");
    }

    [Fact]
    public async Task CancelBooking_WhenBookingIsAwaitConfirmation_StatusMustBeCancelledDirectly()
    {
        // Arrange: бронирование НЕ подтверждено — резервации в Catalog нет
        var (bookingId, _) = await CreateBookingAsync();

        // Act
        await BookingService.CancelBooking(bookingId);

        // Assert: для AwaitConfirmation отмена по-прежнему немедленная
        var booking = await Context.Bookings.FindAsync(bookingId);
        booking!.Status.Should().Be(BookingStatus.Cancelled,
            because: "Бронирование в AwaitConfirmation не имеет резервации в Catalog, " +
                     "поэтому отмена может быть немедленной — промежуточный статус не нужен");
    }

    [Fact]
    public async Task HandleCancellationError_WhenBookingIsCancellationPending_RollsBackToConfirmed()
    {
        // Arrange: доводим до статуса CancellationPending
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await ConfirmBookingAsync(catalogRequestId);
        await BookingService.CancelBooking(bookingId);

        var booking = await Context.Bookings.FindAsync(bookingId);
        booking!.Status.Should().Be(BookingStatus.CancellationPending, "precondition");

        // Act: симулируем ошибку DLQ от Catalog Service
        await BookingService.HandleCancellationError(catalogRequestId);

        // Assert
        await Context.Entry(booking).ReloadAsync();
        booking.Status.Should().Be(BookingStatus.Confirmed,
            because: "DLQ-ошибка означает, что Catalog Service не обработал команду отмены — " +
                     "компенсирующая транзакция должна вернуть статус в Confirmed");
        booking.CancellationRequestedAt.Should().BeNull(
            because: "После отката метка времени должна очищаться");
    }

    [Fact]
    public async Task HandleCancellationError_WhenBookingNotFound_DoesNotThrow()
    {
        // Arrange: несуществующий requestId
        var unknownRequestId = Guid.NewGuid();

        // Act & Assert: не должно бросать исключений
        var act = async () => await BookingService.HandleCancellationError(unknownRequestId);
        await act.Should().NotThrowAsync(
            because: "Если бронирование не найдено, DLQ-событие должно быть проигнорировано без ошибки");
    }

    [Fact]
    public async Task HandleCancellationError_WhenBookingIsAlreadyConfirmed_DoesNotChangeStatus()
    {
        // Arrange: бронирование уже подтверждено (ошибка пришла поздно / дублирована)
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await ConfirmBookingAsync(catalogRequestId);

        // Act
        await BookingService.HandleCancellationError(catalogRequestId);

        // Assert: статус не должен измениться
        var booking = await Context.Bookings.FindAsync(bookingId);
        booking!.Status.Should().Be(BookingStatus.Confirmed,
            because: "Дублированное или запоздалое DLQ-событие не должно ломать уже подтверждённое бронирование");
    }

    [Fact]
    public async Task Migration_CancellationRequestedAt_ColumnExists()
    {
        // Проверяем, что колонка добавлена миграцией
        var columnExists = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM information_schema.columns " +
            "WHERE table_name = 'bookings' AND column_name = 'cancellation_requested_at'")
            .SingleAsync();

        columnExists.Should().Be(1,
            because: "Миграция AddCancellationRequestedAt должна добавить колонку cancellation_requested_at");
    }
}
