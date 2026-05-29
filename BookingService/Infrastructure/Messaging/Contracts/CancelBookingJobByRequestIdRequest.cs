namespace BookingService.Infrastructure.Messaging.Contracts;

/// <summary>
/// Команда на отмену задания бронирования в Catalog Service по RequestId.
/// Публикуется в topic exchange, Catalog Service подписан на этот тип.
/// </summary>
public class CancelBookingJobByRequestIdRequest
{
    /// <summary>Идентификатор события (для трассировки)</summary>
    public Guid EventId { get; set; }

    /// <summary>Распределённый идентификатор запроса (для поиска задания в Catalog Service)</summary>
    public Guid RequestId { get; set; }
}
