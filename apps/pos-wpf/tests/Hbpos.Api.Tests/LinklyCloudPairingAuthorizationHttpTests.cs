using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Hbpos.Api;
using Hbpos.Api.Auth;
using Hbpos.Api.Services;
using Hbpos.Contracts.Cashiers;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.Linkly;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Tests;

public sealed class LinklyCloudPairingAuthorizationHttpTests
{
    [Fact]
    public async Task Pair_without_authorization_returns_401()
    {
        await using var factory = new LinklyCloudPairingAuthorizationApiFactory(
            new CapturingLinklyCloudPairingService(),
            paymentSettingsGranted: false);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/linkly/cloud-backend/pair",
            new LinklyCloudBackendPairRequest("Sandbox", "123456"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Pair_with_authenticated_device_but_without_payment_settings_returns_403()
    {
        await using var factory = new LinklyCloudPairingAuthorizationApiFactory(
            new CapturingLinklyCloudPairingService(),
            paymentSettingsGranted: false);
        using var client = factory.CreateClient();
        AddDeviceAuthentication(client);
        client.DefaultRequestHeaders.Add(CashierAuthorizationConstants.HeaderName, "valid");

        using var response = await client.PostAsJsonAsync(
            "/api/v1/linkly/cloud-backend/pair",
            new LinklyCloudBackendPairRequest("Sandbox", "123456"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Pair_with_payment_settings_uses_authenticated_store_and_device_claims()
    {
        var pairing = new CapturingLinklyCloudPairingService();
        await using var factory = new LinklyCloudPairingAuthorizationApiFactory(
            pairing,
            paymentSettingsGranted: true);
        using var client = factory.CreateClient();
        AddDeviceAuthentication(client);
        client.DefaultRequestHeaders.Add(CashierAuthorizationConstants.HeaderName, "valid");

        using var response = await client.PostAsJsonAsync(
            "/api/v1/linkly/cloud-backend/pair",
            new LinklyCloudBackendPairRequest("Sandbox", "123456"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, pairing.Calls);
        Assert.Equal("S01", pairing.StoreCode);
        Assert.Equal("POS-01", pairing.DeviceCode);
        Assert.Equal("Sandbox", pairing.Request?.Environment);
        Assert.Equal("123456", pairing.Request?.PairCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    public async Task Pair_invalid_json_body_returns_stable_api_error_envelope(string json)
    {
        var pairing = new CapturingLinklyCloudPairingService();
        await using var factory = new LinklyCloudPairingAuthorizationApiFactory(
            pairing,
            paymentSettingsGranted: true);
        using var client = factory.CreateClient();
        AddDeviceAuthentication(client);
        client.DefaultRequestHeaders.Add(CashierAuthorizationConstants.HeaderName, "valid");
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(
            "/api/v1/linkly/cloud-backend/pair",
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "LINKLY_CLOUD_BACKEND_PAIR_REQUEST_INVALID",
            document.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(0, pairing.Calls);
    }

    private static void AddDeviceAuthentication(HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
    }

    private sealed class LinklyCloudPairingAuthorizationApiFactory(
        CapturingLinklyCloudPairingService pairing,
        bool paymentSettingsGranted) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CashierAuthorization:Mode"] = "Enforce"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = PairingTestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = PairingTestAuthHandler.SchemeName;
                    options.DefaultScheme = PairingTestAuthHandler.SchemeName;
                });

                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, PairingTestAuthHandler>(
                        PairingTestAuthHandler.SchemeName,
                        _ => { });

                services.RemoveAll<ILinklyCloudPairingService>();
                services.AddSingleton<ILinklyCloudPairingService>(pairing);
                services.RemoveAll<ILinklyCloudCredentialService>();
                services.AddSingleton<ILinklyCloudCredentialService>(new NoOpLinklyCloudCredentialService());
                services.RemoveAll<ILinklyCloudBackendAsyncService>();
                services.AddSingleton<ILinklyCloudBackendAsyncService>(new NoOpLinklyCloudBackendAsyncService());

                services.RemoveAll<ICashierAuthorizationTicketService>();
                services.AddSingleton<ICashierAuthorizationTicketService>(new PairingTicketService());
                services.RemoveAll<ICashierService>();
                services.AddSingleton<ICashierService>(new PairingCashierService(paymentSettingsGranted));

                var schemaInitializer = new NoOpLinklySchemaInitializer();
                services.RemoveAll<IStoreSchemaInitializer>();
                services.AddSingleton<IStoreSchemaInitializer>(schemaInitializer);
                services.RemoveAll<IAttendanceQrKeySchemaInitializer>();
                services.AddSingleton<IAttendanceQrKeySchemaInitializer>(schemaInitializer);
                services.RemoveAll<IDeviceRuntimeStatusSchemaInitializer>();
                services.AddSingleton<IDeviceRuntimeStatusSchemaInitializer>(schemaInitializer);
                services.RemoveAll<IAdvertisementSchemaInitializer>();
                services.AddSingleton<IAdvertisementSchemaInitializer>(schemaInitializer);
                services.RemoveAll<ILinklyCloudCredentialSchemaInitializer>();
                services.AddSingleton<ILinklyCloudCredentialSchemaInitializer>(schemaInitializer);
                services.RemoveAll<ILinklyCloudBackendAsyncSchemaInitializer>();
                services.AddSingleton<ILinklyCloudBackendAsyncSchemaInitializer>(schemaInitializer);
                services.RemoveAll<ISquareTokenSchemaInitializer>();
                services.AddSingleton<ISquareTokenSchemaInitializer>(schemaInitializer);
            });
        }
    }

    private sealed class PairingTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "LinklyPairingHttpTestAuth";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!string.Equals(Request.Headers.Authorization.ToString(), "Test", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [
                    new Claim(DeviceAuthConstants.DeviceCodeClaim, "POS-01"),
                    new Claim(DeviceAuthConstants.StoreCodeClaim, "S01"),
                    new Claim(DeviceAuthConstants.HardwareIdClaim, "HW-001")
                ],
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class PairingTicketService : ICashierAuthorizationTicketService
    {
        public (string Token, DateTimeOffset ExpiresAtUtc) Issue(
            string cashierId,
            string userGuid,
            string storeCode,
            string deviceCode) => throw new NotSupportedException();

        public CashierAuthorizationTicket? Validate(string? token) =>
            string.Equals(token, "valid", StringComparison.Ordinal)
                ? new CashierAuthorizationTicket(
                    "C001",
                    "U001",
                    "S01",
                    "POS-01",
                    DateTimeOffset.UtcNow.AddHours(1))
                : null;
    }

    private sealed class PairingCashierService(bool paymentSettingsGranted) : ICashierService
    {
        public Task<CashierSessionDto?> BarcodeLoginAsync(
            CashierBarcodeLoginRequest request,
            CancellationToken cancellationToken) => Task.FromResult<CashierSessionDto?>(null);

        public Task<bool> HasAnyPermissionAsync(
            string userGuid,
            string storeCode,
            IReadOnlyCollection<string> permissionCodes,
            CancellationToken cancellationToken) => Task.FromResult(paymentSettingsGranted);

        public Task<CashierSessionDto?> RefreshSessionAsync(
            CashierAuthorizationTicket ticket,
            CancellationToken cancellationToken) => Task.FromResult<CashierSessionDto?>(null);
    }
}
