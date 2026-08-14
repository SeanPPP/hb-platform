using System.Security.Claims;
using Hbpos.Api.Auth;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/v1/stores")]
[Authorize]
public sealed class StoresController(IStoreReceiptProfileService receiptProfileService) : ControllerBase
{
    [Authorize(Policy = CashierAuthorizationPolicies.ReceiptPrinter)]
    [HttpGet("current/receipt-profile")]
    public async Task<ActionResult<ApiResult<StoreReceiptProfileDto>>> GetCurrentReceiptProfile(
        CancellationToken cancellationToken)
    {
        // 门店代码只信任已验设备/收银员认证声明，绝不接受 query/body 任意分店。
        var storeCode = User.FindFirstValue(DeviceAuthConstants.StoreCodeClaim);
        if (string.IsNullOrWhiteSpace(storeCode))
        {
            return Unauthorized(ApiResult<StoreReceiptProfileDto>.Fail(
                "STORE_CODE_CLAIM_MISSING",
                "门店代码认证声明缺失"));
        }

        var result = await receiptProfileService.GetCurrentAsync(storeCode, cancellationToken);
        if (result.Profile is not null)
        {
            return Ok(ApiResult<StoreReceiptProfileDto>.Ok(result.Profile));
        }

        return result.ErrorCode switch
        {
            StoreReceiptProfileService.StoreNotFoundCode => NotFound(
                ApiResult<StoreReceiptProfileDto>.Fail(
                    result.ErrorCode,
                    result.Message ?? "门店不存在或已停用")),
            _ => BadRequest(ApiResult<StoreReceiptProfileDto>.Fail(
                result.ErrorCode ?? "STORE_PROFILE_INVALID",
                result.Message ?? "门店资料无效"))
        };
    }
}
