using BookingService.Infrastructure.Messaging.Contracts;
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

    public CancelBookingErrorsHandler(Services.BookingService bookingService, ILogger<CancelBookingErrorsHandler> logger)
    {
        _bookingService = bookingService;
        _logger = logger;
    }

    public async Task Handle(CancelBookingJobByRequestIdRequest message)
    {
        _logger.LogWarning(
            "Получена ошибка из DLQ: eventId={EventId}, requestId={RequestId}",
            message.EventId, message.RequestId);

        await _bookingService.HandleCancellationError(message.RequestId);
    }
}
