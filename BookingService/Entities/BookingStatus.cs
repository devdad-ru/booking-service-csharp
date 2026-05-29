namespace BookingService.Entities;

/// <summary>
/// Статус бронирования
/// </summary>
public enum BookingStatus
{
    /// <summary>Отсутствует (0)</summary>
    None = 0,

    /// <summary>Ожидает подтверждения (1)</summary>
    AwaitConfirmation = 1,

    /// <summary>Подтверждено (2)</summary>
    Confirmed = 2,

    /// <summary>Отменено (3)</summary>
    Cancelled = 3,
}
