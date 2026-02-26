using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using System.Security.Claims;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// 购物车管理API控制器
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize]
    public class CartController : ControllerBase
    {
        private readonly ICartService _cartService;
        private readonly ILogger<CartController> _logger;

        public CartController(ICartService cartService, ILogger<CartController> logger)
        {
            _cartService = cartService;
            _logger = logger;
        }

        /// <summary>
        /// 获取当前用户的购物车（不绑定门店）
        /// </summary>
        /// <returns>购物车信息</returns>
        [HttpGet]
        public async Task<IActionResult> GetCart()
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                var cart = await _cartService.GetUserCartAsync(userGuid);
                
                return Ok(new { success = true, data = cart });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get shopping cart");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 获取或创建当前用户的购物车（不绑定门店）
        /// </summary>
        /// <returns>购物车信息</returns>
        [HttpPost("get-or-create")]
        public async Task<IActionResult> GetOrCreateCart()
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                var cart = await _cartService.GetOrCreateUserCartAsync(userGuid);
                
                return Ok(new { success = true, data = cart });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get or create shopping cart");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 添加商品到购物车（不绑定门店）
        /// </summary>
        /// <param name="request">添加到购物车请求</param>
        /// <returns>操作结果</returns>
        [HttpPost("add")]
        public async Task<IActionResult> AddToCart([FromBody] AddToCartRequest request)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                if (!ModelState.IsValid)
                    return BadRequest(new { success = false, message = "Invalid request parameters", errors = ModelState });

                var result = await _cartService.AddToCartAsync(userGuid, request);
                
                if (result)
                    return Ok(new { success = true, message = "Product added to cart" });
                else
                    return BadRequest(new { success = false, message = "Failed to add product to cart" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add product to cart");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 从购物车移除商品
        /// </summary>
        /// <param name="cartItemGuid">购物车项GUID</param>
        /// <returns>操作结果</returns>
        [HttpDelete("{cartItemGuid}")]
        public async Task<IActionResult> RemoveFromCart(string cartItemGuid)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "用户未登录" });

                if (string.IsNullOrEmpty(cartItemGuid))
                    return BadRequest(new { success = false, message = "购物车项GUID不能为空" });

                var result = await _cartService.RemoveFromCartAsync(userGuid, cartItemGuid);
                
                if (result)
                    return Ok(new { success = true, message = "商品已从购物车移除" });
                else
                    return NotFound(new { success = false, message = "购物车项不存在或不属于当前用户" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从购物车移除商品失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 更新购物车项订货数量
        /// </summary>
        /// <param name="request">更新请求</param>
        /// <returns>操作结果</returns>
        [HttpPut("update-quantity")]
        public async Task<IActionResult> UpdateCartItemQuantity([FromBody] UpdateCartItemQuantityRequest request)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "用户未登录" });

                if (!ModelState.IsValid)
                    return BadRequest(new { success = false, message = "请求参数无效", errors = ModelState });

                var result = await _cartService.UpdateCartItemQuantityAsync(userGuid, request);

                if (result)
                    return Ok(new { success = true, message = "购物车项数量已更新" });
                else
                    return NotFound(new { success = false, message = "购物车项不存在或不属于当前用户" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新购物车项数量失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }
       


        /// <summary>
        /// 清空购物车（不绑定门店）
        /// </summary>
        /// <returns>操作结果</returns>
        [HttpDelete("clear")]
        public async Task<IActionResult> ClearCart()
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                var result = await _cartService.ClearCartAsync(userGuid);
                
                return Ok(new { success = true, message = "Cart cleared successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear cart");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

      

        /// <summary>
        /// 批量更新购物车项数量
        /// </summary>
        /// <param name="request">批量更新请求</param>
        /// <returns>操作结果</returns>
        [HttpPut("batch-update-quantities")]
        public async Task<IActionResult> BatchUpdateCartItemQuantities([FromBody] BatchUpdateQuantitiesRequest request)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(ApiResponse<object>.Error("用户未登录", "UNAUTHORIZED"));

                if (request.Updates == null || request.Updates.Count == 0)
                    return BadRequest(ApiResponse<object>.Error("更新数据不能为空", "INVALID_REQUEST"));

                var result = await _cartService.BatchUpdateCartItemQuantitiesAsync(userGuid, request.Updates);
                
                if (result)
                    return Ok(ApiResponse<object>.CreateSuccess("批量更新完成"));
                else
                    return BadRequest(ApiResponse<object>.Error("批量更新失败", "UPDATE_FAILED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新购物车项数量失败");
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// 批量移除购物车项
        /// </summary>
        /// <param name="request">批量移除请求</param>
        /// <returns>操作结果</returns>
        [HttpDelete("batch-remove")]
        public async Task<IActionResult> BatchRemoveCartItems([FromBody] BatchRemoveRequest request)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "用户未登录" });

                if (request.CartItemGuids == null || request.CartItemGuids.Count == 0)
                    return BadRequest(new { success = false, message = "购物车项GUID列表不能为空" });

                var result = await _cartService.BatchRemoveCartItemsAsync(userGuid, request.CartItemGuids);
                
                if (result)
                    return Ok(new { success = true, message = "批量删除完成" });
                else
                    return BadRequest(new { success = false, message = "批量删除失败" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量移除购物车项失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取购物车统计信息（不绑定门店）
        /// </summary>
        /// <returns>购物车统计</returns>
        [HttpGet("summary")]
        public async Task<IActionResult> GetCartSummary()
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                var summary = await _cartService.GetCartSummaryAsync(userGuid);
                
                return Ok(new { success = true, data = summary });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get cart summary");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 检查商品是否在购物车中（不绑定门店）
        /// </summary>
        /// <param name="productCode">商品代码</param>
        /// <returns>检查结果</returns>
        [HttpGet("check-product")]
        public async Task<IActionResult> CheckProductInCart([FromQuery] string productCode)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                if (string.IsNullOrEmpty(productCode))
                    return BadRequest(new { success = false, message = "Product code cannot be empty" });

                var inCart = await _cartService.IsProductInCartAsync(userGuid, productCode);
                var quantity = await _cartService.GetProductQuantityInCartAsync(userGuid, productCode);
                
                return Ok(new { 
                    success = true, 
                    data = new { 
                        inCart = inCart,
                        quantity = quantity
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check if product is in cart");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }
        
        /// <summary>
        /// 批量检查商品是否在购物车中
        /// </summary>
        /// <param name="request">批量检查请求</param>
        /// <returns>批量检查结果</returns>
        [HttpPost("batch-check-products")]
        public async Task<IActionResult> BatchCheckProductsInCart([FromBody] BatchCheckProductsRequest request)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                if (request?.ProductCodes == null || !request.ProductCodes.Any())
                    return BadRequest(new { success = false, message = "Product codes cannot be empty" });

                var results = await _cartService.BatchCheckProductsInCartAsync(userGuid, request.ProductCodes);
                
                return Ok(new { 
                    success = true, 
                    data = results.ToDictionary(
                        x => x.Key, 
                        x => new { 
                            inCart = x.Value.InCart,
                            quantity = x.Value.Quantity
                        }
                    )
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to batch check products in cart");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 清理过期购物车（管理员功能）
        /// </summary>
        /// <returns>清理数量</returns>
        [HttpDelete("cleanup-expired")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CleanupExpiredCarts()
        {
            try
            {
                var count = await _cartService.CleanExpiredCartsAsync();
                
                return Ok(new { success = true, data = new { cleanedCount = count }, message = $"已清理 {count} 个过期购物车" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理过期购物车失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        #region Private Methods

        /// <summary>
        /// 获取当前用户GUID
        /// </summary>
        /// <returns>用户GUID</returns>
        private string? GetCurrentUserGuid()
        {
            return User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("UserGUID")?.Value;
        }

        #endregion

        /// <summary>
        /// 处理购物车（清空购物车，不创建订单记录）
        /// </summary>
        /// <param name="request">处理请求</param>
        /// <returns>操作结果</returns>
        [HttpPost("process-cart")]
        public async Task<IActionResult> ProcessCart([FromBody] CreateOrderFromCartRequest request)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                if (!ModelState.IsValid)
                    return BadRequest(new { success = false, message = "Invalid request parameters", errors = ModelState });

                var result = await _cartService.CreateOrderFromCartAsync(userGuid, request);
                
                if (!string.IsNullOrEmpty(result))
                {
                    return Ok(new { 
                        success = true, 
                        message = "Cart processed successfully",
                        result = result
                    });
                }
                else
                {
                    return BadRequest(new { 
                        success = false, 
                        message = "Failed to process cart"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process cart");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 保存购物车状态（更新状态为Save）
        /// </summary>
        /// <param name="request">保存购物车请求</param>
        /// <returns>操作结果</returns>
        [HttpPost("save")]
        public async Task<IActionResult> SaveCartStatus([FromBody] SaveCartStatusRequest request)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                if (!ModelState.IsValid)
                    return BadRequest(new { success = false, message = "Invalid request parameters", errors = ModelState });

                var result = await _cartService.SaveCartStatusAsync(userGuid, request);
                
                if (result)
                    return Ok(new { success = true, message = "购物车已保存" });
                else
                    return BadRequest(new { success = false, message = "保存购物车失败" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save cart status");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 提交购物车（更新状态为Submitted，不创建订单记录）
        /// </summary>
        /// <param name="request">提交购物车请求</param>
        /// <returns>购物车号</returns>
        [HttpPost("submit")]
        public async Task<IActionResult> SubmitCart([FromBody] SubmitCartRequest request)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                if (!ModelState.IsValid)
                    return BadRequest(new { success = false, message = "Invalid request parameters", errors = ModelState });

                var cartNumber = await _cartService.SubmitCartAsync(userGuid, request);
                
                if (!string.IsNullOrEmpty(cartNumber))
                    return Ok(new { success = true, message = "Cart submitted successfully (status updated only)", data = new { OrderNumber = cartNumber } });
                else
                    return BadRequest(new { success = false, message = "Failed to submit cart" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to submit cart");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 获取购物车列表（支持状态过滤和分页）
        /// </summary>
        /// <param name="status">状态过滤</param>
        /// <param name="page">页码</param>
        /// <param name="pageSize">每页大小</param>
        /// <param name="searchKeyword">搜索关键词</param>
        /// <param name="storeId">分店ID过滤</param>
        /// <param name="excludeStatus">排除的状态（可多个）</param>
        /// <returns>购物车列表</returns>
        [HttpGet("list")]
        public async Task<IActionResult> GetCartList(
            [FromQuery] string? status = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? searchKeyword = null,
            [FromQuery] string? storeId = null,
            [FromQuery] List<string>? excludeStatus = null)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                var request = new CartListRequest
                {
                    Status = status,
                    Page = page,
                    PageSize = pageSize,
                    SearchKeyword = searchKeyword,
                    StoreId = storeId,
                    ExcludeStatuses = excludeStatus ?? new List<string>()
                };

                var result = await _cartService.GetCartListAsync(userGuid, request);
                
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get cart list");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 根据购物车GUID获取购物车详情
        /// </summary>
        /// <param name="cartGuid">购物车GUID</param>
        /// <returns>购物车详情</returns>
        [HttpGet("{cartGuid}")]
        public async Task<IActionResult> GetCartById(string cartGuid)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                if (string.IsNullOrEmpty(cartGuid))
                    return BadRequest(new { success = false, message = "Cart GUID cannot be empty" });

                var cart = await _cartService.GetCartByIdAsync(cartGuid);
                if (cart == null)
                    return NotFound(new { success = false, message = "Cart not found" });
                
                return Ok(new { success = true, data = cart });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get cart by ID {CartGuid}", cartGuid);
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 获取指定购物车的商品详情
        /// </summary>
        /// <param name="cartGuid">购物车GUID</param>
        /// <returns>商品列表</returns>
        [HttpGet("{cartGuid}/items")]
        public async Task<IActionResult> GetCartItems(string cartGuid)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                if (string.IsNullOrEmpty(cartGuid))
                    return BadRequest(new { success = false, message = "Cart GUID cannot be empty" });

                var cartItems = await _cartService.GetCartItemsByCartGuidAsync(cartGuid);
                
                return Ok(new { success = true, data = cartItems });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get cart items for cart {CartGuid}", cartGuid);
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 更改订单所属分店
        /// </summary>
        /// <param name="cartGuid">购物车GUID</param>
        /// <param name="request">更改分店请求</param>
        /// <returns>操作结果</returns>
        [HttpPut("{cartGuid}/change-store")]
        public async Task<IActionResult> ChangeOrderStore(string cartGuid, [FromBody] ChangeStoreRequest request)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                if (string.IsNullOrEmpty(cartGuid))
                    return BadRequest(new { success = false, message = "Cart GUID cannot be empty" });

                if (string.IsNullOrEmpty(request.NewStoreGuid))
                    return BadRequest(new { success = false, message = "New store GUID cannot be empty" });

                if (string.IsNullOrEmpty(request.Reason))
                    return BadRequest(new { success = false, message = "Reason cannot be empty" });

                var result = await _cartService.ChangeOrderStoreAsync(userGuid, cartGuid, request.NewStoreGuid, request.Reason);
                
                if (result)
                    return Ok(new { success = true, message = "Store changed successfully" });
                else
                    return BadRequest(new { success = false, message = "Failed to change store" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to change store for cart {CartGuid}", cartGuid);
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 检查用户是否有Active状态的购物车
        /// </summary>
        /// <returns>检查结果</returns>
        [HttpGet("check-active")]
        public async Task<IActionResult> CheckActiveCart()
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                var result = await _cartService.CheckActiveCartAsync(userGuid);
                
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check active cart");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 切换购物车状态
        /// </summary>
        /// <param name="request">状态切换请求</param>
        /// <returns>操作结果</returns>
        [HttpPost("switch-status")]
        public async Task<IActionResult> SwitchCartStatus([FromBody] CartStatusSwitchRequest request)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                if (!ModelState.IsValid)
                    return BadRequest(new { success = false, message = "Invalid request parameters", errors = ModelState });

                var result = await _cartService.SwitchCartStatusAsync(userGuid, request);
                
                if (result)
                    return Ok(new { success = true, message = "购物车状态切换成功" });
                else
                    return BadRequest(new { success = false, message = "购物车状态切换失败" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to switch cart status");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 合并购物车
        /// </summary>
        /// <param name="request">合并请求</param>
        /// <returns>操作结果</returns>
        [HttpPost("merge")]
        public async Task<IActionResult> MergeCarts([FromBody] CartMergeRequest request)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                if (!ModelState.IsValid)
                    return BadRequest(new { success = false, message = "Invalid request parameters", errors = ModelState });

                var result = await _cartService.MergeCartsAsync(userGuid, request);
                
                if (result)
                    return Ok(new { success = true, message = "购物车合并成功" });
                else
                    return BadRequest(new { success = false, message = "购物车合并失败" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to merge carts");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        #region Request DTOs

        /// <summary>
        /// 批量更新数量请求
        /// </summary>
        public class BatchUpdateQuantitiesRequest
        {
            /// <summary>
            /// 更新字典（CartItemGUID -> 新数量）
            /// </summary>
            public Dictionary<string, int> Updates { get; set; } = new Dictionary<string, int>();
        }

        /// <summary>
        /// 批量移除请求
        /// </summary>
        public class BatchRemoveRequest
        {
            /// <summary>
            /// 购物车项GUID列表
            /// </summary>
            public List<string> CartItemGuids { get; set; } = new List<string>();
        }



        /// <summary>
        /// 软删除购物车（仅限Saved状态的购物车）
        /// </summary>
        /// <param name="cartGuid">购物车GUID</param>
        /// <returns>操作结果</returns>
        [HttpDelete("soft-delete/{cartGuid}")]
        public async Task<IActionResult> SoftDeleteCart(string cartGuid)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                if (string.IsNullOrEmpty(cartGuid))
                    return BadRequest(new { success = false, message = "Cart GUID cannot be empty" });

                var result = await _cartService.SoftDeleteCartAsync(userGuid, cartGuid);
                
                if (result)
                    return Ok(new { success = true, message = "Cart deleted successfully" });
                else
                    return BadRequest(new { success = false, message = "Failed to delete cart or cart is not eligible for deletion" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to soft delete cart: CartGuid={CartGuid}", cartGuid);
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 恢复删除的购物车
        /// </summary>
        /// <param name="cartGuid">购物车GUID</param>
        /// <returns>操作结果</returns>
        [HttpPost("restore/{cartGuid}")]
        public async Task<IActionResult> RestoreCart(string cartGuid)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                if (string.IsNullOrEmpty(cartGuid))
                    return BadRequest(new { success = false, message = "Cart GUID cannot be empty" });

                var result = await _cartService.RestoreCartAsync(userGuid, cartGuid);
                
                if (result)
                    return Ok(new { success = true, message = "Cart restored successfully" });
                else
                    return BadRequest(new { success = false, message = "Failed to restore cart or cart is not eligible for restoration" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore cart: CartGuid={CartGuid}", cartGuid);
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 更新购物车备注
        /// </summary>
        /// <param name="request">备注更新请求</param>
        /// <returns>操作结果</returns>
        [HttpPut("remarks")]
        public async Task<IActionResult> UpdateCartRemarks([FromBody] UpdateCartRemarksRequest request)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "User not logged in" });

                var result = await _cartService.UpdateCartRemarksAsync(userGuid, request.Remarks);
                
                if (result)
                    return Ok(new { success = true, message = "Cart remarks updated successfully" });
                else
                    return BadRequest(new { success = false, message = "Failed to update cart remarks or no active cart found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update cart remarks: UserGuid={UserGuid}, Remarks={Remarks}", GetCurrentUserGuid(), request?.Remarks);
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 更新购物车项价格
        /// </summary>
        [HttpPost("update-item-price")]
        public async Task<IActionResult> UpdateCartItemPrice([FromBody] UpdateCartItemPriceRequest request)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized();

                var result = await _cartService.UpdateCartItemPriceAsync(userGuid, request);
                return Ok(new { success = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating cart item price");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 批量更新购物车项价格
        /// </summary>
        [HttpPost("batch-update-prices")]
        public async Task<IActionResult> BatchUpdateCartItemPrices([FromBody] BatchUpdatePricesRequest request)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(ApiResponse<object>.Error("用户未登录", "UNAUTHORIZED"));

                var result = await _cartService.BatchUpdateCartItemPricesAsync(userGuid, request.Updates);
                return Ok(ApiResponse<bool>.OK(result, result ? "批量更新价格完成" : "批量更新价格失败"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error batch updating cart item prices");
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// 更新购物车折扣和运费
        /// </summary>
        /// <param name="cartGuid">购物车GUID</param>
        /// <param name="request">更新请求</param>
        /// <returns>操作结果</returns>
        [HttpPut("{cartGuid}/discount-freight")]
        public async Task<IActionResult> UpdateCartDiscountAndFreight(string cartGuid, [FromBody] UpdateCartDiscountFreightRequest request)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "用户未登录" });

                if (string.IsNullOrEmpty(cartGuid))
                    return BadRequest(new { success = false, message = "购物车GUID不能为空" });

                var result = await _cartService.UpdateCartDiscountAndFreightAsync(cartGuid, request.Discount, request.FreightFee, userGuid);
                
                if (result)
                    return Ok(new { success = true, message = "购物车折扣和运费更新成功" });
                else
                    return BadRequest(new { success = false, message = "更新失败" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating cart discount and freight for cart {CartGuid}", cartGuid);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        #endregion

        #region 仓库管理员订单管理功能

        /// <summary>
        /// 仓库管理员创建新订单（为指定分店创建空订单）
        /// </summary>
        /// <param name="request">创建订单请求</param>
        /// <returns>新创建的订单信息</returns>
        [HttpPost("create-store-order")]
        [Authorize(Roles = "Admin,Manager,WarehouseManager")]
        public async Task<IActionResult> CreateStoreOrder([FromBody] CreateStoreOrderRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(new { success = false, message = "请求参数无效", errors = ModelState });

                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "用户未登录" });

                var cart = await _cartService.CreateStoreOrderAsync(userGuid, request);
                
                if (cart != null)
                {
                    return Ok(new { 
                        success = true, 
                        message = "订单创建成功",
                        data = cart
                    });
                }
                else
                {
                    return BadRequest(new { 
                        success = false, 
                        message = "订单创建失败"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建分店订单失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }


        /// <summary>
        /// 通过货号批量查询商品信息
        /// </summary>
        /// <param name="request">批量查询请求</param>
        /// <returns>商品信息列表</returns>
        [HttpPost("batch-search-products")]
        [Authorize(Roles = "Admin,Manager,WarehouseManager")]
        public async Task<IActionResult> BatchSearchProducts([FromBody] BatchSearchProductsRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(new { success = false, message = "请求参数无效", errors = ModelState });

                var products = await _cartService.BatchSearchProductsAsync(request.ItemNumbers);
                
                return Ok(new { 
                    success = true, 
                    data = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量查询商品失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 批量添加商品到订单
        /// </summary>
        /// <param name="request">批量添加请求</param>
        /// <returns>添加结果</returns>
        [HttpPost("batch-add-items")]
        [Authorize(Roles = "Admin,Manager,WarehouseManager")]
        public async Task<IActionResult> BatchAddItems([FromBody] BatchAddItemsRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(new { success = false, message = "请求参数无效", errors = ModelState });

                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "用户未登录" });

                var result = await _cartService.BatchAddItemsToCartAsync(request, userGuid);
                
                return Ok(new { 
                    success = true, 
                    message = "商品批量添加完成",
                    data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量添加商品失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 清除指定购物车的所有商品（仓库管理员功能）
        /// </summary>
        /// <param name="cartGuid">购物车GUID</param>
        /// <returns>操作结果</returns>
        [HttpDelete("clear/{cartGuid}")]
        [Authorize(Roles = "Admin,Manager,WarehouseManager")]
        public async Task<IActionResult> ClearCartById(string cartGuid)
        {
            try
            {
                _logger.LogInformation("仓库管理员清除购物车请求: CartGuid={CartGuid}", cartGuid);

                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "用户未登录" });

                var result = await _cartService.ClearCartByIdAsync(cartGuid, userGuid);

                if (result)
                {
                    return Ok(new { success = true, message = "购物车已清空" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "清空购物车失败" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清除购物车失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 批量删除指定购物车的商品项（仓库管理员功能）
        /// </summary>
        /// <param name="cartGuid">购物车GUID</param>
        /// <param name="request">批量删除请求</param>
        /// <returns>操作结果</returns>
        [HttpDelete("batch-remove/{cartGuid}")]
        [Authorize(Roles = "Admin,Manager,WarehouseManager")]
        public async Task<IActionResult> BatchRemoveCartItemsByCartId(string cartGuid, [FromBody] BatchRemoveCartItemsRequest request)
        {
            try
            {
                _logger.LogInformation("仓库管理员批量删除购物车项请求: CartGuid={CartGuid}, ItemCount={ItemCount}", 
                    cartGuid, request.CartItemGuids?.Count ?? 0);

                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "用户未登录" });

                if (request?.CartItemGuids == null || request.CartItemGuids.Count == 0)
                    return BadRequest(new { success = false, message = "请求的商品ID列表为空" });

                var result = await _cartService.BatchRemoveCartItemsByCartIdAsync(cartGuid, request.CartItemGuids, userGuid);

                if (result)
                {
                    return Ok(new { success = true, message = "商品已删除" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "删除商品失败" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除购物车项失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// Excel导入商品到购物车
        /// </summary>
        /// <param name="request">Excel导入请求</param>
        /// <returns>导入结果</returns>
        [HttpPost("import-excel")]
        [Authorize(Roles = "Admin,Manager,WarehouseManager")]
        public async Task<IActionResult> ImportExcelItems([FromBody] ExcelImportRequest request)
        {
            try
            {
                _logger.LogInformation("Excel导入API调用: CartGUID={CartGUID}, ItemCount={ItemCount}", 
                    request.CartGUID, request.Items.Count);

                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "用户未登录" });

                var result = await _cartService.ImportExcelItemsToCartAsync(request, userGuid);

                return Ok(new { 
                    success = true, 
                    data = result,
                    message = $"导入完成：成功 {result.SuccessCount} 个，失败 {result.FailureCount} 个，共 {result.TotalCount} 个"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excel导入API失败: CartGUID={CartGUID}", request.CartGUID);
                return StatusCode(500, new { 
                    success = false, 
                    message = "Excel导入失败",
                    error = ex.Message 
                });
            }
        }

        /// <summary>
        /// Excel文件上传并解析导入到购物车
        /// </summary>
        /// <param name="cartGuid">购物车GUID</param>
        /// <param name="file">Excel文件</param>
        /// <param name="clearExisting">是否清除原有商品</param>
        /// <returns>导入结果</returns>
        [HttpPost("upload-excel/{cartGuid}")]
        [Authorize(Roles = "Admin,Manager,WarehouseManager")]
        public async Task<IActionResult> UploadExcelFile(
            string cartGuid, 
            IFormFile file, 
            [FromForm] bool clearExisting = true)
        {
            try
            {
                _logger.LogInformation("Excel文件上传请求: CartGuid={CartGuid}, FileName={FileName}, Size={Size}KB", 
                    cartGuid, file?.FileName, file?.Length / 1024);

                if (string.IsNullOrEmpty(cartGuid))
                    return BadRequest(new { success = false, message = "购物车GUID不能为空" });

                if (file == null || file.Length == 0)
                    return BadRequest(new { success = false, message = "请选择Excel文件" });

                // 验证文件格式
                var allowedExtensions = new[] { ".xlsx", ".xls" };
                var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(fileExtension))
                    return BadRequest(new { success = false, message = "只支持.xlsx或.xls格式的Excel文件" });

                // 验证文件大小（最大5MB）
                if (file.Length > 5 * 1024 * 1024)
                    return BadRequest(new { success = false, message = "文件大小不能超过5MB" });

                // 解析Excel文件
                var excelItems = await ParseExcelFileAsync(file);
                if (!excelItems.Any())
                    return BadRequest(new { success = false, message = "Excel文件中没有找到有效的商品数据" });

                // 创建导入请求
                var importRequest = new ExcelImportRequest
                {
                    CartGUID = cartGuid,
                    Items = excelItems,
                    ClearExistingItems = clearExisting
                };

                // 获取用户GUID
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                    return Unauthorized(new { success = false, message = "用户未登录" });

                // 执行导入
                var result = await _cartService.ImportExcelItemsToCartAsync(importRequest, userGuid);

                return Ok(new { 
                    success = true, 
                    data = result,
                    message = $"Excel导入完成：成功 {result.SuccessCount} 个，失败 {result.FailureCount} 个，共 {result.TotalCount} 个"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excel文件上传失败: CartGuid={CartGuid}, FileName={FileName}", 
                    cartGuid, file?.FileName);
                return StatusCode(500, new { 
                    success = false, 
                    message = "Excel文件处理失败",
                    error = ex.Message 
                });
            }
        }

        /// <summary>
        /// 解析Excel文件获取商品信息
        /// </summary>
        /// <param name="file">Excel文件</param>
        /// <returns>商品列表</returns>
        private async Task<List<ExcelImportItem>> ParseExcelFileAsync(IFormFile file)
        {
            var items = new List<ExcelImportItem>();

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1); // 使用第一个工作表

            // 查找表头行
            var headerRow = FindHeaderRow(worksheet);
            if (headerRow == 0)
            {
                throw new InvalidOperationException("未找到有效的表头行，请确保Excel文件包含'货号'和'数量'列");
            }

            // 获取列索引
            var itemNumberColumn = FindColumnIndex(worksheet, headerRow, new[] { "货号", "商品编码", "产品编码", "ItemNumber", "ProductCode" });
            var quantityColumn = FindColumnIndex(worksheet, headerRow, new[] { "数量", "订货数量", "Quantity", "Qty" });
            var priceColumn = FindColumnIndex(worksheet, headerRow, new[] { "价格", "单价", "Price", "UnitPrice" }, required: false);

            if (itemNumberColumn == 0)
                throw new InvalidOperationException("未找到货号列，请确保Excel文件包含'货号'或'商品编码'列");

            if (quantityColumn == 0)
                throw new InvalidOperationException("未找到数量列，请确保Excel文件包含'数量'或'订货数量'列");

            // 读取数据行
            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? headerRow;
            for (int row = headerRow + 1; row <= lastRow; row++)
            {
                try
                {
                    var itemNumberCell = worksheet.Cell(row, itemNumberColumn);
                    var quantityCell = worksheet.Cell(row, quantityColumn);

                    // 跳过空行
                    if (itemNumberCell.IsEmpty() && quantityCell.IsEmpty())
                        continue;

                    var itemNumber = itemNumberCell.GetString().Trim();
                    if (string.IsNullOrEmpty(itemNumber))
                        continue;

                    // 解析数量
                    if (!int.TryParse(quantityCell.GetString(), out var quantity) || quantity <= 0)
                    {
                        _logger.LogWarning("第{Row}行数量无效: {Quantity}", row, quantityCell.GetString());
                        continue;
                    }

                    // 解析价格（可选）
                    decimal? price = null;
                    if (priceColumn > 0)
                    {
                        var priceCell = worksheet.Cell(row, priceColumn);
                        if (!priceCell.IsEmpty() && decimal.TryParse(priceCell.GetString(), out var priceValue) && priceValue > 0)
                        {
                            price = priceValue;
                        }
                    }

                    items.Add(new ExcelImportItem
                    {
                        ItemNumber = itemNumber,
                        Quantity = quantity,
                        Price = price
                    });

                    _logger.LogDebug("解析Excel行 {Row}: 货号={ItemNumber}, 数量={Quantity}, 价格={Price}", 
                        row, itemNumber, quantity, price);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "解析Excel第{Row}行失败", row);
                    continue;
                }
            }

            _logger.LogInformation("Excel解析完成，共解析出 {Count} 个有效商品", items.Count);
            return items;
        }

        /// <summary>
        /// 查找表头行
        /// </summary>
        /// <param name="worksheet">工作表</param>
        /// <returns>表头行号，0表示未找到</returns>
        private int FindHeaderRow(ClosedXML.Excel.IXLWorksheet worksheet)
        {
            var maxRowToCheck = Math.Min(10, worksheet.LastRowUsed()?.RowNumber() ?? 1);
            
            for (int row = 1; row <= maxRowToCheck; row++)
            {
                var rowRange = worksheet.Row(row);
                var cellValues = rowRange.CellsUsed().Select(c => c.GetString().Trim().ToLower()).ToList();
                
                // 检查是否包含必要的列名
                var hasItemNumber = cellValues.Any(v => v.Contains("货号") || v.Contains("商品编码") || v.Contains("产品编码") || 
                                                       v.Contains("itemnumber") || v.Contains("productcode"));
                var hasQuantity = cellValues.Any(v => v.Contains("数量") || v.Contains("订货数量") || 
                                                     v.Contains("quantity") || v.Contains("qty"));
                
                if (hasItemNumber && hasQuantity)
                {
                    _logger.LogInformation("找到表头行: 第{Row}行", row);
                    return row;
                }
            }
            
            return 0;
        }

        /// <summary>
        /// 查找指定列的索引
        /// </summary>
        /// <param name="worksheet">工作表</param>
        /// <param name="headerRow">表头行号</param>
        /// <param name="columnNames">可能的列名</param>
        /// <param name="required">是否必需</param>
        /// <returns>列索引，0表示未找到</returns>
        private int FindColumnIndex(ClosedXML.Excel.IXLWorksheet worksheet, int headerRow, string[] columnNames, bool required = true)
        {
            var headerRowRange = worksheet.Row(headerRow);
            var lastColumn = headerRowRange.LastCellUsed()?.Address.ColumnNumber ?? 1;
            
            for (int col = 1; col <= lastColumn; col++)
            {
                var cellValue = worksheet.Cell(headerRow, col).GetString().Trim().ToLower();
                if (columnNames.Any(name => cellValue.Contains(name.ToLower())))
                {
                    _logger.LogDebug("找到列 '{ColumnNames}' 在第{Column}列", string.Join(",", columnNames), col);
                    return col;
                }
            }
            
            if (required)
                _logger.LogWarning("未找到必需的列: {ColumnNames}", string.Join(",", columnNames));
            
            return 0;
        }

        #endregion

        #region 用户订单和仓库订单分离查询

        /// <summary>
        /// 获取用户相关订单列表（用户创建和关联分店的订单）
        /// </summary>
        [HttpGet("user-related")]
        [Authorize]
        public async Task<IActionResult> GetUserRelatedOrders([FromQuery] CartListRequest request)
        {
            try
            {
                var userGuid = GetCurrentUserGuid();
                if (string.IsNullOrEmpty(userGuid))
                {
                    return Unauthorized(new { success = false, message = "User not authenticated" });
                }

                var result = await _cartService.GetUserRelatedOrdersAsync(userGuid, request);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get user related orders");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// 获取所有订单列表（仓库管理员视图）
        /// </summary>
        [HttpGet("all")]
        [Authorize(Roles = "Admin,Manager,WarehouseManager")]
        public async Task<IActionResult> GetAllOrders([FromQuery] CartListRequest request)
        {
            try
            {
                var result = await _cartService.GetAllOrdersAsync(request);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all orders");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        #endregion
    }
}
