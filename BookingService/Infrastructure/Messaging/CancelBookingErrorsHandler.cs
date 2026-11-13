using BookingService.Infrastructure.Messaging.Contracts;
using BookingService.Services;
using Rebus.Handlers;

namespace BookingService.Infrastructure.Messaging;

/// <summary>
/// Обработчик ошибок отмены бронирования из Dead Letter Queue Catalog Service.
/// Вызывается когда Catalog Service не смог обработать CancelBookingJobByRequestIdRequest.
/// </summary>
public class CancelBookingErrorsHandler : IHandleMessages<CancelBookingJobByRequestIdRequest>
{
    private readonly Services.BookingService _bookingService;
    private readonly ILogger<CancelBookingErrorsHandler> _logger;
    private readonly EventProcessingService _processingEventService;

    public CancelBookingErrorsHandler(Services.BookingService bookingService, ILogger<CancelBookingErrorsHandler> logger, EventProcessingService processingEventService)
    {
        _bookingService = bookingService;
        _logger = logger;
        _processingEventService = processingEventService;
    }

    public async Task Handle(CancelBookingJobByRequestIdRequest message)
    {
        _logger.LogInformation(
            "Получено событие CancellationError: eventId={EventId}, requestId={RequestId}",
            message.EventId, message.RequestId);

        await _processingEventService.ProcessEvent(message.EventId, "CancellationError",
            async () => {
                await _bookingService.HandleCancellationError(message.RequestId);
            });
    }
}
