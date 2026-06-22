using Hbpos.Contracts.Square;

namespace Hbpos.Api.Services;

public interface ISquareTerminalBackendService
{
    Task<IReadOnlyList<SquareLocationDto>> GetLocationsAsync(
        string environment,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SquareDeviceDto>> GetDevicesAsync(
        string environment,
        string locationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SquareDeviceCodeDto>> GetDeviceCodesAsync(
        string environment,
        string locationId,
        CancellationToken cancellationToken);

    Task<SquareDeviceCodeDto> CreateDeviceCodeAsync(
        SquareCreateDeviceCodeRequest request,
        CancellationToken cancellationToken);

    Task<SquareDeviceCodeDto?> GetDeviceCodeAsync(
        string environment,
        string deviceCodeId,
        CancellationToken cancellationToken);

    Task<SquareCheckoutStatusResponse> CreateCheckoutAsync(
        SquareCreateCheckoutRequest request,
        CancellationToken cancellationToken);

    Task<SquareCheckoutStatusResponse?> GetCheckoutAsync(
        string environment,
        string checkoutId,
        CancellationToken cancellationToken);

    Task<SquarePaymentStatusDto?> GetPaymentAsync(
        string environment,
        string paymentId,
        CancellationToken cancellationToken);

    Task<SquareCheckoutStatusResponse> CancelCheckoutAsync(
        string checkoutId,
        SquareCheckoutActionRequest request,
        CancellationToken cancellationToken);

    Task<SquareCheckoutStatusResponse> DismissCheckoutAsync(
        string checkoutId,
        SquareCheckoutActionRequest request,
        CancellationToken cancellationToken);

    Task<SquareRefundResponse> CreateRefundAsync(
        SquareRefundRequest request,
        CancellationToken cancellationToken);

    Task<SquareWebhookAcceptedResponse> AcceptWebhookAsync(
        SquareWebhookRequest request,
        CancellationToken cancellationToken);
}
