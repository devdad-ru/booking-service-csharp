using BookingService.Entities;
using BookingService.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Tests.IntegrationTests;

/// <summary>
/// Интеграционные тесты для Задачи 05 — Идемпотентность обработки событий.
///
/// Проверяют:
/// 1. Миграция создаёт таблицу processed_events для хранения обработанных event_id
/// 2. Повторная доставка BookingJobConfirmed не изменяет статус бронирования
/// 3. Повторная доставка BookingJobDenied не бросает исключение
/// 4. После первичной обработки идентификатор события сохраняется в processed_events
///
/// Идемпотентность критична в распределённых системах: RabbitMQ гарантирует
/// доставку at-least-once, поэтому одно сообщение может прийти дважды.
/// </summary>
public class Task05_IdempotencyTests : IntegrationTestBase
{
    // -----------------------------------------------------------------------
    // Схема: таблица processed_events
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Migration_ProcessedEventsTable_Exists()
    {
        var tableExists = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM information_schema.tables " +
            "WHERE table_schema = 'public' AND table_name = 'processed_events'")
            .SingleAsync();

        tableExists.Should().Be(1,
            because: "Миграция задачи 05 должна создать таблицу processed_events");
    }

    [Fact]
    public async Task Migration_ProcessedEvents_HasRequiredColumns()
    {
        var requiredColumns = new[] { "event_id", "processed_at" };

        foreach (var column in requiredColumns)
        {
            var exists = await Context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*)::int FROM information_schema.columns " +
                "WHERE table_name = 'processed_events' AND column_name = {0}",
                column)
                .SingleAsync();

            exists.Should().Be(1,
                because: $"Таблица processed_events должна содержать колонку '{column}'");
        }
    }

    // -----------------------------------------------------------------------
    // Идемпотентность HandleBookingJobConfirmed
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleBookingJobConfirmed_CalledTwiceWithSameRequestId_DoesNotThrow()
    {
        // Arrange
        var (_, catalogRequestId) = await CreateBookingAsync();

        // Первый вызов — нормальная обработка
        await BookingService.HandleBookingJobConfirmed(catalogRequestId);

        // Act: повторная доставка (at-least-once) — не должна бросать исключение
        var act = async () => await BookingService.HandleBookingJobConfirmed(catalogRequestId);

        // Assert
        await act.Should().NotThrowAsync(
            because: "Повторная доставка BookingJobConfirmed должна быть проигнорирована " +
                     "без исключений — идемпотентность обязательна при at-least-once доставке");
    }

    [Fact]
    public async Task HandleBookingJobConfirmed_CalledTwiceWithSameRequestId_BookingRemainsConfirmed()
    {
        // Arrange
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await BookingService.HandleBookingJobConfirmed(catalogRequestId);

        // Act: дубликат события
        await BookingService.HandleBookingJobConfirmed(catalogRequestId);

        // Assert: статус должен остаться Confirmed, а не откатиться
        var booking = await Context.Bookings.FindAsync(bookingId);
        booking!.Status.Should().Be(BookingStatus.Confirmed,
            because: "Повторная обработка не должна изменять статус уже подтверждённого бронирования");
    }

    // -----------------------------------------------------------------------
    // Идемпотентность HandleBookingJobDenied
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleBookingJobDenied_CalledTwiceWithSameRequestId_DoesNotThrow()
    {
        // Arrange
        var (_, catalogRequestId) = await CreateBookingAsync();

        // Первый вызов — нормальная обработка
        await BookingService.HandleBookingJobDenied(catalogRequestId);

        // Act: дубликат события
        var act = async () => await BookingService.HandleBookingJobDenied(catalogRequestId);

        // Assert
        await act.Should().NotThrowAsync(
            because: "Повторная доставка BookingJobDenied должна быть проигнорирована без исключений");
    }

    [Fact]
    public async Task HandleBookingJobDenied_CalledTwiceWithSameRequestId_BookingRemainsCancelled()
    {
        // Arrange
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await BookingService.HandleBookingJobDenied(catalogRequestId);

        // Act: дубликат события
        await BookingService.HandleBookingJobDenied(catalogRequestId);

        // Assert
        var booking = await Context.Bookings.FindAsync(bookingId);
        booking!.Status.Should().Be(BookingStatus.Cancelled,
            because: "Повторная обработка не должна изменять статус уже отменённого бронирования");
    }

    // -----------------------------------------------------------------------
    // Запись в processed_events
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessedEvents_AfterHandleBookingJobConfirmed_ContainsEventId()
    {
        // Arrange
        var (_, catalogRequestId) = await CreateBookingAsync();

        // Act
        await BookingService.HandleBookingJobConfirmed(catalogRequestId);

        // Assert: идентификатор события должен быть записан
        var recordedEventCount = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM processed_events WHERE event_id = {0}",
            catalogRequestId)
            .SingleAsync();

        recordedEventCount.Should().BeGreaterThanOrEqualTo(1,
            because: "Обработанное событие должно быть сохранено в processed_events " +
                     "для последующей проверки дубликатов");
    }

    [Fact]
    public async Task ProcessedEvents_AfterHandleBookingJobConfirmed_CalledTwice_ContainsOnlyOneRecord()
    {
        // Arrange
        var (_, catalogRequestId) = await CreateBookingAsync();

        // Act: первичная обработка + дубликат
        await BookingService.HandleBookingJobConfirmed(catalogRequestId);
        await BookingService.HandleBookingJobConfirmed(catalogRequestId);

        // Assert: не должно быть дублирующих записей
        var recordedEventCount = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM processed_events WHERE event_id = {0}",
            catalogRequestId)
            .SingleAsync();

        recordedEventCount.Should().Be(1,
            because: "Таблица processed_events должна содержать ровно одну запись для события, " +
                     "даже если оно было получено дважды");
    }
}
