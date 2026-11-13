using BookingService.Infrastructure.Messaging.Contracts;
using BookingService.Services;
using Rebus.Handlers;

namespace BookingService.Infrastructure.Messaging;

/// <summary>
/// Обработчик событий от Catalog Service.
/// Принимает BookingJobConfirmed и BookingJobDenied, делегирует обработку в BookingService.
/// </summary>
public class BookingEventsHandler :
    IHandleMessages<BookingJobConfirmed>,
    IHandleMessages<BookingJobDenied>
{
    private readonly Services.BookingService _bookingService;

    private readonly EventProcessingService _eventProcessingService;
    private readonly ILogger<BookingEventsHandler> _logger;

    public BookingEventsHandler(Services.BookingService bookingService, ILogger<BookingEventsHandler> logger, EventProcessingService eventProcessingService)
    {
        _bookingService = bookingService;
        _logger = logger;
        _eventProcessingService = eventProcessingService;
    }

    public async Task Handle(BookingJobConfirmed message)
    {
        _logger.LogInformation(
            "Получено событие BookingJobConfirmed: eventId={EventId}, requestId={RequestId}",
            message.EventId, message.RequestId);

        await _eventProcessingService.ProcessEvent(message.EventId, "BookingJobConfirmed", async () => {
            await _bookingService.HandleBookingJobConfirmed(message.RequestId);
            });
    }

    public async Task Handle(BookingJobDenied message)
    {
        _logger.LogInformation(
            "Получено событие BookingJobDenied: eventId={EventId}, requestId={RequestId}",
            message.EventId, message.RequestId);

        await _eventProcessingService.ProcessEvent(message.EventId, "BookingJobDenied", 
            async () => {
                await _bookingService.HandleBookingJobDenied(message.RequestId);
            });
    }
}
