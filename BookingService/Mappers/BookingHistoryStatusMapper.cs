using BookingService.Dto.Response;
using BookingService.Entities;

namespace BookingService.Mappers;

/// <summary>
/// Маппер для преобразования Entity в DTO
/// </summary>
public class BookingStatusHistoryMapper
{
    public BookingStatusHistoryResponse ToResponse(BookingStatusHistory entity)
        => new(
            entity.Id,
            entity.BookingId,
            entity.StatusFrom,
            entity.StatusTo,
            entity.ChangedAt
        );

    public List<BookingStatusHistoryResponse> ToResponseList(List<BookingStatusHistory> histories)
        => histories.Select(ToResponse).ToList();
}
