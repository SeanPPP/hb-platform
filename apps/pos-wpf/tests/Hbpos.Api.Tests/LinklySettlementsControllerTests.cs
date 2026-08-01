using System.Reflection;
using System.Security.Claims;
using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.Linkly;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Tests;

public sealed class LinklySettlementsControllerTests
{
    [Fact]
    public void Controller_uses_device_authentication_without_cashier_permission_policy()
    {
        var authorize = Assert.Single(
            typeof(LinklySettlementsController).GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(DeviceAuthConstants.Scheme, authorize.AuthenticationSchemes);
        Assert.Null(authorize.Policy);
    }

    [Fact]
    public async Task Sync_requires_authenticated_device_scope()
    {
        var service = new RecordingSyncService();
        var controller = CreateController(service);

        var result = await controller.Sync(CreateRequest(), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Null(service.Request);
    }

    [Fact]
    public async Task Sync_rejects_body_scope_that_differs_from_claims()
    {
        var service = new RecordingSyncService();
        var controller = CreateController(service, "S001", "POS-01");
        var request = CreateRequest() with { StoreCode = "S002" };

        var result = await controller.Sync(request, CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Null(service.Request);
    }

    [Fact]
    public async Task Sync_passes_authoritative_claim_scope_to_service()
    {
        var service = new RecordingSyncService();
        var controller = CreateController(service, "S001", "POS-01");

        var result = await controller.Sync(CreateRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.IsType<LinklySettlementSyncResponse>(ok.Value);
        Assert.Equal("S001", service.StoreCode);
        Assert.Equal("POS-01", service.DeviceCode);
    }

    [Fact]
    public async Task Sync_maps_revision_conflict_to_http_409()
    {
        var service = new RecordingSyncService
        {
            Exception = new LinklySettlementConflictException("REVISION_CONTENT_CONFLICT", "conflict")
        };
        var controller = CreateController(service, "S001", "POS-01");

        var result = await controller.Sync(CreateRequest(), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    private static LinklySettlementsController CreateController(
        ILinklySettlementSyncService service,
        string? storeCode = null,
        string? deviceCode = null)
    {
        var controller = new LinklySettlementsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        if (storeCode is not null && deviceCode is not null)
        {
            controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(DeviceAuthConstants.StoreCodeClaim, storeCode),
                    new Claim(DeviceAuthConstants.DeviceCodeClaim, deviceCode)
                ],
                DeviceAuthConstants.Scheme));
        }

        return controller;
    }

    private static LinklySettlementSyncRequest CreateRequest()
    {
        return new LinklySettlementSyncRequest(
            1,
            Guid.NewGuid(),
            "S001",
            "POS-01",
            new DateOnly(2026, 8, 1),
            "LocalIp",
            "Production",
            "session-1",
            "Succeeded",
            "00",
            "APPROVED",
            "TOTAL=10.00",
            ["SETTLEMENT RECEIPT"],
            new DateTimeOffset(2026, 8, 1, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 1, 1, 0, TimeSpan.Zero),
            null,
            null,
            0,
            null,
            1);
    }

    private sealed class RecordingSyncService : ILinklySettlementSyncService
    {
        public LinklySettlementSyncRequest? Request { get; private set; }

        public string? StoreCode { get; private set; }

        public string? DeviceCode { get; private set; }

        public Exception? Exception { get; init; }

        public Task<LinklySettlementSyncResponse> SyncAsync(
            LinklySettlementSyncRequest request,
            string storeCode,
            string deviceCode,
            CancellationToken cancellationToken)
        {
            Request = request;
            StoreCode = storeCode;
            DeviceCode = deviceCode;
            return Exception is null
                ? Task.FromResult(new LinklySettlementSyncResponse(true, false, request.ClientRevision))
                : Task.FromException<LinklySettlementSyncResponse>(Exception);
        }
    }
}
