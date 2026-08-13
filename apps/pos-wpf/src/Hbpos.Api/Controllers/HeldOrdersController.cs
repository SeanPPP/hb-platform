using Hbpos.Api.Auth;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.HeldOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/v1/held-orders")]
public sealed class HeldOrdersController(
    ISharedHeldOrderService? service = null,
    ISharedHeldOrderIdentityResolver? identityResolver = null) : ControllerBase
{
    [Authorize]
    [HttpGet("capabilities")]
    public ActionResult<ApiResult<SharedHeldOrderCapabilitiesResponse>> Capabilities()
    {
        return Ok(ApiResult<SharedHeldOrderCapabilitiesResponse>.Ok(
            RequireService().GetCapabilities()));
    }

    [Authorize(Policy = CashierAuthorizationPolicies.HoldOrder)]
    [HttpPost]
    public async Task<ActionResult<ApiResult<SharedHeldOrderPublishResponse>>> Publish(
        [FromBody] SharedHeldOrderPublishRequest request,
        CancellationToken cancellationToken)
    {
        if (!this.IsDeviceScopeAllowed(request.StoreCode, request.DeviceCode))
        {
            return DeviceAuthorizationExtensions.DeviceScopeForbidden<SharedHeldOrderPublishResponse>(
                "Device is not authorized for this store.");
        }

        var identity = await ResolveIdentityAsync(cancellationToken);
        if (identity is null)
        {
            return CashierIdentityRequired<SharedHeldOrderPublishResponse>();
        }

        try
        {
            var response = await RequireService().PublishAsync(
                request,
                identity,
                cancellationToken);
            return Ok(ApiResult<SharedHeldOrderPublishResponse>.Ok(response));
        }
        catch (SharedHeldOrderException ex)
        {
            return Error<SharedHeldOrderPublishResponse>(ex);
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.RecallOrder)]
    [HttpGet]
    public async Task<ActionResult<ApiResult<IReadOnlyList<SharedHeldOrderListItemDto>>>> ListPending(
        [FromQuery] IReadOnlyCollection<int>? supportedPayloadVersions = null,
        CancellationToken cancellationToken = default)
    {
        var identity = await ResolveIdentityAsync(cancellationToken);
        if (identity is null)
        {
            return CashierIdentityRequired<IReadOnlyList<SharedHeldOrderListItemDto>>();
        }

        try
        {
            var response = await RequireService().ListPendingAsync(
                identity,
                supportedPayloadVersions,
                cancellationToken);
            return Ok(ApiResult<IReadOnlyList<SharedHeldOrderListItemDto>>.Ok(response));
        }
        catch (SharedHeldOrderException ex)
        {
            return Error<IReadOnlyList<SharedHeldOrderListItemDto>>(ex);
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.RecallOrder)]
    [HttpPost("{holdGuid:guid}/claims/prepare")]
    public async Task<ActionResult<ApiResult<SharedHeldOrderClaimPrepareResponse>>> Prepare(
        Guid holdGuid,
        [FromBody] SharedHeldOrderClaimPrepareRequest request,
        [FromQuery] IReadOnlyCollection<int>? supportedPayloadVersions = null,
        CancellationToken cancellationToken = default)
    {
        var identity = await ResolveIdentityAsync(cancellationToken);
        if (identity is null)
        {
            return CashierIdentityRequired<SharedHeldOrderClaimPrepareResponse>();
        }

        try
        {
            var response = await RequireService().PrepareAsync(
                holdGuid,
                request,
                identity,
                supportedPayloadVersions,
                cancellationToken);
            return Ok(ApiResult<SharedHeldOrderClaimPrepareResponse>.Ok(response));
        }
        catch (SharedHeldOrderException ex)
        {
            return Error<SharedHeldOrderClaimPrepareResponse>(ex);
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.RecallOrder)]
    [HttpPost("{holdGuid:guid}/claims/{claimGuid:guid}/activate")]
    public async Task<ActionResult<ApiResult<SharedHeldOrderClaimDto>>> Activate(
        Guid holdGuid,
        Guid claimGuid,
        CancellationToken cancellationToken)
    {
        var identity = await ResolveIdentityAsync(cancellationToken);
        if (identity is null)
        {
            return CashierIdentityRequired<SharedHeldOrderClaimDto>();
        }

        try
        {
            var response = await RequireService().ActivateAsync(
                holdGuid,
                claimGuid,
                identity,
                cancellationToken);
            return Ok(ApiResult<SharedHeldOrderClaimDto>.Ok(response));
        }
        catch (SharedHeldOrderException ex)
        {
            return Error<SharedHeldOrderClaimDto>(ex);
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.RecallOrder)]
    [HttpPost("{holdGuid:guid}/claims/{claimGuid:guid}/release")]
    public async Task<ActionResult<ApiResult<SharedHeldOrderClaimDto>>> Release(
        Guid holdGuid,
        Guid claimGuid,
        CancellationToken cancellationToken)
    {
        var identity = await ResolveIdentityAsync(cancellationToken);
        if (identity is null)
        {
            return CashierIdentityRequired<SharedHeldOrderClaimDto>();
        }

        try
        {
            var response = await RequireService().ReleaseAsync(
                holdGuid,
                claimGuid,
                identity,
                cancellationToken);
            return Ok(ApiResult<SharedHeldOrderClaimDto>.Ok(response));
        }
        catch (SharedHeldOrderException ex)
        {
            return Error<SharedHeldOrderClaimDto>(ex);
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.HistoryRecall)]
    [HttpPost("{holdGuid:guid}/claims/{claimGuid:guid}/force-release")]
    public async Task<ActionResult<ApiResult<SharedHeldOrderClaimDto>>> ForceRelease(
        Guid holdGuid,
        Guid claimGuid,
        [FromBody] SharedHeldOrderForceReleaseRequest request,
        CancellationToken cancellationToken)
    {
        var identity = await ResolveIdentityAsync(cancellationToken);
        if (identity is null)
        {
            return CashierIdentityRequired<SharedHeldOrderClaimDto>();
        }

        try
        {
            var response = await RequireService().ForceReleaseAsync(
                holdGuid,
                claimGuid,
                request,
                identity,
                cancellationToken);
            return Ok(ApiResult<SharedHeldOrderClaimDto>.Ok(response));
        }
        catch (SharedHeldOrderException ex)
        {
            return Error<SharedHeldOrderClaimDto>(ex);
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.HistoryRecall)]
    [HttpPost("{holdGuid:guid}/cancel")]
    public async Task<ActionResult<ApiResult<SharedHeldOrderCancelResponse>>> Cancel(
        Guid holdGuid,
        CancellationToken cancellationToken)
    {
        var identity = await ResolveIdentityAsync(cancellationToken);
        if (identity is null)
        {
            return CashierIdentityRequired<SharedHeldOrderCancelResponse>();
        }

        try
        {
            var response = await RequireService().CancelAsync(
                holdGuid,
                identity,
                cancellationToken);
            return Ok(ApiResult<SharedHeldOrderCancelResponse>.Ok(response));
        }
        catch (SharedHeldOrderException ex)
        {
            return Error<SharedHeldOrderCancelResponse>(ex);
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.RecallOrder)]
    [HttpGet("claims/mine")]
    public async Task<ActionResult<ApiResult<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>>> ClaimsMine(
        [FromQuery] IReadOnlyCollection<int>? supportedPayloadVersions = null,
        CancellationToken cancellationToken = default)
    {
        var identity = await ResolveIdentityAsync(cancellationToken);
        if (identity is null)
        {
            return CashierIdentityRequired<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>();
        }

        try
        {
            var response = await RequireService().ListMyClaimsAsync(
                identity,
                supportedPayloadVersions,
                cancellationToken);
            return Ok(ApiResult<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>.Ok(response));
        }
        catch (SharedHeldOrderException ex)
        {
            return Error<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>(ex);
        }
    }

    private ISharedHeldOrderService RequireService()
    {
        return service
            ?? throw new InvalidOperationException("Shared held order service is not configured.");
    }

    private Task<SharedHeldOrderIdentity?> ResolveIdentityAsync(CancellationToken cancellationToken)
    {
        return identityResolver is null
            ? Task.FromResult<SharedHeldOrderIdentity?>(null)
            : identityResolver.ResolveAsync(HttpContext, cancellationToken);
    }

    private static ActionResult<ApiResult<T>> CashierIdentityRequired<T>()
    {
        return new ObjectResult(ApiResult<T>.Fail(
            "CASHIER_AUTH_REQUIRED",
            "A verified cashier authorization ticket is required."))
        {
            StatusCode = StatusCodes.Status401Unauthorized
        };
    }

    private static ActionResult<ApiResult<T>> Error<T>(SharedHeldOrderException exception)
    {
        var statusCode = exception.Code switch
        {
            SharedHeldOrderErrorCodes.NotFound => StatusCodes.Status404NotFound,
            SharedHeldOrderErrorCodes.Invalid => StatusCodes.Status400BadRequest,
            SharedHeldOrderErrorCodes.PermissionDenied => StatusCodes.Status403Forbidden,
            SharedHeldOrderErrorCodes.CrossStore => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status409Conflict
        };
        return new ObjectResult(ApiResult<T>.Fail(exception.Code, exception.Message))
        {
            StatusCode = statusCode
        };
    }
}
