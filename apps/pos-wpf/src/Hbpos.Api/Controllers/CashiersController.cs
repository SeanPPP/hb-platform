using Hbpos.Api.Auth;
using Hbpos.Api.Services;
using Hbpos.Contracts.Cashiers;
using Hbpos.Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/v1/cashiers")]
public sealed class CashiersController(
    ICashierService cashierService,
    ICashierAuthorizationTicketService ticketService) : ControllerBase
{
    [Authorize]
    [HttpPost("barcode-login")]
    public async Task<ActionResult<ApiResult<CashierSessionDto>>> BarcodeLogin(
        [FromBody] CashierBarcodeLoginRequest request,
        CancellationToken cancellationToken)
    {
        if (!this.IsDeviceScopeAllowed(request.StoreCode, request.DeviceCode))
        {
            return DeviceAuthorizationExtensions.DeviceScopeForbidden<CashierSessionDto>("Device is not authorized for this store.");
        }

        var session = await cashierService.BarcodeLoginAsync(request, cancellationToken);
        return session is null
            ? Unauthorized(ApiResult<CashierSessionDto>.Fail("CASHIER_LOGIN_FAILED", "收银员条码无效或已停用"))
            : Ok(ApiResult<CashierSessionDto>.Ok(session));
    }

    [Authorize]
    [HttpGet("session")]
    public async Task<ActionResult<ApiResult<CashierSessionDto>>> GetSession(
        CancellationToken cancellationToken)
    {
        var token = Request.Headers[CashierAuthorizationConstants.HeaderName].ToString();
        var ticket = ticketService.Validate(token);
        if (ticket is null)
        {
            return Unauthorized(ApiResult<CashierSessionDto>.Fail(
                "CASHIER_SESSION_INVALID",
                "收银员会话已失效，请重新登录"));
        }

        if (!this.IsDeviceScopeAllowed(ticket.StoreCode, ticket.DeviceCode))
        {
            return DeviceAuthorizationExtensions.DeviceScopeForbidden<CashierSessionDto>(
                "Device is not authorized for this store.");
        }

        var session = await cashierService.RefreshSessionAsync(ticket, cancellationToken);
        return session is null
            ? Unauthorized(ApiResult<CashierSessionDto>.Fail(
                "CASHIER_SESSION_REVOKED",
                "收银员已停用或不再属于当前分店"))
            : Ok(ApiResult<CashierSessionDto>.Ok(session));
    }
}
