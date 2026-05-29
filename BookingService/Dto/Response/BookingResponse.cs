using BookingService.Entities;

namespace BookingService.Dto.Response;

/// <summary>
/// DTO для передачи данных о бронировании
/// </summary>
public record BookingResponse(
    long Id,
    BookingStatus BookingStatus,
    long UserId,
    long ResourceId,
    DateOnly BookedFrom,
    DateOnly BookedTo,
    DateTimeOffset CreatedAt
);
