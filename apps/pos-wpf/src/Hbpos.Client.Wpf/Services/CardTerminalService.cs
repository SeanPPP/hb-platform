using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Orders;
using Hbpos.Contracts.Square;

namespace Hbpos.Client.Wpf.Services;

public enum CardProcessorKind
{
    None,
    Linkly,
    Square
}

public enum CardTerminalEnvironment
{
    Production,
    Sandbox
}

public enum LinklyConnectionMode
{
    LocalIp = 0,
    CloudDirectSync = 1,
    CloudBackendAsync = 2,

    // 旧配置兼容：历史 Local 等同于本地 IP 接口，历史 Cloud 等同于 Cloud 同步直连。
    Local = LocalIp,
    Cloud = CloudDirectSync
}

public sealed record CardTerminalConfiguration(
    CardProcessorKind Processor,
    CardTerminalEnvironment Environment,
    string LinklyHost,
    int LinklyPort,
    string? SquareLocationId,
    string? SquareDeviceId,
    bool HasProtectedSquareAccessToken,
    int TerminalTimeoutSeconds,
    LinklyConnectionMode LinklyConnectionMode = LinklyConnectionMode.LocalIp,
    bool HasProtectedLinklyCloudSecret = false,
    IReadOnlyList<LinklyConnectionMode>? LinklyConnectionModePriority = null)
{
    public static CardTerminalConfiguration Default { get; } = new(
        CardProcessorKind.None,
        CardTerminalEnvironment.Production,
        "127.0.0.1",
        2011,
        null,
        null,
        false,
        90,
        LinklyConnectionMode.LocalIp,
        false);
}

public sealed record CardTerminalSettings(
    CardProcessorKind Processor,
    CardTerminalEnvironment Environment,
    string LinklyHost,
    int LinklyPort,
    string? SquareAccessToken,
    string? SquareLocationId,
    string? SquareDeviceId,
    string SquareApiBaseUrl,
    TimeSpan TerminalTimeout,
    LinklyConnectionMode LinklyConnectionMode = LinklyConnectionMode.LocalIp,
    string? LinklyCloudSecret = null,
    string LinklyCloudAuthBaseUrl = "https://auth.cloud.pceftpos.com/v1/",
    string LinklyCloudRestBaseUrl = "https://rest.pos.cloud.pceftpos.com/v1/",
    string LinklyPosName = "HBPOS",
    string LinklyPosVersion = "1.0.0",
    string? LinklyPosVendorId = null,
    IReadOnlyList<LinklyConnectionMode>? LinklyConnectionModePriority = null)
{
    public const string SquareVersion = "2026-01-22";
    public const string DefaultLinklyPosName = "HotBargainPOS";
    public const string DefaultLinklyPosVersion = "2026.5.1";
    public const string SandboxPlaceholderLinklyPosVendorId = "11111111-1111-4111-8111-111111111111";
    public static readonly IReadOnlyList<LinklyConnectionMode> DefaultLinklyConnectionModePriority =
    [
        LinklyConnectionMode.LocalIp,
        LinklyConnectionMode.CloudDirectSync,
        LinklyConnectionMode.CloudBackendAsync
    ];
    private static readonly IReadOnlyList<LinklyConnectionMode> FallbackLinklyConnectionModeOrder =
    [
        LinklyConnectionMode.CloudDirectSync,
        LinklyConnectionMode.LocalIp,
        LinklyConnectionMode.CloudBackendAsync
    ];

    public static CardTerminalSettings FromEnvironment()
    {
        var processorText = System.Environment.GetEnvironmentVariable("HBPOS_CARD_PROCESSOR") ?? string.Empty;
        var processor = processorText.Trim().ToUpperInvariant() switch
        {
            "LINKLY" or "ANZ" => CardProcessorKind.Linkly,
            "SQUARE" => CardProcessorKind.Square,
            _ => CardProcessorKind.None
        };

        var terminalEnvironment = ReadEnvironment();
        var apiBase = System.Environment.GetEnvironmentVariable("HBPOS_SQUARE_API_BASE_URL")?.Trim();

        return new CardTerminalSettings(
            processor,
            terminalEnvironment,
            System.Environment.GetEnvironmentVariable("HBPOS_LINKLY_HOST")?.Trim() is { Length: > 0 } host ? host : "127.0.0.1",
            int.TryParse(System.Environment.GetEnvironmentVariable("HBPOS_LINKLY_PORT"), out var port) ? port : 2011,
            null,
            System.Environment.GetEnvironmentVariable("HBPOS_SQUARE_LOCATION_ID"),
            System.Environment.GetEnvironmentVariable("HBPOS_SQUARE_DEVICE_ID"),
            string.IsNullOrWhiteSpace(apiBase)
                ? GetSquareApiBaseUrl(terminalEnvironment)
                : NormalizeSquareApiBaseUrl(apiBase),
            TimeSpan.FromSeconds(
                int.TryParse(System.Environment.GetEnvironmentVariable("HBPOS_CARD_TERMINAL_TIMEOUT_SECONDS"), out var timeoutSeconds) && timeoutSeconds > 0
                    ? timeoutSeconds
                    : 90),
            ReadLinklyConnectionMode(),
            null,
            ResolveLinklyCloudAuthBaseUrl(terminalEnvironment),
            ResolveLinklyCloudRestBaseUrl(terminalEnvironment),
            ReadText("HBPOS_LINKLY_POS_NAME", DefaultLinklyPosName),
            ReadText("HBPOS_LINKLY_POS_VERSION", DefaultLinklyPosVersion),
            ResolveLinklyPosVendorId(terminalEnvironment),
            NormalizeLinklyConnectionModePriority(null, ReadLinklyConnectionMode()));
    }

    public static string GetSquareApiBaseUrl(CardTerminalEnvironment environment)
    {
        return environment == CardTerminalEnvironment.Sandbox
            ? "https://connect.squareupsandbox.com/v2/"
            : "https://connect.squareup.com/v2/";
    }

    public static string NormalizeSquareApiBaseUrl(string apiBaseUrl)
    {
        var trimmed = apiBaseUrl.Trim();
        if (trimmed.Length == 0)
        {
            return GetSquareApiBaseUrl(CardTerminalEnvironment.Production);
        }

        trimmed = trimmed.TrimEnd('/');
        if (!trimmed.EndsWith("/v2", StringComparison.OrdinalIgnoreCase))
        {
            trimmed += "/v2";
        }

        return trimmed + "/";
    }

    public static string GetLinklyCloudAuthBaseUrl(CardTerminalEnvironment environment)
    {
        return environment == CardTerminalEnvironment.Sandbox
            ? "https://auth.sandbox.cloud.pceftpos.com/v1/"
            : "https://auth.cloud.pceftpos.com/v1/";
    }

    public static string GetLinklyCloudRestBaseUrl(CardTerminalEnvironment environment)
    {
        return environment == CardTerminalEnvironment.Sandbox
            ? "https://rest.pos.sandbox.cloud.pceftpos.com/v1/"
            : "https://rest.pos.cloud.pceftpos.com/v1/";
    }

    public static string ResolveLinklyCloudAuthBaseUrl(CardTerminalEnvironment environment)
    {
        return ResolveLinklyCloudBaseUrl(
            environment,
            "HBPOS_LINKLY_CLOUD_AUTH_BASE_URL",
            "HBPOS_LINKLY_CLOUD_AUTH_BASE_URL_PRODUCTION",
            "HBPOS_LINKLY_CLOUD_AUTH_BASE_URL_SANDBOX",
            GetLinklyCloudAuthBaseUrl(environment));
    }

    public static string ResolveLinklyCloudRestBaseUrl(CardTerminalEnvironment environment)
    {
        return ResolveLinklyCloudBaseUrl(
            environment,
            "HBPOS_LINKLY_CLOUD_REST_BASE_URL",
            "HBPOS_LINKLY_CLOUD_REST_BASE_URL_PRODUCTION",
            "HBPOS_LINKLY_CLOUD_REST_BASE_URL_SANDBOX",
            GetLinklyCloudRestBaseUrl(environment));
    }

    private static CardTerminalEnvironment ReadEnvironment()
    {
        var environmentText = System.Environment.GetEnvironmentVariable("HBPOS_CARD_TERMINAL_ENVIRONMENT") ??
            System.Environment.GetEnvironmentVariable("HBPOS_SQUARE_ENVIRONMENT") ??
            System.Environment.GetEnvironmentVariable("SQUARE_ENVIRONMENT") ??
            string.Empty;

        return environmentText.Trim().ToUpperInvariant() switch
        {
            "SANDBOX" or "TEST" => CardTerminalEnvironment.Sandbox,
            _ => CardTerminalEnvironment.Production
        };
    }

    private static LinklyConnectionMode ReadLinklyConnectionMode()
    {
        var modeText = System.Environment.GetEnvironmentVariable("HBPOS_LINKLY_CONNECTION_MODE") ??
            System.Environment.GetEnvironmentVariable("HBPOS_LINKLY_MODE") ??
            string.Empty;

        return NormalizeLinklyConnectionMode(modeText, LinklyConnectionMode.LocalIp);
    }

    public static LinklyConnectionMode NormalizeLinklyConnectionMode(
        string? value,
        LinklyConnectionMode fallback)
    {
        var normalizedFallback = NormalizeLinklyConnectionMode(fallback);
        var modeKey = NormalizeLinklyConnectionModeKey(value);

        return modeKey switch
        {
            "LOCAL" or "LOCALIP" => LinklyConnectionMode.LocalIp,
            "CLOUD" or "CLOUDDIRECTSYNC" => LinklyConnectionMode.CloudDirectSync,
            "CLOUDBACKENDASYNC" or "CLOUDAPIASYNC" => LinklyConnectionMode.CloudBackendAsync,
            _ => normalizedFallback
        };
    }

    public static LinklyConnectionMode NormalizeLinklyConnectionMode(LinklyConnectionMode mode)
    {
        return (int)mode switch
        {
            0 => LinklyConnectionMode.LocalIp,
            1 => LinklyConnectionMode.CloudDirectSync,
            2 => LinklyConnectionMode.CloudBackendAsync,
            _ => LinklyConnectionMode.LocalIp
        };
    }

    public static string FormatLinklyConnectionMode(LinklyConnectionMode mode)
    {
        return NormalizeLinklyConnectionMode(mode) switch
        {
            LinklyConnectionMode.CloudDirectSync => nameof(LinklyConnectionMode.CloudDirectSync),
            LinklyConnectionMode.CloudBackendAsync => nameof(LinklyConnectionMode.CloudBackendAsync),
            _ => nameof(LinklyConnectionMode.LocalIp)
        };
    }

    public static IReadOnlyList<LinklyConnectionMode> NormalizeLinklyConnectionModePriority(
        IEnumerable<LinklyConnectionMode>? priority,
        LinklyConnectionMode firstFallback)
    {
        var modes = new List<LinklyConnectionMode>();
        if (priority is not null)
        {
            foreach (var mode in priority)
            {
                AddIfMissing(modes, NormalizeLinklyConnectionMode(mode));
            }
        }

        AddIfMissing(modes, NormalizeLinklyConnectionMode(firstFallback));
        foreach (var mode in FallbackLinklyConnectionModeOrder)
        {
            AddIfMissing(modes, mode);
        }

        return modes;
    }

    public static IReadOnlyList<LinklyConnectionMode> ParseLinklyConnectionModePriority(
        string? value,
        LinklyConnectionMode firstFallback)
    {
        var parsed = new List<LinklyConnectionMode>();
        if (!string.IsNullOrWhiteSpace(value))
        {
            foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var modeKey = NormalizeLinklyConnectionModeKey(token);
                var isKnown = modeKey is "LOCAL" or "LOCALIP" or "CLOUD" or "CLOUDDIRECTSYNC" or "CLOUDBACKENDASYNC" or "CLOUDAPIASYNC";
                if (!isKnown)
                {
                    continue;
                }

                AddIfMissing(parsed, NormalizeLinklyConnectionMode(token, firstFallback));
            }
        }

        return NormalizeLinklyConnectionModePriority(parsed, firstFallback);
    }

    public static string FormatLinklyConnectionModePriority(IEnumerable<LinklyConnectionMode>? priority)
    {
        return string.Join(
            ",",
            NormalizeLinklyConnectionModePriority(priority, LinklyConnectionMode.LocalIp)
                .Select(FormatLinklyConnectionMode));
    }

    private static void AddIfMissing(List<LinklyConnectionMode> modes, LinklyConnectionMode mode)
    {
        if (!modes.Contains(mode))
        {
            modes.Add(mode);
        }
    }

    public static string? ResolveLinklyPosVendorId(
        CardTerminalEnvironment environment,
        string? configuredVendorId = null)
    {
        var configured = NormalizeOptional(configuredVendorId)
            ?? NormalizeOptional(System.Environment.GetEnvironmentVariable("HBPOS_LINKLY_POS_VENDOR_ID"));
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return environment == CardTerminalEnvironment.Sandbox
            ? SandboxPlaceholderLinklyPosVendorId
            : null;
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim();
        return trimmed.EndsWith("/", StringComparison.Ordinal) ? trimmed : trimmed + "/";
    }

    private static string ResolveLinklyCloudBaseUrl(
        CardTerminalEnvironment environment,
        string globalKey,
        string productionKey,
        string sandboxKey,
        string fallback)
    {
        var environmentKey = environment == CardTerminalEnvironment.Sandbox ? sandboxKey : productionKey;
        var value = System.Environment.GetEnvironmentVariable(environmentKey);
        if (string.IsNullOrWhiteSpace(value))
        {
            value = System.Environment.GetEnvironmentVariable(globalKey);
        }

        return string.IsNullOrWhiteSpace(value) ? fallback : NormalizeBaseUrl(value);
    }

    public static string? ValidateLinklyCloudAuthBaseUrl(
        CardTerminalEnvironment environment,
        string baseUrl)
    {
        return ValidateLinklyCloudBaseUrl(
            environment,
            baseUrl,
            "Auth",
            GetLinklyCloudAuthBaseUrl(CardTerminalEnvironment.Production),
            GetLinklyCloudAuthBaseUrl(CardTerminalEnvironment.Sandbox));
    }

    public static string? ValidateLinklyCloudRestBaseUrl(
        CardTerminalEnvironment environment,
        string baseUrl)
    {
        return ValidateLinklyCloudBaseUrl(
            environment,
            baseUrl,
            "REST",
            GetLinklyCloudRestBaseUrl(CardTerminalEnvironment.Production),
            GetLinklyCloudRestBaseUrl(CardTerminalEnvironment.Sandbox));
    }

    private static string? ValidateLinklyCloudBaseUrl(
        CardTerminalEnvironment environment,
        string baseUrl,
        string endpointName,
        string productionBaseUrl,
        string sandboxBaseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var configuredUri))
        {
            return $"Linkly Cloud {endpointName} endpoint is not a valid absolute URL. Update the configured host and try again.";
        }

        var productionHost = new Uri(productionBaseUrl).Host;
        var sandboxHost = new Uri(sandboxBaseUrl).Host;

        // 仅拦截官方生产/沙箱 host 的环境错配，自定义代理域名保持兼容。
        if (environment == CardTerminalEnvironment.Production &&
            string.Equals(configuredUri.Host, sandboxHost, StringComparison.OrdinalIgnoreCase))
        {
            return $"Linkly Cloud {endpointName} endpoint does not match the selected Production environment. Update the configured host and try again.";
        }

        if (environment == CardTerminalEnvironment.Sandbox &&
            string.Equals(configuredUri.Host, productionHost, StringComparison.OrdinalIgnoreCase))
        {
            return $"Linkly Cloud {endpointName} endpoint does not match the selected Sandbox environment. Update the configured host and try again.";
        }

        return null;
    }

    private static string ReadText(string key, string fallback)
    {
        var value = System.Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeLinklyConnectionModeKey(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Concat(value.Trim().Where(char.IsLetterOrDigit)).ToUpperInvariant();
    }
}

public sealed record LinklyCloudCredentialSettings(
    string? Username,
    string? Password,
    bool HasProtectedPassword);

public interface ICardTerminalSettingsProvider
{
    Task<CardTerminalSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
}

public interface ISquareAccessTokenProvider
{
    Task<string?> GetSquareAccessTokenAsync(
        CardTerminalEnvironment environment,
        bool forceRefresh,
        CancellationToken cancellationToken = default);
}

public interface ISquareTokenResolver : ISquareAccessTokenProvider
{
    Task<string?> GetTokenAsync(
        CardTerminalEnvironment environment,
        CancellationToken cancellationToken = default);

    Task<string?> RefreshTokenAsync(
        CardTerminalEnvironment environment,
        CancellationToken cancellationToken = default);
}

public interface ILinklyCloudSecretStore
{
    Task<string?> GetLinklyCloudSecretAsync(
        CardTerminalEnvironment environment,
        CancellationToken cancellationToken = default);

    Task SaveLinklyCloudSecretAsync(
        CardTerminalEnvironment environment,
        string secret,
        CancellationToken cancellationToken = default);

    Task<string> GetOrCreateLinklyCloudPosIdAsync(
        CardTerminalEnvironment environment,
        string storeCode,
        string deviceCode,
        CancellationToken cancellationToken = default);

    Task<LinklyCloudCredentialSettings> GetLinklyCloudCredentialAsync(
        CardTerminalEnvironment environment,
        CancellationToken cancellationToken = default);

    Task SaveLinklyCloudCredentialAsync(
        CardTerminalEnvironment environment,
        string username,
        string password,
        CancellationToken cancellationToken = default);
}

public interface ICardTerminalSettingsStore : ICardTerminalSettingsProvider, ISquareTokenResolver, ILinklyCloudSecretStore
{
    Task<CardTerminalConfiguration> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        CardTerminalConfiguration configuration,
        string? squareAccessToken,
        CancellationToken cancellationToken = default);

    Task<string?> GetSquareAccessTokenAsync(CancellationToken cancellationToken = default);
}

public sealed class StaticCardTerminalSettingsProvider(CardTerminalSettings settings) : ICardTerminalSettingsProvider
{
    public Task<CardTerminalSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(settings);
    }
}

public sealed class ConfiguredCardTerminalClient : ICardTerminalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan SquarePollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SquareCleanupTimeout = TimeSpan.FromSeconds(5);
    private const string SquareTimedOutStatusKey = "payment.card.squareTimedOut";
    private const string SquareTerminalOfflineStatusKey = "payment.card.squareTerminalOffline";
    private const string SquareTerminalNotPickedUpStatusKey = "payment.card.squareTerminalNotPickedUp";
    private const string SquareTimedOutMessage = "Square checkout timed out before the customer completed payment.";
    private const string SquareTerminalOfflineMessage = "Square terminal is offline. Check the terminal network and try again.";
    private const string SquareTerminalNotPickedUpMessage = "Square terminal did not pick up the checkout. Check that the terminal is online, then try again.";

    private readonly ICardTerminalSettingsProvider _settingsProvider;
    private readonly HttpClient _httpClient;
    private readonly ILinklyTerminalClient? _linklyTerminalClient;
    private readonly ILocalizationService? _localization;
    private readonly ISquarePaymentAttemptContextAccessor? _squarePaymentAttemptContextAccessor;
    private readonly ILocalSquarePaymentAttemptRepository? _squarePaymentAttemptRepository;
    private readonly ILinklyPaymentAttemptContextAccessor? _linklyPaymentAttemptContextAccessor;
    private readonly ConcurrentDictionary<SquareRefundAttemptKey, string> _squareRefundIdempotencyKeys = new();

    public ConfiguredCardTerminalClient(
        ICardTerminalSettingsProvider settingsProvider,
        HttpClient httpClient,
        ILinklyTerminalClient? linklyTerminalClient = null,
        ISquareAccessTokenProvider? squareAccessTokenProvider = null,
        ILocalizationService? localization = null,
        ISquarePaymentAttemptContextAccessor? squarePaymentAttemptContextAccessor = null,
        ILocalSquarePaymentAttemptRepository? squarePaymentAttemptRepository = null,
        ILinklyPaymentAttemptContextAccessor? linklyPaymentAttemptContextAccessor = null)
    {
        _settingsProvider = settingsProvider;
        _httpClient = httpClient;
        _linklyTerminalClient = linklyTerminalClient;
        _localization = localization;
        _squarePaymentAttemptContextAccessor = squarePaymentAttemptContextAccessor;
        _squarePaymentAttemptRepository = squarePaymentAttemptRepository;
        _linklyPaymentAttemptContextAccessor = linklyPaymentAttemptContextAccessor;
    }

    public async Task<PaymentAuthorizationResult> AuthorizeAsync(
        decimal amount,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0m)
        {
            return new PaymentAuthorizationResult(false, null, T("payment.card.amountMustBePositive", "Card amount must be greater than zero."));
        }

        var settings = await _settingsProvider.GetSettingsAsync(cancellationToken);
        return settings.Processor switch
        {
            CardProcessorKind.Linkly => _linklyTerminalClient is null
                ? new PaymentAuthorizationResult(false, null, T("payment.card.linklyUnavailable", "ANZ Linkly terminal adapter is unavailable."))
                : await AuthorizeLinklyAsync(amount, session, settings, cancellationToken),
            CardProcessorKind.Square => await AuthorizeSquareAsync(settings, amount, session, cancellationToken),
            _ => new PaymentAuthorizationResult(false, null, T("payment.card.status.notConfigured", "Card terminal is not configured."))
        };
    }

    private Task<PaymentAuthorizationResult> AuthorizeLinklyAsync(
        decimal amount,
        PosSessionState session,
        CardTerminalSettings settings,
        CancellationToken cancellationToken)
    {
        var mode = CardTerminalSettings.NormalizeLinklyConnectionMode(settings.LinklyConnectionMode);
        var txnRef = mode == LinklyConnectionMode.LocalIp
            ? _linklyPaymentAttemptContextAccessor?.Current?.TxnRef
            : null;

        // LocalIp 断电恢复必须用付款前已经落库的 TxnRef，否则重启 GetLast 会查错交易。
        return !string.IsNullOrWhiteSpace(txnRef)
            ? _linklyTerminalClient!.PurchaseWithReferenceAsync(amount, session, settings, txnRef, cancellationToken)
            : _linklyTerminalClient!.PurchaseAsync(amount, session, settings, cancellationToken);
    }

    public async Task<PaymentAuthorizationResult> RefundAsync(
        decimal amount,
        PosSessionState session,
        string? originalReference,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0m)
        {
            return new PaymentAuthorizationResult(false, null, T("payment.card.amountMustBePositive", "Card amount must be greater than zero."));
        }

        var settings = await _settingsProvider.GetSettingsAsync(cancellationToken);
        return settings.Processor switch
        {
            CardProcessorKind.Linkly => _linklyTerminalClient is null
                ? new PaymentAuthorizationResult(false, null, T("payment.card.linklyUnavailable", "ANZ Linkly terminal adapter is unavailable."))
                : await _linklyTerminalClient.RefundAsync(amount, session, settings, originalReference, cancellationToken),
            CardProcessorKind.Square => await RefundSquareAsync(settings, amount, originalReference, cancellationToken),
            _ => new PaymentAuthorizationResult(false, null, T("payment.card.status.notConfigured", "Card terminal is not configured."))
        };
    }

    private async Task<PaymentAuthorizationResult> AuthorizeSquareAsync(
        CardTerminalSettings settings,
        decimal amount,
        PosSessionState session,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.SquareLocationId) ||
            string.IsNullOrWhiteSpace(settings.SquareDeviceId))
        {
            return new PaymentAuthorizationResult(false, null, T("payment.card.squareConfigIncomplete", "Square terminal configuration is incomplete."));
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(settings.TerminalTimeout);

        var checkoutDeviceId = SquareDeviceIdNormalizer.NormalizeForTerminalCheckout(settings.SquareDeviceId);
        if (string.IsNullOrWhiteSpace(checkoutDeviceId))
        {
            return new PaymentAuthorizationResult(false, null, T("payment.card.squareConfigIncomplete", "Square terminal configuration is incomplete."));
        }

        string? checkoutId = null;
        var pollCount = 0;
        string? lastLoggedStatus = null;
        var sawSquareInProgress = false;
        var sawSquareCancelRequested = false;
        var reference = Limit($"{session.DeviceCode}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}", 40);
        const string squareCurrency = "AUD";
        var requestedMinorAmount = ToMinorUnits(amount);
        var squareAttempt = _squarePaymentAttemptContextAccessor?.Current;
        var createRequest = new SquareCreateCheckoutRequest(
            settings.Environment.ToString(),
            squareAttempt?.IdempotencyKey ?? Guid.NewGuid().ToString("N"),
            checkoutDeviceId,
            settings.SquareLocationId!,
            new SquareMoneyDto(requestedMinorAmount, squareCurrency),
            ReferenceId: reference,
            Note: Limit($"HBPOS {session.StoreCode} {session.DeviceCode}", 500));

        LogSquare(
            $"authorize start storeCode={session.StoreCode} deviceCode={session.DeviceCode} amount={amount:0.00} environment={settings.Environment} locationId={LogValue(settings.SquareLocationId)} storedSquareDeviceId={LogValue(settings.SquareDeviceId)} checkoutDeviceId={LogValue(checkoutDeviceId)} timeoutSeconds={(int)settings.TerminalTimeout.TotalSeconds}");

        try
        {
            // Square 主付款链路统一改走 Hbpos API，终端侧只保留本地 attempt、轮询与校验语义。
            using var createResponse = await SendSquareApiAsync(
                HttpMethod.Post,
                "api/v1/square/checkouts",
                createRequest,
                timeoutCts.Token);
            var createBody = await ReadResponseBodyAsync(createResponse, timeoutCts.Token);
            if (!createResponse.IsSuccessStatusCode)
            {
                LogSquare($"checkout create failed http={(int)createResponse.StatusCode} detail={LogValue(ReadSquareErrorMessage(createBody))}");
                return FailSquareRequest("checkout", createResponse.StatusCode, createBody);
            }

            var createdCheckout = ReadSquareCheckoutStatusResponse(createBody);
            checkoutId = createdCheckout.CheckoutId;
            lastLoggedStatus = createdCheckout.Status;
            if (string.IsNullOrWhiteSpace(checkoutId))
            {
                LogSquare("checkout create returned empty checkout id");
                return new PaymentAuthorizationResult(false, null, T("payment.card.squareMissingCheckoutId", "Square checkout did not return an id."));
            }

            if (squareAttempt is not null && _squarePaymentAttemptRepository is not null)
            {
                await _squarePaymentAttemptRepository.MarkCheckoutCreatedAsync(
                    squareAttempt.AttemptGuid,
                    checkoutId,
                    lastLoggedStatus,
                    DateTimeOffset.UtcNow,
                    timeoutCts.Token);
            }

            LogSquare($"checkout create succeeded checkoutId={checkoutId} status={LogValue(lastLoggedStatus)}");

            while (true)
            {
                timeoutCts.Token.ThrowIfCancellationRequested();
                pollCount++;
                using var getResponse = await SendSquareApiAsync(
                    HttpMethod.Get,
                    $"api/v1/square/checkouts/{Uri.EscapeDataString(checkoutId)}?environment={Uri.EscapeDataString(settings.Environment.ToString())}",
                    body: null,
                    timeoutCts.Token);
                var getBody = await ReadResponseBodyAsync(getResponse, timeoutCts.Token);
                if (!getResponse.IsSuccessStatusCode)
                {
                    LogSquare($"checkout poll failed checkoutId={checkoutId} poll={pollCount} http={(int)getResponse.StatusCode} detail={LogValue(ReadSquareErrorMessage(getBody))}");
                    return FailSquareRequest("checkout status", getResponse.StatusCode, getBody);
                }

                var currentCheckout = ReadSquareCheckoutStatusResponse(getBody);
                var status = currentCheckout.Status ?? string.Empty;
                if (string.Equals(status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
                {
                    sawSquareInProgress = true;
                }

                if (string.Equals(status, "CANCEL_REQUESTED", StringComparison.OrdinalIgnoreCase))
                {
                    sawSquareCancelRequested = true;
                }

                if (!string.Equals(lastLoggedStatus, status, StringComparison.OrdinalIgnoreCase))
                {
                    LogSquare($"checkout status checkoutId={checkoutId} poll={pollCount} status={status}");
                    lastLoggedStatus = status;
                }

                if (string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                {
                    // checkout 完成只表示终端流程已结束，仍要读取 payment 详情再确认实际收款状态。
                    var paymentId = currentCheckout.Payment?.PaymentId ??
                        currentCheckout.PaymentIds?.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
                    if (string.IsNullOrWhiteSpace(paymentId))
                    {
                        LogSquare($"checkout completed without payment id checkoutId={checkoutId}");
                        await MarkSquareAttemptFailureAsync(
                            squareAttempt,
                            LocalSquarePaymentAttemptStatus.Unknown,
                            status,
                            paymentStatus: null,
                            responseCode: null,
                            responseText: "Square checkout did not return a payment id.",
                            timeoutCts.Token);
                        return new PaymentAuthorizationResult(false, null, T("payment.card.squareMissingPaymentId", "Square checkout did not return a payment id."));
                    }

                    using var paymentResponse = await SendSquareApiAsync(
                        HttpMethod.Get,
                        $"api/v1/square/payments/{Uri.EscapeDataString(paymentId)}?environment={Uri.EscapeDataString(settings.Environment.ToString())}",
                        body: null,
                        timeoutCts.Token);
                    var paymentBody = await ReadResponseBodyAsync(paymentResponse, timeoutCts.Token);
                    if (!paymentResponse.IsSuccessStatusCode)
                    {
                        LogSquare($"payment lookup failed checkoutId={checkoutId} paymentId={paymentId} http={(int)paymentResponse.StatusCode} detail={LogValue(ReadSquareErrorMessage(paymentBody))}");
                        await MarkSquareAttemptFailureAsync(
                            squareAttempt,
                            LocalSquarePaymentAttemptStatus.Unknown,
                            status,
                            paymentStatus: null,
                            responseCode: ((int)paymentResponse.StatusCode).ToString(CultureInfo.InvariantCulture),
                            responseText: ReadSquareErrorMessage(paymentBody),
                            timeoutCts.Token);
                        return FailSquareRequest("payment", paymentResponse.StatusCode, paymentBody);
                    }

                    var payment = ReadSquarePaymentStatusResponse(paymentBody);
                    var paymentStatus = payment.Status ?? string.Empty;
                    var amountMoney = payment.ApprovedMoney ?? payment.TotalMoney;
                    if (amountMoney is null || string.IsNullOrWhiteSpace(amountMoney.Currency))
                    {
                        LogSquare($"payment missing amount checkoutId={checkoutId} paymentId={paymentId}");
                        await MarkSquareAttemptFailureAsync(
                            squareAttempt,
                            LocalSquarePaymentAttemptStatus.Unknown,
                            status,
                            paymentStatus,
                            responseCode: null,
                            responseText: "Square payment is missing amount_money.",
                            timeoutCts.Token);
                        return new PaymentAuthorizationResult(false, null, T("payment.card.squareInvalidResponse", "Square terminal returned an invalid response."));
                    }

                    var verification = SquarePaymentVerifier.Verify(
                        paymentStatus,
                        amountMoney.Amount,
                        amountMoney.Currency,
                        requestedMinorAmount,
                        squareCurrency);
                    if (!verification.Verified)
                    {
                        LogSquare($"payment verification failed checkoutId={checkoutId} paymentId={paymentId} reason={verification.Failure} status={paymentStatus} requestedMinor={requestedMinorAmount} paymentMinor={amountMoney.Amount} requestedCurrency={squareCurrency} paymentCurrency={LogValue(amountMoney.Currency)}");
                        await MarkSquareAttemptFailureAsync(
                            squareAttempt,
                            MapSquarePaymentFailureStatus(paymentStatus, verification.Failure),
                            status,
                            paymentStatus,
                            responseCode: null,
                            responseText: verification.Message,
                            timeoutCts.Token);
                        return verification.Failure switch
                        {
                            SquarePaymentVerificationFailure.Status => new PaymentAuthorizationResult(
                                false,
                                null,
                                string.Format(
                                    _localization?.CurrentCulture ?? CultureInfo.CurrentCulture,
                                    T("payment.card.squarePaymentStatus", "Square payment status is {0}."),
                                    paymentStatus)),
                            SquarePaymentVerificationFailure.Amount => new PaymentAuthorizationResult(false, null, T("payment.card.squarePaymentAmountMismatch", "Square payment amount did not match the requested amount.")),
                            SquarePaymentVerificationFailure.Currency => new PaymentAuthorizationResult(false, null, T("payment.card.squarePaymentCurrencyMismatch", "Square payment currency did not match the requested currency.")),
                            _ => new PaymentAuthorizationResult(false, null, T("payment.card.squareInvalidResponse", "Square terminal returned an invalid response."))
                        };
                    }

                    var authorizedAmount = amountMoney.Amount / 100m;
                    if (squareAttempt is not null && _squarePaymentAttemptRepository is not null)
                    {
                        await _squarePaymentAttemptRepository.MarkPaymentVerifiedAsync(
                            squareAttempt.AttemptGuid,
                            paymentId,
                            paymentStatus,
                            responseCode: null,
                            responseText: "Payment verified.",
                            DateTimeOffset.UtcNow,
                            timeoutCts.Token);
                    }

                    LogSquare($"checkout completed checkoutId={checkoutId} paymentId={paymentId} amount={authorizedAmount:0.00}");
                    LogSquare($"payment verified checkoutId={checkoutId} paymentId={paymentId} status={paymentStatus} amount={authorizedAmount:0.00} currency={amountMoney.Currency}");
                    return new PaymentAuthorizationResult(
                        true,
                        $"SQ:{paymentId}",
                        "Square",
                        authorizedAmount,
                        [
                            new CardTransactionDto(
                                "Square",
                                paymentId,
                                payment.AuthCode,
                                payment.CardBrand,
                                null,
                                payment.MaskedCardNumber,
                                null,
                                null,
                                paymentStatus,
                                null,
                                DateTimeOffset.UtcNow,
                                authorizedAmount,
                                null)
                        ]);
                }

                if (string.Equals(status, "CANCELED", StringComparison.OrdinalIgnoreCase))
                {
                    var cancelOutcome = MapSquareCanceledCheckout(
                        currentCheckout.CancelReason,
                        currentCheckout.DeviceId ?? checkoutDeviceId,
                        sawSquareInProgress,
                        sawSquareCancelRequested);
                    LogSquare($"checkout canceled checkoutId={checkoutId} poll={pollCount} cancelReason={LogValue(currentCheckout.CancelReason)} mappedStatusKey={cancelOutcome.StatusKey}");
                    await MarkSquareAttemptFailureAsync(
                        squareAttempt,
                        cancelOutcome.AttemptStatus,
                        status,
                        paymentStatus: null,
                        responseCode: null,
                        responseText: cancelOutcome.Message,
                        timeoutCts.Token,
                        cancelReason: currentCheckout.CancelReason);
                    return new PaymentAuthorizationResult(
                        false,
                        null,
                        cancelOutcome.Message,
                        StatusKey: cancelOutcome.StatusKey);
                }

                if (!IsSquarePendingStatus(status))
                {
                    LogSquare($"checkout entered unexpected status checkoutId={checkoutId} poll={pollCount} status={status}");
                    await MarkSquareAttemptFailureAsync(
                        squareAttempt,
                        LocalSquarePaymentAttemptStatus.Unknown,
                        status,
                        paymentStatus: null,
                        responseCode: null,
                        responseText: $"Square checkout entered unexpected status '{status}'.",
                        timeoutCts.Token);
                    return new PaymentAuthorizationResult(
                        false,
                        null,
                        string.Format(
                            _localization?.CurrentCulture ?? CultureInfo.CurrentCulture,
                            T("payment.card.squareUnexpectedStatus", "Square checkout entered unexpected status '{0}'."),
                            status));
                }

                await Task.Delay(SquarePollInterval, timeoutCts.Token);
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            var wasCallerCancelled = cancellationToken.IsCancellationRequested;
            var cancellationReason = wasCallerCancelled ? "caller-cancelled" : "local-timeout";
            LogSquare($"authorize canceled checkoutId={LogValue(checkoutId)} reason={cancellationReason}; starting cleanup");
            await CleanupSquareCheckoutBestEffortAsync(
                settings,
                checkoutId,
                allowRefresh: true);
            if (wasCallerCancelled)
            {
                throw;
            }

            // 本地轮询超时时，是否见过 IN_PROGRESS 用来区分顾客未完成与终端未接单。
            var timeoutOutcome = MapSquareLocalTimeout(sawSquareInProgress);
            await MarkSquareAttemptFailureAsync(
                squareAttempt,
                timeoutOutcome.AttemptStatus,
                lastLoggedStatus,
                paymentStatus: null,
                responseCode: null,
                responseText: timeoutOutcome.Message,
                CancellationToken.None);
            return new PaymentAuthorizationResult(
                false,
                null,
                timeoutOutcome.Message,
                StatusKey: timeoutOutcome.StatusKey);
        }
        catch (HttpRequestException ex)
        {
            LogSquare($"authorize network failure checkoutId={LogValue(checkoutId)} message={LogValue(ex.Message)}");
            await MarkSquareAttemptFailureAsync(
                squareAttempt,
                LocalSquarePaymentAttemptStatus.Unknown,
                checkoutStatus: null,
                paymentStatus: null,
                responseCode: null,
                responseText: ex.Message,
                CancellationToken.None);
            return new PaymentAuthorizationResult(false, null, T("payment.card.squareCommunicationFailed", "Square terminal communication failed."));
        }
        catch (JsonException ex)
        {
            LogSquare($"authorize invalid response checkoutId={LogValue(checkoutId)} message={LogValue(ex.Message)}");
            await MarkSquareAttemptFailureAsync(
                squareAttempt,
                LocalSquarePaymentAttemptStatus.Unknown,
                checkoutStatus: null,
                paymentStatus: null,
                responseCode: null,
                responseText: ex.Message,
                CancellationToken.None);
            return new PaymentAuthorizationResult(false, null, T("payment.card.squareInvalidResponse", "Square terminal returned an invalid response."));
        }
    }

    private async Task MarkSquareAttemptFailureAsync(
        SquarePaymentAttemptContext? squareAttempt,
        LocalSquarePaymentAttemptStatus status,
        string? checkoutStatus,
        string? paymentStatus,
        string? responseCode,
        string? responseText,
        CancellationToken cancellationToken,
        string? cancelReason = null)
    {
        if (squareAttempt is null || _squarePaymentAttemptRepository is null)
        {
            return;
        }

        // 取消原因和失败结果必须同一次写入，避免两次 UPDATE 中断时留下半成品记录。
        await _squarePaymentAttemptRepository.MarkFailedAsync(
            squareAttempt.AttemptGuid,
            status,
            checkoutStatus,
            paymentStatus,
            responseCode,
            responseText,
            DateTimeOffset.UtcNow,
            cancellationToken,
            cancelReason);
    }

    private sealed record SquareCanceledCheckoutOutcome(
        string StatusKey,
        string Message,
        LocalSquarePaymentAttemptStatus AttemptStatus);

    private SquareCanceledCheckoutOutcome MapSquareCanceledCheckout(
        string? cancelReason,
        string? checkoutDeviceId,
        bool sawSquareInProgress,
        bool sawSquareCancelRequested)
    {
        var normalizedReason = cancelReason?.Trim();
        if (string.Equals(normalizedReason, "BUYER_CANCELED", StringComparison.OrdinalIgnoreCase))
        {
            return new SquareCanceledCheckoutOutcome(
                "payment.card.squareCanceledBuyer",
                T("payment.card.squareCanceledBuyer", "Square checkout was canceled by the buyer. Ask the customer to try again or choose another payment method."),
                LocalSquarePaymentAttemptStatus.Canceled);
        }

        if (string.Equals(normalizedReason, "SELLER_CANCELED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedReason, "CANCEL_REQUESTED", StringComparison.OrdinalIgnoreCase) ||
            sawSquareCancelRequested)
        {
            return new SquareCanceledCheckoutOutcome(
                "payment.card.squareCanceledSeller",
                T("payment.card.squareCanceledSeller", "Square checkout was canceled. Please start the card payment again."),
                LocalSquarePaymentAttemptStatus.Canceled);
        }

        if (string.Equals(normalizedReason, "TIMED_OUT", StringComparison.OrdinalIgnoreCase))
        {
            // TIMED_OUT 是否见过 IN_PROGRESS 是收银端区分“顾客没完成”和“终端没接单”的关键依据。
            // Sandbox 官方超时设备会立即返回 TIMED_OUT，不一定先进入 IN_PROGRESS，必须保留为 Square 超时。
            if (SquareSandboxTerminalDeviceIds.AreSameDeviceId(
                    checkoutDeviceId,
                    SquareSandboxTerminalDeviceIds.SquareTimedOut))
            {
                return new SquareCanceledCheckoutOutcome(
                    SquareTimedOutStatusKey,
                    T(SquareTimedOutStatusKey, SquareTimedOutMessage),
                    LocalSquarePaymentAttemptStatus.TimedOut);
            }

            return sawSquareInProgress
                ? new SquareCanceledCheckoutOutcome(
                    SquareTimedOutStatusKey,
                    T(SquareTimedOutStatusKey, SquareTimedOutMessage),
                    LocalSquarePaymentAttemptStatus.TimedOut)
                : new SquareCanceledCheckoutOutcome(
                    SquareTerminalNotPickedUpStatusKey,
                    T(SquareTerminalNotPickedUpStatusKey, SquareTerminalNotPickedUpMessage),
                    LocalSquarePaymentAttemptStatus.TimedOut);
        }

        return new SquareCanceledCheckoutOutcome(
            "payment.card.squareCanceled",
            T("payment.card.squareCanceled", "Square checkout was not completed. Please try again."),
            LocalSquarePaymentAttemptStatus.Canceled);
    }

    private SquareCanceledCheckoutOutcome MapSquareLocalTimeout(bool sawSquareInProgress)
    {
        return sawSquareInProgress
            ? new SquareCanceledCheckoutOutcome(
                SquareTimedOutStatusKey,
                T(SquareTimedOutStatusKey, SquareTimedOutMessage),
                LocalSquarePaymentAttemptStatus.TimedOut)
            : new SquareCanceledCheckoutOutcome(
                SquareTerminalNotPickedUpStatusKey,
                T(SquareTerminalNotPickedUpStatusKey, SquareTerminalNotPickedUpMessage),
                LocalSquarePaymentAttemptStatus.TimedOut);
    }

    private static LocalSquarePaymentAttemptStatus MapSquarePaymentFailureStatus(
        string paymentStatus,
        SquarePaymentVerificationFailure failure)
    {
        if (failure != SquarePaymentVerificationFailure.Status)
        {
            return LocalSquarePaymentAttemptStatus.Unknown;
        }

        return string.Equals(paymentStatus, "CANCELED", StringComparison.OrdinalIgnoreCase)
            ? LocalSquarePaymentAttemptStatus.Canceled
            : string.Equals(paymentStatus, "FAILED", StringComparison.OrdinalIgnoreCase)
                ? LocalSquarePaymentAttemptStatus.Failed
                : LocalSquarePaymentAttemptStatus.Unknown;
    }

    private async Task<PaymentAuthorizationResult> RefundSquareAsync(
        CardTerminalSettings settings,
        decimal amount,
        string? originalReference,
        CancellationToken cancellationToken)
    {
        var paymentId = TryParseSquarePaymentId(originalReference);
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            return new PaymentAuthorizationResult(false, null, T("payment.card.squareRefundMissingReference", "Square refund requires an original Square payment reference."));
        }

        try
        {
            var minorAmount = ToMinorUnits(amount);
            var refundAttemptKey = new SquareRefundAttemptKey(settings.Environment, paymentId, minorAmount);
            var request = new SquareRefundRequest(
                settings.Environment.ToString(),
                _squareRefundIdempotencyKeys.GetOrAdd(refundAttemptKey, _ => Guid.NewGuid().ToString("N")),
                paymentId,
                new SquareMoneyDto(minorAmount, "AUD"));

            using var response = await SendSquareApiAsync(
                HttpMethod.Post,
                "api/v1/square/refunds",
                request,
                cancellationToken);
            var body = await ReadResponseBodyAsync(response, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // 后端 502/超时前 Square 可能已经受理退款；结果未知时必须保留同一个幂等键供下次重试。
                return FailSquareRequest("refund", response.StatusCode, body);
            }

            var refund = ReadSquareRefundResponse(body);
            var refundId = refund.RefundId;
            var status = refund.Status ?? string.Empty;
            if (string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "REJECTED", StringComparison.OrdinalIgnoreCase))
            {
                _squareRefundIdempotencyKeys.TryRemove(refundAttemptKey, out _);
                return new PaymentAuthorizationResult(
                    false,
                    $"SQRF:{refundId}",
                    string.Format(
                        _localization?.CurrentCulture ?? CultureInfo.CurrentCulture,
                        T("payment.card.squareRefundStatus", "Square refund status is {0}."),
                        status));
            }

            _squareRefundIdempotencyKeys.TryRemove(refundAttemptKey, out _);
            return new PaymentAuthorizationResult(
                true,
                $"SQRF:{refundId}",
                status,
                amount,
                [
                    new CardTransactionDto(
                        "Square",
                        refundId,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        status,
                        null,
                        DateTimeOffset.UtcNow,
                        amount,
                        null)
                ]);
        }
        catch (HttpRequestException)
        {
            return new PaymentAuthorizationResult(false, null, T("payment.card.squareCommunicationFailed", "Square terminal communication failed."));
        }
        catch (JsonException)
        {
            return new PaymentAuthorizationResult(false, null, T("payment.card.squareInvalidResponse", "Square terminal returned an invalid response."));
        }
    }

    private string T(string key, string fallback)
    {
        return _localization?.T(key) ?? fallback;
    }

    private async Task<HttpResponseMessage> SendSquareApiAsync(
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private static async Task<string> ReadResponseBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        return response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task CleanupSquareCheckoutBestEffortAsync(
        CardTerminalSettings settings,
        string? checkoutId,
        bool allowRefresh)
    {
        if (string.IsNullOrWhiteSpace(checkoutId))
        {
            LogSquare("cleanup skipped because checkout id is empty");
            return;
        }

        LogSquare($"cleanup start checkoutId={checkoutId} allowRefresh={allowRefresh}");
        using var cleanupCts = new CancellationTokenSource(SquareCleanupTimeout);
        var cancelResult = await CancelSquareCheckoutBestEffortAsync(
            settings,
            checkoutId,
            allowRefresh,
            cleanupCts.Token);
        if (cancelResult.ShouldDismiss)
        {
            LogSquare($"cleanup dismiss required checkoutId={checkoutId}");
            await DismissSquareCheckoutBestEffortAsync(
                settings,
                checkoutId,
                allowRefresh,
                cleanupCts.Token);
        }
        else
        {
            LogSquare($"cleanup finished without dismiss checkoutId={checkoutId}");
        }
    }

    private async Task<SquareCleanupResult> CancelSquareCheckoutBestEffortAsync(
        CardTerminalSettings settings,
        string checkoutId,
        bool allowRefresh,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendSquareApiAsync(
                HttpMethod.Post,
                $"api/v1/square/checkouts/{Uri.EscapeDataString(checkoutId)}/cancel",
                new SquareCheckoutActionRequest(settings.Environment.ToString()),
                cancellationToken);
            var body = await ReadResponseBodyAsync(response, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogSquare($"checkout cancel failed checkoutId={checkoutId} http={(int)response.StatusCode} detail={LogValue(ReadSquareErrorMessage(body))}");
                return new SquareCleanupResult(ShouldDismiss: true);
            }

            var status = ReadSquareCheckoutStatusSafely(body);
            LogSquare($"checkout cancel result checkoutId={checkoutId} status={LogValue(status)} shouldDismiss={!IsSquareTerminalStatusFinal(status)}");
            return new SquareCleanupResult(!IsSquareTerminalStatusFinal(status));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            LogSquare($"checkout cancel exception checkoutId={checkoutId} message={LogValue(ex.Message)}");
            return new SquareCleanupResult(ShouldDismiss: true);
        }
    }

    private async Task DismissSquareCheckoutBestEffortAsync(
        CardTerminalSettings settings,
        string checkoutId,
        bool allowRefresh,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendSquareApiAsync(
                HttpMethod.Post,
                $"api/v1/square/checkouts/{Uri.EscapeDataString(checkoutId)}/dismiss",
                new SquareCheckoutActionRequest(settings.Environment.ToString()),
                cancellationToken);
            _ = await ReadResponseBodyAsync(response, cancellationToken);
            LogSquare($"checkout dismiss result checkoutId={checkoutId} http={(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            LogSquare($"checkout dismiss exception checkoutId={checkoutId} message={LogValue(ex.Message)}");
        }
    }

    private PaymentAuthorizationResult FailSquareRequest(
        string operation,
        System.Net.HttpStatusCode statusCode,
        string? responseBody)
    {
        var detail = ReadSquareErrorMessage(responseBody);
        if (string.Equals(operation, "checkout", StringComparison.OrdinalIgnoreCase) &&
            IsSquareTerminalOfflineError(detail))
        {
            return new PaymentAuthorizationResult(
                false,
                null,
                T(SquareTerminalOfflineStatusKey, SquareTerminalOfflineMessage),
                StatusKey: SquareTerminalOfflineStatusKey);
        }

        var message = string.IsNullOrWhiteSpace(detail)
            ? $"Square {operation} failed with HTTP {(int)statusCode}."
            : $"Square {operation} failed with HTTP {(int)statusCode}: {detail}";
        return new PaymentAuthorizationResult(false, null, message);
    }

    private static bool IsSquareTerminalOfflineError(string? detail)
    {
        return !string.IsNullOrWhiteSpace(detail) &&
            detail.Contains("offline", StringComparison.OrdinalIgnoreCase) &&
            detail.Contains("device", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadSquareErrorMessage(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("message", out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.String)
            {
                var message = messageElement.GetString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }

            if (!document.RootElement.TryGetProperty("errors", out var errorsElement) ||
                errorsElement.ValueKind != JsonValueKind.Array ||
                errorsElement.GetArrayLength() == 0)
            {
                return null;
            }

            var error = errorsElement[0];
            var code = error.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.String
                ? codeElement.GetString()
                : null;
            var detail = error.TryGetProperty("detail", out var detailElement) && detailElement.ValueKind == JsonValueKind.String
                ? detailElement.GetString()
                : null;

            return string.IsNullOrWhiteSpace(code)
                ? detail
                : string.IsNullOrWhiteSpace(detail)
                    ? code
                    : $"{code}: {detail}";
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadSquareCheckoutStatusSafely(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            return ReadSquareCheckoutStatusResponse(responseBody).Status;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SquareCheckoutStatusResponse ReadSquareCheckoutStatusResponse(string responseBody)
    {
        if (TryReadApiResult<SquareCheckoutStatusResponse>(responseBody, out var apiCheckout) &&
            apiCheckout is not null)
        {
            return apiCheckout;
        }

        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("checkout", out var checkoutElement) || checkoutElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Square response is missing required object 'checkout'.");
        }

        var paymentIds = ReadPaymentIds(checkoutElement);
        var paymentId = paymentIds.FirstOrDefault();
        var amountMoney = TryReadMoneyDto(checkoutElement, "amount_money");
        return new SquareCheckoutStatusResponse(
            ReadRequiredString(checkoutElement, "id"),
            Environment: string.Empty,
            Status: ReadRequiredString(checkoutElement, "status"),
            DeviceId: ReadCheckoutDeviceId(checkoutElement),
            LocationId: null,
            AmountMoney: amountMoney,
            Payment: string.IsNullOrWhiteSpace(paymentId) ? null : new SquarePaymentStatusDto(paymentId),
            PaymentIds: paymentIds,
            CancelReason: ReadOptionalString(checkoutElement, "cancel_reason"),
            UpdatedAt: null);
    }

    private static string? ReadCheckoutDeviceId(JsonElement checkoutElement)
    {
        var directDeviceId = ReadOptionalString(checkoutElement, "device_id");
        if (!string.IsNullOrWhiteSpace(directDeviceId))
        {
            return directDeviceId;
        }

        return checkoutElement.TryGetProperty("device_options", out var deviceOptions) &&
            deviceOptions.ValueKind == JsonValueKind.Object
                ? ReadOptionalString(deviceOptions, "device_id")
                : null;
    }

    private static SquarePaymentStatusDto ReadSquarePaymentStatusResponse(string responseBody)
    {
        if (TryReadApiResult<SquarePaymentStatusDto>(responseBody, out var apiPayment) &&
            apiPayment is not null)
        {
            return apiPayment;
        }

        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("payment", out var paymentElement) || paymentElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Square response is missing required object 'payment'.");
        }

        var amountMoney = TryReadMoneyDto(paymentElement, "amount_money");
        return new SquarePaymentStatusDto(
            ReadRequiredString(paymentElement, "id"),
            Status: ReadRequiredString(paymentElement, "status"),
            ApprovedMoney: amountMoney,
            TotalMoney: amountMoney,
            UpdatedAt: null,
            CardBrand: NormalizeOptionalText(ReadOptionalNestedString(paymentElement, "card_details", "card", "card_brand")),
            // raw Square 响应只取 last_4 生成脱敏卡号；完整卡资料不能进入本地订单。
            MaskedCardNumber: FormatMaskedCardNumber(ReadOptionalNestedString(paymentElement, "card_details", "card", "last_4")),
            AuthCode: NormalizeOptionalText(ReadOptionalNestedString(paymentElement, "card_details", "auth_result_code")));
    }

    private static SquareRefundResponse ReadSquareRefundResponse(string responseBody)
    {
        if (TryReadApiResult<SquareRefundResponse>(responseBody, out var apiRefund) &&
            apiRefund is not null)
        {
            return apiRefund;
        }

        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("refund", out var refundElement) || refundElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Square response is missing required object 'refund'.");
        }

        return new SquareRefundResponse(
            ReadRequiredString(refundElement, "id"),
            Environment: string.Empty,
            Status: ReadRequiredString(refundElement, "status"),
            PaymentId: ReadOptionalString(refundElement, "payment_id"),
            AmountMoney: TryReadMoneyDto(refundElement, "amount_money"),
            UpdatedAt: null);
    }

    private static long ToMinorUnits(decimal amount)
    {
        return decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static SquareMoneyDto? TryReadMoneyDto(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var money) || money.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!money.TryGetProperty("amount", out var amountElement) || !amountElement.TryGetInt64(out var amount))
        {
            return null;
        }

        var currency = ReadOptionalString(money, "currency");
        return string.IsNullOrWhiteSpace(currency)
            ? null
            : new SquareMoneyDto(amount, currency);
    }

    private static bool TryReadApiResult<T>(string responseBody, out T? data)
    {
        data = default;

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("success", out _))
            {
                return false;
            }

            var result = JsonSerializer.Deserialize<ApiResult<T>>(responseBody, JsonOptions)
                ?? throw new JsonException("Square API result is null.");
            if (!result.Success || result.Data is null)
            {
                throw new JsonException("Square API result did not contain data.");
            }

            data = result.Data;
            return true;
        }
        catch (JsonException)
        {
            data = default;
            return false;
        }
    }

    private static IReadOnlyList<string> ReadPaymentIds(JsonElement checkout)
    {
        if (!checkout.TryGetProperty("payment_ids", out var paymentIds) ||
            paymentIds.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var paymentId in paymentIds.EnumerateArray())
        {
            if (paymentId.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = paymentId.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }

        return values;
    }

    private static string? TryParseSquarePaymentId(string? originalReference)
    {
        if (string.IsNullOrWhiteSpace(originalReference))
        {
            return null;
        }

        var trimmed = originalReference.Trim();
        return trimmed.StartsWith("SQ:", StringComparison.OrdinalIgnoreCase)
            ? trimmed[3..].Trim()
            : null;
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        var value = ReadOptionalString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException($"Square response is missing required property '{propertyName}'.");
        }

        return value;
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var propertyElement) &&
            propertyElement.ValueKind == JsonValueKind.String
                ? propertyElement.GetString()
                : null;
    }

    private static string? ReadOptionalNestedString(JsonElement element, params string[] propertyPath)
    {
        var current = element;
        foreach (var propertyName in propertyPath)
        {
            if (!current.TryGetProperty(propertyName, out current) ||
                current.ValueKind == JsonValueKind.Null)
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? FormatMaskedCardNumber(string? last4)
    {
        var normalized = NormalizeOptionalText(last4);
        if (normalized is null)
        {
            return null;
        }

        var digits = new string(normalized.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return null;
        }

        var lastFour = digits.Length > 4 ? digits[^4..] : digits;
        return $"****{lastFour}";
    }

    private static string Limit(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static bool IsSquarePendingStatus(string status)
    {
        return string.Equals(status, "PENDING", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "CANCEL_REQUESTED", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSquareTerminalStatusFinal(string? status)
    {
        return string.Equals(status, "CANCELED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase);
    }

    private static void LogSquare(string message)
    {
        ConsoleLog.Write("Square", message);
    }

    private static string LogValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<null>" : value;
    }

    private sealed record SquareRefundAttemptKey(
        CardTerminalEnvironment Environment,
        string PaymentId,
        long MinorAmount);

    private sealed record SquareCleanupResult(bool ShouldDismiss);
}
