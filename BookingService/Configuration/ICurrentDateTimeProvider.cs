namespace BookingService.Configuration;

/// <summary>
/// Провайдер текущего времени. Абстракция нужна для тестирования.
/// </summary>
public interface ICurrentDateTimeProvider
{
    DateTimeOffset UtcNow();
}

/// <summary>
/// Реализация по умолчанию — возвращает реальное UTC-время
/// </summary>
public class CurrentDateTimeProvider : ICurrentDateTimeProvider
{
    public DateTimeOffset UtcNow() => DateTimeOffset.UtcNow;
}
