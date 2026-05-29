namespace BookingService.Configuration;

/// <summary>
/// Настройки RabbitMQ (Rebus transport)
/// </summary>
public class RabbitMqSettings
{
    public string ConnectionString { get; set; } = default!;

    /// <summary>Имя входящей очереди сервиса</summary>
    public string InputQueue { get; set; } = "booking-service";

    /// <summary>Direct exchange для DLQ</summary>
    public string DirectExchange { get; set; } = "booking-service-direct";

    /// <summary>Topic exchange для публикации/подписки (должен совпадать с Catalog Service)</summary>
    public string TopicExchange { get; set; } = "booking-service-topics";
}
