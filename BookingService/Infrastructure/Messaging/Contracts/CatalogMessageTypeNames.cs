namespace BookingService.Infrastructure.Messaging.Contracts;

/// <summary>
/// Имена типов сообщений так, как их видит Catalog Service.
/// Rebus по умолчанию берёт имя .NET-типа ("Namespace.Type, Assembly") и для заголовка rbs2-msg-type,
/// и для имени топика. Наши контракты лежат в другом namespace и сборке, поэтому без явного
/// сопоставления Catalog не получит наши команды, а мы — его события.
/// </summary>
public static class CatalogMessageTypeNames
{
    private const string Assembly = "BookingService.Catalog.Async.Api.Contracts";

    public const string CreateBookingJobRequest =
        "BookingService.Catalog.Async.Api.Contracts.Requests.CreateBookingJobRequest, " + Assembly;

    public const string CancelBookingJobByRequestIdRequest =
        "BookingService.Catalog.Async.Api.Contracts.Requests.CancelBookingJobByRequestIdRequest, " + Assembly;

    public const string BookingJobConfirmed =
        "BookingService.Catalog.Async.Api.Contracts.Events.BookingJobConfirmed, " + Assembly;

    public const string BookingJobDenied =
        "BookingService.Catalog.Async.Api.Contracts.Events.BookingJobDenied, " + Assembly;
}
