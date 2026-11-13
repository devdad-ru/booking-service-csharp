using BookingService.Configuration;
using BookingService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingService.Services
{
    public class EventProcessingService
    {
        private readonly ProcessedEventRepository _processedEventRepository;

        private readonly ICurrentDateTimeProvider _currentDateTimeProvider;
        private readonly ILogger<EventProcessingService> _logger;

        public EventProcessingService(ProcessedEventRepository processedEventRepository, ILogger<EventProcessingService> logger, ICurrentDateTimeProvider currentDateTimeProvider)
        {
            _processedEventRepository = processedEventRepository;
            _logger = logger;
            _currentDateTimeProvider = currentDateTimeProvider;
        }

        public async Task ProcessEvent(Guid eventId, string eventName, Func<Task> action)
        {
            if (eventId == Guid.Empty)
            {
                throw new ArgumentException("EventId не может быть пустым", nameof(eventId));
            }

            var processedEvent = await _processedEventRepository.FindByIdAsync(eventId);

            if (processedEvent != null)
            {
                _logger.LogWarning(
                    "Событие {EventName} с eventId={EventId} уже обработано ранее, повторная обработка игнорируется", eventName,
                    eventId);
                return;
            }

            _processedEventRepository.Save(Entities.ProcessedEvent.Create(eventId, _currentDateTimeProvider.UtcNow()));

            try
            {
                await action();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                _logger.LogWarning(
                    "Событие {EventName} с eventId={EventId} уже обработано ранее, повторная обработка игнорируется",
                    eventName,
                    eventId);
            }
        }
    }
}
