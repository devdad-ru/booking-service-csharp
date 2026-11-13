using BookingService.Configuration;
using BookingService.Entities;
using BookingService.Infrastructure.Data;
using BookingService.Infrastructure.Messaging;

namespace BookingService.BackgroundJobs;
public class StuckCancellationsJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentDateTimeProvider _dateTimeProvider;
    private readonly ILogger<StuckCancellationsJob> _logger;

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private const int CANCELLATION_TIMEOUT_MINUTES = 5;

    public StuckCancellationsJob(ICurrentDateTimeProvider dateTimeProvider, ILogger<StuckCancellationsJob> logger, IServiceScopeFactory scopeFactory)
    {
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StuckCancellationsJob начал свою работу");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<BookingRepository>();
                var bookingEventPublisher = scope.ServiceProvider.GetRequiredService<BookingEventPublisher>();

                var cutoff = _dateTimeProvider.UtcNow() - TimeSpan.FromMinutes(CANCELLATION_TIMEOUT_MINUTES);
                var pendingBookings = await repository.FindStuckCancellationsAsync(cutoff);
    
                if (pendingBookings.Count == 0)
                {
                    _logger.LogInformation("StuckCancellationsJob не найдено зависших бронирований");
                    await Task.Delay(Interval, stoppingToken);
                    continue;
                }

                _logger.LogInformation("StuckCancellationsJob Найдено {Count} зависших бронирований", pendingBookings.Count);
                foreach (var booking in pendingBookings)
                {
                    try
                    {
                        _logger.LogInformation("StuckCancellationsJob Бронирование Id={Id} обрабатывается...",
                                booking.Id);

                        if (booking.CatalogRequestId is null)
                        {
                            _logger.LogWarning("StuckCancellationsJob Бронирование Id={Id} не имеет CatalogRequestId и будет пропущено при обработке",
                                booking.Id);
                            continue;
                        }

                        var command = new Infrastructure.Messaging.Contracts.CancelBookingJobByRequestIdRequest
                        {
                            RequestId = booking.CatalogRequestId!.Value,
                            EventId = Guid.NewGuid()
                        };

                        await bookingEventPublisher.PublishCancelBookingJob(command);

                        _logger.LogInformation("StuckCancellationsJob команда отмены Id={Id} успешно отправлена",
                                command.EventId);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError("StuckCancellationsJob При обработке зависшего бронирования Id={Id} возникла ошибка {Message}", booking.Id, ex.Message);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError("StuckCancellationsJob возникла ошибка: {Message}", ex.Message);
            }


            await Task.Delay(Interval, stoppingToken);
        }
    }
}
