using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Hbpos.Api;
using Hbpos.Api.Auth;
using Hbpos.Api.Services;
using Hbpos.Contracts.Cashiers;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.RemoteMaintenance;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Tests;

public sealed class RemoteMaintenanceAuthorizationHttpTests
{
    [Fact]
    public async Task 未认证设备访问远程维护返回401()
    {
        await using var factory = new RemoteMaintenanceApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/remote-maintenance/prepare",
            new RemoteMaintenancePrepareRequest(Guid.NewGuid(), "POS-01"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Audit")]
    [InlineData("Enforce")]
    public async Task 已认证设备但没有cashier_ticket返回403(string mode)
    {
        await using var factory = new RemoteMaintenanceApiFactory(ticket: null, mode: mode);
        using var client = factory.CreateClient();
        AddDeviceAuthentication(client);

        using var response = await client.PostAsJsonAsync(
            "/api/remote-maintenance/prepare",
            new RemoteMaintenancePrepareRequest(Guid.NewGuid(), "POS-01"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 有效cashier_ticket无需设备设置权限且重新核对当前授权码()
    {
        await using var factory = new RemoteMaintenanceApiFactory(
            new CashierAuthorizationTicket(
                "cashier-1", "user-1", "S01", "POS-01", DateTimeOffset.UtcNow.AddMinutes(5), "HW-001"));
        using var client = factory.CreateClient();
        AddDeviceAuthentication(client);
        client.DefaultRequestHeaders.Add(CashierAuthorizationConstants.HeaderName, "cashier");

        using var response = await client.PostAsJsonAsync(
            "/api/remote-maintenance/prepare",
            new RemoteMaintenancePrepareRequest(Guid.NewGuid(), "POS-01"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RemoteMaintenancePrepareResponse>();
        Assert.Equal("api/remote-maintenance/artifacts/rustdesk", payload?.ArtifactManifest.Rustdesk.DownloadUrl);
    }

    private static void AddDeviceAuthentication(HttpClient client) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "device");

    [Theory]
    [InlineData("HW-OLD", "device", HttpStatusCode.Forbidden)]
    [InlineData("HW-001", "old-device", HttpStatusCode.Unauthorized)]
    public async Task 旧硬件票据或旧设备授权码无法调用登记(string hardware, string authorization, HttpStatusCode expected)
    {
        await using var factory = new RemoteMaintenanceApiFactory(new CashierAuthorizationTicket(
            "cashier-1", "user-1", "S01", "POS-01", DateTimeOffset.UtcNow.AddMinutes(5), hardware), "Audit");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authorization);
        client.DefaultRequestHeaders.Add(CashierAuthorizationConstants.HeaderName, "cashier");
        using var response = await client.PostAsJsonAsync("/api/remote-maintenance/prepare", new RemoteMaintenancePrepareRequest(Guid.NewGuid(), "POS-01"));
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, factory.Gateway.Calls);
    }

    private sealed class RemoteMaintenanceApiFactory(CashierAuthorizationTicket? ticket = null, string mode = "Enforce")
        : WebApplicationFactory<Program>
    {
        public TestGateway Gateway { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["CashierAuthorization:Mode"] = mode }));
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthHandler.Scheme;
                    options.DefaultScheme = TestAuthHandler.Scheme;
                });
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
                services.RemoveAll<ICashierAuthorizationTicketService>();
                services.AddSingleton<ICashierAuthorizationTicketService>(new TestTicketService(ticket));
                services.RemoveAll<ICashierService>();
                services.AddSingleton<ICashierService>(new NoOpCashierService());
                services.RemoveAll<IPosIpadAppReviewAuthorizationBoundary>();
                services.AddSingleton<IPosIpadAppReviewAuthorizationBoundary>(new NonReviewBoundary());
                services.RemoveAll<IAppUpdateDeviceIdentityValidator>();
                services.AddSingleton<IAppUpdateDeviceIdentityValidator>(new CurrentIdentityValidator());
                services.RemoveAll<IRemoteMaintenanceGateway>();
                services.AddSingleton<IRemoteMaintenanceGateway>(Gateway);
                var noOp = new NoOpSchemaInitializer();
                services.RemoveAll<IStoreSchemaInitializer>();
                services.AddSingleton<IStoreSchemaInitializer>(noOp);
                services.RemoveAll<IAttendanceQrKeySchemaInitializer>();
                services.AddSingleton<IAttendanceQrKeySchemaInitializer>(noOp);
                services.RemoveAll<IAdvertisementSchemaInitializer>();
                services.AddSingleton<IAdvertisementSchemaInitializer>(noOp);
                services.RemoveAll<ILinklyCloudCredentialSchemaInitializer>();
                services.AddSingleton<ILinklyCloudCredentialSchemaInitializer>(noOp);
                services.RemoveAll<ISquareTokenSchemaInitializer>();
                services.AddSingleton<ISquareTokenSchemaInitializer>(noOp);
            });
        }
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "RemoteMaintenanceHttpTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
                [
                    new Claim(DeviceAuthConstants.DeviceCodeClaim, "POS-01"),
                    new Claim(DeviceAuthConstants.StoreCodeClaim, "S01"),
                    new Claim(DeviceAuthConstants.HardwareIdClaim, "HW-001")
                ], Scheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
        }
    }

    private sealed class TestTicketService(CashierAuthorizationTicket? ticket) : ICashierAuthorizationTicketService
    {
        public (string Token, DateTimeOffset ExpiresAtUtc) Issue(string cashierId, string userGuid, string storeCode, string deviceCode) => throw new NotSupportedException();
        public CashierAuthorizationTicket? Validate(string? token) => token == "cashier" ? ticket : null;
    }

    private sealed class CurrentIdentityValidator : IAppUpdateDeviceIdentityValidator
    {
        public Task<AppUpdateValidatedDeviceIdentity?> ValidateAsync(string hardwareId, string authorizationCode, CancellationToken cancellationToken) =>
            Task.FromResult<AppUpdateValidatedDeviceIdentity?>(
                authorizationCode == "device" && hardwareId == "HW-001" ? new("HW-001") : null);
        public Task<AppUpdateValidatedDeviceIdentity?> ValidateAsync(string hardwareId, string authorizationCode, string storeCode, string deviceCode, CancellationToken cancellationToken) =>
            Task.FromResult<AppUpdateValidatedDeviceIdentity?>(authorizationCode == "device" && hardwareId == "HW-001" && storeCode == "S01" && deviceCode == "POS-01"
                ? new("HW-001", "S01", "POS-01") : null);
    }

    private sealed class TestGateway : IRemoteMaintenanceGateway
    {
        public int Calls { get; private set; }
        public Task<RemoteMaintenanceGatewayResult<RemoteMaintenancePrepareResponse>> PrepareAsync(string hardwareId, RemoteMaintenancePrepareRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(RemoteMaintenanceGatewayResult<RemoteMaintenancePrepareResponse>.Ok(new(
                request.OperationId, Guid.NewGuid(), new("hotbargain.vip:21116", "hotbargain.vip:21117", "public"),
                new("hotbargain.vip:21116", "hotbargain.vip:21117", "public",
                    new("1", "rustdesk.exe", "/admin", "sha", 1), new("1", "agent.exe", "/admin", "sha", 1)))));
        }
        public Task<RemoteMaintenanceGatewayResult<RemoteMaintenanceCommitResponse>> CommitAsync(string hardwareId, RemoteMaintenanceCommitRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RemoteMaintenanceGatewayResult<Stream>> DownloadArtifactAsync(string hardwareId, string kind, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NonReviewBoundary : IPosIpadAppReviewAuthorizationBoundary
    {
        public Task<bool> IsReviewDeviceAsync(string storeCode, string deviceCode, string hardwareId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> IsActiveEmployeeCashierAsync(string cashierId, string userGuid, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class NoOpSchemaInitializer :
        IStoreSchemaInitializer,
        IAttendanceQrKeySchemaInitializer,
        IAdvertisementSchemaInitializer,
        ILinklyCloudCredentialSchemaInitializer,
        ISquareTokenSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpCashierService : ICashierService
    {
        public Task<CashierSessionDto?> BarcodeLoginAsync(CashierBarcodeLoginRequest request, CancellationToken cancellationToken) => Task.FromResult<CashierSessionDto?>(null);
        public Task<bool> HasAnyPermissionAsync(string userGuid, string storeCode, IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<CashierSessionDto?> RefreshSessionAsync(CashierAuthorizationTicket ticket, CancellationToken cancellationToken) => Task.FromResult<CashierSessionDto?>(null);
    }
}
