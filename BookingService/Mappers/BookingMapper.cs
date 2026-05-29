using BookingService.Dto.Response;
using BookingService.Entities;

namespace BookingService.Mappers;

/// <summary>
/// Маппер для преобразования Entity в DTO
/// </summary>
public class BookingMapper
{
    public BookingResponse ToResponse(Booking booking)
        => new(
            booking.Id,
            booking.Status,
            booking.UserId,
            booking.ResourceId,
            booking.BookedFrom,
            booking.BookedTo,
            booking.CreatedAt
        );

    public List<BookingResponse> ToResponseList(List<Booking> bookings)
        => bookings.Select(ToResponse).ToList();
}
