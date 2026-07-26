using BookingService.Configuration;
using BookingService.Infrastructure.Data;
using BookingService.Infrastructure.Messaging.Contracts;

namespace BookingService.Infrastructure.Messaging;

public class StuckCancellationsJob : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CancellationTimeout = TimeSpan.FromMinutes(5);

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
        using var timer = new PeriodicTimer(CheckInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await ProcessStuckCancellationsAsync();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessStuckCancellationsAsync()
    {
        using var scope = _scopeFactory.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<BookingRepository>();
        var publisher = scope.ServiceProvider.GetRequiredService<BookingEventPublisher>();
        var dateTimeProvider = scope.ServiceProvider.GetRequiredService<ICurrentDateTimeProvider>();

        var cutoff = dateTimeProvider.UtcNow() - CancellationTimeout;
        var stuckBookings = await repository.FindStuckCancellationsAsync(cutoff);

        foreach (var booking in stuckBookings)
        {
            try
            {
                if (booking.CatalogRequestId is null)
                {
                    _logger.LogWarning(
                        "Stuck cancellation has no catalog request id: id={Id}",
                        booking.Id);
                    continue;
                }

                await publisher.PublishCancelBookingJob(new CancelBookingJobByRequestIdRequest
                {
                    EventId = Guid.NewGuid(),
                    RequestId = booking.CatalogRequestId.Value
                });

                _logger.LogWarning(
                    "Retried stuck cancellation: id={Id}, requestId={RequestId}",
                    booking.Id, booking.CatalogRequestId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to retry stuck cancellation: id={Id}, requestId={RequestId}",
                    booking.Id, booking.CatalogRequestId);
            }
        }
    }
}
