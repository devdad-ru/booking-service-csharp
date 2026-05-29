using BookingService.Entities;
using BookingService.Exceptions;
using FluentAssertions;

namespace BookingService.Tests.UnitTests;

/// <summary>
/// Юнит-тесты для доменной логики сущности Booking.
/// Не требуют БД — проверяют только стейт-машину.
/// Быстрые, запускаются без инфраструктуры.
/// </summary>
public class BookingEntityTests
{
    private static Booking CreateConfirmedBooking(DateOnly bookedFrom, DateOnly bookedTo)
    {
        var createdAt = DateTimeOffset.UtcNow.AddDays(-1); // вчера
        var booking = Booking.Create(1, 1, bookedFrom, bookedTo, createdAt);
        booking.SetCatalogRequestId(Guid.NewGuid());
        booking.Confirm();
        return booking;
    }

    // -----------------------------------------------------------------------
    // Базовая стейт-машина (покрывает исходный код + фундамент для задач)
    // -----------------------------------------------------------------------

    [Fact]
    public void Cancel_WhenStatusIsAwaitConfirmation_SetsStatusToCancelled()
    {
        var createdAt = DateTimeOffset.UtcNow.AddDays(-1);
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var booking = Booking.Create(1, 1, from, from.AddDays(3), createdAt);

        booking.Cancel(DateOnly.FromDateTime(DateTime.UtcNow));

        booking.Status.Should().Be(BookingStatus.Cancelled);
    }

    [Fact]
    public void Cancel_WhenStatusIsConfirmed_AndDateIsAfterBookingStart_ThrowsBusinessException()
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)); // вчера
        var to = from.AddDays(3);
        var createdAt = DateTimeOffset.UtcNow.AddDays(-10);
        var booking = CreateConfirmedBooking(from, to);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var act = () => booking.Cancel(today);

        act.Should().Throw<BusinessException>()
            .WithMessage("*начавшееся*");
    }

    // -----------------------------------------------------------------------
    // ЗАДАЧА 01: Compensating Transaction — юнит-тесты
    // -----------------------------------------------------------------------

    [Fact]
    public void Cancel_WhenStatusIsConfirmed_AndDateBeforeBooking_SetsCancellationPending()
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        var to = from.AddDays(3);
        var booking = CreateConfirmedBooking(from, to);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        booking.Cancel(today);

        // После задачи 01: должен быть CancellationPending, а не Cancelled
        booking.Status.Should().Be(BookingStatus.CancellationPending,
            because: "Confirmed-бронирование при отмене должно переходить в CancellationPending, не в Cancelled напрямую");
    }

    [Fact]
    public void Cancel_WhenStatusIsConfirmed_SetsCancellationRequestedAt()
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        var booking = CreateConfirmedBooking(from, from.AddDays(3));

        booking.Cancel(DateOnly.FromDateTime(DateTime.UtcNow));

        booking.CancellationRequestedAt.Should().NotBeNull(
            because: "При переходе в CancellationPending должна фиксироваться метка времени");
        booking.CancellationRequestedAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RollbackCancellation_WhenStatusIsCancellationPending_SetsStatusToConfirmed()
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        var booking = CreateConfirmedBooking(from, from.AddDays(3));
        booking.Cancel(DateOnly.FromDateTime(DateTime.UtcNow));
        booking.Status.Should().Be(BookingStatus.CancellationPending);

        booking.RollbackCancellation();

        booking.Status.Should().Be(BookingStatus.Confirmed,
            because: "DLQ-ошибка должна откатить статус обратно в Confirmed");
        booking.CancellationRequestedAt.Should().BeNull(
            because: "После отката метка времени отмены должна очищаться");
    }

    [Fact]
    public void CompleteCancellation_WhenStatusIsCancellationPending_SetsStatusToCancelled()
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        var booking = CreateConfirmedBooking(from, from.AddDays(3));
        booking.Cancel(DateOnly.FromDateTime(DateTime.UtcNow));

        booking.CompleteCancellation();

        booking.Status.Should().Be(BookingStatus.Cancelled,
            because: "После успешной обработки командой Catalog отмена должна быть завершена");
        booking.CancellationRequestedAt.Should().BeNull();
    }

    [Fact]
    public void RollbackCancellation_WhenStatusIsNotCancellationPending_ThrowsBusinessException()
    {
        var createdAt = DateTimeOffset.UtcNow.AddDays(-1);
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var booking = Booking.Create(1, 1, from, from.AddDays(3), createdAt);
        // статус: AwaitConfirmation

        var act = () => booking.RollbackCancellation();

        act.Should().Throw<BusinessException>(
            because: "Откат отмены допустим только из статуса CancellationPending");
    }

    [Fact]
    public void CompleteCancellation_WhenStatusIsNotCancellationPending_ThrowsBusinessException()
    {
        var createdAt = DateTimeOffset.UtcNow.AddDays(-1);
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var booking = Booking.Create(1, 1, from, from.AddDays(3), createdAt);

        var act = () => booking.CompleteCancellation();

        act.Should().Throw<BusinessException>(
            because: "Завершение отмены допустимо только из статуса CancellationPending");
    }

    [Fact]
    public void Cancel_WhenStatusIsCancellationPending_ThrowsBusinessException()
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        var booking = CreateConfirmedBooking(from, from.AddDays(3));
        booking.Cancel(DateOnly.FromDateTime(DateTime.UtcNow));
        booking.Status.Should().Be(BookingStatus.CancellationPending);

        var act = () => booking.Cancel(DateOnly.FromDateTime(DateTime.UtcNow));

        act.Should().Throw<BusinessException>(
            because: "Нельзя отменить бронирование, уже находящееся в процессе отмены");
    }
}
