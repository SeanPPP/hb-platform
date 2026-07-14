using Hbpos.Api.Auth;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Vouchers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/v1/vouchers")]
public sealed class VouchersController(IStoreVoucherService voucherService) : ControllerBase
{
    [Authorize(Policy = CashierAuthorizationPolicies.Voucher)]
    [HttpGet("{voucherCode}")]
    public async Task<ActionResult<ApiResult<StoreVoucherQueryResponse>>> Get(
        string voucherCode,
        [FromQuery] string storeCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeCode))
        {
            return BadRequest(ApiResult<StoreVoucherQueryResponse>.Fail("STORE_CODE_REQUIRED", "storeCode is required"));
        }

        if (string.IsNullOrWhiteSpace(voucherCode))
        {
            return BadRequest(ApiResult<StoreVoucherQueryResponse>.Fail("VOUCHER_CODE_REQUIRED", "voucherCode is required"));
        }

        if (!this.IsDeviceScopeAllowed(storeCode))
        {
            return DeviceAuthorizationExtensions.DeviceScopeForbidden<StoreVoucherQueryResponse>("Device is not authorized for this store.");
        }

        var response = await voucherService.QueryAsync(storeCode, voucherCode, cancellationToken);
        if (!response.Found)
        {
            return NotFound(ApiResult<StoreVoucherQueryResponse>.Fail("VOUCHER_NOT_FOUND", "voucher was not found or unavailable"));
        }

        return Ok(ApiResult<StoreVoucherQueryResponse>.Ok(response));
    }

    [Authorize(Policy = CashierAuthorizationPolicies.Voucher)]
    [HttpPost("lock")]
    public async Task<ActionResult<ApiResult<StoreVoucherLockResponse>>> Lock(
        [FromBody] StoreVoucherLockRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.StoreCode))
        {
            return BadRequest(ApiResult<StoreVoucherLockResponse>.Fail("STORE_CODE_REQUIRED", "storeCode is required"));
        }

        if (string.IsNullOrWhiteSpace(request.VoucherCode))
        {
            return BadRequest(ApiResult<StoreVoucherLockResponse>.Fail("VOUCHER_CODE_REQUIRED", "voucherCode is required"));
        }

        if (request.RequestedAmount <= 0)
        {
            return BadRequest(ApiResult<StoreVoucherLockResponse>.Fail("REQUESTED_AMOUNT_INVALID", "requestedAmount must be greater than zero"));
        }

        if (!this.IsDeviceScopeAllowed(request.StoreCode!))
        {
            return DeviceAuthorizationExtensions.DeviceScopeForbidden<StoreVoucherLockResponse>("Device is not authorized for this store.");
        }

        try
        {
            var response = await voucherService.LockAsync(request, cancellationToken);
            return Ok(ApiResult<StoreVoucherLockResponse>.Ok(response));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResult<StoreVoucherLockResponse>.Fail("VOUCHER_LOCK_INVALID", ex.Message));
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.Voucher)]
    [HttpPost("release")]
    public async Task<ActionResult<ApiResult<StoreVoucherReleaseResponse>>> Release(
        [FromBody] StoreVoucherReleaseRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.StoreCode))
        {
            return BadRequest(ApiResult<StoreVoucherReleaseResponse>.Fail("STORE_CODE_REQUIRED", "storeCode is required"));
        }

        if (string.IsNullOrWhiteSpace(request.VoucherCode))
        {
            return BadRequest(ApiResult<StoreVoucherReleaseResponse>.Fail("VOUCHER_CODE_REQUIRED", "voucherCode is required"));
        }

        if (string.IsNullOrWhiteSpace(request.ReservationToken))
        {
            return BadRequest(ApiResult<StoreVoucherReleaseResponse>.Fail("RESERVATION_TOKEN_REQUIRED", "reservationToken is required"));
        }

        if (!this.IsDeviceScopeAllowed(request.StoreCode!))
        {
            return DeviceAuthorizationExtensions.DeviceScopeForbidden<StoreVoucherReleaseResponse>("Device is not authorized for this store.");
        }

        try
        {
            var response = await voucherService.ReleaseAsync(request, cancellationToken);
            return Ok(ApiResult<StoreVoucherReleaseResponse>.Ok(response));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResult<StoreVoucherReleaseResponse>.Fail("VOUCHER_RELEASE_INVALID", ex.Message));
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.VoucherRefund)]
    [HttpPost("refund")]
    public async Task<ActionResult<ApiResult<StoreVoucherIssueRefundResponse>>> IssueRefund(
        [FromBody] StoreVoucherIssueRefundRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.StoreCode))
        {
            return BadRequest(ApiResult<StoreVoucherIssueRefundResponse>.Fail("STORE_CODE_REQUIRED", "storeCode is required"));
        }

        if (string.IsNullOrWhiteSpace(request.StoreCode))
        {
            return BadRequest(ApiResult<StoreVoucherIssueResponse>.Fail("STORE_CODE_REQUIRED", "storeCode is required"));
        }

        if (request.Amount <= 0)
        {
            return BadRequest(ApiResult<StoreVoucherIssueRefundResponse>.Fail("AMOUNT_INVALID", "amount must be greater than zero"));
        }

        if (string.IsNullOrWhiteSpace(request.CashierId))
        {
            return BadRequest(ApiResult<StoreVoucherIssueRefundResponse>.Fail("CASHIER_ID_REQUIRED", "cashierId is required"));
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return BadRequest(ApiResult<StoreVoucherIssueRefundResponse>.Fail("IDEMPOTENCY_KEY_REQUIRED", "idempotencyKey is required"));
        }

        if (!this.IsDeviceScopeAllowed(request.StoreCode!))
        {
            return DeviceAuthorizationExtensions.DeviceScopeForbidden<StoreVoucherIssueRefundResponse>("Device is not authorized for this store.");
        }

        try
        {
            var authorizedCashierId = HttpContext.Items[CashierAuthorizationContext.CashierIdItemKey] as string;
            // Audit 阶段兼容旧客户端；Enforce 阶段授权处理器一定会覆盖客户端传入的收银员身份。
            var authorizedRequest = string.IsNullOrWhiteSpace(authorizedCashierId)
                ? request
                : request with { CashierId = authorizedCashierId };
            var response = await voucherService.IssueRefundAsync(authorizedRequest, cancellationToken);
            return Ok(ApiResult<StoreVoucherIssueRefundResponse>.Ok(response));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResult<StoreVoucherIssueRefundResponse>.Fail("VOUCHER_REFUND_INVALID", ex.Message));
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.VoucherRefund)]
    [HttpPost("issue")]
    public async Task<ActionResult<ApiResult<StoreVoucherIssueResponse>>> Issue(
        [FromBody] StoreVoucherIssueRequest request,
        CancellationToken cancellationToken)
    {
        // 通用签发入口没有可由服务端核验的原始业务单据，保留路由但永久禁用，避免任意金额铸券。
        await Task.CompletedTask;
        return StatusCode(
            StatusCodes.Status410Gone,
            ApiResult<StoreVoucherIssueResponse>.Fail(
                "VOUCHER_ISSUE_DISABLED",
                "Direct voucher issuing is disabled."));
    }
}
