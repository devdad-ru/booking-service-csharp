// [Task 09] Интерфейс HTTP-клиента для Notification Service
using BookingService.Entities;

namespace BookingService.Infrastructure.Notifications;

/// <summary>
/// Отправляет HTTP-уведомления во внешний Notification Service при изменении статуса бронирования.
/// TODO: Task 09 — реализовать NotificationService.cs, использующий HttpClient
/// </summary>
public interface INotificationService
{
    Task NotifyStatusChangedAsync(long bookingId, BookingStatus oldStatus, BookingStatus newStatus);
}

/// <summary>
/// Graceful degradation: логирует ошибку, не пробрасывает исключение.
/// Использовать в BookingService вместо прямого вызова NotifyStatusChangedAsync.
/// </summary>
public static class NotificationServiceExtensions
{
    public static async Task NotifySafeAsync(
        this INotificationService? service,
        long bookingId,
        BookingStatus oldStatus,
        BookingStatus newStatus,
        ILogger logger)
    {
        if (service is null) return;
        try
        {
            await service.NotifyStatusChangedAsync(bookingId, oldStatus, newStatus);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось отправить уведомление для бронирования id={Id}. Продолжаем работу.", bookingId);
        }
    }
}
