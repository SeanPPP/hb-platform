using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using Hbpos.Api;
using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.Square;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Tests;

public sealed class SquareControllerTests
{
    private const string BackendToken = "opaque-api-square-token";

    [Fact]
    public void SquareTokenEndpoint_KeepsExpectedRouteAndAuthorization()
    {
        Assert.Equal("token", typeof(SquareController)
            .GetMethod(nameof(SquareController.GetToken))?
            .GetCustomAttributes(typeof(HttpGetAttribute), inherit: false)
            .Cast<HttpGetAttribute>()
            .Single()
            .Template);
        Assert.NotNull(typeof(SquareController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .SingleOrDefault());
    }

    [Fact]
    public void SquareTerminalEndpoints_KeepExpectedRouteTemplates()
    {
        AssertHttpTemplate("GetLocations", typeof(HttpGetAttribute), "locations");
        AssertHttpTemplate("GetDevices", typeof(HttpGetAttribute), "devices");
        AssertHttpTemplate("GetDeviceCodes", typeof(HttpGetAttribute), "device-codes");
        AssertHttpTemplate("CreateDeviceCode", typeof(HttpPostAttribute), "device-codes");
        AssertHttpTemplate("GetDeviceCode", typeof(HttpGetAttribute), "device-codes/{deviceCodeId}");
        AssertHttpTemplate("CreateCheckout", typeof(HttpPostAttribute), "checkouts");
        AssertHttpTemplate("GetCheckout", typeof(HttpGetAttribute), "checkouts/{checkoutId}");
        AssertHttpTemplate("GetPayment", typeof(HttpGetAttribute), "payments/{paymentId}");
        AssertHttpTemplate("CancelCheckout", typeof(HttpPostAttribute), "checkouts/{checkoutId}/cancel");
        AssertHttpTemplate("DismissCheckout", typeof(HttpPostAttribute), "checkouts/{checkoutId}/dismiss");
        AssertHttpTemplate("CreateRefund", typeof(HttpPostAttribute), "refunds");
        AssertHttpTemplate("ReceiveWebhook", typeof(HttpPostAttribute), "webhooks");

        Assert.NotNull(typeof(SquareController)
            .GetMethod("ReceiveWebhook")?
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false)
            .SingleOrDefault());
    }

    [Fact]
    public void SquareCreateCheckoutRequest_RequiresLocationId()
    {
        var request = new SquareCreateCheckoutRequest(
            "Production",
            "idem-001",
            "device-001",
            "location-001",
            new SquareMoneyDto(1299, "AUD"));

        Assert.Equal("location-001", request.LocationId);
    }

    [Fact]
    public async Task GetToken_RequiresAuthentication()
    {
        await using var factory = new SquareApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/square/token?environment=Production");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetLocations_RequiresAuthentication()
    {
        await using var factory = new SquareApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/square/locations?environment=Production");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetToken_ReturnsStatusWithoutAccessToken()
    {
        var expected = new SquareTokenResponse(
            "Production",
            BackendToken,
            new DateTimeOffset(2026, 5, 26, 4, 0, 0, TimeSpan.Zero));
        string? requestedEnvironment = null;

        await using var factory = new SquareApiFactory(new StubSquareTokenService(
            responseFactory: environment =>
            {
                requestedEnvironment = environment;
                return Task.FromResult<SquareTokenResponse?>(expected);
            }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        using var response = await client.GetAsync("/api/v1/square/token?environment=production");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawContent = await response.Content.ReadAsStringAsync();
        var apiResult = await response.Content.ReadFromJsonAsync<ApiResult<SquareTokenStatusResponse>>();
        Assert.NotNull(apiResult);
        Assert.True(apiResult.Success);
        Assert.Equal(expected.Environment, apiResult.Data?.Environment);
        Assert.Equal(expected.UpdatedAt, apiResult.Data?.UpdatedAt);
        Assert.True(apiResult.Data?.Configured);
        Assert.True(apiResult.Data?.Enabled);
        Assert.DoesNotContain(BackendToken, rawContent, StringComparison.Ordinal);
        Assert.Equal("Production", requestedEnvironment);
    }

    [Fact]
    public async Task GetToken_ReturnsBadRequestForInvalidEnvironment()
    {
        await using var factory = new SquareApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        using var response = await client.GetAsync("/api/v1/square/token?environment=staging");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var apiResult = await response.Content.ReadFromJsonAsync<ApiResult<SquareTokenStatusResponse>>();
        Assert.NotNull(apiResult);
        Assert.False(apiResult.Success);
        Assert.Equal("SQUARE_ENVIRONMENT_INVALID", apiResult.ErrorCode);
    }

    [Fact]
    public async Task GetLocations_ReturnsBadRequestForInvalidEnvironment()
    {
        await using var factory = new SquareApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        using var response = await client.GetAsync("/api/v1/square/locations?environment=staging");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var apiResult = await response.Content.ReadFromJsonAsync<ApiResult<JsonElement>>();
        Assert.NotNull(apiResult);
        Assert.False(apiResult.Success);
        Assert.Equal("SQUARE_ENVIRONMENT_INVALID", apiResult.ErrorCode);
    }

    [Fact]
    public async Task GetToken_ReturnsStableNotFoundWhenTokenIsMissing()
    {
        await using var factory = new SquareApiFactory(new StubSquareTokenService(
            responseFactory: _ => Task.FromResult<SquareTokenResponse?>(null)));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        using var response = await client.GetAsync("/api/v1/square/token?environment=Sandbox");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var apiResult = await response.Content.ReadFromJsonAsync<ApiResult<SquareTokenStatusResponse>>();
        Assert.NotNull(apiResult);
        Assert.False(apiResult.Success);
        Assert.Equal("SQUARE_TOKEN_NOT_CONFIGURED", apiResult.ErrorCode);
        Assert.Equal("Square token is not configured for this environment.", apiResult.Message);
    }

    [Fact]
    public async Task GetLocations_ReturnsStableNotFoundWhenTokenIsMissing()
    {
        await using var factory = new SquareApiFactory(new StubSquareTokenService(
            responseFactory: _ => Task.FromResult<SquareTokenResponse?>(null)));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        using var response = await client.GetAsync("/api/v1/square/locations?environment=Sandbox");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var apiResult = await response.Content.ReadFromJsonAsync<ApiResult<JsonElement>>();
        Assert.NotNull(apiResult);
        Assert.False(apiResult.Success);
        Assert.Equal("SQUARE_TOKEN_NOT_CONFIGURED", apiResult.ErrorCode);
        Assert.Equal("Square token is not configured for this environment.", apiResult.Message);
        Assert.DoesNotContain(BackendToken, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_FailsWhenSquareBackendServiceIsMissing()
    {
        var expected = new SquareTokenResponse(
            "Production",
            BackendToken,
            new DateTimeOffset(2026, 5, 26, 4, 0, 0, TimeSpan.Zero));

        using var factory = new SquareApiFactory(
            new StubSquareTokenService(responseFactory: _ => Task.FromResult<SquareTokenResponse?>(expected)),
            registerBackendService: false);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains(nameof(ISquareTerminalBackendService), exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(BackendToken, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_FailsWhenSquareApiVersionIsInvalid()
    {
        using var factory = new SquareApiFactory(squareApiVersion: "20260520");

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Square:ApiVersion must use yyyy-MM-dd.", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(BackendToken, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetToken_ReturnsSanitizedServerErrorWhenServiceThrows()
    {
        var squareLogger = new RecordingLogger<SquareController>();
        await using var factory = new SquareApiFactory(new StubSquareTokenService(
            exceptionFactory: _ => new InvalidOperationException($"SQL timeout from POSM_SquareToken on server db01 for token {BackendToken}")),
            squareLogger: squareLogger);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        using var response = await client.GetAsync("/api/v1/square/token?environment=Production");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var apiResult = await response.Content.ReadFromJsonAsync<ApiResult<SquareTokenStatusResponse>>();
        Assert.NotNull(apiResult);
        var result = apiResult!;
        Assert.False(result.Success);
        Assert.Equal("SQUARE_TOKEN_READ_FAILED", result.ErrorCode);
        var message = result.Message ?? string.Empty;
        Assert.Equal("Failed to load Square token configuration.", message);
        Assert.DoesNotContain("SQL", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("POSM_SquareToken", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("db01", message, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            !message.Contains(BackendToken, StringComparison.Ordinal),
            "sanitized API error should not include token values");
        Assert.Contains(squareLogger.Lines, line =>
            line.Contains("token-status", StringComparison.Ordinal) &&
            line.Contains("SQUARE_TOKEN_READ_FAILED", StringComparison.Ordinal) &&
            line.Contains("InvalidOperationException", StringComparison.Ordinal) &&
            line.Contains("token [REDACTED]", StringComparison.Ordinal));
        Assert.All(squareLogger.Lines, line =>
            Assert.DoesNotContain(BackendToken, line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetLocations_ReturnsStableBackendFailureWithoutLeakingToken()
    {
        var expected = new SquareTokenResponse(
            "Production",
            BackendToken,
            new DateTimeOffset(2026, 5, 26, 4, 0, 0, TimeSpan.Zero));

        var squareLogger = new RecordingLogger<SquareController>();
        await using var factory = new SquareApiFactory(
            new StubSquareTokenService(responseFactory: _ => Task.FromResult<SquareTokenResponse?>(expected)),
            backendService: new ThrowingSquareTerminalBackendService(
                () => new SquareTerminalBackendException(
                    "SQUARE_BACKEND_REQUEST_FAILED",
                    "token opaque-api-square-token should never leak",
                    HttpStatusCode.ServiceUnavailable)),
            squareLogger: squareLogger);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        using var response = await client.GetAsync("/api/v1/square/locations?environment=Production");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var rawContent = await response.Content.ReadAsStringAsync();
        var apiResult = await response.Content.ReadFromJsonAsync<ApiResult<JsonElement>>();
        Assert.NotNull(apiResult);
        Assert.False(apiResult.Success);
        Assert.Equal("SQUARE_BACKEND_REQUEST_FAILED", apiResult.ErrorCode);
        Assert.Equal("Square backend request failed.", apiResult.Message);
        Assert.DoesNotContain(BackendToken, rawContent, StringComparison.Ordinal);
        Assert.Contains(squareLogger.Lines, line =>
            line.Contains("locations", StringComparison.Ordinal) &&
            line.Contains("SQUARE_BACKEND_REQUEST_FAILED", StringComparison.Ordinal) &&
            line.Contains("503", StringComparison.Ordinal) &&
            line.Contains("SquareTerminalBackendException", StringComparison.Ordinal) &&
            line.Contains("token [REDACTED]", StringComparison.Ordinal));
        Assert.All(squareLogger.Lines, line =>
            Assert.DoesNotContain(BackendToken, line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetLocations_ReturnsStableUpstreamFailureWithoutLeakingToken()
    {
        var expected = new SquareTokenResponse(
            "Production",
            BackendToken,
            new DateTimeOffset(2026, 5, 26, 4, 0, 0, TimeSpan.Zero));

        var squareLogger = new RecordingLogger<SquareController>();
        await using var factory = new SquareApiFactory(
            new StubSquareTokenService(responseFactory: _ => Task.FromResult<SquareTokenResponse?>(expected)),
            backendService: new ThrowingSquareTerminalBackendService(
                () => new SquareTerminalRestException(
                    HttpStatusCode.BadGateway,
                    $"Square upstream exploded with token {BackendToken}")),
            squareLogger: squareLogger);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        using var response = await client.GetAsync("/api/v1/square/locations?environment=Production");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var rawContent = await response.Content.ReadAsStringAsync();
        var apiResult = await response.Content.ReadFromJsonAsync<ApiResult<JsonElement>>();
        Assert.NotNull(apiResult);
        Assert.False(apiResult.Success);
        Assert.Equal("SQUARE_UPSTREAM_REQUEST_FAILED", apiResult.ErrorCode);
        Assert.Equal("Square upstream request failed.", apiResult.Message);
        Assert.DoesNotContain(BackendToken, rawContent, StringComparison.Ordinal);
        Assert.Contains(squareLogger.Lines, line =>
            line.Contains("locations", StringComparison.Ordinal) &&
            line.Contains("SQUARE_UPSTREAM_REQUEST_FAILED", StringComparison.Ordinal) &&
            line.Contains("502", StringComparison.Ordinal) &&
            line.Contains("SquareTerminalRestException", StringComparison.Ordinal) &&
            line.Contains("token [REDACTED]", StringComparison.Ordinal));
        Assert.All(squareLogger.Lines, line =>
            Assert.DoesNotContain(BackendToken, line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateDeviceCode_ReturnsBadRequestWhenIdempotencyKeyIsMissing()
    {
        var tokenRequested = false;
        var backendService = new CapturingSquareTerminalBackendService();
        var expected = new SquareTokenResponse(
            "Production",
            BackendToken,
            new DateTimeOffset(2026, 5, 26, 4, 0, 0, TimeSpan.Zero));

        await using var factory = new SquareApiFactory(
            new StubSquareTokenService(responseFactory: _ =>
            {
                tokenRequested = true;
                return Task.FromResult<SquareTokenResponse?>(expected);
            }),
            backendService: backendService);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        using var response = await client.PostAsJsonAsync(
            "/api/v1/square/device-codes",
            new SquareCreateDeviceCodeRequest(
                "Production",
                IdempotencyKey: "",
                LocationId: "location-001",
                Name: "Front Counter"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var apiResult = await response.Content.ReadFromJsonAsync<ApiResult<SquareDeviceCodeDto>>();
        Assert.NotNull(apiResult);
        Assert.False(apiResult.Success);
        Assert.Equal("SQUARE_IDEMPOTENCY_KEY_REQUIRED", apiResult.ErrorCode);
        Assert.Equal("idempotencyKey is required.", apiResult.Message);
        Assert.False(tokenRequested);
        Assert.Equal(0, backendService.CreateDeviceCodeCalls);
    }

    [Fact]
    public async Task CreateCheckout_ReturnsBadRequestBeforeReadingTokenWhenIdempotencyKeyIsMissing()
    {
        var tokenRequested = false;
        var backendService = new CapturingSquareTerminalBackendService();
        var expected = new SquareTokenResponse(
            "Production",
            BackendToken,
            new DateTimeOffset(2026, 5, 26, 4, 0, 0, TimeSpan.Zero));

        await using var factory = new SquareApiFactory(
            new StubSquareTokenService(responseFactory: _ =>
            {
                tokenRequested = true;
                return Task.FromResult<SquareTokenResponse?>(expected);
            }),
            backendService: backendService);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        using var response = await client.PostAsJsonAsync(
            "/api/v1/square/checkouts",
            new SquareCreateCheckoutRequest(
                "Production",
                IdempotencyKey: " ",
                DeviceId: "device-001",
                LocationId: "location-001",
                AmountMoney: new SquareMoneyDto(1299, "AUD")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var apiResult = await response.Content.ReadFromJsonAsync<ApiResult<SquareCheckoutStatusResponse>>();
        Assert.NotNull(apiResult);
        Assert.False(apiResult.Success);
        Assert.Equal("SQUARE_IDEMPOTENCY_KEY_REQUIRED", apiResult.ErrorCode);
        Assert.Equal("idempotencyKey is required.", apiResult.Message);
        Assert.False(tokenRequested);
        Assert.Equal(0, backendService.CreateCheckoutCalls);
    }

    [Fact]
    public async Task CreateRefund_ReturnsBadRequestBeforeReadingTokenWhenIdempotencyKeyIsMissing()
    {
        var tokenRequested = false;
        var backendService = new CapturingSquareTerminalBackendService();
        var expected = new SquareTokenResponse(
            "Production",
            BackendToken,
            new DateTimeOffset(2026, 5, 26, 4, 0, 0, TimeSpan.Zero));

        await using var factory = new SquareApiFactory(
            new StubSquareTokenService(responseFactory: _ =>
            {
                tokenRequested = true;
                return Task.FromResult<SquareTokenResponse?>(expected);
            }),
            backendService: backendService);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        using var response = await client.PostAsJsonAsync(
            "/api/v1/square/refunds",
            new SquareRefundRequest(
                "Production",
                IdempotencyKey: "",
                PaymentId: "payment-001",
                AmountMoney: new SquareMoneyDto(500, "AUD")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var apiResult = await response.Content.ReadFromJsonAsync<ApiResult<SquareRefundResponse>>();
        Assert.NotNull(apiResult);
        Assert.False(apiResult.Success);
        Assert.Equal("SQUARE_IDEMPOTENCY_KEY_REQUIRED", apiResult.ErrorCode);
        Assert.Equal("idempotencyKey is required.", apiResult.Message);
        Assert.False(tokenRequested);
        Assert.Equal(0, backendService.CreateRefundCalls);
    }

    [Fact]
    public async Task ReceiveWebhook_PassesRawBodyHeadersAndNotificationUrlToBackendService()
    {
        const string signature = "test-signature";
        const string squareEnvironment = "sandbox";
        const string rawBody = "{\"merchant_id\":\"merchant-123\",\"type\":\"terminal.checkout.updated\"}";
        var backendService = new CapturingSquareTerminalBackendService();

        await using var factory = new SquareApiFactory(backendService: backendService);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/square/webhooks");
        request.Headers.TryAddWithoutValidation("x-square-hmacsha256-signature", signature);
        request.Headers.TryAddWithoutValidation("square-environment", squareEnvironment);
        request.Content = new StringContent(rawBody, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(backendService.LastWebhookRequest);
        Assert.Equal(signature, backendService.LastWebhookRequest!.SignatureHeader);
        Assert.Equal(squareEnvironment, backendService.LastWebhookRequest.SquareEnvironmentHeader);
        Assert.Equal(rawBody, backendService.LastWebhookRequest.RawBody);
        Assert.Equal("http://localhost/api/v1/square/webhooks", backendService.LastWebhookRequest.NotificationUrl);
    }

    [Fact]
    public async Task ReceiveWebhook_ReturnsStableForbiddenWhenSignatureIsInvalid()
    {
        const string rawBody = "{\"event_id\":\"event-001\",\"type\":\"terminal.checkout.updated\"}";

        await using var factory = new SquareApiFactory(
            backendService: new ThrowingSquareTerminalBackendService(
                () => new SquareTerminalBackendException(
                    "SQUARE_WEBHOOK_SIGNATURE_INVALID",
                    "Square webhook signature is invalid.",
                    HttpStatusCode.Forbidden)));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/square/webhooks");
        request.Headers.TryAddWithoutValidation("x-square-hmacsha256-signature", "invalid-signature");
        request.Content = new StringContent(rawBody, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var apiResult = await response.Content.ReadFromJsonAsync<ApiResult<SquareWebhookAcceptedResponse>>();
        Assert.NotNull(apiResult);
        Assert.False(apiResult.Success);
        Assert.Equal("SQUARE_WEBHOOK_SIGNATURE_INVALID", apiResult.ErrorCode);
        Assert.Equal("Square webhook signature is invalid.", apiResult.Message);
    }

    [Fact]
    public void Startup_FailsWhenSquareTokenSchemaInitializerThrows()
    {
        using var factory = new SquareApiFactory(
            schemaInitializer: new ThrowingSquareTokenSchemaInitializer(
                new InvalidOperationException("schema bootstrap failed without token values")));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("schema bootstrap failed", exception.Message);
        Assert.DoesNotContain(BackendToken, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_FailsWhenSquareWebhookSchemaInitializerThrows()
    {
        using var factory = new SquareApiFactory(
            webhookSchemaInitializer: new ThrowingSquareWebhookSchemaInitializer(
                new InvalidOperationException("square webhook schema bootstrap failed without secret leakage")),
            posmConnectionString: "Server=(localdb)\\MSSQLLocalDB;Database=hbpos-test;Trusted_Connection=True;");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("square webhook schema bootstrap failed", exception.Message);
        Assert.DoesNotContain(BackendToken, exception.Message, StringComparison.Ordinal);
    }

    private static void AssertHttpTemplate(string methodName, Type attributeType, string expectedTemplate)
    {
        var attribute = typeof(SquareController)
            .GetMethod(methodName)?
            .GetCustomAttributes(attributeType, inherit: false)
            .OfType<HttpMethodAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal(expectedTemplate, attribute!.Template);
    }

    private sealed class SquareApiFactory(
        ISquareTokenService? squareTokenService = null,
        ISquareTokenSchemaInitializer? schemaInitializer = null,
        ISquareWebhookSchemaInitializer? webhookSchemaInitializer = null,
        ISquareTerminalBackendService? backendService = null,
        string? posmConnectionString = null,
        string? squareApiVersion = null,
        RecordingLogger<SquareController>? squareLogger = null,
        bool registerBackendService = true)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            if (!string.IsNullOrWhiteSpace(posmConnectionString) || squareApiVersion is not null)
            {
                builder.ConfigureAppConfiguration((_, configurationBuilder) =>
                {
                    // 测试配置只覆盖显式传入项，避免改变默认启动路径。
                    var settings = new Dictionary<string, string?>();
                    if (!string.IsNullOrWhiteSpace(posmConnectionString))
                    {
                        settings["ConnectionStrings:MainConnection"] = "Server=(localdb)\\MSSQLLocalDB;Database=hbpos-main-test;Trusted_Connection=True;";
                        settings["ConnectionStrings:PosmConnection"] = posmConnectionString;
                    }

                    if (squareApiVersion is not null)
                    {
                        settings["Square:ApiVersion"] = squareApiVersion;
                    }

                    configurationBuilder.AddInMemoryCollection(settings);
                });
            }

            builder.ConfigureServices(services =>
            {
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                    options.DefaultScheme = TestAuthHandler.SchemeName;
                });

                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.SchemeName,
                        _ => { });

                services.RemoveAll<ISquareTokenService>();
                services.AddSingleton(squareTokenService ?? new StubSquareTokenService());

                services.RemoveAll<IStoreSchemaInitializer>();
                services.AddSingleton<IStoreSchemaInitializer>(new NoOpStoreSchemaInitializer());

                services.RemoveAll<IDeviceRuntimeStatusSchemaInitializer>();
                services.AddSingleton<IDeviceRuntimeStatusSchemaInitializer>(new NoOpDeviceRuntimeStatusSchemaInitializer());

                services.RemoveAll<IOperationAuditSchemaInitializer>();
                services.AddSingleton<IOperationAuditSchemaInitializer>(new TestNoOpOperationAuditSchemaInitializer());

                services.RemoveAll<ISquareTokenSchemaInitializer>();
                services.AddSingleton(schemaInitializer ?? new NoOpSquareTokenSchemaInitializer());

                services.RemoveAll<ILinklyCloudCredentialSchemaInitializer>();
                services.AddSingleton<ILinklyCloudCredentialSchemaInitializer>(
                    new NoOpLinklyCloudCredentialSchemaInitializer());

                services.RemoveAll<ILinklyCloudBackendAsyncSchemaInitializer>();
                services.AddSingleton<ILinklyCloudBackendAsyncSchemaInitializer>(
                    new NoOpLinklyCloudBackendAsyncSchemaInitializer());

                services.RemoveAll<IAdvertisementSchemaInitializer>();
                services.AddSingleton<IAdvertisementSchemaInitializer>(new NoOpAdvertisementSchemaInitializer());

                services.RemoveAll<ISquareWebhookSchemaInitializer>();
                services.AddSingleton(webhookSchemaInitializer ?? new NoOpSquareWebhookSchemaInitializer());

                services.RemoveAll<ISquareTerminalBackendService>();
                if (registerBackendService)
                {
                    services.AddSingleton(backendService ?? new CapturingSquareTerminalBackendService());
                }

                if (squareLogger is not null)
                {
                    services.AddSingleton<ILogger<SquareController>>(squareLogger);
                }
            });
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Lines.Add(formatter(state, exception));
        }
    }

    private sealed class NoOpSquareTokenSchemaInitializer : ISquareTokenSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSquareTokenSchemaInitializer(Exception exception) : ISquareTokenSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            throw exception;
        }
    }

    private sealed class NoOpSquareWebhookSchemaInitializer : ISquareWebhookSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSquareWebhookSchemaInitializer(Exception exception) : ISquareWebhookSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            throw exception;
        }
    }

    private sealed class NoOpStoreSchemaInitializer : IStoreSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpDeviceRuntimeStatusSchemaInitializer : IDeviceRuntimeStatusSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpLinklyCloudCredentialSchemaInitializer : ILinklyCloudCredentialSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpLinklyCloudBackendAsyncSchemaInitializer : ILinklyCloudBackendAsyncSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpAdvertisementSchemaInitializer : IAdvertisementSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "SquareTestAuth";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(header) || !string.Equals(header, "Test", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim(DeviceAuthConstants.DeviceCodeClaim, "POS-01"),
                new Claim(DeviceAuthConstants.StoreCodeClaim, "S01"),
                new Claim(DeviceAuthConstants.HardwareIdClaim, "HW-001")
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class StubSquareTokenService(
        Func<string, Task<SquareTokenResponse?>>? responseFactory = null,
        Func<string, Exception>? exceptionFactory = null) : ISquareTokenService
    {
        public Task<SquareTokenResponse?> GetActiveTokenAsync(
            string environment,
            CancellationToken cancellationToken)
        {
            if (exceptionFactory is not null)
            {
                throw exceptionFactory(environment);
            }

            if (responseFactory is not null)
            {
                return responseFactory(environment);
            }

            return Task.FromResult<SquareTokenResponse?>(null);
        }
    }

    private sealed class ThrowingSquareTerminalBackendService(Func<Exception> exceptionFactory) : ISquareTerminalBackendService
    {
        public Task<IReadOnlyList<SquareLocationDto>> GetLocationsAsync(string environment, CancellationToken cancellationToken)
        {
            throw exceptionFactory();
        }

        public Task<IReadOnlyList<SquareDeviceDto>> GetDevicesAsync(string environment, string locationId, CancellationToken cancellationToken)
        {
            throw exceptionFactory();
        }

        public Task<IReadOnlyList<SquareDeviceCodeDto>> GetDeviceCodesAsync(string environment, string locationId, CancellationToken cancellationToken)
        {
            throw exceptionFactory();
        }

        public Task<SquareDeviceCodeDto> CreateDeviceCodeAsync(SquareCreateDeviceCodeRequest request, CancellationToken cancellationToken)
        {
            throw exceptionFactory();
        }

        public Task<SquareDeviceCodeDto?> GetDeviceCodeAsync(string environment, string deviceCodeId, CancellationToken cancellationToken)
        {
            throw exceptionFactory();
        }

        public Task<SquareCheckoutStatusResponse> CreateCheckoutAsync(SquareCreateCheckoutRequest request, CancellationToken cancellationToken)
        {
            throw exceptionFactory();
        }

        public Task<SquareCheckoutStatusResponse?> GetCheckoutAsync(string environment, string checkoutId, CancellationToken cancellationToken)
        {
            throw exceptionFactory();
        }

        public Task<SquarePaymentStatusDto?> GetPaymentAsync(string environment, string paymentId, CancellationToken cancellationToken)
        {
            throw exceptionFactory();
        }

        public Task<SquareCheckoutStatusResponse> CancelCheckoutAsync(string checkoutId, SquareCheckoutActionRequest request, CancellationToken cancellationToken)
        {
            throw exceptionFactory();
        }

        public Task<SquareCheckoutStatusResponse> DismissCheckoutAsync(string checkoutId, SquareCheckoutActionRequest request, CancellationToken cancellationToken)
        {
            throw exceptionFactory();
        }

        public Task<SquareRefundResponse> CreateRefundAsync(SquareRefundRequest request, CancellationToken cancellationToken)
        {
            throw exceptionFactory();
        }

        public Task<SquareWebhookAcceptedResponse> AcceptWebhookAsync(SquareWebhookRequest request, CancellationToken cancellationToken)
        {
            throw exceptionFactory();
        }
    }

    private sealed class CapturingSquareTerminalBackendService : ISquareTerminalBackendService
    {
        public SquareWebhookRequest? LastWebhookRequest { get; private set; }

        public int CreateDeviceCodeCalls { get; private set; }

        public int CreateCheckoutCalls { get; private set; }

        public int CreateRefundCalls { get; private set; }

        public Task<IReadOnlyList<SquareLocationDto>> GetLocationsAsync(string environment, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<SquareDeviceDto>> GetDevicesAsync(string environment, string locationId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<SquareDeviceCodeDto>> GetDeviceCodesAsync(string environment, string locationId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<SquareDeviceCodeDto> CreateDeviceCodeAsync(SquareCreateDeviceCodeRequest request, CancellationToken cancellationToken)
        {
            CreateDeviceCodeCalls++;
            return Task.FromResult(new SquareDeviceCodeDto(
                "device-code-001",
                Code: "ABCDEF",
                Status: "UNPAIRED",
                LocationId: request.LocationId,
                Name: request.Name));
        }

        public Task<SquareDeviceCodeDto?> GetDeviceCodeAsync(string environment, string deviceCodeId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<SquareCheckoutStatusResponse> CreateCheckoutAsync(SquareCreateCheckoutRequest request, CancellationToken cancellationToken)
        {
            CreateCheckoutCalls++;
            return Task.FromResult(new SquareCheckoutStatusResponse(
                "checkout-001",
                request.Environment,
                Status: "PENDING",
                DeviceId: request.DeviceId,
                LocationId: request.LocationId,
                AmountMoney: request.AmountMoney));
        }

        public Task<SquareCheckoutStatusResponse?> GetCheckoutAsync(string environment, string checkoutId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<SquarePaymentStatusDto?> GetPaymentAsync(string environment, string paymentId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<SquareCheckoutStatusResponse> CancelCheckoutAsync(string checkoutId, SquareCheckoutActionRequest request, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<SquareCheckoutStatusResponse> DismissCheckoutAsync(string checkoutId, SquareCheckoutActionRequest request, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<SquareRefundResponse> CreateRefundAsync(SquareRefundRequest request, CancellationToken cancellationToken)
        {
            CreateRefundCalls++;
            return Task.FromResult(new SquareRefundResponse(
                "refund-001",
                request.Environment,
                Status: "PENDING",
                PaymentId: request.PaymentId,
                AmountMoney: request.AmountMoney));
        }

        public Task<SquareWebhookAcceptedResponse> AcceptWebhookAsync(SquareWebhookRequest request, CancellationToken cancellationToken)
        {
            LastWebhookRequest = request;
            return Task.FromResult(new SquareWebhookAcceptedResponse("accepted", Message: "captured"));
        }
    }
}
