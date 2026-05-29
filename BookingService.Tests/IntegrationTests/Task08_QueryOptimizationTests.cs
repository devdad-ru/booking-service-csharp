using BookingService.Entities;
using BookingService.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Tests.IntegrationTests;

/// <summary>
/// Интеграционные тесты для Задачи 08 — Оптимизация запросов.
///
/// Проверяют наличие индексов, необходимых для эффективной работы:
/// 1. Составной индекс на (resource_id, booked_from, booked_to) — поиск пересечений дат
/// 2. Индекс на catalog_request_id — быстрый поиск по входящим событиям Catalog Service
/// 3. Составной индекс на (user_id, status) — типичный фильтр "мои активные бронирования"
/// 4. Индекс на (created_at DESC) — сортировка по дате создания в листинге
///
/// Индексы проверяются через системное представление pg_indexes PostgreSQL.
/// Все индексы должны быть добавлены через EF Core миграции.
/// </summary>
public class Task08_QueryOptimizationTests : IntegrationTestBase
{
    // -----------------------------------------------------------------------
    // Индекс для поиска пересечений дат (главный сценарий поиска доступности)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Index_ResourceIdBookedFromBookedTo_Exists()
    {
        // Составной индекс критичен для запросов типа:
        // "Есть ли у ресурса X бронирования в период [from, to]?"
        var indexExists = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM pg_indexes " +
            "WHERE tablename = 'bookings' " +
            "AND indexdef LIKE '%resource_id%booked_from%booked_to%' " +
            "OR (tablename = 'bookings' AND indexdef LIKE '%resource_id%' " +
            "    AND indexdef LIKE '%booked_from%' AND indexdef LIKE '%booked_to%')")
            .SingleAsync();

        indexExists.Should().BeGreaterThan(0,
            because: "Составной индекс на (resource_id, booked_from, booked_to) необходим " +
                     "для эффективного поиска пересечений дат при создании нового бронирования");
    }

    // -----------------------------------------------------------------------
    // Индекс на catalog_request_id (критичен для входящих событий от Catalog)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Index_CatalogRequestId_Exists()
    {
        // Каждое входящее событие BookingJobConfirmed/Denied делает запрос по этому полю
        var indexExists = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM pg_indexes " +
            "WHERE tablename = 'bookings' AND indexname = 'idx_bookings_catalog_request_id'")
            .SingleAsync();

        indexExists.Should().Be(1,
            because: "Индекс idx_bookings_catalog_request_id необходим для быстрого поиска " +
                     "бронирования при обработке входящих событий от Catalog Service " +
                     "(FindByCatalogRequestIdAsync вызывается на каждом Confirmed/Denied событии)");
    }

    [Fact]
    public async Task Index_CatalogRequestId_IsUnique()
    {
        // catalog_request_id должен быть уникальным — у каждого бронирования свой requestId
        var isUnique = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM pg_indexes " +
            "WHERE tablename = 'bookings' " +
            "AND indexname = 'idx_bookings_catalog_request_id' " +
            "AND indexdef LIKE '%UNIQUE%'")
            .SingleAsync();

        isUnique.Should().Be(1,
            because: "Индекс на catalog_request_id должен быть уникальным — " +
                     "каждый requestId принадлежит ровно одному бронированию");
    }

    // -----------------------------------------------------------------------
    // Составной индекс (user_id, status) для листинга пользователя
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Index_UserIdAndStatus_Exists()
    {
        // Типичный запрос: "все активные бронирования пользователя 42"
        var indexExists = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM pg_indexes " +
            "WHERE tablename = 'bookings' " +
            "AND indexdef LIKE '%user_id%' AND indexdef LIKE '%status%'")
            .SingleAsync();

        indexExists.Should().BeGreaterThan(0,
            because: "Составной индекс на (user_id, status) необходим для быстрой фильтрации " +
                     "бронирований пользователя по статусу — наиболее частый шаблон запроса");
    }

    // -----------------------------------------------------------------------
    // Индекс на created_at для сортировки в листинге
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Index_CreatedAt_Exists()
    {
        var indexExists = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM pg_indexes " +
            "WHERE tablename = 'bookings' AND indexdef LIKE '%created_at%'")
            .SingleAsync();

        indexExists.Should().BeGreaterThan(0,
            because: "Индекс на created_at необходим для эффективной сортировки " +
                     "в постраничном листинге (ORDER BY created_at DESC)");
    }

    // -----------------------------------------------------------------------
    // EF Core модель — конфигурация индексов
    // -----------------------------------------------------------------------

    [Fact]
    public void BookingDbContext_HasIndexConfiguredForCatalogRequestId()
    {
        // Проверяем, что EF Core знает об индексе через модель
        var entityType = Context.Model.FindEntityType(typeof(Booking))!;
        var indexes = entityType.GetIndexes();

        indexes.Should().Contain(
            idx => idx.Properties.Any(p => p.GetColumnName() == "catalog_request_id"),
            because: "EF Core модель должна содержать конфигурацию индекса для catalog_request_id");
    }

    [Fact]
    public async Task AllRequiredIndexes_ExistOnBookingsTable()
    {
        // Суммарная проверка: таблица bookings должна иметь как минимум N индексов
        var indexCount = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM pg_indexes WHERE tablename = 'bookings'")
            .SingleAsync();

        // До оптимизации (задачи 01-03) было 4 индекса:
        // idx_bookings_status, idx_bookings_user_id, idx_bookings_resource_id,
        // idx_bookings_cancellation_pending
        // Задача 08 добавляет как минимум 2 новых
        indexCount.Should().BeGreaterThanOrEqualTo(6,
            because: "После оптимизации запросов таблица bookings должна иметь минимум 6 индексов: " +
                     "4 существующих + как минимум 2 новых (catalog_request_id, составной)");
    }
}
