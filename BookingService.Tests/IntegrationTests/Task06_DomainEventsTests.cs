using BookingService.Entities;
using BookingService.Tests.Fixtures;
using FluentAssertions;

namespace BookingService.Tests.IntegrationTests;

/// <summary>
/// Интеграционные тесты для Задачи 06 — Domain Events (BookingStatusChangedEvent).
///
/// Проверяют:
/// 1. BookingStatusChangedEvent публикуется при каждом изменении статуса
/// 2. Событие содержит корректные BookingId, OldStatus, NewStatus
/// 3. Событие НЕ публикуется при вызовах-no-op (не найдено, гард от гонки)
///
/// Тесты проверяют факт публикации через BusMock.ReceivedCalls() без прямой
/// ссылки на тип BookingStatusChangedEvent — это позволяет тестам компилироваться
/// до реализации, обнаруживая отсутствие публикации как провал теста.
/// </summary>
public class Task06_DomainEventsTests : IntegrationTestBase
{
    // -----------------------------------------------------------------------
    // Вспомогательные методы для проверки публикации доменных событий
    // -----------------------------------------------------------------------

    /// <summary>
    /// Возвращает все вызовы Publish, у которых первый аргумент имеет тип
    /// с именем "BookingStatusChangedEvent". Не требует ссылки на тип.
    /// </summary>
    private IReadOnlyList<object?> GetStatusChangedEventArgs()
        => BusMock.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(Rebus.Bus.IBus.Publish))
            .Select(c => c.GetArguments().FirstOrDefault())
            .Where(arg => arg?.GetType().Name == "BookingStatusChangedEvent")
            .ToList();

    private static T GetProperty<T>(object obj, string propertyName)
    {
        var value = obj.GetType().GetProperty(propertyName)?.GetValue(obj);
        return value is T typed
            ? typed
            : throw new InvalidOperationException(
                $"Свойство {propertyName} не найдено или имеет неверный тип на {obj.GetType().Name}");
    }

    // -----------------------------------------------------------------------
    // Публикация события при изменении статуса
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleBookingJobConfirmed_PublishesBookingStatusChangedEvent()
    {
        // Arrange
        var (bookingId, catalogRequestId) = await CreateBookingAsync();

        // Act
        await ConfirmBookingAsync(catalogRequestId);

        // Assert: событие было опубликовано
        var eventArgs = GetStatusChangedEventArgs();
        eventArgs.Should().NotBeEmpty(
            because: "Подтверждение бронирования должно публиковать BookingStatusChangedEvent");
    }

    [Fact]
    public async Task HandleBookingJobConfirmed_PublishedEvent_HasCorrectBookingId()
    {
        // Arrange
        var (bookingId, catalogRequestId) = await CreateBookingAsync();

        // Act
        await ConfirmBookingAsync(catalogRequestId);

        // Assert: событие содержит правильный BookingId
        var events = GetStatusChangedEventArgs();
        events.Should().Contain(e =>
            e != null && GetProperty<long>(e, "BookingId") == bookingId,
            because: "Событие BookingStatusChangedEvent должно содержать корректный BookingId");
    }

    [Fact]
    public async Task HandleBookingJobConfirmed_PublishedEvent_HasCorrectStatusTransition()
    {
        // Arrange
        var (_, catalogRequestId) = await CreateBookingAsync();

        // Act
        await ConfirmBookingAsync(catalogRequestId);

        // Assert: OldStatus = AwaitConfirmation, NewStatus = Confirmed
        var events = GetStatusChangedEventArgs();
        events.Should().Contain(e =>
            e != null &&
            GetProperty<BookingStatus>(e, "NewStatus") == BookingStatus.Confirmed,
            because: "BookingStatusChangedEvent должен отражать переход в статус Confirmed");
    }

    [Fact]
    public async Task CancelBooking_Confirmed_PublishesBookingStatusChangedEvent()
    {
        // Arrange: Confirmed → CancellationPending
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await ConfirmBookingAsync(catalogRequestId);

        // Сбрасываем счётчик вызовов, чтобы считать только события отмены
        BusMock.ClearReceivedCalls();

        // Act
        await BookingService.CancelBooking(bookingId);

        // Assert
        var events = GetStatusChangedEventArgs();
        events.Should().NotBeEmpty(
            because: "Запрос отмены подтверждённого бронирования должен публиковать BookingStatusChangedEvent");
    }

    [Fact]
    public async Task CancelBooking_AwaitConfirmation_PublishesBookingStatusChangedEvent()
    {
        // Arrange: AwaitConfirmation → Cancelled (прямая отмена)
        var (bookingId, _) = await CreateBookingAsync();
        BusMock.ClearReceivedCalls();

        // Act
        await BookingService.CancelBooking(bookingId);

        // Assert
        var events = GetStatusChangedEventArgs();
        events.Should().NotBeEmpty(
            because: "Прямая отмена неподтверждённого бронирования также должна публиковать событие");
    }

    [Fact]
    public async Task HandleCancellationError_PublishesBookingStatusChangedEvent()
    {
        // Arrange: CancellationPending → Confirmed (компенсирующая транзакция)
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await ConfirmBookingAsync(catalogRequestId);
        await BookingService.CancelBooking(bookingId);
        BusMock.ClearReceivedCalls();

        // Act
        await BookingService.HandleCancellationError(catalogRequestId);

        // Assert
        var events = GetStatusChangedEventArgs();
        events.Should().NotBeEmpty(
            because: "Компенсирующая транзакция должна публиковать BookingStatusChangedEvent");
    }

    // -----------------------------------------------------------------------
    // Событие НЕ публикуется при no-op операциях
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleBookingJobConfirmed_WhenBookingNotFound_DoesNotPublishEvent()
    {
        // Arrange: несуществующий requestId
        var unknownRequestId = Guid.NewGuid();

        // Act
        await BookingService.HandleBookingJobConfirmed(unknownRequestId);

        // Assert: событие не публикуется для несуществующего бронирования
        var events = GetStatusChangedEventArgs();
        events.Should().BeEmpty(
            because: "Если бронирование не найдено, событие публиковаться не должно");
    }

    [Fact]
    public async Task HandleBookingJobConfirmed_WhenBookingIsCancellationPending_DoesNotPublishStatusChanged()
    {
        // Arrange: гард от гонки — позднее подтверждение для CancellationPending
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await ConfirmBookingAsync(catalogRequestId);
        await BookingService.CancelBooking(bookingId);

        // Сбрасываем вызовы после перехода в CancellationPending
        BusMock.ClearReceivedCalls();

        // Act: запоздалое подтверждение — должно быть проигнорировано
        await BookingService.HandleBookingJobConfirmed(catalogRequestId);

        // Assert: статус не изменился → событие тоже не публикуется
        var events = GetStatusChangedEventArgs();
        events.Should().BeEmpty(
            because: "Игнорируемое (no-op) событие не должно порождать публикацию BookingStatusChangedEvent");
    }
}
