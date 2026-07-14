using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using BlazorApp.Shared.Security;
using Hbpos.Api.Auth;
using Hbpos.Api.Controllers;
using Hbpos.Api.Data;
using Hbpos.Api.Services;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.EmergencyLogin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hbpos.Api.Tests;

public sealed class EmergencyLoginPublicKeysTests
{
    [Fact]
    public void Controller_exposes_device_authenticated_public_key_routes_without_secret_fields()
    {
        var controllerType = typeof(EmergencyLoginPublicKeysController);
        Assert.Equal(
            "api/v1/emergency-login/public-keys",
            controllerType.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.NotNull(controllerType.GetCustomAttribute<AuthorizeAttribute>());

        Assert.Equal("", controllerType.GetMethod(nameof(EmergencyLoginPublicKeysController.Get))?
            .GetCustomAttribute<HttpGetAttribute>()?.Template);
        Assert.Equal("ack", controllerType.GetMethod(nameof(EmergencyLoginPublicKeysController.Acknowledge))?
            .GetCustomAttribute<HttpPostAttribute>()?.Template);

        var responseProperties = typeof(EmergencyLoginPublicKeyPackage).GetProperties()
            .Concat(typeof(EmergencyLoginPublicKey).GetProperties())
            .Select(property => property.Name)
            .ToArray();
        Assert.DoesNotContain(responseProperties, name =>
            name.Contains("Private", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Protected", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Encrypted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Get_returns_304_for_matching_etag()
    {
        var package = CreatePackage(7);
        var controller = CreateController(new StubService(package));
        controller.Request.Headers.IfNoneMatch = "\"emergency-login-keys-v7\"";

        var result = await controller.Get(CancellationToken.None);

        Assert.Equal(StatusCodes.Status304NotModified, Assert.IsType<StatusCodeResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task Ack_uses_authenticated_device_claims_and_rejects_future_version()
    {
        var service = new StubService(CreatePackage(7))
        {
            AckResult = EmergencyLoginPublicKeyAckResult.FutureVersion
        };
        var controller = CreateController(service);

        var result = await controller.Acknowledge(
            new EmergencyLoginPublicKeyAckRequest(8),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("POS-01", service.LastDevice?.DeviceCode);
        Assert.Equal("S001", service.LastDevice?.StoreCode);
        Assert.Equal("hardware-1", service.LastDevice?.HardwareId);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    [Fact]
    public async Task Distribution_returns_only_non_retired_keys_and_ignores_stale_ack()
    {
        var repository = new InMemoryRepository
        {
            Snapshot = new EmergencyLoginPublicKeySetSnapshot(
                4,
                "KACTIVE",
                DateTime.Parse("2026-07-15T01:00:00Z").ToUniversalTime(),
                [
                    new("KSTAGED", "ES256", "staged-pem", "AA", "Staged"),
                    new("KACTIVE", "ES256", "active-pem", "BB", "Active"),
                    new("KOLD", "ES256", "old-pem", "CC", "Retired")
                ])
        };
        var service = new EmergencyLoginPublicKeyDistributionService(repository, new FixedTimeProvider());

        var package = await service.GetAsync(CancellationToken.None);
        var ack = await service.AcknowledgeAsync(
            new EmergencyLoginDeviceIdentity("POS-01", "S001", "hardware-1"),
            3,
            CancellationToken.None);

        Assert.Equal(["KSTAGED", "KACTIVE"], package.Keys.Select(key => key.Kid));
        Assert.Equal(EmergencyLoginPublicKeyAckResult.StaleIgnored, ack);
        Assert.Empty(repository.Acknowledgements);
    }

    [Fact]
    public async Task Grant_validation_unknown_kid_forces_one_refresh_and_database_failure_stays_closed()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = DateTimeOffset.Parse("2026-07-15T02:00:00Z");
        var token = EmergencyLoginTokenCodec.Sign(new EmergencyLoginTokenPayload
        {
            GrantId = Guid.NewGuid(),
            StoreCode = "S001",
            BusinessDate = "2026-07-15",
            Issuer = "admin",
            IssuedAtUtc = now.UtcDateTime,
            NotBeforeUtc = now.AddMinutes(-1).UtcDateTime,
            ExpiresAtUtc = now.AddHours(1).UtcDateTime
        }, "K2", key.ExportECPrivateKeyPem());
        var provider = new RefreshingPublicKeyProvider(
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["K2"] = key.ExportSubjectPublicKeyInfoPem()
            });
        var unavailableDb = (HbposSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(HbposSqlSugarContext));
        var service = new EmergencyGrantAuthorizationService(
            unavailableDb,
            provider,
            NullLogger<EmergencyGrantAuthorizationService>.Instance,
            new FixedTimeProvider());

        var result = await service.ValidateAsync(token, "S001", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal([false, true], provider.ForceRefreshCalls);
    }

    private static EmergencyLoginPublicKeysController CreateController(
        IEmergencyLoginPublicKeyDistributionService service)
    {
        var controller = new EmergencyLoginPublicKeysController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(DeviceAuthConstants.DeviceCodeClaim, "POS-01"),
                        new Claim(DeviceAuthConstants.StoreCodeClaim, "S001"),
                        new Claim(DeviceAuthConstants.HardwareIdClaim, "hardware-1")
                    ], "test"))
                }
            }
        };
        return controller;
    }

    private static EmergencyLoginPublicKeyPackage CreatePackage(long version) => new(
        version,
        "K1",
        DateTime.Parse("2026-07-15T01:00:00Z").ToUniversalTime(),
        [new("K1", "ES256", "pem", "AA")]);

    private sealed class StubService(EmergencyLoginPublicKeyPackage package)
        : IEmergencyLoginPublicKeyDistributionService
    {
        public EmergencyLoginPublicKeyAckResult AckResult { get; set; } = EmergencyLoginPublicKeyAckResult.Acknowledged;
        public EmergencyLoginDeviceIdentity? LastDevice { get; private set; }

        public Task<EmergencyLoginPublicKeyPackage> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(package);

        public Task<EmergencyLoginPublicKeyAckResult> AcknowledgeAsync(
            EmergencyLoginDeviceIdentity device,
            long version,
            CancellationToken cancellationToken)
        {
            LastDevice = device;
            return Task.FromResult(AckResult);
        }
    }

    private sealed class InMemoryRepository : IEmergencyLoginPublicKeyRepository
    {
        public required EmergencyLoginPublicKeySetSnapshot Snapshot { get; set; }
        public List<(int DeviceId, long Version, string KeyId)> Acknowledgements { get; } = [];

        public Task<EmergencyLoginPublicKeySetSnapshot> GetKeySetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Snapshot);

        public Task<int?> FindDeviceRegistrationIdAsync(
            EmergencyLoginDeviceIdentity device,
            CancellationToken cancellationToken) => Task.FromResult<int?>(42);

        public Task UpsertAcknowledgementAsync(
            int deviceRegistrationId,
            long version,
            string keyId,
            DateTime acknowledgedAtUtc,
            CancellationToken cancellationToken)
        {
            Acknowledgements.Add((deviceRegistrationId, version, keyId));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-07-15T02:00:00Z");
    }

    private sealed class RefreshingPublicKeyProvider(
        IReadOnlyDictionary<string, string> cached,
        IReadOnlyDictionary<string, string> refreshed) : IEmergencyLoginPublicKeyProvider
    {
        public List<bool> ForceRefreshCalls { get; } = [];

        public Task<IReadOnlyDictionary<string, string>> GetKeysAsync(
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            ForceRefreshCalls.Add(forceRefresh);
            return Task.FromResult(forceRefresh ? refreshed : cached);
        }
    }
}
