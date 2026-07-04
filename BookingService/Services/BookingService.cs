using BookingService.Configuration;
using BookingService.Dto.Response;
using BookingService.Entities;
using BookingService.Exceptions;
using BookingService.Infrastructure.Data;
using BookingService.Infrastructure.Messaging;
using BookingService.Infrastructure.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Services;

/// <summary>
/// Сервис для работы с бронированиями.
/// Объединяет CRUD операции, бизнес-логику и обработку событий от Catalog Service.
/// </summary>
public class BookingService
{
    private readonly BookingRepository _repository;
    private readonly BookingEventPublisher _publisher;
    private readonly ICurrentDateTimeProvider _dateTimeProvider;
    private readonly ILogger<BookingService> _logger;

    public BookingService(
        BookingRepository repository,
        BookingEventPublisher publisher,
        ICurrentDateTimeProvider dateTimeProvider,
        ILogger<BookingService> logger)
    {
        _repository = repository;
        _publisher = publisher;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    // === КОМАНДЫ (Use Cases) ===

    /// <summary>
    /// Создать новое бронирование.
    /// Отправляет асинхронную команду в Catalog Service для резервирования ресурса.
    /// </summary>
    /// <returns>ID созданного бронирования</returns>
    public async Task<long> CreateBooking(long userId, long resourceId, DateOnly bookedFrom, DateOnly bookedTo)
    {
        var now = _dateTimeProvider.UtcNow();
        var booking = Booking.Create(userId, resourceId, bookedFrom, bookedTo, now);

        var requestId = Guid.NewGuid();
        booking.SetCatalogRequestId(requestId);

        await _repository.SaveAsync(booking);

        var command = new CreateBookingJobRequest
        {
            EventId = Guid.NewGuid(),
            RequestId = requestId,
            ResourceId = booking.ResourceId,
            StartDate = booking.BookedFrom,
            EndDate = booking.BookedTo
        };

        await _publisher.PublishCreateBookingJob(command);

        _logger.LogInformation("Создано бронирование с ID: {Id}, requestId: {RequestId}", booking.Id, requestId);

        return booking.Id;
    }

    /// <summary>
    /// Отменить бронирование.
    /// Для подтверждённых бронирований переходит в CancellationPending (Задача 01).
    /// </summary>
    public async Task CancelBooking(long id)
    {
        var booking = await _repository.FindByIdAsync(id)
            ?? throw new BusinessException($"Бронирование с указанным id: '{id}' не найдено.");

        var currentDate = DateOnly.FromDateTime(_dateTimeProvider.UtcNow().UtcDateTime);
        booking.Cancel(currentDate);

        await _repository.SaveAsync(booking);

        if (booking.CatalogRequestId is not null)
        {
            var command = new CancelBookingJobByRequestIdRequest
            {
                EventId = Guid.NewGuid(),
                RequestId = booking.CatalogRequestId.Value
            };

            await _publisher.PublishCancelBookingJob(command);
        }

        _logger.LogInformation("Инициирована отмена бронирования с ID: {Id}, новый статус: {Status}",
            id, booking.Status);
    }

    // === ЗАПРОСЫ (Queries) ===

    /// <summary>Получить бронирование по ID</summary>
    public async Task<Booking> GetById(long id)
        => await _repository.FindByIdAsync(id)
           ?? throw new BusinessException($"Бронирование с указанным id: '{id}' не найдено.");

    /// <summary>Получить бронирования по фильтрам с пагинацией</summary>
    public async Task<List<Booking>> GetByFilter(
        long? userId,
        long? resourceId,
        BookingStatus? status,
        int pageNumber,
        int pageSize)
        => await _repository.FindByFilterAsync(userId, resourceId, status, pageNumber, pageSize);

    /// <summary>Получить только статус бронирования по ID</summary>
    public async Task<BookingStatus?> GetStatusById(long id)
        => await _repository.FindStatusByIdAsync(id);

    // === EVENT HANDLERS (Обработка асинхронных событий от Catalog Service) ===

    /// <summary>
    /// Обработать событие подтверждения booking job от Catalog Service.
    /// Переводит бронирование в статус Confirmed.
    /// </summary>
    public async Task HandleBookingJobConfirmed(Guid requestId)
    {
        _logger.LogInformation("Получено событие BookingJobConfirmed: requestId={RequestId}", requestId);

        var booking = await _repository.FindByCatalogRequestIdAsync(requestId);
        if (booking is null)
        {
            _logger.LogWarning("Бронирование не найдено по requestId: {RequestId}. Событие проигнорировано.", requestId);
            return;
        }

        _logger.LogInformation("Найдено бронирование: id={Id}, статус={Status}. Подтверждаем...",
            booking.Id, booking.Status);

        booking.Confirm();

        _logger.LogInformation("Бронирование успешно подтверждено: id={Id}, новый статус={Status}",
            booking.Id, booking.Status);
    }

    /// <summary>
    /// Обработать событие отклонения booking job от Catalog Service.
    /// Отменяет бронирование.
    /// </summary>
    public async Task HandleBookingJobDenied(Guid requestId)
    {
        _logger.LogInformation("Получено событие BookingJobDenied: requestId={RequestId}", requestId);

        var booking = await _repository.FindByCatalogRequestIdAsync(requestId);
        if (booking is null)
        {
            _logger.LogWarning("Бронирование не найдено по requestId: {RequestId}. Событие проигнорировано.", requestId);
            return;
        }

        _logger.LogInformation("Найдено бронирование: id={Id}, статус={Status}. Отменяем...",
            booking.Id, booking.Status);

        var currentDate = DateOnly.FromDateTime(_dateTimeProvider.UtcNow().UtcDateTime);
        booking.Cancel(currentDate);
        await _repository.SaveAsync(booking);

        _logger.LogInformation("Бронирование успешно отменено: id={Id}, новый статус={Status}",
            booking.Id, booking.Status);
    }

    /// <summary>Устаревший метод-заглушка — используйте HandleCancellationError</summary>
    public Task HandleError(Guid requestId) => HandleCancellationError(requestId);

    /// <summary>
    /// Обработать ошибку отмены
    /// </summary>
    /// <param name="requestId"></param>
    /// <returns></returns>
    public async Task HandleCancellationError(Guid requestId)
    {
        _logger.LogInformation("Получено событие CancellationError: requestId={RequestId}", requestId);
        var booking = await _repository.FindByCatalogRequestIdAsync(requestId);
        if (booking is null)
        {
            _logger.LogWarning("Бронирование не найдено по requestId: {RequestId}. Событие проигнорировано.", requestId);
            return;
        }

        if (booking.Status != BookingStatus.CancellationPending)
        {
            _logger.LogInformation("Некорректный статус id={Id}, статус={Status}. Событие проигнорировано.",
                booking.Id, booking.Status);
            return;
        }
        
        _logger.LogInformation("Найдено бронирование: id={Id}, статус={Status}. Выполняем rollback...",
            booking.Id, booking.Status);

        booking.RollbackCancellation();
        await _repository.SaveAsync(booking);

        _logger.LogInformation("Rollback проведен успешно: id={Id}, новый статус={Status}",
            booking.Id, booking.Status);
    }
}
