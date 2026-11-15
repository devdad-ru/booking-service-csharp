using BookingService.Infrastructure.Data;
using BookingService.Infrastructure.Messaging;
using BookingService.Infrastructure.Messaging.Contracts;

namespace BookingService.Services;

public class StuckCancellationsJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StuckCancellationsJob> _logger;
    
    public StuckCancellationsJob(
        IServiceScopeFactory scopeFactory,
        ILogger<StuckCancellationsJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StuckCancellationsJob запущен");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessStuckCancellationAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Ошибка при обработке зависших отмен");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task ProcessStuckCancellationAsync()
    {
        using var scope = _scopeFactory.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<BookingRepository>();
        var publisher = scope.ServiceProvider.GetRequiredService<BookingEventPublisher>();
        
        var timeout = DateTimeOffset.UtcNow.AddMinutes(-5);
        var bookings = await repository.FindStuckCancellationsAsync(timeout);

        foreach (var booking in bookings)
            try
            {
                if (booking.CatalogRequestId is null)
                {
                    _logger.LogWarning("У бронирования id = {Id} поле requestId = null", booking.Id);
                
                    continue;
                }
                var command = new CancelBookingJobByRequestIdRequest
                {
                    EventId = Guid.NewGuid(),
                    RequestId = booking.CatalogRequestId.Value
                };
            
                await publisher.PublishCancelBookingJob(command);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Не удалось повторно отправить команду отмены: bookingId={Id}",
                    booking.Id);
            }
    }
}