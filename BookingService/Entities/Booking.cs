using BookingService.Exceptions;

namespace BookingService.Entities;

/// <summary>
/// EF Core Entity для бронирования с инкапсулированной бизнес-логикой
/// </summary>
public class Booking
{
    public long Id { get; private set; }
    public BookingStatus Status { get; private set; }
    public long UserId { get; private set; }
    public long ResourceId { get; private set; }
    public DateOnly BookedFrom { get; private set; }
    public DateOnly BookedTo { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CancellationRequestedAt { get; private set; }
    public Guid? CatalogRequestId { get; private set; }

    // Parameterless constructor required by EF Core
    private Booking() { }

    /// <summary>
    /// Factory method для создания нового бронирования с валидацией бизнес-правил
    /// </summary>
    public static Booking Create(
        long userId,
        long resourceId,
        DateOnly bookedFrom,
        DateOnly bookedTo,
        DateTimeOffset createdAt)
    {
        if (userId <= 0)
            throw new BusinessException($"Некорректный идентификатор пользователя {userId}");

        if (resourceId <= 0)
            throw new BusinessException($"Некорректный идентификатор ресурса {resourceId}");

        var currentDate = DateOnly.FromDateTime(createdAt.UtcDateTime);

        if (bookedFrom <= currentDate)
            throw new BusinessException("Дата начала бронирования должна быть больше текущей даты");

        if (bookedTo < bookedFrom)
            throw new BusinessException("Выбранная дата окончания бронирования раньше даты начала бронирования");

        return new Booking
        {
            Status = BookingStatus.AwaitConfirmation,
            UserId = userId,
            ResourceId = resourceId,
            BookedFrom = bookedFrom,
            BookedTo = bookedTo,
            CreatedAt = createdAt
        };
    }

    /// <summary>
    /// Установить идентификатор запроса в Catalog Service
    /// </summary>
    public void SetCatalogRequestId(Guid catalogRequestId)
    {
        if (CatalogRequestId is not null)
            throw new BusinessException($"CatalogRequestId уже имеет значение: {CatalogRequestId}");

        CatalogRequestId = catalogRequestId;
    }

    /// <summary>
    /// Подтвердить бронирование (переход из AwaitConfirmation в Confirmed)
    /// </summary>
    public void Confirm()
    {
        if (Status != BookingStatus.AwaitConfirmation)
            throw new BusinessException($"Статус заявки некорректен, заявка должна быть в статусе {BookingStatus.AwaitConfirmation}");

        Status = BookingStatus.Confirmed;
    }

    /// <summary>
    /// Отменить бронирование с учётом бизнес-правил
    /// </summary>
    public void Cancel(DateOnly currentDate)
    {
        switch (Status)
        {
            case BookingStatus.None:
            case BookingStatus.Cancelled: 
            case BookingStatus.CancellationPending:
                throw new BusinessException("Отмена уже в обработке");
                
            case BookingStatus.AwaitConfirmation:
                // Бронирование ещё не подтверждено Catalog Service —
                // отменяем немедленно, откат не нужен
                Status = BookingStatus.Cancelled;
                break;

            case BookingStatus.Confirmed:
                Status = BookingStatus.CancellationPending;
                break;
            
            default:
                throw new BusinessException("Некорректный статус для отмены");
        }
    }
    
    public void ConfirmCancellation()
    {
        if (Status != BookingStatus.CancellationPending)
            throw new BusinessException($"Отмена не находится в процессе обработки, текущий статус: {Status}");
        Status = BookingStatus.Cancelled;
    }
    
    public void RollbackCancellation()
    {
        if (Status != BookingStatus.CancellationPending)
            throw new BusinessException($"Отмена не находится в процессе обработки, текущий статус: {Status}");
        Status = BookingStatus.Confirmed;
    }
}
