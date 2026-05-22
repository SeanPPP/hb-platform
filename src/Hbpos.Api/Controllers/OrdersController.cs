using Hbpos.Api.Auth;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/v1/orders")]
public sealed class OrdersController(IOrderSyncService orderSyncService) : ControllerBase
{
    [Authorize]
    [HttpPost("sync")]
    public async Task<ActionResult<ApiResult<OrderSyncResponse>>> Sync(
        [FromBody] OrderSyncRequest request,
        CancellationToken cancellationToken)
    {
        if (!this.IsDeviceScopeAllowed(request.StoreCode, request.DeviceCode))
        {
            return DeviceAuthorizationExtensions.DeviceScopeForbidden<OrderSyncResponse>("Device is not authorized for this store.");
        }

        if (request.Lines.Count == 0)
        {
            return BadRequest(ApiResult<OrderSyncResponse>.Fail("ORDER_LINES_REQUIRED", "订单明细不能为空"));
        }

        if (request.Payments.Count == 0)
        {
            return BadRequest(ApiResult<OrderSyncResponse>.Fail("ORDER_PAYMENTS_REQUIRED", "订单付款不能为空"));
        }

        var response = await orderSyncService.SyncAsync(request, cancellationToken);
        return Ok(ApiResult<OrderSyncResponse>.Ok(response));
    }
}
