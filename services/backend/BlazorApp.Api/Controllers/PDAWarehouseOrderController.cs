using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/v1/pda/warehouse-order")]
    [AllowAnonymous]
    public class PDAWarehouseOrderController : ControllerBase
    {
        private readonly IPDAWarehouseOrderService _warehouseOrderService;
        private readonly IDeviceRegistrationService _deviceService;
        private readonly ILogger<PDAWarehouseOrderController> _logger;

        public PDAWarehouseOrderController(
            IPDAWarehouseOrderService warehouseOrderService,
            IDeviceRegistrationService deviceService,
            ILogger<PDAWarehouseOrderController> logger
        )
        {
            _warehouseOrderService = warehouseOrderService;
            _deviceService = deviceService;
            _logger = logger;
        }

        private async Task<bool> ValidateDeviceAuthAsync()
        {
            try
            {
                var hardwareId = Request.Headers["X-Device-Id"].FirstOrDefault();
                var authCode = Request.Headers["X-Auth-Code"].FirstOrDefault();

                if (string.IsNullOrEmpty(hardwareId) || string.IsNullOrEmpty(authCode))
                {
                    _logger.LogWarning("设备认证失败：缺少设备ID或授权码");
                    return false;
                }

                var isValid = await _deviceService.ValidateDeviceAuthCodeAsync(
                    hardwareId,
                    authCode
                );
                if (!isValid)
                {
                    _logger.LogWarning(
                        "设备认证失败：无效的设备ID或授权码，HardwareId: {HardwareId}",
                        hardwareId
                    );
                }

                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设备认证过程中发生异常");
                return false;
            }
        }

        private string? GetDeviceHardwareId()
        {
            return Request.Headers["X-Device-Id"].FirstOrDefault();
        }

        private async Task<string?> GetStoreCodeFromDeviceAsync()
        {
            var hardwareId = GetDeviceHardwareId();

            if (!string.IsNullOrEmpty(hardwareId))
            {
                var device = await _deviceService.GetDeviceByHardwareIdAsync(hardwareId);
                return device?.分店代码;
            }

            return null;
        }

        private async Task<string?> GetBoundStoreCodeAsync()
        {
            var storeCode = await GetStoreCodeFromDeviceAsync();
            if (!string.IsNullOrWhiteSpace(storeCode))
            {
                return storeCode;
            }

            // 设备已认证但未绑定分店时，必须在控制器层拦截，避免把空分店传给 service。
            _logger.LogWarning(
                "设备已认证但未绑定分店，HardwareId: {HardwareId}",
                GetDeviceHardwareId()
            );
            return null;
        }

        private IActionResult DeviceStoreNotBound()
        {
            return StatusCode(403, new { success = false, message = "设备未绑定分店" });
        }

        #region 订单管理

        [HttpGet("list")]
        public async Task<IActionResult> GetOrderList([FromQuery] PDAWarehouseOrderFilterDto filter)
        {
            try
            {
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var storeCode = await GetBoundStoreCodeAsync();
                if (storeCode == null)
                {
                    return DeviceStoreNotBound();
                }

                var result = await _warehouseOrderService.GetOrderListAsync(filter, storeCode);

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PDA仓库订单列表失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpGet("detail/{orderGuid}")]
        public async Task<IActionResult> GetOrderDetail(string orderGuid)
        {
            try
            {
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var storeCode = await GetBoundStoreCodeAsync();
                if (storeCode == null)
                {
                    return DeviceStoreNotBound();
                }

                var result = await _warehouseOrderService.GetOrderDetailAsync(orderGuid, storeCode);

                if (result == null)
                {
                    return NotFound(new { success = false, message = "订单不存在" });
                }

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PDA仓库订单详情失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateOrder(
            [FromBody] CreatePDAWarehouseOrderRequestDto request
        )
        {
            try
            {
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var storeCode = await GetBoundStoreCodeAsync();
                if (storeCode == null)
                {
                    return DeviceStoreNotBound();
                }

                // 创建订单必须使用设备绑定分店，不能信任客户端传入的 StoreCode。
                request.StoreCode = storeCode;
                var hardwareId = GetDeviceHardwareId() ?? string.Empty;
                var result = await _warehouseOrderService.CreateOrderAsync(request, hardwareId);

                if (!result.Success)
                {
                    return BadRequest(new { success = false, message = result.Message });
                }

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建PDA仓库订单失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("update")]
        public async Task<IActionResult> UpdateOrder(
            [FromBody] UpdatePDAWarehouseOrderRequestDto request
        )
        {
            try
            {
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var storeCode = await GetBoundStoreCodeAsync();
                if (storeCode == null)
                {
                    return DeviceStoreNotBound();
                }

                var hardwareId = GetDeviceHardwareId() ?? string.Empty;
                var result = await _warehouseOrderService.UpdateOrderAsync(
                    request,
                    storeCode,
                    hardwareId
                );

                if (!result.Success)
                {
                    return BadRequest(new { success = false, message = result.Message });
                }

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新PDA仓库订单失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("submit")]
        public async Task<IActionResult> SubmitOrder(
            [FromBody] SubmitPDAWarehouseOrderRequestDto request
        )
        {
            try
            {
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var storeCode = await GetBoundStoreCodeAsync();
                if (storeCode == null)
                {
                    return DeviceStoreNotBound();
                }

                // 订单归属与 Preorder 门禁必须由 service 在同一 StoreGate/事务内按顺序判定。
                var hardwareId = GetDeviceHardwareId() ?? string.Empty;
                var result = await _warehouseOrderService.SubmitOrderAsync(
                    request,
                    storeCode,
                    hardwareId
                );

                if (!result.Success)
                {
                    if (result.ErrorCode == "PDA_ORDER_STORE_MISMATCH")
                    {
                        return StatusCode(
                            StatusCodes.Status403Forbidden,
                            ApiResponse<object>.Error(
                                result.Message ?? "无权提交该订单",
                                result.ErrorCode
                            )
                        );
                    }
                    if (result.ErrorCode == "PREORDER_REQUIRED")
                    {
                        return Conflict(
                            ApiResponse<object>.Error(result.Message ?? "请先完成 Preorder", result.ErrorCode)
                        );
                    }
                    if (result.ErrorCode == "PREORDER_GATE_UNAVAILABLE")
                    {
                        return StatusCode(
                            StatusCodes.Status503ServiceUnavailable,
                            ApiResponse<object>.Error(
                                result.Message ?? "Preorder 状态暂时无法确认，请稍后重试",
                                result.ErrorCode
                            )
                        );
                    }
                    return BadRequest(new { success = false, message = result.Message });
                }

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "提交PDA仓库订单失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpDelete("{orderGuid}")]
        public async Task<IActionResult> DeleteOrder(string orderGuid)
        {
            try
            {
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var storeCode = await GetBoundStoreCodeAsync();
                if (storeCode == null)
                {
                    return DeviceStoreNotBound();
                }

                var hardwareId = GetDeviceHardwareId() ?? string.Empty;
                var result = await _warehouseOrderService.DeleteOrderAsync(
                    orderGuid,
                    storeCode,
                    hardwareId
                );

                if (!result.Success)
                {
                    return BadRequest(new { success = false, message = result.Message });
                }

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除PDA仓库订单失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        #endregion

        #region 订单明细管理

        [HttpPost("line/add")]
        public async Task<IActionResult> AddOrderLine(
            [FromBody] AddPDAWarehouseOrderLineRequestDto request
        )
        {
            try
            {
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var storeCode = await GetBoundStoreCodeAsync();
                if (storeCode == null)
                {
                    return DeviceStoreNotBound();
                }

                var result = await _warehouseOrderService.AddOrderLineAsync(request, storeCode);

                if (!result.Success)
                {
                    return BadRequest(new { success = false, message = result.Message });
                }

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加PDA仓库订单明细失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("line/update")]
        public async Task<IActionResult> UpdateOrderLine(
            [FromBody] UpdatePDAWarehouseOrderLineRequestDto request
        )
        {
            try
            {
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var storeCode = await GetBoundStoreCodeAsync();
                if (storeCode == null)
                {
                    return DeviceStoreNotBound();
                }

                var result = await _warehouseOrderService.UpdateOrderLineAsync(request, storeCode);

                if (!result.Success)
                {
                    return BadRequest(new { success = false, message = result.Message });
                }

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新PDA仓库订单明细失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpDelete("line/{detailGuid}")]
        public async Task<IActionResult> DeleteOrderLine(string detailGuid)
        {
            try
            {
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var storeCode = await GetBoundStoreCodeAsync();
                if (storeCode == null)
                {
                    return DeviceStoreNotBound();
                }

                var result = await _warehouseOrderService.DeleteOrderLineAsync(
                    detailGuid,
                    storeCode
                );

                if (!result.Success)
                {
                    return BadRequest(new { success = false, message = result.Message });
                }

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除PDA仓库订单明细失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("line/batch-add")]
        public async Task<IActionResult> BatchAddOrderLines(
            [FromBody] BatchAddPDAWarehouseOrderLinesRequestDto request
        )
        {
            try
            {
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var storeCode = await GetBoundStoreCodeAsync();
                if (storeCode == null)
                {
                    return DeviceStoreNotBound();
                }

                var result = await _warehouseOrderService.BatchAddOrderLinesAsync(
                    request,
                    storeCode
                );

                if (!result.Success)
                {
                    return BadRequest(new { success = false, message = result.Message });
                }

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量添加PDA仓库订单明细失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        #endregion

        #region 商品查询

        [HttpGet("products")]
        public async Task<IActionResult> GetProducts(
            [FromQuery] PDAWarehouseProductFilterDto filter
        )
        {
            try
            {
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var storeCode = await GetBoundStoreCodeAsync();
                if (storeCode == null)
                {
                    return DeviceStoreNotBound();
                }

                var result = await _warehouseOrderService.GetProductsAsync(filter);

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PDA仓库商品列表失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpGet("product/{productCode}")]
        public async Task<IActionResult> GetProductByCode(string productCode)
        {
            try
            {
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var storeCode = await GetBoundStoreCodeAsync();
                if (storeCode == null)
                {
                    return DeviceStoreNotBound();
                }

                var result = await _warehouseOrderService.GetProductByCodeAsync(productCode);

                if (result == null)
                {
                    return NotFound(new { success = false, message = "商品不存在" });
                }

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PDA仓库商品详情失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("product/scan")]
        public async Task<IActionResult> ScanProduct([FromBody] PDAScanProductRequestDto request)
        {
            try
            {
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var storeCode = await GetBoundStoreCodeAsync();
                if (storeCode == null)
                {
                    return DeviceStoreNotBound();
                }

                var result = await _warehouseOrderService.ScanProductAsync(request);

                if (result == null)
                {
                    return NotFound(new { success = false, message = "未找到匹配的商品" });
                }

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "扫码查询PDA仓库商品失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("product/batch-scan")]
        public async Task<IActionResult> BatchScanProducts(
            [FromBody] PDABatchScanProductsRequestDto request
        )
        {
            try
            {
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var storeCode = await GetBoundStoreCodeAsync();
                if (storeCode == null)
                {
                    return DeviceStoreNotBound();
                }

                var result = await _warehouseOrderService.BatchGetProductsByItemNumbersAsync(
                    request.ItemNumbers
                );

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量扫码查询PDA仓库商品失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        #endregion
    }
}
