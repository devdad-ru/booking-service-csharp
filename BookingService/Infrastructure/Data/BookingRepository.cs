using BookingService.Dto.Response;
using BookingService.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Infrastructure.Data;

/// <summary>
/// Репозиторий для работы с бронированиями через EF Core
/// </summary>
public class BookingRepository
{
    private readonly BookingDbContext _context;
    private const int TOP_RESOURCES_COUNT = 5;
    private const int DB_CONCURRENCY_TRIES_COUNT = 10;
    public BookingRepository(BookingDbContext context)
    {
        _context = context;
    }

    public async Task<Booking?> FindByIdAsync(long id)
        => await _context.Bookings.FindAsync(id);

    public async Task<Booking?> FindByCatalogRequestIdAsync(Guid catalogRequestId)
        => await _context.Bookings.FirstOrDefaultAsync(b => b.CatalogRequestId == catalogRequestId);

    /// <summary>
    /// Найти бронирования по опциональным фильтрам с пагинацией
    /// </summary>
    public async Task<List<Booking>> FindByFilterAsync(
        long? userId,
        long? resourceId,
        BookingStatus? status,
        int pageNumber,
        int pageSize)
    {
        var query = _context.Bookings.AsQueryable();

        if (userId.HasValue)
            query = query.Where(b => b.UserId == userId.Value);

        if (resourceId.HasValue)
            query = query.Where(b => b.ResourceId == resourceId.Value);

        if (status.HasValue)
            query = query.Where(b => b.Status == status.Value);

        return await query
            .Skip(pageNumber * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<List<Entities.BookingStatusHistory>> GetBookingHistoryAsync(long bookingId)
    {
        return await _context.BookingStatusHistories.Where(x => x.BookingId == bookingId).OrderBy(x => x.ChangedAt).ToListAsync();
    }

    /// <summary>
    /// Получить только статус бронирования по ID
    /// </summary>
    public async Task<BookingStatus?> FindStatusByIdAsync(long id)
        => await _context.Bookings
            .Where(b => b.Id == id)
            .Select(b => (BookingStatus?)b.Status)
            .FirstOrDefaultAsync();

    public async Task SaveAsync(Booking booking)
    {
        if (booking.Id == 0)
            _context.Bookings.Add(booking);

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Обработка DbUpdateConcurrencyException, повтор операции с перезагрузкой сущности
    /// </summary>
    /// <param name="booking"></param>
    /// <returns></returns>
    public async Task ConcurrencyBulkSaveAsync(Booking booking)
    {
        bool saveFailed;
        int tryCount;

        do
        {
            saveFailed = false;
            tryCount = 0;

            try
            {
                await SaveAsync(booking);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                saveFailed = true;
                tryCount++;

                // Update the values of the entity that failed to save from the store 
                ex.Entries.Single().Reload();
            }

        } while (saveFailed && tryCount <= DB_CONCURRENCY_TRIES_COUNT);
    }

    public async Task<StatisticsResponse> GetStatisticsAsync()
    {
        var totalBookingsCount = await _context.Bookings.CountAsync();

        var statusCounts = await _context.Bookings.GroupBy(x => x.Status).Select(x =>
                new StatusCount
                {
                    Status = x.Key,
                    Count = x.Count()
                }
            ).ToListAsync();

        var top5 = await _context.Bookings.GroupBy(x => x.ResourceId).Select(x => 
            new ResourceCount
            {
                ResourceId = x.Key,
                BookingCount = x.Count()
            }
        ).OrderByDescending(x => x.BookingCount).Take(TOP_RESOURCES_COUNT).ToListAsync();

        return new StatisticsResponse
        {
            TotalCount = totalBookingsCount,
            ByStatus = statusCounts,
            TopResources = top5
        };
    }

    // бронирования, застрявшие в CancellationPending
    public async Task<List<Booking>> FindStuckCancellationsAsync(DateTimeOffset cancellationRequestedBefore)
    {
        return await _context.Bookings.Where(
            b => b.Status == BookingStatus.CancellationPending
            && b.CancellationRequestedAt!.Value < cancellationRequestedBefore)
            .ToListAsync();
    }
}
