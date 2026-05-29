namespace BookingService.Infrastructure.Messaging.Contracts;

/// <summary>
/// Событие отклонения задания бронирования от Catalog Service.
/// Публикуется Catalog Service, принимается Booking Service.
/// </summary>
public class BookingJobDenied
{
    /// <summary>Идентификатор события (для трассировки)</summary>
    public Guid EventId { get; set; }

    /// <summary>Распределённый идентификатор запроса (связывает с исходной командой)</summary>
    public Guid RequestId { get; set; }
}
