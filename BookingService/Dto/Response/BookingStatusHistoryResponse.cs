using BookingService.Entities;

namespace BookingService.Dto.Response;

/// <summary>
/// DTO для передачи данных о истории изменения статусов бронирования
/// </summary>
public record BookingStatusHistoryResponse(
    long Id,
    long BookingId,
    BookingStatus? StatusFrom,
    BookingStatus StatusTo,
    DateTimeOffset ChangedAt
);