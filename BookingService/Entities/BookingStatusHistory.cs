// [Task 04] Entity для хранения истории изменений статуса бронирования
namespace BookingService.Entities;

public class BookingStatusHistory
{
    public long Id { get; private set; }
    public long BookingId { get; private set; }
    public BookingStatus? StatusFrom { get; private set; }
    public BookingStatus StatusTo { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }

    private BookingStatusHistory() { }

    public static BookingStatusHistory Create(long bookingId, BookingStatus? statusFrom, BookingStatus statusTo)
        => new()
        {
            BookingId = bookingId,
            StatusFrom = statusFrom,
            StatusTo = statusTo,
            ChangedAt = DateTimeOffset.UtcNow
        };
}
