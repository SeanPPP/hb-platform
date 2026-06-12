using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// PDA购物车管理API控制器
    /// </summary>
    [ApiController]
    [Route("api/v1/pda/[controller]")]
    [Authorize]
    public class PDACartController : ControllerBase
    {
        private readonly ICartService _cartService;
        private readonly IDeviceRegistrationService _deviceService;
        private readonly IStoreService _storeService;
        private readonly ILogger<PDACartController> _logger;

        public PDACartController(
            ICartService cartService,
            IDeviceRegistrationService deviceService,
            IStoreService storeService,
            ILogger<PDACartController> logger
        )
        {
            _cartService = cartService;
            _deviceService = deviceService;
            _storeService = storeService;
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

        /// <summary>
        /// 获取设备关联的分店信息
        /// </summary>
        private async Task<(string? storeGuid, string? storeName)> GetDeviceStoreInfoAsync(
            string hardwareId
        )
        {
            try
            {
                // 从设备注册信息中获取分店信息
                var deviceInfo = await _deviceService.GetDeviceByHardwareIdAsync(hardwareId);
                if (deviceInfo != null && !string.IsNullOrEmpty(deviceInfo.分店代码))
                {
                    _logger.LogInformation(
                        "设备 {HardwareId} 关联分店代码: {StoreCode}",
                        hardwareId,
                        deviceInfo.分店代码
                    );

                    // 根据StoreCode查询完整的分店信息
                    var storeResult = await _storeService.GetStoreByCodeAsync(deviceInfo.分店代码);
                    if (storeResult.Success && storeResult.Data != null)
                    {
                        _logger.LogInformation(
                            "成功获取分店信息: {StoreCode} -> {StoreName} (GUID: {StoreGUID})",
                            deviceInfo.分店代码,
                            storeResult.Data.StoreName,
                            storeResult.Data.StoreGUID
                        );
                        return (storeResult.Data.StoreGUID, storeResult.Data.StoreName);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "根据分店代码 {StoreCode} 未找到分店信息: {Message}",
                            deviceInfo.分店代码,
                            storeResult.Message
                        );
                        // 如果找不到分店信息，返回分店代码作为备用
                        return (deviceInfo.分店代码, $"分店-{deviceInfo.分店代码}");
                    }
                }

                _logger.LogWarning("设备 {HardwareId} 未关联任何分店", hardwareId);
                return (null, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取设备分店信息失败，HardwareId: {HardwareId}", hardwareId);
                return (null, null);
            }
        }

        /// <summary>
        /// 获取购物车列表（PDA设备专用）
        /// </summary>
        [HttpGet("list")]
        public async Task<IActionResult> GetCartList(
            [FromQuery] string? status = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? searchKeyword = null,
            [FromQuery] string? storeId = null,
            [FromQuery] List<string>? excludeStatus = null
        )
        {
            try
            {
                // 验证设备授权
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var hardwareId = GetDeviceHardwareId();
                _logger.LogInformation("PDA设备获取购物车列表，设备ID: {HardwareId}", hardwareId);

                // 获取设备关联的分店信息
                var (deviceStoreGuid, deviceStoreName) = await GetDeviceStoreInfoAsync(hardwareId!);

                var request = new CartListRequest
                {
                    Status = status,
                    Page = page,
                    PageSize = pageSize,
                    SearchKeyword = searchKeyword,
                    StoreId = storeId ?? deviceStoreGuid, // 优先使用请求中的分店，否则使用设备关联的分店
                    ExcludeStatuses = excludeStatus ?? new List<string>(),
                };

                // 使用PDA专用方法获取购物车列表
                var result = await _cartService.GetPDACartListAsync(
                    hardwareId!,
                    storeId ?? deviceStoreGuid,
                    request
                );

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取购物车列表失败");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 根据购物车GUID获取购物车详情（PDA设备专用）
        /// </summary>
        [HttpGet("{cartId}")]
        public async Task<IActionResult> GetCartById(string cartId)
        {
            try
            {
                // 验证设备授权
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var hardwareId = GetDeviceHardwareId();
                _logger.LogInformation(
                    "PDA设备获取购物车详情，设备ID: {HardwareId}, 购物车ID: {CartId}",
                    hardwareId,
                    cartId
                );

                // 使用PDA专用方法获取购物车详情
                var cart = await _cartService.GetPDACartByIdAsync(hardwareId!, cartId);
                if (cart == null)
                {
                    return NotFound(new { success = false, message = "购物车不存在" });
                }

                return Ok(new { success = true, data = cart });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取购物车详情失败，CartId: {CartId}", cartId);
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 创建新购物车（PDA设备专用）
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateCart([FromBody] CreateCartRequest request)
        {
            try
            {
                // 验证设备授权
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var hardwareId = GetDeviceHardwareId();
                _logger.LogInformation("PDA设备创建购物车，设备ID: {HardwareId}", hardwareId);

                if (!ModelState.IsValid)
                {
                    return BadRequest(new { success = false, message = "请求数据格式无效" });
                }

                // 获取设备关联的分店信息
                var (deviceStoreGuid, deviceStoreName) = await GetDeviceStoreInfoAsync(hardwareId!);

                // 决定使用哪个分店GUID
                var finalStoreGuid = request.StoreGuid ?? deviceStoreGuid ?? "";
                _logger.LogInformation(
                    "创建购物车分店信息 - 请求分店: {RequestStoreGuid}, 设备分店: {DeviceStoreGuid}, 最终分店: {FinalStoreGuid}",
                    request.StoreGuid,
                    deviceStoreGuid,
                    finalStoreGuid
                );

                // 使用PDA专用方法创建购物车
                var result = await _cartService.CreatePDACartAsync(
                    hardwareId!,
                    finalStoreGuid,
                    request.Name ?? "PDA购物车",
                    request.Description
                );

                if (result != null)
                {
                    return Ok(new { success = true, data = result });
                }
                else
                {
                    return BadRequest(new { success = false, message = "创建购物车失败" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建购物车失败");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 更新购物车（PDA设备专用）
        /// </summary>
        [HttpPut("{cartId}")]
        public async Task<IActionResult> UpdateCart(
            string cartId,
            [FromBody] UpdateCartRequest request
        )
        {
            try
            {
                // 验证设备授权
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var hardwareId = GetDeviceHardwareId();
                _logger.LogInformation(
                    "PDA设备更新购物车，设备ID: {HardwareId}, 购物车ID: {CartId}",
                    hardwareId,
                    cartId
                );

                if (!ModelState.IsValid)
                {
                    return BadRequest(new { success = false, message = "请求数据格式无效" });
                }

                // 使用PDA专用方法更新购物车
                var result = await _cartService.UpdatePDACartAsync(
                    hardwareId!,
                    cartId,
                    request.Name,
                    request.Description
                );

                if (result != null)
                {
                    return Ok(new { success = true, data = result });
                }
                else
                {
                    return BadRequest(new { success = false, message = "更新购物车失败" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新购物车失败，CartId: {CartId}", cartId);
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 搜索商品（PDA设备专用）
        /// </summary>
        [HttpGet("products/search")]
        public async Task<IActionResult> SearchProducts([FromQuery] string keyword)
        {
            try
            {
                // 验证设备授权
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var hardwareId = GetDeviceHardwareId();
                _logger.LogInformation(
                    "PDA设备搜索商品，设备ID: {HardwareId}, 关键词: {Keyword}",
                    hardwareId,
                    keyword
                );

                if (string.IsNullOrWhiteSpace(keyword))
                {
                    return BadRequest(new { success = false, message = "搜索关键词不能为空" });
                }

                // 获取设备关联的分店信息（用于库存查询）
                var (deviceStoreGuid, deviceStoreName) = await GetDeviceStoreInfoAsync(hardwareId!);

                // 使用PDA专用的商品搜索方法
                var products = await _cartService.SearchPDAProductsAsync(
                    hardwareId!,
                    keyword,
                    deviceStoreGuid
                );

                return Ok(new { success = true, data = products });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "搜索商品失败，Keyword: {Keyword}", keyword);
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 添加商品到购物车（PDA设备专用）
        /// </summary>
        [HttpPost("{cartId}/items")]
        public async Task<IActionResult> AddProductToCart(
            string cartId,
            [FromBody] AddProductToCartRequest request
        )
        {
            try
            {
                // 验证设备授权
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var hardwareId = GetDeviceHardwareId();
                _logger.LogInformation(
                    "PDA设备添加商品到购物车，设备ID: {HardwareId}, 购物车ID: {CartId}",
                    hardwareId,
                    cartId
                );

                if (!ModelState.IsValid)
                {
                    return BadRequest(new { success = false, message = "请求数据格式无效" });
                }

                var result = await _cartService.AddProductToPDACartAsync(
                    hardwareId!,
                    cartId,
                    request.ProductCode,
                    request.Quantity,
                    request.UnitPrice
                );

                if (result)
                {
                    return Ok(new { success = true, message = "商品添加成功" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "添加商品失败" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加商品到购物车失败，CartId: {CartId}", cartId);
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 批量添加商品到购物车（PDA设备专用）
        /// </summary>
        [HttpPost("{cartId}/items/batch")]
        public async Task<IActionResult> BatchAddProductsToCart(
            string cartId,
            [FromBody] BatchAddProductsRequest request
        )
        {
            try
            {
                // 验证设备授权
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var hardwareId = GetDeviceHardwareId();
                _logger.LogInformation(
                    "PDA设备批量添加商品到购物车，设备ID: {HardwareId}, 购物车ID: {CartId}",
                    hardwareId,
                    cartId
                );

                if (!ModelState.IsValid || request.Items == null || !request.Items.Any())
                {
                    return BadRequest(
                        new { success = false, message = "请求数据格式无效或商品列表为空" }
                    );
                }

                var items = request
                    .Items.Select(i => (i.ProductCode, i.Quantity, i.UnitPrice))
                    .ToList();
                var (successCount, failureCount, errors) =
                    await _cartService.BatchAddProductsToPDACartAsync(hardwareId!, cartId, items);

                return Ok(
                    new
                    {
                        success = true,
                        data = new
                        {
                            successCount,
                            failureCount,
                            errors,
                        },
                        message = $"批量添加完成：成功 {successCount} 个，失败 {failureCount} 个",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量添加商品到购物车失败，CartId: {CartId}", cartId);
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 更新购物车商品数量（PDA设备专用）
        /// </summary>
        [HttpPut("{cartId}/items/{cartItemId}")]
        public async Task<IActionResult> UpdateCartItemQuantity(
            string cartId,
            string cartItemId,
            [FromBody] UpdateCartItemQuantityRequest request
        )
        {
            try
            {
                // 验证设备授权
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var hardwareId = GetDeviceHardwareId();
                _logger.LogInformation(
                    "PDA设备更新购物车商品数量，设备ID: {HardwareId}, 购物车ID: {CartId}, 商品项ID: {CartItemId}",
                    hardwareId,
                    cartId,
                    cartItemId
                );

                if (!ModelState.IsValid)
                {
                    return BadRequest(new { success = false, message = "请求数据格式无效" });
                }

                var result = await _cartService.UpdatePDACartItemQuantityAsync(
                    hardwareId!,
                    cartId,
                    cartItemId,
                    request.Quantity
                );

                if (result)
                {
                    return Ok(new { success = true, message = "商品数量更新成功" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "更新商品数量失败" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "更新购物车商品数量失败，CartId: {CartId}, CartItemId: {CartItemId}",
                    cartId,
                    cartItemId
                );
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 从购物车移除商品（PDA设备专用）
        /// </summary>
        [HttpDelete("{cartId}/items/{cartItemId}")]
        public async Task<IActionResult> RemoveProductFromCart(string cartId, string cartItemId)
        {
            try
            {
                // 验证设备授权
                if (!await ValidateDeviceAuthAsync())
                {
                    return Unauthorized(new { success = false, message = "设备未授权" });
                }

                var hardwareId = GetDeviceHardwareId();
                _logger.LogInformation(
                    "PDA设备移除购物车商品，设备ID: {HardwareId}, 购物车ID: {CartId}, 商品项ID: {CartItemId}",
                    hardwareId,
                    cartId,
                    cartItemId
                );

                var result = await _cartService.RemoveProductFromPDACartAsync(
                    hardwareId!,
                    cartId,
                    cartItemId
                );

                if (result)
                {
                    return Ok(new { success = true, message = "商品移除成功" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "移除商品失败" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "移除购物车商品失败，CartId: {CartId}, CartItemId: {CartItemId}",
                    cartId,
                    cartItemId
                );
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }
    }
}
