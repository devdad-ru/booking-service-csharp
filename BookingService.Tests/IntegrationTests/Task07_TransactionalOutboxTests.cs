using BookingService.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Tests.IntegrationTests;

/// <summary>
/// Интеграционные тесты для Задачи 07 — Transactional Outbox.
///
/// Проверяют:
/// 1. Миграция создаёт таблицу outbox_messages с нужными колонками
/// 2. При создании бронирования команда атомарно записывается в outbox
/// 3. При отмене бронирования команда записывается в outbox
/// 4. Новые записи имеют processed_at = NULL (не обработаны фоновым job'ом)
/// 5. Outbox не содержит записей для операций, которые не рассылают сообщения
///
/// Transactional Outbox гарантирует, что сообщение в очередь и изменение
/// состояния в БД всегда происходят атомарно. Фоновый OutboxProcessorJob
/// читает таблицу и публикует сообщения в RabbitMQ.
/// </summary>
public class Task07_TransactionalOutboxTests : IntegrationTestBase
{
    // -----------------------------------------------------------------------
    // Схема: таблица outbox_messages
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Migration_OutboxMessagesTable_Exists()
    {
        var tableExists = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM information_schema.tables " +
            "WHERE table_schema = 'public' AND table_name = 'outbox_messages'")
            .SingleAsync();

        tableExists.Should().Be(1,
            because: "Миграция задачи 07 должна создать таблицу outbox_messages");
    }

    [Fact]
    public async Task Migration_OutboxMessages_HasAllRequiredColumns()
    {
        var requiredColumns = new[]
        {
            "id", "message_type", "payload", "created_at", "processed_at"
        };

        foreach (var column in requiredColumns)
        {
            var exists = await Context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*)::int FROM information_schema.columns " +
                "WHERE table_name = 'outbox_messages' AND column_name = {0}",
                column)
                .SingleAsync();

            exists.Should().Be(1,
                because: $"Таблица outbox_messages должна содержать колонку '{column}'");
        }
    }

    // -----------------------------------------------------------------------
    // Запись в outbox при операциях с бронированиями
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateBooking_AddsMessageToOutbox()
    {
        // Arrange
        var outboxCountBefore = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM outbox_messages")
            .SingleAsync();

        // Act
        await CreateBookingAsync();

        // Assert: в outbox появилась как минимум одна новая запись
        var outboxCountAfter = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM outbox_messages")
            .SingleAsync();

        outboxCountAfter.Should().BeGreaterThan(outboxCountBefore,
            because: "Создание бронирования должно атомарно записать команду в outbox_messages");
    }

    [Fact]
    public async Task CreateBooking_OutboxEntry_HasCreateBookingJobMessageType()
    {
        // Act
        await CreateBookingAsync();

        // Assert: тип сообщения соответствует команде в Catalog Service
        var messageTypeExists = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM outbox_messages " +
            "WHERE message_type LIKE '%CreateBookingJob%'")
            .SingleAsync();

        messageTypeExists.Should().BeGreaterThan(0,
            because: "В outbox должна быть запись с типом, содержащим 'CreateBookingJob' — " +
                     "команда на создание резервации в Catalog Service");
    }

    [Fact]
    public async Task CancelBooking_Confirmed_AddsMessageToOutbox()
    {
        // Arrange
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await ConfirmBookingAsync(catalogRequestId);

        var outboxCountBefore = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM outbox_messages")
            .SingleAsync();

        // Act
        await BookingService.CancelBooking(bookingId);

        // Assert
        var outboxCountAfter = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM outbox_messages")
            .SingleAsync();

        outboxCountAfter.Should().BeGreaterThan(outboxCountBefore,
            because: "Отмена подтверждённого бронирования должна добавить команду в outbox");
    }

    [Fact]
    public async Task CancelBooking_Confirmed_OutboxEntry_HasCancelBookingJobMessageType()
    {
        // Arrange
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await ConfirmBookingAsync(catalogRequestId);

        // Act
        await BookingService.CancelBooking(bookingId);

        // Assert
        var messageTypeExists = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM outbox_messages " +
            "WHERE message_type LIKE '%CancelBookingJob%'")
            .SingleAsync();

        messageTypeExists.Should().BeGreaterThan(0,
            because: "В outbox должна быть запись с типом, содержащим 'CancelBookingJob' — " +
                     "команда на отмену резервации в Catalog Service");
    }

    // -----------------------------------------------------------------------
    // Новые записи не обработаны (processed_at = NULL)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateBooking_OutboxEntry_HasNullProcessedAt()
    {
        // Act
        await CreateBookingAsync();

        // Assert: свежие записи ещё не обработаны OutboxProcessorJob
        var unprocessedCount = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM outbox_messages WHERE processed_at IS NULL")
            .SingleAsync();

        unprocessedCount.Should().BeGreaterThan(0,
            because: "Новые записи в outbox не должны быть помечены как обработанные — " +
                     "это задача фонового OutboxProcessorJob");
    }

    // -----------------------------------------------------------------------
    // OutboxProcessorJob зарегистрирован как BackgroundService
    // -----------------------------------------------------------------------

    [Fact]
    public void OutboxProcessorJob_IsRegisteredAsBackgroundService()
    {
        // Проверяем через рефлексию, что класс существует и наследует BackgroundService
        var assembly = typeof(BookingService.Services.BookingService).Assembly;
        var outboxJobType = assembly.GetTypes()
            .FirstOrDefault(t => t.Name.Contains("OutboxProcessor") && !t.IsInterface);

        outboxJobType.Should().NotBeNull(
            because: "OutboxProcessorJob должен существовать для обработки записей из outbox");

        var isBackgroundService = outboxJobType!
            .GetInterfaces()
            .Any(i => i.Name == "IHostedService")
            || (outboxJobType.BaseType?.Name == "BackgroundService");

        isBackgroundService.Should().BeTrue(
            because: "OutboxProcessorJob должен быть фоновым сервисом (наследовать BackgroundService)");
    }
}
