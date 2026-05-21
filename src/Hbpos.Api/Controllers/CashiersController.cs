using Hbpos.Api.Services;
using Hbpos.Contracts.Cashiers;
using Hbpos.Contracts.Common;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/v1/cashiers")]
public sealed class CashiersController(ICashierService cashierService) : ControllerBase
{
    [HttpPost("barcode-login")]
    public async Task<ActionResult<ApiResult<CashierSessionDto>>> BarcodeLogin(
        [FromBody] CashierBarcodeLoginRequest request,
        CancellationToken cancellationToken)
    {
        var session = await cashierService.BarcodeLoginAsync(request, cancellationToken);
        return session is null
            ? Unauthorized(ApiResult<CashierSessionDto>.Fail("CASHIER_LOGIN_FAILED", "收银员条码无效或已停用"))
            : Ok(ApiResult<CashierSessionDto>.Ok(session));
    }
}
