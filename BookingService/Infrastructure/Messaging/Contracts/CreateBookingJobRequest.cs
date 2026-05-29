namespace BookingService.Infrastructure.Messaging.Contracts;

/// <summary>
/// Команда на создание задания бронирования в Catalog Service.
/// Публикуется в topic exchange, Catalog Service подписан на этот тип.
/// </summary>
public class CreateBookingJobRequest
{
    /// <summary>Идентификатор события (для трассировки)</summary>
    public Guid EventId { get; set; }

    /// <summary>Распределённый идентификатор запроса (связывает команду и ответные события)</summary>
    public Guid RequestId { get; set; }

    /// <summary>Идентификатор ресурса для бронирования</summary>
    public long ResourceId { get; set; }

    /// <summary>Дата начала бронирования</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Дата окончания бронирования</summary>
    public DateOnly EndDate { get; set; }
}
