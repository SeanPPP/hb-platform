using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Hbpos.Client.Wpf.Models;

namespace Hbpos.Client.Wpf.Services;

public enum CardProcessorKind
{
    None,
    Linkly,
    Square
}

public sealed record CardTerminalSettings(
    CardProcessorKind Processor,
    string LinklyHost,
    int LinklyPort,
    string? SquareAccessToken,
    string? SquareLocationId,
    string? SquareDeviceId,
    string SquareApiBaseUrl,
    TimeSpan TerminalTimeout)
{
    public static CardTerminalSettings FromEnvironment()
    {
        var processorText = Environment.GetEnvironmentVariable("HBPOS_CARD_PROCESSOR") ?? string.Empty;
        var processor = processorText.Trim().ToUpperInvariant() switch
        {
            "LINKLY" or "ANZ" => CardProcessorKind.Linkly,
            "SQUARE" => CardProcessorKind.Square,
            _ => CardProcessorKind.None
        };

        return new CardTerminalSettings(
            processor,
            Environment.GetEnvironmentVariable("HBPOS_LINKLY_HOST")?.Trim() is { Length: > 0 } host ? host : "127.0.0.1",
            int.TryParse(Environment.GetEnvironmentVariable("HBPOS_LINKLY_PORT"), out var port) ? port : 2011,
            Environment.GetEnvironmentVariable("HBPOS_SQUARE_ACCESS_TOKEN") ??
                Environment.GetEnvironmentVariable("HBPOS_SQUARE_TOKEN") ??
                Environment.GetEnvironmentVariable("SQUARE_TOKEN"),
            Environment.GetEnvironmentVariable("HBPOS_SQUARE_LOCATION_ID"),
            Environment.GetEnvironmentVariable("HBPOS_SQUARE_DEVICE_ID"),
            Environment.GetEnvironmentVariable("HBPOS_SQUARE_API_BASE_URL")?.Trim() is { Length: > 0 } squareApiBaseUrl
                ? squareApiBaseUrl
                : "https://connect.squareup.com/",
            TimeSpan.FromSeconds(
                int.TryParse(Environment.GetEnvironmentVariable("HBPOS_CARD_TERMINAL_TIMEOUT_SECONDS"), out var timeoutSeconds) && timeoutSeconds > 0
                    ? timeoutSeconds
                    : 90));
    }
}

public sealed class ConfiguredCardTerminalClient(CardTerminalSettings settings) : ICardTerminalClient
{
    private static readonly HttpClient SquareHttpClient = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan SquarePollInterval = TimeSpan.FromSeconds(2);

    public async Task<PaymentAuthorizationResult> AuthorizeAsync(
        decimal amount,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0m)
        {
            return new PaymentAuthorizationResult(false, null, "Card amount must be greater than zero.");
        }

        return settings.Processor switch
        {
            CardProcessorKind.Linkly => new PaymentAuthorizationResult(
                false,
                null,
                "ANZ Linkly terminal adapter is not wired to the official SDK in this build."),
            CardProcessorKind.Square => await AuthorizeSquareAsync(amount, session, cancellationToken),
            _ => new PaymentAuthorizationResult(false, null, "Card terminal is not configured.")
        };
    }

    private async Task<PaymentAuthorizationResult> AuthorizeSquareAsync(
        decimal amount,
        PosSessionState session,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.SquareAccessToken) ||
            string.IsNullOrWhiteSpace(settings.SquareLocationId) ||
            string.IsNullOrWhiteSpace(settings.SquareDeviceId))
        {
            return new PaymentAuthorizationResult(false, null, "Square terminal configuration is incomplete.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(settings.TerminalTimeout);

        var reference = Limit($"{session.DeviceCode}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}", 40);
        var createRequest = new
        {
            idempotency_key = Guid.NewGuid().ToString("N"),
            checkout = new
            {
                amount_money = new
                {
                    amount = ToMinorUnits(amount),
                    currency = "AUD"
                },
                device_options = new
                {
                    device_id = settings.SquareDeviceId
                },
                location_id = settings.SquareLocationId,
                reference_id = reference,
                note = Limit($"HBPOS {session.StoreCode} {session.DeviceCode}", 500)
            }
        };

        using var createResponse = await SendSquareAsync(
            HttpMethod.Post,
            "v2/terminals/checkouts",
            createRequest,
            timeoutCts.Token);
        var createBody = await createResponse.Content.ReadAsStringAsync(timeoutCts.Token);
        if (!createResponse.IsSuccessStatusCode)
        {
            return new PaymentAuthorizationResult(false, null, $"Square checkout failed: {createBody}");
        }

        using var createDocument = JsonDocument.Parse(createBody);
        var checkout = createDocument.RootElement.GetProperty("checkout");
        var checkoutId = checkout.GetProperty("id").GetString();
        if (string.IsNullOrWhiteSpace(checkoutId))
        {
            return new PaymentAuthorizationResult(false, null, "Square checkout did not return an id.");
        }

        while (true)
        {
            timeoutCts.Token.ThrowIfCancellationRequested();
            using var getResponse = await SendSquareAsync(
                HttpMethod.Get,
                $"v2/terminals/checkouts/{Uri.EscapeDataString(checkoutId)}",
                body: null,
                timeoutCts.Token);
            var getBody = await getResponse.Content.ReadAsStringAsync(timeoutCts.Token);
            if (!getResponse.IsSuccessStatusCode)
            {
                return new PaymentAuthorizationResult(false, null, $"Square checkout status failed: {getBody}");
            }

            using var getDocument = JsonDocument.Parse(getBody);
            var currentCheckout = getDocument.RootElement.GetProperty("checkout");
            var status = currentCheckout.GetProperty("status").GetString();
            if (string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                var authorizedAmount = ReadAmount(currentCheckout) ?? amount;
                if (authorizedAmount != amount)
                {
                    return new PaymentAuthorizationResult(false, null, "Square authorized amount did not match the requested amount.");
                }

                var paymentId = ReadFirstPaymentId(currentCheckout) ?? checkoutId;
                return new PaymentAuthorizationResult(true, $"SQ:{paymentId}", "Square", authorizedAmount);
            }

            if (string.Equals(status, "CANCELED", StringComparison.OrdinalIgnoreCase))
            {
                return new PaymentAuthorizationResult(false, null, "Square checkout was canceled.");
            }

            await Task.Delay(SquarePollInterval, timeoutCts.Token);
        }
    }

    private async Task<HttpResponseMessage> SendSquareAsync(
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken)
    {
        var baseUri = settings.SquareApiBaseUrl.EndsWith("/")
            ? new Uri(settings.SquareApiBaseUrl, UriKind.Absolute)
            : new Uri(settings.SquareApiBaseUrl + "/", UriKind.Absolute);
        using var request = new HttpRequestMessage(method, new Uri(baseUri, relativeUrl));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.SquareAccessToken);
        request.Headers.Add("Square-Version", "2026-01-22");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return await SquareHttpClient.SendAsync(request, cancellationToken);
    }

    private static long ToMinorUnits(decimal amount)
    {
        return decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static decimal? ReadAmount(JsonElement checkout)
    {
        return checkout.TryGetProperty("amount_money", out var money) &&
            money.TryGetProperty("amount", out var amount) &&
            amount.TryGetInt64(out var minorUnits)
                ? minorUnits / 100m
                : null;
    }

    private static string? ReadFirstPaymentId(JsonElement checkout)
    {
        return checkout.TryGetProperty("payment_ids", out var paymentIds) &&
            paymentIds.ValueKind == JsonValueKind.Array &&
            paymentIds.GetArrayLength() > 0
                ? paymentIds[0].GetString()
                : null;
    }

    private static string Limit(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
