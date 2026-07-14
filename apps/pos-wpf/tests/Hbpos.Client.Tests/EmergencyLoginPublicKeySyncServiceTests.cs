using System.Security.Cryptography;
using BlazorApp.Shared.Security;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.EmergencyLogin;

namespace Hbpos.Client.Tests;

public sealed class EmergencyLoginPublicKeySyncServiceTests
{
    [Fact]
    public async Task Sync_valid_package_atomically_replaces_encrypted_cache_before_ack()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var package = CreatePackage(2, "K2", key);
        var settings = new InMemorySettingsRepository();
        var protector = new PassthroughProtector();
        var cache = new EmergencyLoginPublicKeyCache(settings, protector);
        var api = new StubApiClient(package, () => settings.Values.Count > 0);
        var service = new EmergencyLoginPublicKeySyncService(api, cache);

        var result = await service.SyncAsync();

        Assert.True(result);
        Assert.Equal(2, (await cache.GetAsync())?.Version);
        Assert.Single(api.AcknowledgedVersions, 2);
        Assert.DoesNotContain("BEGIN PUBLIC KEY", Assert.Single(settings.Values).Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sync_rejects_lower_version_bad_fingerprint_and_network_failure_without_replacing_cache()
    {
        using var oldKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var settings = new InMemorySettingsRepository();
        var cache = new EmergencyLoginPublicKeyCache(settings, new PassthroughProtector());
        await cache.ReplaceAsync(CreatePackage(5, "K5", oldKey));

        var lower = new EmergencyLoginPublicKeySyncService(
            new StubApiClient(CreatePackage(4, "K4", newKey)), cache);
        var badFingerprintPackage = CreatePackage(6, "K6", newKey) with
        {
            Keys = [CreatePackage(6, "K6", newKey).Keys[0] with { Fingerprint = "00" }]
        };
        var invalid = new EmergencyLoginPublicKeySyncService(
            new StubApiClient(badFingerprintPackage), cache);
        var unavailable = new EmergencyLoginPublicKeySyncService(
            new StubApiClient(new HttpRequestException("offline")), cache);

        Assert.False(await lower.SyncAsync());
        Assert.False(await invalid.SyncAsync());
        Assert.False(await unavailable.SyncAsync());
        Assert.Equal(5, (await cache.GetAsync())?.Version);
    }

    [Fact]
    public async Task Sync_rejects_empty_package_and_bad_pem_without_replacing_cache()
    {
        using var oldKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var settings = new InMemorySettingsRepository();
        var cache = new EmergencyLoginPublicKeyCache(settings, new PassthroughProtector());
        await cache.ReplaceAsync(CreatePackage(5, "K5", oldKey));
        var emptyPackage = new EmergencyLoginPublicKeyPackage(
            6,
            null,
            DateTime.Parse("2026-07-15T01:00:00Z").ToUniversalTime(),
            []);
        var badPemPackage = emptyPackage with
        {
            ActiveKeyId = "K6",
            Keys = [new EmergencyLoginPublicKey("K6", "ES256", "not-a-pem", new string('A', 64))]
        };

        Assert.False(await new EmergencyLoginPublicKeySyncService(
            new StubApiClient(emptyPackage), cache).SyncAsync());
        Assert.False(await new EmergencyLoginPublicKeySyncService(
            new StubApiClient(badPemPackage), cache).SyncAsync());
        Assert.Equal(5, (await cache.GetAsync())?.Version);
    }

    [Fact]
    public async Task Emergency_login_unknown_kid_syncs_once_then_retries_cached_keys()
    {
        using var oldKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = DateTimeOffset.Parse("2026-07-15T03:00:00Z");
        var settings = new InMemorySettingsRepository();
        var cache = new EmergencyLoginPublicKeyCache(settings, new PassthroughProtector());
        await cache.ReplaceAsync(CreatePackage(1, "K1", oldKey));
        var sync = new ReplacingSyncService(cache, CreatePackage(2, "K2", newKey));
        var token = EmergencyLoginTokenCodec.Sign(new EmergencyLoginTokenPayload
        {
            GrantId = Guid.NewGuid(),
            StoreCode = "S001",
            BusinessDate = "2026-07-15",
            Issuer = "admin",
            IssuedAtUtc = now.UtcDateTime,
            NotBeforeUtc = now.AddMinutes(-1).UtcDateTime,
            ExpiresAtUtc = now.AddHours(1).UtcDateTime
        }, "K2", newKey.ExportECPrivateKeyPem());
        var service = new EmergencyLoginTokenService(
            cache,
            sync,
            settings,
            new PassthroughProtector(),
            new FixedTimeProvider(now));

        var result = await service.LoginAsync(token, "S001", "POS-01");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, sync.CallCount);
    }

    private static EmergencyLoginPublicKeyPackage CreatePackage(long version, string kid, ECDsa key)
    {
        var publicKey = key.ExportSubjectPublicKeyInfo();
        return new EmergencyLoginPublicKeyPackage(
            version,
            kid,
            DateTime.Parse("2026-07-15T01:00:00Z").ToUniversalTime(),
            [new(kid, "ES256", key.ExportSubjectPublicKeyInfoPem(), Convert.ToHexString(SHA256.HashData(publicKey)))]);
    }

    private sealed class StubApiClient : IEmergencyLoginPublicKeyApiClient
    {
        private readonly EmergencyLoginPublicKeyPackage? _package;
        private readonly Exception? _exception;
        private readonly Func<bool>? _beforeAck;

        public StubApiClient(EmergencyLoginPublicKeyPackage package, Func<bool>? beforeAck = null)
        {
            _package = package;
            _beforeAck = beforeAck;
        }

        public StubApiClient(Exception exception) => _exception = exception;

        public List<long> AcknowledgedVersions { get; } = [];

        public Task<EmergencyLoginPublicKeyFetchResult> GetAsync(
            long? currentVersion,
            CancellationToken cancellationToken = default)
        {
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(EmergencyLoginPublicKeyFetchResult.Changed(_package!));
        }

        public Task AcknowledgeAsync(long version, CancellationToken cancellationToken = default)
        {
            Assert.True(_beforeAck?.Invoke() ?? true, "ACK 必须发生在缓存保存之后");
            AcknowledgedVersions.Add(version);
            return Task.CompletedTask;
        }
    }

    private sealed class ReplacingSyncService(
        IEmergencyLoginPublicKeyCache cache,
        EmergencyLoginPublicKeyPackage package) : IEmergencyLoginPublicKeySyncService
    {
        public int CallCount { get; private set; }

        public async Task<bool> SyncAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            await cache.ReplaceAsync(package, cancellationToken);
            return true;
        }
    }

    private sealed class InMemorySettingsRepository : ILocalAppSettingsRepository
    {
        public Dictionary<string, string> Values { get; } = [];

        public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.GetValueOrDefault(key));

        public Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }

        public Task DeleteValueAsync(string key, CancellationToken cancellationToken = default)
        {
            Values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class PassthroughProtector : IDeviceAuthorizationProtector
    {
        public string? Protect(string? value) => value is null ? null : $"encrypted:{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))}";

        public string? Unprotect(string? protectedValue) =>
            protectedValue?.StartsWith("encrypted:", StringComparison.Ordinal) == true
                ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue["encrypted:".Length..]))
                : null;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
