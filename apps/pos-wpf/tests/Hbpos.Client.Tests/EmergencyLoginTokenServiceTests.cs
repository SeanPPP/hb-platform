using System.Security.Cryptography;
using BlazorApp.Shared.Security;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Cashiers;
using Hbpos.Contracts.EmergencyLogin;

namespace Hbpos.Client.Tests;

public sealed class EmergencyLoginTokenServiceTests
{
    [Fact]
    public async Task Signed_token_allows_same_store_on_multiple_devices_and_rejects_other_store()
    {
        var fixture = CreateFixture();

        var first = await fixture.Service.LoginAsync(fixture.Token, "S001", "POS-01");
        var second = await fixture.Service.LoginAsync(fixture.Token, "S001", "POS-02");
        var otherStore = await fixture.Service.LoginAsync(fixture.Token, "S002", "POS-01");

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.True(first.Session!.IsEmergencyOverride);
        Assert.False(first.Session.IsSuperAdmin);
        Assert.Equal("POS-02", second.Session!.DeviceCode);
        Assert.False(otherStore.Succeeded);
    }

    [Fact]
    public async Task Signed_token_rejects_tampering_and_trusted_time_rollback()
    {
        var fixture = CreateFixture();
        var tampered = fixture.Token[..^1] + (fixture.Token[^1] == '0' ? '1' : '0');

        Assert.False((await fixture.Service.LoginAsync(tampered, "S001", "POS-01")).Succeeded);
        Assert.True((await fixture.Service.LoginAsync(fixture.Token, "S001", "POS-01")).Succeeded);

        fixture.Time.UtcNow = fixture.Time.UtcNow.AddMinutes(-1);
        var rolledBack = await fixture.Service.LoginAsync(fixture.Token, "S001", "POS-01");

        Assert.False(rolledBack.Succeeded);
        Assert.Contains("可信时间", rolledBack.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cashier_login_routes_emergency_prefix_before_online_barcode_api_and_does_not_cache()
    {
        var settings = new InMemorySettingsRepository();
        var emergencySession = CashierSessionContext.CreateEmergencyOverride(
            "S001", "POS-02", Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1), "HBPOSE1-token");
        var emergency = new StubEmergencyLoginTokenService(CashierLoginResult.Success(emergencySession));
        var api = new CountingCashierLoginApiClient();
        var service = new CashierLoginService(api, settings, new PassthroughProtector(), emergency);

        var result = await service.LoginAsync("S001", "POS-02", "HBPOSE1-K1-AA-BB");

        Assert.True(result.Succeeded);
        Assert.Equal(0, api.CallCount);
        Assert.Equal(1, emergency.CallCount);
        Assert.Empty(settings.Values);
    }

    [Fact]
    public async Task Cashier_login_does_not_treat_date_derived_digits_as_emergency_authorization()
    {
        var emergency = new StubEmergencyLoginTokenService(CashierLoginResult.Fail("unexpected"));
        var api = new CountingCashierLoginApiClient();
        var service = new CashierLoginService(
            api,
            new InMemorySettingsRepository(),
            new PassthroughProtector(),
            emergency);

        var result = await service.LoginAsync("S001", "POS-01", "202607142");

        Assert.False(result.Succeeded);
        Assert.Equal(1, api.CallCount);
        Assert.Equal(0, emergency.CallCount);
    }

    private static Fixture CreateFixture()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = DateTimeOffset.Parse("2026-07-14T03:00:00Z");
        var payload = new EmergencyLoginTokenPayload
        {
            GrantId = Guid.NewGuid(),
            StoreCode = "S001",
            BusinessDate = "2026-07-14",
            Issuer = "admin",
            IssuedAtUtc = now.UtcDateTime,
            NotBeforeUtc = now.AddMinutes(-1).UtcDateTime,
            ExpiresAtUtc = now.AddHours(2).UtcDateTime
        };
        var token = EmergencyLoginTokenCodec.Sign(payload, "K1", key.ExportECPrivateKeyPem());
        var time = new MutableTimeProvider(now);
        var settings = new InMemorySettingsRepository();
        var protector = new PassthroughProtector();
        var cache = new EmergencyLoginPublicKeyCache(settings, protector);
        cache.ReplaceAsync(new EmergencyLoginPublicKeyPackage(
            1,
            "K1",
            now.UtcDateTime,
            [new EmergencyLoginPublicKey(
                "K1",
                "ES256",
                key.ExportSubjectPublicKeyInfoPem(),
                Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())))])).GetAwaiter().GetResult();
        var service = new EmergencyLoginTokenService(
            cache,
            new NoOpPublicKeySyncService(),
            settings,
            protector,
            time);
        return new Fixture(service, token, time);
    }

    private sealed record Fixture(
        EmergencyLoginTokenService Service,
        string Token,
        MutableTimeProvider Time);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
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
        public string? Protect(string? value) => value is null ? null : $"protected:{value}";

        public string? Unprotect(string? protectedValue) =>
            protectedValue?.StartsWith("protected:", StringComparison.Ordinal) == true
                ? protectedValue["protected:".Length..]
                : null;
    }

    private sealed class NoOpPublicKeySyncService : IEmergencyLoginPublicKeySyncService
    {
        public Task<bool> SyncAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class CountingCashierLoginApiClient : ICashierLoginApiClient
    {
        public int CallCount { get; private set; }

        public Task<CashierLoginAttempt> LoginAsync(
            CashierBarcodeLoginRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(CashierLoginAttempt.OnlineRejected("unexpected"));
        }
    }

    private sealed class StubEmergencyLoginTokenService(CashierLoginResult result)
        : IEmergencyLoginTokenService
    {
        public int CallCount { get; private set; }

        public Task<CashierLoginResult> LoginAsync(
            string token,
            string storeCode,
            string deviceCode,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }
}
