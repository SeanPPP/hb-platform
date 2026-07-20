using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// PDA购物车转订单控制器
    /// </summary>
    [ApiController]
    [Route("api/v1/pda/[controller]")]
    [AllowAnonymous]
    public class PDACartToOrderController : ControllerBase
    {
        private readonly IPDACartToOrderService _cartToOrderService;
        private readonly IDeviceRegistrationService _deviceService;
        private readonly ILogger<PDACartToOrderController> _logger;

        public PDACartToOrderController(
            IPDACartToOrderService cartToOrderService,
            IDeviceRegistrationService deviceService,
            ILogger<PDACartToOrderController> logger
        )
        {
            _cartToOrderService = cartToOrderService;
            _deviceService = deviceService;
            _logger = logger;
        }

        /// <summary>
        /// 验证设备授权
        /// </summary>
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

        /// <summary>
        /// 获取设备硬件ID
        /// </summary>
        private string? GetDeviceHardwareId()
        {
            return Request.Headers["X-Device-Id"].FirstOrDefault();
        }

        private async Task<string?> GetBoundStoreCodeAsync()
        {
            var hardwareId = GetDeviceHardwareId();
            if (string.IsNullOrWhiteSpace(hardwareId))
            {
                return null;
            }

            var device = await _deviceService.GetDeviceByHardwareIdAsync(hardwareId);
            return string.IsNullOrWhiteSpace(device?.分店代码) ? null : device.分店代码;
        }

        /// <summary>
        /// 将购物车转换为仓库订单
        /// 如果分店存在待提交订单（FlowStatus=0），则添加到现有订单
        /// 否则创建新订单
        /// </summary>
        /// <param name="request">转换请求</param>
        /// <returns>转换结果</returns>
        [HttpPost("convert")]
        public async Task<IActionResult> ConvertCartToOrder(
            [FromBody] CartToOrderRequestDto request
        )
        {
            try
            {
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var hardwareId = GetDeviceHardwareId();
                _logger.LogInformation(
                    "PDA设备请求转换购物车为订单，设备ID: {HardwareId}, 购物车GUID: {CartGUID}",
                    hardwareId,
                    request.CartGUID
                );

                if (string.IsNullOrEmpty(request.CartGUID))
                {
                    return BadRequest(new { success = false, message = "购物车GUID不能为空" });
                }

                var storeCode = await GetBoundStoreCodeAsync();
                if (storeCode == null)
                {
                    return StatusCode(403, new { success = false, message = "设备未绑定分店" });
                }

                // 购物车转普通草稿不受 Preorder 门禁限制，但必须由 service 校验设备绑定分店归属。
                var result = await _cartToOrderService.ConvertCartToOrderAsync(
                    request,
                    hardwareId ?? "",
                    storeCode
                );

                if (result.Success)
                {
                    return Ok(new { success = true, data = result });
                }
                if (result.ErrorCode == "PDA_CART_STORE_MISMATCH")
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        ApiResponse<object>.Error(result.Message ?? "无权转换该购物车", result.ErrorCode)
                    );
                }
                return BadRequest(
                    ApiResponse<object>.Error(result.Message ?? "转换购物车失败", result.ErrorCode)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "转换购物车为订单失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }
    }
}
