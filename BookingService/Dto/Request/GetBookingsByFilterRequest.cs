using BookingService.Entities;

namespace BookingService.Dto.Request;

/// <summary>
/// Запрос на получение списка бронирований с фильтрацией и пагинацией
/// </summary>
public record GetBookingsByFilterRequest(
    long? UserId,
    long? ResourceId,
    BookingStatus? Status,
    int PageNumber = 0,
    int PageSize = 25
);
