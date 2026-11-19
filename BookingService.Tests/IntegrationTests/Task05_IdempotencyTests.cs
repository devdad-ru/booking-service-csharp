using BookingService.Configuration;
using BookingService.Entities;
using BookingService.Infrastructure.Data;
using BookingService.Infrastructure.Messaging;
using BookingService.Infrastructure.Messaging.Contracts;
using BookingService.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingService.Tests.IntegrationTests;

/// <summary>
/// Интеграционные тесты для Задачи 05 — Идемпотентность обработки событий.
///
/// Проверяют:
/// 1. Миграция создаёт таблицу processed_events для хранения обработанных event_id
/// 2. Повторная доставка BookingJobConfirmed не изменяет статус бронирования
/// 3. Повторная доставка BookingJobDenied не бросает исключение
/// 4. Rebus-хендлер сохраняет EventId сообщения в processed_events и пропускает уже обработанные EventId
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
            "SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables " +
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
                "SELECT COUNT(*)::int AS \"Value\" FROM information_schema.columns " +
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
    // Запись в processed_events: ключ — EventId входящего сообщения
    // -----------------------------------------------------------------------

    /// <summary>
    /// Создаёт Rebus-хендлер через DI, чтобы тест не зависел от набора зависимостей
    /// в его конструкторе: можно внедрить сервис, репозиторий, DbContext или publisher.
    /// </summary>
    private BookingEventsHandler CreateHandler()
    {
        var services = new ServiceCollection()
            .AddSingleton(Context)
            .AddSingleton(new BookingRepository(Context))
            .AddSingleton(BookingService)
            .AddSingleton(new BookingEventPublisher(BusMock, NullLogger<BookingEventPublisher>.Instance, Context))
            .AddSingleton(BusMock)
            .AddSingleton<ICurrentDateTimeProvider, CurrentDateTimeProvider>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .BuildServiceProvider();

        return ActivatorUtilities.CreateInstance<BookingEventsHandler>(services);
    }

    private async Task<int> CountProcessedEventsAsync(Guid eventId)
        => await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int AS \"Value\" FROM processed_events WHERE event_id = {0}",
            eventId)
            .SingleAsync();

    [Fact]
    public async Task ProcessedEvents_AfterHandlerProcessesMessage_ContainsMessageEventId()
    {
        // Arrange
        var (_, catalogRequestId) = await CreateBookingAsync();
        var message = new BookingJobConfirmed { EventId = Guid.NewGuid(), RequestId = catalogRequestId };

        // Act
        await CreateHandler().Handle(message);

        // Assert: записан именно EventId сообщения, а не RequestId заявки
        (await CountProcessedEventsAsync(message.EventId)).Should().Be(1,
            because: "Для идемпотентности в processed_events сохраняется EventId входящего сообщения");
    }

    [Fact]
    public async Task ProcessedEvents_WhenSameMessageDeliveredTwice_ContainsOnlyOneRecord()
    {
        // Arrange
        var (_, catalogRequestId) = await CreateBookingAsync();
        var message = new BookingJobConfirmed { EventId = Guid.NewGuid(), RequestId = catalogRequestId };
        var handler = CreateHandler();

        // Act: первичная доставка + повторная доставка того же сообщения (at-least-once)
        await handler.Handle(message);
        var act = async () => await handler.Handle(message);

        // Assert
        await act.Should().NotThrowAsync(
            because: "Повторная доставка сообщения должна тихо пропускаться");
        (await CountProcessedEventsAsync(message.EventId)).Should().Be(1,
            because: "Таблица processed_events должна содержать ровно одну запись для события, " +
                     "даже если оно было получено дважды");
    }

    [Fact]
    public async Task Handler_WhenEventIdAlreadyProcessed_SkipsMessage()
    {
        // Arrange: событие уже отмечено как обработанное (например, другим инстансом сервиса)
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        var message = new BookingJobConfirmed { EventId = Guid.NewGuid(), RequestId = catalogRequestId };
        await Context.Database.ExecuteSqlRawAsync(
            "INSERT INTO processed_events (event_id, processed_at) VALUES ({0}, {1})",
            message.EventId, DateTimeOffset.UtcNow);

        // Act
        await CreateHandler().Handle(message);

        // Assert: сообщение с уже обработанным EventId не должно менять бронирование
        var booking = await Context.Bookings.FindAsync(bookingId);
        booking!.Status.Should().Be(BookingStatus.AwaitConfirmation,
            because: "Хендлер должен пропускать сообщение, EventId которого уже есть в processed_events");
    }
}
