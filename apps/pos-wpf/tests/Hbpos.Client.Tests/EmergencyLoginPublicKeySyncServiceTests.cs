using System.Security.Cryptography;
using BlazorApp.Shared.Security;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.EmergencyLogin;
using Hbpos.Client.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
    public async Task Sync_invalid_cached_package_does_not_send_etag_and_forces_full_replacement()
    {
        using var oldKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var settings = new InMemorySettingsRepository();
        var cache = new EmergencyLoginPublicKeyCache(settings, new PassthroughProtector());
        await cache.ReplaceAsync(CreatePackage(9, "BAD-KID", oldKey));
        var api = new StubApiClient(CreatePackage(10, "K10", newKey));

        var result = await new EmergencyLoginPublicKeySyncService(api, cache).SyncAsync();

        Assert.True(result);
        Assert.Null(Assert.Single(api.RequestedVersions));
        Assert.Equal(10, (await cache.GetAsync())?.Version);
        Assert.Single(api.AcknowledgedVersions, 10);
    }

    [Fact]
    public async Task Sync_304_never_acknowledges_invalid_cached_package()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var settings = new InMemorySettingsRepository();
        var cache = new EmergencyLoginPublicKeyCache(settings, new PassthroughProtector());
        await cache.ReplaceAsync(CreatePackage(9, "BAD-KID", key));
        var api = new StubApiClient(EmergencyLoginPublicKeyFetchResult.Unchanged());

        var result = await new EmergencyLoginPublicKeySyncService(api, cache).SyncAsync();

        Assert.False(result);
        Assert.Null(Assert.Single(api.RequestedVersions));
        Assert.Empty(api.AcknowledgedVersions);
    }

    [Fact]
    public async Task Sync_stale_ack_immediately_refetches_without_etag_then_saves_and_acks_current_version()
    {
        using var oldKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var settings = new InMemorySettingsRepository();
        var cache = new EmergencyLoginPublicKeyCache(settings, new PassthroughProtector());
        await cache.ReplaceAsync(CreatePackage(7, "K7", oldKey));
        var api = new RacingApiClient(CreatePackage(8, "K8", newKey), secondAckSucceeds: true);

        var result = await new EmergencyLoginPublicKeySyncService(api, cache).SyncAsync();

        Assert.True(result);
        Assert.Equal([7L, null], api.RequestedVersions);
        Assert.Equal([7L, 8L], api.AcknowledgedVersions);
        Assert.Equal(8, (await cache.GetAsync())?.Version);
    }

    [Fact]
    public async Task Sync_stops_after_one_immediate_retry_when_rotation_continues()
    {
        using var oldKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var settings = new InMemorySettingsRepository();
        var cache = new EmergencyLoginPublicKeyCache(settings, new PassthroughProtector());
        await cache.ReplaceAsync(CreatePackage(7, "K7", oldKey));
        var api = new RacingApiClient(CreatePackage(8, "K8", newKey), secondAckSucceeds: false);

        var result = await new EmergencyLoginPublicKeySyncService(api, cache).SyncAsync();

        Assert.False(result);
        Assert.Equal(2, api.RequestedVersions.Count);
        Assert.Equal(2, api.AcknowledgedVersions.Count);
    }

    [Fact]
    public void Validator_rejects_non_es256_non_p256_and_invalid_kid()
    {
        using var p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var nonEs256 = CreatePackage(1, "K1", p256) with
        {
            Keys = [CreatePackage(1, "K1", p256).Keys[0] with { Algorithm = "ES384" }]
        };
        var nonP256 = CreatePackage(1, "K1", p384);
        var invalidKid = CreatePackage(1, "BAD-KID", p256);

        Assert.False(EmergencyLoginPublicKeyValidator.TryValidate(nonEs256));
        Assert.False(EmergencyLoginPublicKeyValidator.TryValidate(nonP256));
        Assert.False(EmergencyLoginPublicKeyValidator.TryValidate(invalidKid));
    }

    [Fact]
    public void Hosted_sync_contract_registers_and_runs_on_first_authorization_then_every_six_hours()
    {
        var services = new ServiceCollection();
        services.AddHbposClientServices(new AppStartupOptions([], true, null, null));
        using var provider = services.BuildServiceProvider();

        var hostedDescriptor = services.First(descriptor => descriptor.ServiceType == typeof(IHostedService));
        var hostedService = hostedDescriptor.ImplementationFactory?.Invoke(provider);
        Assert.IsType<EmergencyLoginPublicKeySyncHostedService>(hostedService);
        var now = DateTimeOffset.Parse("2026-07-15T03:00:00Z");
        Assert.True(EmergencyLoginPublicKeySyncHostedService.ShouldSyncForTests(
            now, null, null, newlyAuthorized: true));
        Assert.False(EmergencyLoginPublicKeySyncHostedService.ShouldSyncForTests(
            now.AddHours(5).AddMinutes(59), now, now, newlyAuthorized: false));
        Assert.True(EmergencyLoginPublicKeySyncHostedService.ShouldSyncForTests(
            now.AddHours(6), now, now, newlyAuthorized: false));
        Assert.Equal(TimeSpan.FromHours(6), EmergencyLoginPublicKeySyncHostedService.SyncIntervalForTests);
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
        var token = EmergencyLoginTokenCodec.SignV2(
            Guid.NewGuid(),
            "S001",
            now.AddMinutes(-1).UtcDateTime,
            now.AddHours(1).UtcDateTime,
            "K2",
            newKey.ExportECPrivateKeyPem());
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
        private readonly EmergencyLoginPublicKeyFetchResult? _fetchResult;

        public StubApiClient(EmergencyLoginPublicKeyPackage package, Func<bool>? beforeAck = null)
        {
            _package = package;
            _beforeAck = beforeAck;
        }

        public StubApiClient(Exception exception) => _exception = exception;

        public StubApiClient(EmergencyLoginPublicKeyFetchResult fetchResult) => _fetchResult = fetchResult;

        public List<long> AcknowledgedVersions { get; } = [];
        public List<long?> RequestedVersions { get; } = [];

        public Task<EmergencyLoginPublicKeyFetchResult> GetAsync(
            long? currentVersion,
            CancellationToken cancellationToken = default)
        {
            RequestedVersions.Add(currentVersion);
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_fetchResult ?? EmergencyLoginPublicKeyFetchResult.Changed(_package!));
        }

        public Task<EmergencyLoginPublicKeyAckClientResult> AcknowledgeAsync(
            long version,
            CancellationToken cancellationToken = default)
        {
            Assert.True(_beforeAck?.Invoke() ?? true, "ACK 必须发生在缓存保存之后");
            AcknowledgedVersions.Add(version);
            return Task.FromResult(new EmergencyLoginPublicKeyAckClientResult(true, version));
        }
    }

    private sealed class RacingApiClient(
        EmergencyLoginPublicKeyPackage currentPackage,
        bool secondAckSucceeds) : IEmergencyLoginPublicKeyApiClient
    {
        public List<long?> RequestedVersions { get; } = [];
        public List<long> AcknowledgedVersions { get; } = [];

        public Task<EmergencyLoginPublicKeyFetchResult> GetAsync(
            long? currentVersion,
            CancellationToken cancellationToken = default)
        {
            RequestedVersions.Add(currentVersion);
            return Task.FromResult(currentVersion is null
                ? EmergencyLoginPublicKeyFetchResult.Changed(currentPackage)
                : EmergencyLoginPublicKeyFetchResult.Unchanged());
        }

        public Task<EmergencyLoginPublicKeyAckClientResult> AcknowledgeAsync(
            long version,
            CancellationToken cancellationToken = default)
        {
            AcknowledgedVersions.Add(version);
            var first = AcknowledgedVersions.Count == 1;
            return Task.FromResult(first
                ? new EmergencyLoginPublicKeyAckClientResult(false, currentPackage.Version)
                : new EmergencyLoginPublicKeyAckClientResult(secondAckSucceeds, currentPackage.Version));
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
