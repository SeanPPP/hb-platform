using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Hbpos.Api;
using BlazorApp.Shared.DTOs;
using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hbpos.Api.Tests;

public sealed class OperationAuditControllerTests
{
    [Fact]
    public async Task Batch_requires_authenticated_device_claims()
    {
        var service = new RecordingOperationAuditIngestService();
        var controller = CreateController(service);

        var action = await controller.Batch(CreateRequest(), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(action.Result);
        Assert.Null(service.Request);
    }

    [Fact]
    public async Task Batch_rejects_event_scope_that_differs_from_device_claims()
    {
        var service = new RecordingOperationAuditIngestService();
        var controller = CreateController(service, "STORE-1", "POS-1");
        var request = CreateRequest();
        request.Events[0].StoreCode = "STORE-2";

        var action = await controller.Batch(request, CancellationToken.None);

        Assert.IsType<ForbidResult>(action.Result);
        Assert.Null(service.Request);
    }

    [Fact]
    public async Task Batch_requires_exact_case_sensitive_scope_match()
    {
        var service = new RecordingOperationAuditIngestService();
        var controller = CreateController(service, "STORE-1", "POS-1");
        var request = CreateRequest();
        request.Events[0].StoreCode = "store-1";

        var action = await controller.Batch(request, CancellationToken.None);

        Assert.IsType<ForbidResult>(action.Result);
        Assert.Null(service.Request);
    }

    [Fact]
    public async Task Batch_passes_authoritative_claim_scope_to_ingest_service()
    {
        var service = new RecordingOperationAuditIngestService();
        var controller = CreateController(service, "STORE-1", "POS-1");
        var request = CreateRequest();

        var action = await controller.Batch(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var payload = Assert.IsType<OperationAuditBatchResultDto>(ok.Value);
        Assert.Equal(1, payload.AcceptedCount);
        Assert.Same(request, service.Request);
        Assert.Equal("STORE-1", service.StoreCode);
        Assert.Equal("POS-1", service.DeviceCode);
    }

    [Fact]
    public async Task Batch_rejects_more_than_one_hundred_events()
    {
        var service = new RecordingOperationAuditIngestService();
        var controller = CreateController(service, "STORE-1", "POS-1");
        var request = new OperationAuditBatchRequestDto
        {
            Events = Enumerable.Range(0, 101)
                .Select(_ => CreateRequest().Events[0])
                .ToList()
        };

        var action = await controller.Batch(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action.Result);
        Assert.Null(service.Request);
    }

    [Fact]
    public async Task Batch_rejects_request_larger_than_four_mebibytes()
    {
        var service = new RecordingOperationAuditIngestService();
        var controller = CreateController(service, "STORE-1", "POS-1");
        controller.HttpContext.Request.ContentLength = (4 * 1024 * 1024) + 1;

        var action = await controller.Batch(CreateRequest(), CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, result.StatusCode);
        Assert.Null(service.Request);
    }

    [Fact]
    public async Task Batch_rejects_null_event_in_payload()
    {
        var service = new RecordingOperationAuditIngestService();
        var controller = CreateController(service, "STORE-1", "POS-1");
        var request = CreateRequest();
        request.Events[0] = null!;

        var action = await controller.Batch(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action.Result);
        Assert.Null(service.Request);
    }

    [Fact]
    public async Task Batch_http_pipeline_requires_bearer_and_device_headers_then_uses_authenticated_scope()
    {
        var service = new RecordingOperationAuditIngestService();
        await using var factory = new OperationAuditApiFactory(service);
        using var client = factory.CreateClient();

        var unauthorized = await client.PostAsJsonAsync(
            "/api/v1/operation-audits/batch",
            CreateRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Null(service.Request);

        using var authenticatedRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/operation-audits/batch")
        {
            Content = JsonContent.Create(CreateRequest())
        };
        authenticatedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "valid-device-token");
        authenticatedRequest.Headers.Add(DeviceAuthConstants.StoreCodeHeader, "STORE-1");
        authenticatedRequest.Headers.Add(DeviceAuthConstants.DeviceCodeHeader, "POS-1");
        authenticatedRequest.Headers.Add(DeviceAuthConstants.HardwareIdHeader, "HW-1");

        var response = await client.SendAsync(authenticatedRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OperationAuditBatchResultDto>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.AcceptedCount);
        Assert.Equal("STORE-1", service.StoreCode);
        Assert.Equal("POS-1", service.DeviceCode);
    }

    private static OperationAuditsController CreateController(
        IOperationAuditIngestService service,
        string? storeCode = null,
        string? deviceCode = null)
    {
        var httpContext = new DefaultHttpContext();
        if (storeCode is not null && deviceCode is not null)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(DeviceAuthConstants.StoreCodeClaim, storeCode),
                new Claim(DeviceAuthConstants.DeviceCodeClaim, deviceCode)
            ], DeviceAuthConstants.Scheme));
        }

        return new OperationAuditsController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static OperationAuditBatchRequestDto CreateRequest()
    {
        return new OperationAuditBatchRequestDto
        {
            Events =
            [
                new OperationAuditEventDto
                {
                    EventId = Guid.NewGuid(),
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    OperationType = "CASH_DRAWER_OPEN",
                    Outcome = "Succeeded",
                    StoreCode = "STORE-1",
                    DeviceCode = "POS-1"
                }
            ]
        };
    }

    private sealed class RecordingOperationAuditIngestService : IOperationAuditIngestService
    {
        public OperationAuditBatchRequestDto? Request { get; private set; }

        public string? StoreCode { get; private set; }

        public string? DeviceCode { get; private set; }

        public Task<OperationAuditBatchResultDto> IngestAsync(
            OperationAuditBatchRequestDto request,
            string storeCode,
            string deviceCode,
            CancellationToken cancellationToken)
        {
            Request = request;
            StoreCode = storeCode;
            DeviceCode = deviceCode;
            return Task.FromResult(new OperationAuditBatchResultDto
            {
                AcceptedCount = request.Events.Count,
                Results = request.Events.Select(x => new OperationAuditItemResultDto
                {
                    EventId = x.EventId,
                    Status = "accepted"
                }).ToList()
            });
        }
    }

    private sealed class OperationAuditApiFactory(IOperationAuditIngestService ingestService)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDeviceAuthorizationService>();
                services.AddSingleton<IDeviceAuthorizationService>(new TestDeviceAuthorizationService());
                services.RemoveAll<IOperationAuditIngestService>();
                services.AddSingleton(ingestService);
                services.RemoveAll<IStoreSchemaInitializer>();
                services.AddSingleton<IStoreSchemaInitializer>(new NoOpStoreSchemaInitializer());
                services.RemoveAll<IAdvertisementSchemaInitializer>();
                services.AddSingleton<IAdvertisementSchemaInitializer>(new NoOpAdvertisementSchemaInitializer());
                services.RemoveAll<ILinklyCloudCredentialSchemaInitializer>();
                services.AddSingleton<ILinklyCloudCredentialSchemaInitializer>(new NoOpLinklyCloudCredentialSchemaInitializer());
                services.RemoveAll<ISquareTokenSchemaInitializer>();
                services.AddSingleton<ISquareTokenSchemaInitializer>(new NoOpSquareTokenSchemaInitializer());
                services.RemoveAll<IOperationAuditSchemaInitializer>();
                services.AddSingleton<IOperationAuditSchemaInitializer>(new TestNoOpOperationAuditSchemaInitializer());
                services.RemoveAll<IDeviceRuntimeStatusSchemaInitializer>();
                services.AddSingleton<IDeviceRuntimeStatusSchemaInitializer>(new NoOpDeviceRuntimeStatusSchemaInitializer());
                services.RemoveAll<ILinklyCloudBackendAsyncSchemaInitializer>();
                services.AddSingleton<ILinklyCloudBackendAsyncSchemaInitializer>(new NoOpLinklyCloudBackendAsyncSchemaInitializer());
                services.RemoveAll<ISquareWebhookSchemaInitializer>();
                services.AddSingleton<ISquareWebhookSchemaInitializer>(new NoOpSquareWebhookSchemaInitializer());
            });
        }
    }

    private sealed class TestDeviceAuthorizationService : IDeviceAuthorizationService
    {
        public Task<DeviceAuthorizationResult?> ValidateAsync(
            string authorizationCode,
            string deviceCode,
            string storeCode,
            string? hardwareId,
            CancellationToken cancellationToken)
        {
            DeviceAuthorizationResult? result =
                authorizationCode == "valid-device-token" &&
                deviceCode == "POS-1" &&
                storeCode == "STORE-1" &&
                hardwareId == "HW-1"
                    ? new DeviceAuthorizationResult(deviceCode, storeCode, hardwareId)
                    : null;
            return Task.FromResult(result);
        }
    }

    private sealed class NoOpStoreSchemaInitializer : IStoreSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpAdvertisementSchemaInitializer : IAdvertisementSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpLinklyCloudCredentialSchemaInitializer : ILinklyCloudCredentialSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpSquareTokenSchemaInitializer : ISquareTokenSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpDeviceRuntimeStatusSchemaInitializer : IDeviceRuntimeStatusSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpLinklyCloudBackendAsyncSchemaInitializer : ILinklyCloudBackendAsyncSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpSquareWebhookSchemaInitializer : ISquareWebhookSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
