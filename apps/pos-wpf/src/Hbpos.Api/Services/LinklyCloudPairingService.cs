using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hbpos.Api.Data;
using Hbpos.Contracts.Linkly;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Services;

public interface ILinklyCloudPairingService
{
    Task<LinklyCloudBackendTerminalCredentialResponse> PairAsync(
        string storeCode,
        string deviceCode,
        LinklyCloudBackendPairRequest request,
        string? updatedBy,
        CancellationToken cancellationToken);
}

public interface ILinklyCloudPairingTransport
{
    Task<LinklyCloudPairingTransportResponse> PairAsync(
        string authBaseUrl,
        string username,
        string password,
        string pairCode,
        CancellationToken cancellationToken);
}

public sealed record LinklyCloudPairingTransportResponse(
    HttpStatusCode StatusCode,
    string? Secret);

public sealed class LinklyCloudPairingValidationException(string message) : Exception(message);

public sealed class LinklyCloudPairingCredentialMissingException()
    : Exception("Linkly Cloud credential is not configured for this store and environment.");

public sealed class LinklyCloudPairingInProgressException()
    : Exception("Linkly Cloud pairing is already in progress for this terminal.");

public sealed class LinklyCloudPairingRejectedException()
    : Exception("Linkly Cloud rejected the pairing request.");

public sealed class LinklyCloudPairingUpstreamException()
    : Exception("Linkly Cloud pairing failed at the upstream service.");

public sealed class LinklyCloudPairingTimeoutException()
    : Exception("Linkly Cloud pairing timed out or has an uncertain result.");

public sealed class LinklyCloudPairingPersistenceException()
    : Exception("Linkly Cloud pairing succeeded but the terminal credential could not be saved.");

public sealed class LinklyCloudPairingPreparationException()
    : Exception("Linkly Cloud pairing could not load the required configuration.");

public sealed class LinklyCloudPairingService(
    ILinklyCloudCredentialRepository credentialRepository,
    ILinklyCloudBackendTerminalCredentialRepository terminalCredentialRepository,
    ILinklyCloudPairingTransport transport,
    IOptions<LinklyCloudBackendAsyncOptions> options,
    ILogger<LinklyCloudPairingService>? logger = null) : ILinklyCloudPairingService
{
    private static readonly ConcurrentDictionary<PairingScope, byte> InProgress = new();
    private static readonly TimeSpan PersistenceTimeout = TimeSpan.FromSeconds(15);
    // 上游 HTTP 超时、持久化窗口与一分钟收尾余量合计后取安全上界，避免 Pair Code 被并发重放。
    private static readonly TimeSpan LegacyPairingLeaseDuration = TimeSpan.FromSeconds(315);

    public async Task<LinklyCloudBackendTerminalCredentialResponse> PairAsync(
        string storeCode,
        string deviceCode,
        LinklyCloudBackendPairRequest request,
        string? updatedBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var environment = NormalizeEnvironment(request.Environment);
        var normalizedStoreCode = NormalizeRequired(storeCode, "storeCode");
        var normalizedDeviceCode = NormalizeRequired(deviceCode, "deviceCode");
        var pairCode = NormalizePairCode(request.PairCode);
        var scope = new PairingScope(
            environment.ToUpperInvariant(),
            normalizedStoreCode.ToUpperInvariant(),
            normalizedDeviceCode.ToUpperInvariant());

        // 配对是终端级单飞；已有请求直接失败，不能等待或重复消费短时 Pair Code。
        if (!InProgress.TryAdd(scope, 0))
        {
            throw new LinklyCloudPairingInProgressException();
        }

        try
        {
            var attemptId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            try
            {
                await terminalCredentialRepository.AcquireLegacyPairingLeaseAsync(
                    environment,
                    normalizedStoreCode,
                    attemptId,
                    now.Add(LegacyPairingLeaseDuration),
                    now,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 取消时无法证明数据库是否已落租约，按未知结果处理且绝不触发上游重放。
                throw new LinklyCloudPairingTimeoutException();
            }

            try
            {
                return await PairCoreAsync(
                    environment,
                    normalizedStoreCode,
                    normalizedDeviceCode,
                    pairCode,
                    attemptId,
                    updatedBy,
                    cancellationToken);
            }
            catch (LinklyCloudPairingCredentialMissingException)
            {
                await ReleaseExplicitFailureLeaseAsync(environment, normalizedStoreCode, attemptId);
                throw;
            }
            catch (LinklyCloudPairingPreparationException)
            {
                // 配置读取失败发生在上游调用之前，Pair Code 尚未消费，可按 attemptId 安全释放。
                await ReleaseExplicitFailureLeaseAsync(environment, normalizedStoreCode, attemptId);
                throw;
            }
            catch (LinklyCloudPairingRejectedException)
            {
                await ReleaseExplicitFailureLeaseAsync(environment, normalizedStoreCode, attemptId);
                throw;
            }
        }
        finally
        {
            InProgress.TryRemove(scope, out _);
        }
    }

    private async Task<LinklyCloudBackendTerminalCredentialResponse> PairCoreAsync(
        string environment,
        string storeCode,
        string deviceCode,
        string pairCode,
        Guid attemptId,
        string? updatedBy,
        CancellationToken cancellationToken)
    {
        LinklyCloudCredentialRecord? credential;
        LinklyCloudBackendTerminalCredentialRecord? existingTerminalCredential;
        try
        {
            credential = await credentialRepository.GetByStoreCodeAsync(
                storeCode,
                environment,
                cancellationToken);
            existingTerminalCredential = await terminalCredentialRepository.GetByDeviceAsync(
                environment,
                storeCode,
                deviceCode,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw new LinklyCloudPairingTimeoutException();
        }
        catch (Exception ex)
        {
            LogFailure("configuration-read", environment, storeCode, deviceCode, ex);
            throw new LinklyCloudPairingPreparationException();
        }

        if (credential is null ||
            string.IsNullOrWhiteSpace(credential.Username) ||
            string.IsNullOrWhiteSpace(credential.Password))
        {
            throw new LinklyCloudPairingCredentialMissingException();
        }

        var posId = IsUuidV4(existingTerminalCredential?.PosId)
            ? existingTerminalCredential!.PosId!.Trim()
            : Guid.NewGuid().ToString("D");
        var authBaseUrl = GetAuthBaseUrl(environment);
        LinklyCloudPairingTransportResponse upstreamResponse;
        try
        {
            upstreamResponse = await transport.PairAsync(
                authBaseUrl,
                credential.Username.Trim(),
                credential.Password.Trim(),
                pairCode,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 上游调用被取消时无法证明配对是否已完成，向客户端返回不确定结果。
            throw new LinklyCloudPairingTimeoutException();
        }
        catch (Exception ex)
        {
            LogFailure("upstream-call", environment, storeCode, deviceCode, ex);
            throw new LinklyCloudPairingUpstreamException();
        }

        if (upstreamResponse.StatusCode == HttpStatusCode.RequestTimeout)
        {
            // Linkly 408 不能证明终端是否已经消费 Pair Code，必须按不确定结果处理。
            throw new LinklyCloudPairingTimeoutException();
        }

        if ((int)upstreamResponse.StatusCode is >= 400 and < 500)
        {
            throw new LinklyCloudPairingRejectedException();
        }

        if (!((int)upstreamResponse.StatusCode is >= 200 and < 300) ||
            string.IsNullOrWhiteSpace(upstreamResponse.Secret))
        {
            throw new LinklyCloudPairingUpstreamException();
        }

        return await PersistTerminalCredentialAsync(
            environment,
            storeCode,
            deviceCode,
            attemptId,
            posId,
            upstreamResponse.Secret.Trim(),
            updatedBy);
    }

    private async Task<LinklyCloudBackendTerminalCredentialResponse> PersistTerminalCredentialAsync(
        string environment,
        string storeCode,
        string deviceCode,
        Guid attemptId,
        string posId,
        string secret,
        string? updatedBy)
    {
        var now = DateTime.UtcNow;
        using var persistenceTimeout = new CancellationTokenSource(PersistenceTimeout);
        try
        {
            // 上游已经成功后，不能再使用请求 token；客户端断开也必须给持久化一个有界机会。
            var saved = await terminalCredentialRepository.CompleteLegacyPairingAsync(
                environment,
                storeCode,
                deviceCode,
                attemptId,
                now,
                secret,
                posId,
                NormalizeOptional(updatedBy),
                persistenceTimeout.Token).WaitAsync(persistenceTimeout.Token);

            if (saved is null ||
                !string.Equals(saved.Environment?.Trim(), environment, StringComparison.Ordinal) ||
                !string.Equals(saved.StoreCode?.Trim(), storeCode, StringComparison.Ordinal) ||
                !string.Equals(saved.DeviceCode?.Trim(), deviceCode, StringComparison.Ordinal) ||
                !string.Equals(saved.Secret?.Trim(), secret, StringComparison.Ordinal) ||
                !string.Equals(saved.PosId?.Trim(), posId, StringComparison.Ordinal) ||
                !IsUuidV4(saved.PosId))
            {
                throw new InvalidOperationException("terminal credential read-back was incomplete");
            }

            return new LinklyCloudBackendTerminalCredentialResponse(
                environment,
                storeCode,
                deviceCode,
                true,
                saved.PosId!.Trim(),
                new DateTimeOffset(DateTime.SpecifyKind(saved.UpdatedAt ?? now, DateTimeKind.Utc)));
        }
        catch (LinklyCloudLegacyModeDisabledException)
        {
            // 完成 CAS 期间模式可能被切至 Active；必须保留专用 409，而非误报持久化失败。
            throw;
        }
        catch (Exception ex)
        {
            LogFailure("persistence", environment, storeCode, deviceCode, ex);
            throw new LinklyCloudPairingPersistenceException();
        }
    }

    private async Task ReleaseExplicitFailureLeaseAsync(
        string environment,
        string storeCode,
        Guid attemptId)
    {
        try
        {
            await terminalCredentialRepository.ReleaseLegacyPairingLeaseAsync(
                environment,
                storeCode,
                attemptId,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            // 显式失败优先释放；若释放本身失败不能误报为可重试，记录后保留原始结果。
            LogFailure("lease-release", environment, storeCode, "legacy", ex);
        }
    }

    private string GetAuthBaseUrl(string environment)
    {
        return string.Equals(environment, "Sandbox", StringComparison.Ordinal)
            ? options.Value.SandboxAuthBaseUrl
            : options.Value.ProductionAuthBaseUrl;
    }

    private static string NormalizeEnvironment(string? environment)
    {
        return LinklyCloudCredentialService.NormalizeEnvironment(environment)
            ?? throw new LinklyCloudPairingValidationException(
                "environment must be Production or Sandbox");
    }

    private static string NormalizeRequired(string? value, string fieldName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new LinklyCloudPairingValidationException($"{fieldName} is required.")
            : value.Trim();
    }

    private static string NormalizePairCode(string? pairCode)
    {
        var normalized = pairCode?.Trim();
        if (normalized is null || normalized.Length != 6 || normalized.Any(character => character is < '0' or > '9'))
        {
            throw new LinklyCloudPairingValidationException(
                "pairCode must contain exactly 6 digits.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsUuidV4(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 36 &&
            Guid.TryParse(trimmed, out _) &&
            trimmed[14] == '4' &&
            trimmed[19] is '8' or '9' or 'a' or 'A' or 'b' or 'B';
    }

    private void LogFailure(
        string phase,
        string environment,
        string storeCode,
        string deviceCode,
        Exception exception)
    {
        // 只记录范围、阶段和异常类型；Pair Code、账号、密码、secret 和上游 body 永不写日志。
        logger?.LogWarning(
            "Linkly Cloud pairing {Phase} failed environment={Environment} store={StoreCode} device={DeviceCode} error={ErrorType}",
            phase,
            environment,
            storeCode,
            deviceCode,
            exception.GetType().Name);
    }

    private readonly record struct PairingScope(
        string Environment,
        string StoreCode,
        string DeviceCode);
}

public sealed class HttpLinklyCloudPairingTransport(HttpClient httpClient) : ILinklyCloudPairingTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<LinklyCloudPairingTransportResponse> PairAsync(
        string authBaseUrl,
        string username,
        string password,
        string pairCode,
        CancellationToken cancellationToken)
    {
        var requestUri = new Uri(
            new Uri(NormalizeBaseUrl(authBaseUrl), UriKind.Absolute),
            "pairing/cloudpos");
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(
                new LinklyCloudPairingRequest(username, password, pairCode),
                options: JsonOptions)
        };

        // 只发一次上游请求；没有 retry handler，也不在这里重放 Pair Code。
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var secret = response.IsSuccessStatusCode ? ReadSecret(body) : null;
        return new LinklyCloudPairingTransportResponse(response.StatusCode, secret);
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var normalized = string.IsNullOrWhiteSpace(baseUrl)
            ? throw new InvalidOperationException("Linkly Cloud auth base URL is not configured.")
            : baseUrl.Trim();
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var baseUri) ||
            !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Linkly Cloud auth base URL must use HTTPS.");
        }

        return baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseUri.AbsoluteUri
            : baseUri.AbsoluteUri + "/";
    }

    private static string? ReadSecret(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "secret", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString()?.Trim();
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record LinklyCloudPairingRequest(
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("password")] string Password,
        [property: JsonPropertyName("pairCode")] string PairCode);
}
