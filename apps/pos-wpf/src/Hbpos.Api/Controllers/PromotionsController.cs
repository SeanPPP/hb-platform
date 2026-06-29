using Hbpos.Api.Auth;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Promotions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/v1/promotions")]
[Authorize]
public sealed class PromotionsController(IPromotionRuleService promotionRuleService) : ControllerBase
{
    [HttpGet("rules")]
    public async Task<ActionResult<ApiResult<PromotionRulesResponse>>> GetRules(
        [FromQuery] string storeCode,
        [FromQuery] DateTimeOffset? asOf,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeCode))
        {
            return BadRequest(ApiResult<PromotionRulesResponse>.Fail("STORE_CODE_REQUIRED", "storeCode is required"));
        }

        if (!this.IsDeviceScopeAllowed(storeCode))
        {
            return DeviceAuthorizationExtensions.DeviceScopeForbidden<PromotionRulesResponse>("Device is not authorized for this store.");
        }

        var response = await promotionRuleService.GetRulesAsync(storeCode, asOf, cancellationToken);
        return Ok(ApiResult<PromotionRulesResponse>.Ok(response));
    }
}
