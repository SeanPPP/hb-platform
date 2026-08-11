using System.Reflection;
using System.Security.Claims;
using Hbpos.Api.Auth;
using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.HeldOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Tests;

public sealed class HeldOrdersControllerTests
{
    private static readonly Guid HoldGuid = Guid.Parse("12345678-1234-1234-1234-123456789012");
    private static readonly Guid ClaimGuid = Guid.Parse("87654321-4321-4321-4321-210987654321");
    private static readonly SharedHeldOrderIdentity TrustedIdentity =
        new("S01", "POS-01", "C01", "持单收银员", ["Permissions.PosTerminal.Sales.RecallOrder"]);

    [Fact]
    public void Held_order_routes_are_stable_and_authorized()
    {
        AssertRoute(nameof(HeldOrdersController.Capabilities), "capabilities", isGet: true, policy: null);
        AssertRoute(nameof(HeldOrdersController.Publish), null, isGet: false, CashierAuthorizationPolicies.HoldOrder);
        AssertRoute(nameof(HeldOrdersController.ListPending), null, isGet: true, CashierAuthorizationPolicies.RecallOrder);
        AssertRoute(nameof(HeldOrdersController.Prepare), "{holdGuid:guid}/claims/prepare", false, CashierAuthorizationPolicies.RecallOrder);
        AssertRoute(nameof(HeldOrdersController.Activate), "{holdGuid:guid}/claims/{claimGuid:guid}/activate", false, CashierAuthorizationPolicies.RecallOrder);
        AssertRoute(nameof(HeldOrdersController.Release), "{holdGuid:guid}/claims/{claimGuid:guid}/release", false, CashierAuthorizationPolicies.RecallOrder);
        AssertRoute(nameof(HeldOrdersController.ForceRelease), "{holdGuid:guid}/claims/{claimGuid:guid}/force-release", false, "Cashier.HistoryRecall");
        AssertRoute(nameof(HeldOrdersController.ClaimsMine), "claims/mine", isGet: true, CashierAuthorizationPolicies.RecallOrder);
    }

    [Fact]
    public void Cashier_policies_map_hold_recall_and_force_release_to_their_specific_pos_permissions()
    {
        var options = new AuthorizationOptions();
        CashierAuthorizationPolicies.AddPolicies(options);

        var hold = options.GetPolicy(CashierAuthorizationPolicies.HoldOrder);
        var recall = options.GetPolicy(CashierAuthorizationPolicies.RecallOrder);
        var historyRecall = options.GetPolicy("Cashier.HistoryRecall");
        Assert.NotNull(hold);
        Assert.NotNull(recall);
        Assert.NotNull(historyRecall);
        var holdRequirement = Assert.Single(hold!.Requirements.OfType<CashierPermissionRequirement>());
        var recallRequirement = Assert.Single(recall!.Requirements.OfType<CashierPermissionRequirement>());
        var historyRecallRequirement = Assert.Single(historyRecall!.Requirements.OfType<CashierPermissionRequirement>());
        Assert.Equal(["Permissions.PosTerminal.Sales.HoldOrder"], holdRequirement.PermissionCodes);
        Assert.Equal(["Permissions.PosTerminal.Sales.RecallOrder"], recallRequirement.PermissionCodes);
        Assert.Equal(["Permissions.PosTerminal.History.Recall"], historyRecallRequirement.PermissionCodes);
    }

    [Fact]
    public void Capability_returns_envelope_with_disabled_default()
    {
        var controller = CreateController(new FakeService());

        var action = controller.Capabilities();

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var envelope = Assert.IsType<ApiResult<SharedHeldOrderCapabilitiesResponse>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.False(envelope.Data!.Enabled);
        Assert.Equal(120, envelope.Data.PreparedTtlSeconds);
    }

    [Fact]
    public async Task Publish_returns_200_envelope_and_uses_verified_identity()
    {
        var service = new FakeService();
        var identityResolver = new FakeIdentityResolver(TrustedIdentity);
        var controller = CreateController(service, identityResolver);
        controller.HttpContext.User = AuthenticatedDevice("S01", "POS-01");

        var action = await controller.Publish(
            SharedHeldOrderServiceTestSupport.PublishRequest(holdGuid: HoldGuid),
            CancellationToken.None);

        AssertOk(action, data => Assert.Equal(HoldGuid, data.HoldGuid));
        Assert.Equal(TrustedIdentity, service.LastIdentity);
        Assert.Equal(1, identityResolver.ResolveCalls);
    }

    [Fact]
    public async Task Publish_with_forged_store_scope_returns_403_before_service_runs()
    {
        var service = new FakeService();
        var controller = CreateController(service, new FakeIdentityResolver(TrustedIdentity));
        controller.HttpContext.User = AuthenticatedDevice("S01", "POS-01");

        var action = await controller.Publish(
            SharedHeldOrderServiceTestSupport.PublishRequest(holdGuid: HoldGuid) with { StoreCode = "S02" },
            CancellationToken.None);

        AssertError(action, StatusCodes.Status403Forbidden, "DEVICE_SCOPE_FORBIDDEN");
        Assert.Null(service.LastIdentity);
    }

    [Fact]
    public async Task Claim_actions_require_verified_cashier_identity()
    {
        var controller = CreateController(new FakeService(), new FakeIdentityResolver(null));

        AssertError(
            await controller.Prepare(HoldGuid, new SharedHeldOrderClaimPrepareRequest(ClaimGuid, "claim-1"), CancellationToken.None),
            StatusCodes.Status401Unauthorized,
            "CASHIER_AUTH_REQUIRED");
        AssertError(
            await controller.Activate(HoldGuid, ClaimGuid, CancellationToken.None),
            StatusCodes.Status401Unauthorized,
            "CASHIER_AUTH_REQUIRED");
        AssertError(
            await controller.Release(HoldGuid, ClaimGuid, CancellationToken.None),
            StatusCodes.Status401Unauthorized,
            "CASHIER_AUTH_REQUIRED");
        AssertError(
            await controller.ForceRelease(HoldGuid, ClaimGuid, new SharedHeldOrderForceReleaseRequest("原因"), CancellationToken.None),
            StatusCodes.Status401Unauthorized,
            "CASHIER_AUTH_REQUIRED");
        AssertError(
            await controller.ClaimsMine(CancellationToken.None),
            StatusCodes.Status401Unauthorized,
            "CASHIER_AUTH_REQUIRED");
    }

    [Theory]
    [InlineData(SharedHeldOrderErrorCodes.Invalid, StatusCodes.Status400BadRequest)]
    [InlineData(SharedHeldOrderErrorCodes.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(SharedHeldOrderErrorCodes.PermissionDenied, StatusCodes.Status403Forbidden)]
    [InlineData(SharedHeldOrderErrorCodes.CrossStore, StatusCodes.Status403Forbidden)]
    [InlineData(SharedHeldOrderErrorCodes.Busy, StatusCodes.Status409Conflict)]
    [InlineData(SharedHeldOrderErrorCodes.Mismatch, StatusCodes.Status409Conflict)]
    [InlineData(SharedHeldOrderErrorCodes.Expired, StatusCodes.Status409Conflict)]
    [InlineData(SharedHeldOrderErrorCodes.Disabled, StatusCodes.Status409Conflict)]
    public async Task Service_errors_map_to_expected_http_status(
        string errorCode,
        int expectedStatus)
    {
        var controller = CreateController(
            new FakeService { ErrorCode = errorCode },
            new FakeIdentityResolver(TrustedIdentity));

        var action = await controller.Prepare(
            HoldGuid,
            new SharedHeldOrderClaimPrepareRequest(ClaimGuid, "claim-1"),
            CancellationToken.None);

        AssertError(action, expectedStatus, errorCode);
    }

    private static HeldOrdersController CreateController(
        FakeService service,
        FakeIdentityResolver? identityResolver = null)
    {
        var controller = new HeldOrdersController(service, identityResolver ?? new FakeIdentityResolver(TrustedIdentity))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return controller;
    }

    private static ClaimsPrincipal AuthenticatedDevice(string storeCode, string deviceCode) =>
        new(new ClaimsIdentity(
        [
            new Claim(DeviceAuthConstants.StoreCodeClaim, storeCode),
            new Claim(DeviceAuthConstants.DeviceCodeClaim, deviceCode)
        ], "test"));

    private static void AssertRoute(string methodName, string? template, bool isGet, string? policy)
    {
        var method = typeof(HeldOrdersController).GetMethod(methodName);
        Assert.NotNull(method);
        var routeTemplate = isGet
            ? method!.GetCustomAttribute<HttpGetAttribute>()?.Template
            : method!.GetCustomAttribute<HttpPostAttribute>()?.Template;
        Assert.Equal(template, routeTemplate);
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal(policy, authorize!.Policy);
    }

    private static void AssertOk<T>(ActionResult<ApiResult<T>> action, Action<T> assertData)
    {
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var envelope = Assert.IsType<ApiResult<T>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        assertData(envelope.Data!);
    }

    private static void AssertError<T>(ActionResult<ApiResult<T>> action, int statusCode, string errorCode)
    {
        var result = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(statusCode, result.StatusCode);
        var envelope = Assert.IsType<ApiResult<T>>(result.Value);
        Assert.False(envelope.Success);
        Assert.Equal(errorCode, envelope.ErrorCode);
    }

    private sealed class FakeIdentityResolver(SharedHeldOrderIdentity? identity)
        : ISharedHeldOrderIdentityResolver
    {
        public int ResolveCalls { get; private set; }

        public Task<SharedHeldOrderIdentity?> ResolveAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            ResolveCalls++;
            return Task.FromResult(identity);
        }
    }

    private sealed class FakeService : ISharedHeldOrderService
    {
        public string? ErrorCode { get; init; }

        public SharedHeldOrderIdentity? LastIdentity { get; private set; }

        public SharedHeldOrderCapabilitiesResponse GetCapabilities() =>
            new(Enabled: false);

        public Task<SharedHeldOrderPublishResponse> PublishAsync(
            SharedHeldOrderPublishRequest request,
            SharedHeldOrderIdentity identity,
            CancellationToken cancellationToken)
        {
            LastIdentity = identity;
            ThrowIfError();
            return Task.FromResult(new SharedHeldOrderPublishResponse(
                request.HoldGuid,
                SharedHeldOrderStatus.Pending,
                1,
                DateTimeOffset.Parse("2026-08-10T02:00:00Z")));
        }

        public Task<IReadOnlyList<SharedHeldOrderListItemDto>> ListPendingAsync(
            SharedHeldOrderIdentity identity,
            CancellationToken cancellationToken)
        {
            LastIdentity = identity;
            ThrowIfError();
            return Task.FromResult<IReadOnlyList<SharedHeldOrderListItemDto>>([]);
        }

        public Task<SharedHeldOrderClaimPrepareResponse> PrepareAsync(
            Guid holdGuid,
            SharedHeldOrderClaimPrepareRequest request,
            SharedHeldOrderIdentity identity,
            CancellationToken cancellationToken)
        {
            LastIdentity = identity;
            ThrowIfError();
            return Task.FromResult(new SharedHeldOrderClaimPrepareResponse(
                holdGuid,
                request.ClaimGuid,
                SharedHeldOrderClaimStatus.Prepared,
                SharedHeldOrderServiceTestSupport.ValidCart(),
                identity.DeviceCode,
                identity.CashierId,
                identity.CashierName,
                DateTimeOffset.Parse("2026-08-10T02:00:00Z"),
                DateTimeOffset.Parse("2026-08-10T02:02:00Z"),
                1));
        }

        public Task<SharedHeldOrderClaimDto> ActivateAsync(
            Guid holdGuid,
            Guid claimGuid,
            SharedHeldOrderIdentity identity,
            CancellationToken cancellationToken)
        {
            LastIdentity = identity;
            ThrowIfError();
            return Task.FromResult(ClaimDto(claimGuid, SharedHeldOrderClaimStatus.Active));
        }

        public Task<SharedHeldOrderClaimDto> ReleaseAsync(
            Guid holdGuid,
            Guid claimGuid,
            SharedHeldOrderIdentity identity,
            CancellationToken cancellationToken)
        {
            LastIdentity = identity;
            ThrowIfError();
            return Task.FromResult(ClaimDto(claimGuid, SharedHeldOrderClaimStatus.Released));
        }

        public Task<SharedHeldOrderClaimDto> ForceReleaseAsync(
            Guid holdGuid,
            Guid claimGuid,
            SharedHeldOrderForceReleaseRequest request,
            SharedHeldOrderIdentity identity,
            CancellationToken cancellationToken)
        {
            LastIdentity = identity;
            ThrowIfError();
            return Task.FromResult(ClaimDto(claimGuid, SharedHeldOrderClaimStatus.Released) with
            {
                ForceReleased = true,
                ForceReleaseReason = request.Reason
            });
        }

        public Task<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>> ListMyClaimsAsync(
            SharedHeldOrderIdentity identity,
            CancellationToken cancellationToken)
        {
            LastIdentity = identity;
            ThrowIfError();
            return Task.FromResult<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>([]);
        }

        private void ThrowIfError()
        {
            if (ErrorCode is not null)
            {
                throw new SharedHeldOrderException(ErrorCode, "fake failure");
            }
        }

        private static SharedHeldOrderClaimDto ClaimDto(
            Guid claimGuid,
            SharedHeldOrderClaimStatus status) => new(
            HoldGuid,
            claimGuid,
            status,
            "S01",
            "POS-01",
            "C01",
            "持单收银员",
            DateTimeOffset.Parse("2026-08-10T02:00:00Z"),
            DateTimeOffset.Parse("2026-08-10T02:00:00Z"),
            ExpiresAtUtc: null,
            ActivatedAtUtc: null,
            ReleasedAtUtc: null,
            ForceReleased: false,
            ForceReleaseReason: null,
            ForceReleaseCashierId: null,
            ForceReleaseCashierName: null,
            ForceReleasedAtUtc: null,
            Revision: 1);
    }
}
