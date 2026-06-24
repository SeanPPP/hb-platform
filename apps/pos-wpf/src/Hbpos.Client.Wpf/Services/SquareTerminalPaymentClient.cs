using System.Net.Http;
using System.Text.Json;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Square;

namespace Hbpos.Client.Wpf.Services;

public sealed record SquareCheckoutStatusResult(
    string CheckoutId,
    string Status,
    long? AmountCents,
    string? Currency,
    IReadOnlyList<string> PaymentIds,
    string? CancelReason);

public sealed record SquarePaymentStatusResult(
    string PaymentId,
    string Status,
    long AmountCents,
    string Currency,
    string? CardBrand = null,
    string? MaskedCardNumber = null,
    string? AuthCode = null);

public interface ISquareTerminalPaymentClient
{
    Task<SquareCheckoutStatusResult> GetCheckoutAsync(
        CardTerminalSettings settings,
        string checkoutId,
        CancellationToken cancellationToken = default);

    Task<SquarePaymentStatusResult> GetPaymentAsync(
        CardTerminalSettings settings,
        string paymentId,
        CancellationToken cancellationToken = default);
}

public sealed class SquareTerminalPaymentClient(HttpClient httpClient) : ISquareTerminalPaymentClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SquareCheckoutStatusResult> GetCheckoutAsync(
        CardTerminalSettings settings,
        string checkoutId,
        CancellationToken cancellationToken = default)
    {
        var checkout = await SendApiAsync<SquareCheckoutStatusResponse?>(
            HttpMethod.Get,
            $"api/v1/square/checkouts/{Uri.EscapeDataString(checkoutId)}?environment={Uri.EscapeDataString(settings.Environment.ToString())}",
            operationName: "checkout",
            cancellationToken);

        if (checkout is null)
        {
            throw new JsonException("Square checkout API returned no checkout.");
        }

        // 后端会直接返回 checkout.payment_ids；内嵌 payment id 仅作为兼容补充。
        var paymentIds = new List<string>();
        foreach (var paymentId in checkout.PaymentIds ?? [])
        {
            AddPaymentId(paymentIds, paymentId);
        }

        AddPaymentId(paymentIds, checkout.Payment?.PaymentId);

        return new SquareCheckoutStatusResult(
            checkout.CheckoutId,
            checkout.Status ?? string.Empty,
            checkout.AmountMoney?.Amount,
            checkout.AmountMoney?.Currency,
            paymentIds,
            checkout.CancelReason);
    }

    public async Task<SquarePaymentStatusResult> GetPaymentAsync(
        CardTerminalSettings settings,
        string paymentId,
        CancellationToken cancellationToken = default)
    {
        var payment = await SendApiAsync<SquarePaymentStatusDto?>(
            HttpMethod.Get,
            $"api/v1/square/payments/{Uri.EscapeDataString(paymentId)}?environment={Uri.EscapeDataString(settings.Environment.ToString())}",
            operationName: "payment",
            cancellationToken);

        if (payment is null)
        {
            throw new JsonException("Square payment API returned no payment.");
        }

        // 后端优先返回 approved_money；若上游仅有 total_money，则退回 total_money 保持恢复验证可用。
        var amount = payment.ApprovedMoney ?? payment.TotalMoney;
        if (amount is null || string.IsNullOrWhiteSpace(amount.Currency))
        {
            throw new JsonException("Square payment API returned no approved amount.");
        }

        return new SquarePaymentStatusResult(
            payment.PaymentId,
            payment.Status ?? string.Empty,
            amount.Amount,
            amount.Currency,
            payment.CardBrand,
            payment.MaskedCardNumber,
            payment.AuthCode);
    }

    private async Task<T> SendApiAsync<T>(
        HttpMethod method,
        string relativeUrl,
        string operationName,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);

        ApiResult<T>? result = null;
        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                result = JsonSerializer.Deserialize<ApiResult<T>>(content, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new JsonException($"Square {operationName} API returned invalid JSON.", ex);
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result?.Message)
                    ? $"Square {operationName} request failed with HTTP {(int)response.StatusCode}."
                    : $"Square {operationName} request failed with HTTP {(int)response.StatusCode}: {result.Message}");
        }

        if (result is null)
        {
            throw new JsonException($"Square {operationName} API returned an empty response.");
        }

        if (!result.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.Message)
                    ? $"Square {operationName} API returned a failure response."
                    : result.Message);
        }

        return result.Data!;
    }

    private static void AddPaymentId(List<string> paymentIds, string? paymentId)
    {
        if (string.IsNullOrWhiteSpace(paymentId) ||
            paymentIds.Any(existing => string.Equals(existing, paymentId, StringComparison.Ordinal)))
        {
            return;
        }

        paymentIds.Add(paymentId.Trim());
    }
}
