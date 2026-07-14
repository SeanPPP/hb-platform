using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Hbpos.Contracts.EmergencyLogin;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hbpos.Client.Wpf.Services;

public sealed record EmergencyLoginPublicKeyFetchResult(
    bool NotModified,
    EmergencyLoginPublicKeyPackage? Package)
{
    public static EmergencyLoginPublicKeyFetchResult Unchanged() => new(true, null);

    public static EmergencyLoginPublicKeyFetchResult Changed(EmergencyLoginPublicKeyPackage package) =>
        new(false, package);
}

public interface IEmergencyLoginPublicKeyApiClient
{
    Task<EmergencyLoginPublicKeyFetchResult> GetAsync(
        long? currentVersion,
        CancellationToken cancellationToken = default);

    Task AcknowledgeAsync(long version, CancellationToken cancellationToken = default);
}

public sealed class EmergencyLoginPublicKeyApiClient(HttpClient httpClient)
    : IEmergencyLoginPublicKeyApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<EmergencyLoginPublicKeyFetchResult> GetAsync(
        long? currentVersion,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "api/v1/emergency-login/public-keys");
        if (currentVersion is not null)
        {
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(
                $"\"emergency-login-keys-v{currentVersion.Value}\""));
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return EmergencyLoginPublicKeyFetchResult.Unchanged();
        }

        response.EnsureSuccessStatusCode();
        var package = await response.Content.ReadFromJsonAsync<EmergencyLoginPublicKeyPackage>(
            JsonOptions,
            cancellationToken);
        return package is null
            ? throw new JsonException("紧急登录公钥接口返回空响应。")
            : EmergencyLoginPublicKeyFetchResult.Changed(package);
    }

    public async Task AcknowledgeAsync(long version, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "api/v1/emergency-login/public-keys/ack",
            new EmergencyLoginPublicKeyAckRequest(version),
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

public interface IEmergencyLoginPublicKeyCache
{
    Task<EmergencyLoginPublicKeyPackage?> GetAsync(CancellationToken cancellationToken = default);

    Task ReplaceAsync(
        EmergencyLoginPublicKeyPackage package,
        CancellationToken cancellationToken = default);
}

public sealed class EmergencyLoginPublicKeyCache(
    ILocalAppSettingsRepository settingsRepository,
    IDeviceAuthorizationProtector protector) : IEmergencyLoginPublicKeyCache
{
    private const string CacheKey = "emergency-login:public-keys:v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<EmergencyLoginPublicKeyPackage?> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var protectedValue = await settingsRepository.GetValueAsync(CacheKey, cancellationToken);
        var json = protector.Unprotect(protectedValue);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<EmergencyLoginPublicKeyPackage>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Task ReplaceAsync(
        EmergencyLoginPublicKeyPackage package,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(package, JsonOptions);
        var protectedValue = protector.Protect(json)
            ?? throw new InvalidOperationException("无法加密紧急登录公钥缓存。");
        // 单个 AppSettings 行的 UPSERT 是原子操作，失败时旧公钥包保持不变。
        return settingsRepository.SetValueAsync(CacheKey, protectedValue, cancellationToken);
    }
}

public interface IEmergencyLoginPublicKeySyncService
{
    Task<bool> SyncAsync(CancellationToken cancellationToken = default);
}

public sealed class EmergencyLoginPublicKeySyncService(
    IEmergencyLoginPublicKeyApiClient apiClient,
    IEmergencyLoginPublicKeyCache cache,
    ILogger<EmergencyLoginPublicKeySyncService>? logger = null) : IEmergencyLoginPublicKeySyncService
{
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    public async Task<bool> SyncAsync(CancellationToken cancellationToken = default)
    {
        await _syncGate.WaitAsync(cancellationToken);
        try
        {
            var current = await cache.GetAsync(cancellationToken);
            var validCurrent = current is not null && EmergencyLoginPublicKeyValidator.TryValidate(current)
                ? current
                : null;
            // 坏缓存不得参与条件请求，否则错误 304 会让客户端永远无法恢复。
            var fetched = await apiClient.GetAsync(validCurrent?.Version, cancellationToken);
            if (fetched.NotModified)
            {
                if (validCurrent is null)
                {
                    return false;
                }

                await apiClient.AcknowledgeAsync(validCurrent.Version, cancellationToken);
                return true;
            }

            var package = fetched.Package;
            if (package is null ||
                !EmergencyLoginPublicKeyValidator.TryValidate(package) ||
                validCurrent is not null && package.Version < validCurrent.Version)
            {
                return false;
            }

            if (validCurrent is null || package.Version > validCurrent.Version)
            {
                // 关键逻辑：整包校验通过后才原子替换缓存；ACK 必须晚于持久化成功。
                await cache.ReplaceAsync(package, cancellationToken);
            }

            await apiClient.AcknowledgeAsync(package.Version, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "同步紧急登录公钥失败，继续保留本地旧缓存");
            return false;
        }
        finally
        {
            _syncGate.Release();
        }
    }
}

internal static class EmergencyLoginPublicKeyValidator
{
    private const string P256Oid = "1.2.840.10045.3.1.7";

    internal static bool TryValidate(EmergencyLoginPublicKeyPackage package)
    {
        if (package.Version < 0 || package.Keys is null || package.Keys.Count == 0)
        {
            return false;
        }

        var keyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in package.Keys)
        {
            if (!IsValidKeyId(key.Kid) ||
                !keyIds.Add(key.Kid) ||
                !string.Equals(key.Algorithm, "ES256", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(key.PublicKeyPem) ||
                !key.PublicKeyPem.Contains("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal) ||
                key.PublicKeyPem.Contains("PRIVATE KEY", StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(key.PublicKeyPem);
                var parameters = ecdsa.ExportParameters(false);
                if (ecdsa.KeySize != 256 ||
                    !string.Equals(parameters.Curve.Oid.Value, P256Oid, StringComparison.Ordinal))
                {
                    return false;
                }

                var fingerprint = Convert.ToHexString(SHA256.HashData(ecdsa.ExportSubjectPublicKeyInfo()));
                if (!string.Equals(fingerprint, key.Fingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                return false;
            }
        }

        return string.IsNullOrWhiteSpace(package.ActiveKeyId) || keyIds.Contains(package.ActiveKeyId);
    }

    private static bool IsValidKeyId(string? keyId) =>
        !string.IsNullOrEmpty(keyId) &&
        keyId.Length <= 32 &&
        keyId.All(character =>
            character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9');
}

public sealed class EmergencyLoginPublicKeySyncHostedService(
    IEmergencyLoginPublicKeySyncService syncService,
    DeviceAuthorizationState authorizationState,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SyncInterval = TimeSpan.FromHours(6);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    internal static TimeSpan SyncIntervalForTests => SyncInterval;

    internal static bool ShouldSyncForTests(
        DateTimeOffset now,
        DateTimeOffset? lastAttempt,
        DateTimeOffset? lastSuccess,
        bool newlyAuthorized)
    {
        if (newlyAuthorized)
        {
            return true;
        }

        return lastSuccess is null
            ? lastAttempt is null || now - lastAttempt >= RetryInterval
            : now - lastSuccess >= SyncInterval &&
                (lastAttempt is null || lastAttempt <= lastSuccess || now - lastAttempt >= RetryInterval);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DateTimeOffset? lastAttempt = null;
        DateTimeOffset? lastSuccess = null;
        string? lastAuthorizedDevice = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            var authorization = authorizationState.Current;
            var now = _timeProvider.GetUtcNow();
            var authorizedDevice = authorization is null
                ? null
                : $"{authorization.StoreCode}\u001f{authorization.DeviceCode}\u001f{authorization.HardwareId}";
            var newlyAuthorized = authorizedDevice is not null &&
                !string.Equals(lastAuthorizedDevice, authorizedDevice, StringComparison.Ordinal);
            var due = ShouldSyncForTests(now, lastAttempt, lastSuccess, newlyAuthorized: false);

            if (authorization is not null && (newlyAuthorized || due))
            {
                lastAuthorizedDevice = authorizedDevice;
                lastAttempt = now;
                if (await syncService.SyncAsync(stoppingToken))
                {
                    lastSuccess = now;
                }
            }
            else if (authorization is null)
            {
                lastAuthorizedDevice = null;
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
