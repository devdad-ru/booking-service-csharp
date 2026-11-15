using BookingService.Entities;

namespace BookingService.Infrastructure.Data
{
    public class ProcessedEventRepository
    {
        private readonly BookingDbContext _context;

        public ProcessedEventRepository(BookingDbContext context)
        {
            _context = context;
        }

        public async Task<ProcessedEvent?> FindByIdAsync(Guid eventId)
            => await _context.ProcessedEvents.FindAsync(eventId);

        public void Save(ProcessedEvent processedEvent)
        {
            _context.ProcessedEvents.Add(processedEvent);
        }
    }
}
