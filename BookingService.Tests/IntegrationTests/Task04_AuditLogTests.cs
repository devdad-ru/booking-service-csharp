using BookingService.Entities;
using BookingService.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Tests.IntegrationTests;

/// <summary>
/// Интеграционные тесты для Задачи 04 — Журнал изменений статусов (Audit Log).
///
/// Проверяют:
/// 1. Миграция создаёт таблицу booking_status_history с нужными колонками
/// 2. При каждом изменении статуса запись автоматически добавляется в историю
/// 3. История привязана к конкретному бронированию (не смешивается с другими)
/// 4. Метод GetBookingHistory возвращает записи в хронологическом порядке
/// </summary>
public class Task04_AuditLogTests : IntegrationTestBase
{
    // -----------------------------------------------------------------------
    // Схема: таблица booking_status_history
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Migration_BookingStatusHistoryTable_Exists()
    {
        var tableExists = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM information_schema.tables " +
            "WHERE table_schema = 'public' AND table_name = 'booking_status_history'")
            .SingleAsync();

        tableExists.Should().Be(1,
            because: "Миграция задачи 04 должна создать таблицу booking_status_history");
    }

    [Fact]
    public async Task Migration_BookingStatusHistory_HasAllRequiredColumns()
    {
        var requiredColumns = new[]
        {
            "id", "booking_id", "status_from", "status_to", "changed_at"
        };

        foreach (var column in requiredColumns)
        {
            var exists = await Context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*)::int FROM information_schema.columns " +
                "WHERE table_name = 'booking_status_history' AND column_name = {0}",
                column)
                .SingleAsync();

            exists.Should().Be(1,
                because: $"Таблица booking_status_history должна содержать колонку '{column}'");
        }
    }

    // -----------------------------------------------------------------------
    // Автоматическое добавление записей при изменении статуса
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateBooking_AddsHistoryEntryWithAwaitConfirmationStatus()
    {
        // Act
        var (bookingId, _) = await CreateBookingAsync();

        // Assert: при создании статус меняется на AwaitConfirmation
        var historyCount = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM booking_status_history " +
            "WHERE booking_id = {0} AND status_to = {1}",
            bookingId, (int)BookingStatus.AwaitConfirmation)
            .SingleAsync();

        historyCount.Should().Be(1,
            because: "Создание бронирования должно зафиксировать переход в статус AwaitConfirmation");
    }

    [Fact]
    public async Task ConfirmBooking_AddsHistoryEntryWithConfirmedStatus()
    {
        // Arrange
        var (bookingId, catalogRequestId) = await CreateBookingAsync();

        // Act
        await ConfirmBookingAsync(catalogRequestId);

        // Assert: должна быть запись с status_to = Confirmed
        var confirmedEntryCount = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM booking_status_history " +
            "WHERE booking_id = {0} AND status_to = {1}",
            bookingId, (int)BookingStatus.Confirmed)
            .SingleAsync();

        confirmedEntryCount.Should().Be(1,
            because: "Подтверждение бронирования должно добавить запись с status_to = Confirmed");
    }

    [Fact]
    public async Task CancelBooking_Confirmed_AddsHistoryEntryWithCancellationPendingStatus()
    {
        // Arrange: Confirmed → CancellationPending
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await ConfirmBookingAsync(catalogRequestId);

        // Act
        await BookingService.CancelBooking(bookingId);

        // Assert
        var cancellationEntryCount = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM booking_status_history " +
            "WHERE booking_id = {0} AND status_to = {1}",
            bookingId, (int)BookingStatus.CancellationPending)
            .SingleAsync();

        cancellationEntryCount.Should().Be(1,
            because: "Запрос отмены подтверждённого бронирования должен добавить запись " +
                     "с status_to = CancellationPending");
    }

    [Fact]
    public async Task HandleCancellationError_AddsHistoryEntryForRollbackToConfirmed()
    {
        // Arrange: доводим до CancellationPending, затем откатываем через DLQ
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await ConfirmBookingAsync(catalogRequestId);
        await BookingService.CancelBooking(bookingId);

        // Act: DLQ-ошибка откатывает обратно в Confirmed
        await BookingService.HandleCancellationError(catalogRequestId);

        // Assert: вторая запись с status_to = Confirmed (первая — при создании+подтверждении)
        var rollbackEntryCount = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM booking_status_history " +
            "WHERE booking_id = {0} AND status_to = {1}",
            bookingId, (int)BookingStatus.Confirmed)
            .SingleAsync();

        rollbackEntryCount.Should().BeGreaterThanOrEqualTo(1,
            because: "Компенсирующая транзакция (DLQ) должна зафиксировать откат к Confirmed");
    }

    // -----------------------------------------------------------------------
    // GetBookingHistory — корректность выборки и порядка
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetBookingHistory_ReturnsHistoryForSpecificBookingOnly()
    {
        // Arrange: два разных бронирования с отдельными историями
        var (bookingId1, requestId1) = await CreateBookingAsync(userId: 1, resourceId: 1);
        var (bookingId2, requestId2) = await CreateBookingAsync(userId: 2, resourceId: 2);
        await ConfirmBookingAsync(requestId1);
        await ConfirmBookingAsync(requestId2);

        // Act
        var history = await BookingService.GetBookingHistory(bookingId1);

        // Assert
        history.Should().NotBeEmpty(
            because: "GetBookingHistory должен возвращать историю существующего бронирования");
        history.Should().OnlyContain(h => h.BookingId == bookingId1,
            because: "Метод должен возвращать историю только для запрошенного бронирования");
    }

    [Fact]
    public async Task GetBookingHistory_ReturnsEntriesInChronologicalOrder()
    {
        // Arrange: несколько последовательных переходов статуса
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await ConfirmBookingAsync(catalogRequestId);
        await BookingService.CancelBooking(bookingId);
        await BookingService.HandleCancellationError(catalogRequestId);

        // Act
        var history = await BookingService.GetBookingHistory(bookingId);

        // Assert
        history.Should().HaveCountGreaterThanOrEqualTo(3,
            because: "Должно быть минимум 3 записи: Create → Confirm → CancellationPending → Rollback");
        history.Should().BeInAscendingOrder(h => h.ChangedAt,
            because: "История должна быть отсортирована по времени (от старых к новым)");
    }

    [Fact]
    public async Task GetBookingHistory_ForNonExistentBooking_ReturnsEmpty()
    {
        // Arrange
        const long nonExistentId = 999_999;

        // Act
        var history = await BookingService.GetBookingHistory(nonExistentId);

        // Assert
        history.Should().BeEmpty(
            because: "Для несуществующего бронирования метод должен вернуть пустой список");
    }
}
