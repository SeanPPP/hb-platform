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
    [Authorize]
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

    [Authorize]
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

        if (!this.IsDeviceScopeAllowed(request.StoreCode))
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
}
