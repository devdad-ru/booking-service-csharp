using System.ComponentModel.DataAnnotations;

namespace BookingService.Dto.Request;

/// <summary>
/// Запрос на создание бронирования
/// </summary>
public record CreateBookingRequest(
    [Required] long UserId,
    [Required] long ResourceId,
    [Required] DateOnly BookedFrom,
    [Required] DateOnly BookedTo
);
