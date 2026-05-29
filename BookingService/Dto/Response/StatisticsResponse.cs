// [Task 02] DTO ответа для endpoint GET api/bookings/statistics
using BookingService.Entities;

namespace BookingService.Dto.Response;

public class StatisticsResponse
{
    public int TotalCount { get; init; }
    public List<StatusCount> ByStatus { get; init; } = [];
    public List<ResourceCount> TopResources { get; init; } = [];
}

public class StatusCount
{
    public BookingStatus Status { get; init; }
    public int Count { get; init; }
}

public class ResourceCount
{
    public long ResourceId { get; init; }
    public int BookingCount { get; init; }
}
