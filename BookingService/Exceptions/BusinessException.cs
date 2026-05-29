namespace BookingService.Exceptions;

/// <summary>
/// Исключение для бизнес-ошибок (возвращает HTTP 400)
/// </summary>
public class BusinessException : Exception
{
    public BusinessException(string message) : base(message) { }
}
