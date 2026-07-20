using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 购物车服务实现
    /// </summary>
    public class CartService : ICartService
    {
        private static readonly string[] CartWritePermissions =
        {
            Permissions.OrderFront.View,
            Permissions.Orders.Create,
            Permissions.Warehouse.ManageOrders,
            Permissions.Warehouse.Manage,
        };
        private readonly SqlSugarContext _context;
        private readonly IMapper _mapper;
        private readonly ILogger<CartService> _logger;
        private readonly IWarehouseProductService _productService;
        private readonly IOrderNumberGenerator _orderNumberGenerator;
        private readonly IAuthorizationService _authorizationService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly TimeProvider _timeProvider;

        public CartService(
            SqlSugarContext context,
            IMapper mapper,
            ILogger<CartService> logger,
            IWarehouseProductService productService,
            IOrderNumberGenerator orderNumberGenerator,
            IAuthorizationService authorizationService,
            IHttpContextAccessor httpContextAccessor,
            TimeProvider? timeProvider = null
        )
        {
            _context = context;
            _mapper = mapper;
            _logger = logger;
            _productService = productService;
            _orderNumberGenerator = orderNumberGenerator;
            _authorizationService = authorizationService;
            _httpContextAccessor = httpContextAccessor;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task<CartDto?> GetUserCartAsync(string userGuid)
        {
            try
            {
                var cart = await _context
                    .Db.Queryable<Cart>()
                    .Includes(x => x.CartItems)
                    .Where(x => x.UserGUID == userGuid && x.CartStatus == "Active")
                    .FirstAsync();

                if (cart == null)
                    return null;

                return _mapper.Map<CartDto>(cart);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get user cart: UserGuid={UserGuid}", userGuid);
                return null;
            }
        }

        public async Task<CartDto> GetOrCreateUserCartAsync(string userGuid)
        {
            try
            {
                var existingCart = await GetUserCartAsync(userGuid);
                if (existingCart != null)
                    return existingCart;

                // 创建新购物车（不绑定门店）
                var newCart = new Cart
                {
                    CartGUID = Guid.NewGuid().ToString(),
                    UserGUID = userGuid,
                    StoreGUID = null, // 购物车创建时不绑定门店
                    CartStatus = "Active",
                    LastModified = DateTime.Now,
                    ExpiresAt = DateTime.Now.AddDays(30), // 30天后过期
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    CreatedBy = userGuid,
                    UpdatedBy = userGuid,
                };

                await _context.Db.Insertable(newCart).ExecuteCommandAsync();

                _logger.LogInformation(
                    "Created new cart: CartGuid={CartGuid}, UserGuid={UserGuid}",
                    newCart.CartGUID,
                    userGuid
                );

                return _mapper.Map<CartDto>(newCart);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to get or create user cart: UserGuid={UserGuid}",
                    userGuid
                );
                throw;
            }
        }

        public async Task<bool> AddToCartAsync(string userGuid, AddToCartRequest request)
        {
            try
            {
                // 根据ProductGUID获取商品信息
                var product = await GetProductByGuidAsync(request.CartItem.ProductCode);
                if (product == null)
                {
                    _logger.LogWarning(
                        "Product not found: ProductCode={ProductCode}",
                        request.CartItem.ProductCode
                    );
                    return false;
                }

                // 获取或创建购物车（不绑定门店）
                var cart = await GetOrCreateUserCartAsync(userGuid);

                // 检查商品是否已在购物车中
                var existingCartItem = await _context
                    .Db.Queryable<CartItem>()
                    .Where(x =>
                        x.CartGUID == cart.CartGUID && x.ProductCode == request.CartItem.ProductCode
                    )
                    .FirstAsync();

                if (existingCartItem != null)
                {
                    // 更新数量
                    existingCartItem.Quantity += request.CartItem.Quantity;
                    existingCartItem.TotalPrice =
                        existingCartItem.UnitPrice * existingCartItem.Quantity;
                    // 更新实际数量
                    existingCartItem.ActualQuantity = existingCartItem.Quantity;

                    existingCartItem.LastUpdated = DateTime.Now;
                    existingCartItem.UpdatedAt = DateTime.Now;
                    existingCartItem.UpdatedBy = userGuid;

                    await _context.Db.Updateable(existingCartItem).ExecuteCommandAsync();
                }
                else
                {
                    // 创建新的购物车项
                    var cartItem = new CartItem
                    {
                        CartItemGUID = Guid.NewGuid().ToString(),
                        CartGUID = cart.CartGUID!,
                        ProductCode = request.CartItem.ProductCode,
                        ItemNumber = request.CartItem.ItemNumber,
                        ProductName = request.CartItem.ProductName,
                        ProductImage = request.CartItem.ProductImage,
                        UnitPrice = request.CartItem.UnitPrice, // 使用国内价格
                        Quantity = request.CartItem.Quantity,
                        ActualQuantity = request.CartItem.Quantity,
                        ActualPrice = request.CartItem.UnitPrice,
                        TotalPrice = (request.CartItem.UnitPrice) * request.CartItem.Quantity,
                        Volume = request.CartItem.Volume, // 默认值，应该从商品表获取
                        Weight = 0.1m, // 默认值，应该从商品表获取
                        MinOrderQuantity = request.CartItem.MinOrderQuantity,
                        AddedAt = DateTime.Now,
                        LastUpdated = DateTime.Now,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        CreatedBy = userGuid,
                        UpdatedBy = userGuid,
                    };

                    await _context.Db.Insertable(cartItem).ExecuteCommandAsync();
                }

                // 更新购物车摘要
                await UpdateCartSummaryAsync(cart.CartGUID!);

                _logger.LogInformation(
                    "Product added to cart: UserGuid={UserGuid}, ProductCode={ProductCode}, Quantity={Quantity}",
                    userGuid,
                    request.CartItem.ProductCode,
                    request.CartItem.Quantity
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to add product to cart: UserGuid={UserGuid}, ProductCode={ProductCode}",
                    userGuid,
                    request.CartItem.ProductCode
                );
                return false;
            }
        }

        public async Task<bool> RemoveFromCartAsync(string userGuid, string cartItemGuid)
        {
            try
            {
                var cartItem = await _context
                    .Db.Queryable<CartItem>()
                    .Includes(x => x.Cart)
                    .Where(x => x.CartItemGUID == cartItemGuid && x.Cart!.UserGUID == userGuid)
                    .FirstAsync();

                if (cartItem == null)
                {
                    _logger.LogWarning(
                        "购物车项不存在或不属于该用户: CartItemGuid={CartItemGuid}, UserGuid={UserGuid}",
                        cartItemGuid,
                        userGuid
                    );
                    return false;
                }

                await _context
                    .Db.Deleteable<CartItem>()
                    .Where(x => x.CartItemGUID == cartItemGuid)
                    .ExecuteCommandAsync();

                // 更新购物车摘要
                await UpdateCartSummaryAsync(cartItem.CartGUID);

                _logger.LogInformation(
                    "从购物车移除商品: UserGuid={UserGuid}, CartItemGuid={CartItemGuid}",
                    userGuid,
                    cartItemGuid
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "从购物车移除商品失败: UserGuid={UserGuid}, CartItemGuid={CartItemGuid}",
                    userGuid,
                    cartItemGuid
                );
                return false;
            }
        }

        public async Task<bool> UpdateCartItemQuantityAsync(
            string userGuid,
            UpdateCartItemQuantityRequest request
        )
        {
            try
            {
                var cartItem = await _context
                    .Db.Queryable<CartItem>()
                    .Includes(x => x.Cart)
                    .Where(x => x.CartItemGUID == request.CartItemGUID)
                    .FirstAsync();

                if (cartItem == null)
                {
                    _logger.LogWarning(
                        "购物车项不存在或不属于该用户: CartItemGuid={CartItemGuid}, UserGuid={UserGuid}",
                        request.CartItemGUID,
                        userGuid
                    );
                    return false;
                }

                cartItem.Quantity = request.Quantity;
                cartItem.ActualQuantity = request.Quantity;
                cartItem.TotalPrice = cartItem.UnitPrice * request.Quantity;
                cartItem.LastUpdated = DateTime.Now;
                cartItem.UpdatedAt = DateTime.Now;
                cartItem.UpdatedBy = userGuid;

                await _context.Db.Updateable(cartItem).ExecuteCommandAsync();

                // 更新购物车摘要
                await UpdateCartSummaryAsync(cartItem.CartGUID);

                _logger.LogInformation(
                    "更新购物车项数量: UserGuid={UserGuid}, CartItemGuid={CartItemGuid}, Quantity={Quantity}",
                    userGuid,
                    request.CartItemGUID,
                    request.Quantity
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "更新购物车项数量失败: UserGuid={UserGuid}, CartItemGuid={CartItemGuid}",
                    userGuid,
                    request.CartItemGUID
                );
                return false;
            }
        }

        public async Task<bool> ClearCartAsync(string userGuid)
        {
            try
            {
                var cart = await _context
                    .Db.Queryable<Cart>()
                    .Where(x => x.UserGUID == userGuid && x.CartStatus == "Active")
                    .FirstAsync();

                if (cart == null)
                    return true; // 购物车不存在，认为清空成功

                // 删除所有购物车项
                await _context
                    .Db.Deleteable<CartItem>()
                    .Where(x => x.CartGUID == cart.CartGUID)
                    .ExecuteCommandAsync();

                // 更新购物车摘要
                await UpdateCartSummaryAsync(cart.CartGUID);

                _logger.LogInformation("Cleared cart: UserGuid={UserGuid}", userGuid);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear cart: UserGuid={UserGuid}", userGuid);
                return false;
            }
        }

        public async Task<CartDto> SyncCartAsync(string userGuid, CartSyncRequest request)
        {
            try
            {
                var storeGuid = request.StoreGUID ?? await GetUserDefaultStoreAsync(userGuid);
                if (string.IsNullOrEmpty(storeGuid))
                {
                    throw new InvalidOperationException("无法获取用户门店信息");
                }

                // 获取或创建购物车
                var cart = await GetOrCreateUserCartAsync(userGuid);

                // 清空现有购物车项
                await _context
                    .Db.Deleteable<CartItem>()
                    .Where(x => x.CartGUID == cart.CartGUID)
                    .ExecuteCommandAsync();

                // 添加本地购物车项
                foreach (var localItem in request.LocalCartItems)
                {
                    // 通过ProductCode查找商品GUID
                    var product = await _context
                        .Db.Queryable<WarehouseProduct>()
                        .Where(x => x.ProductCode == localItem.ProductCode)
                        .FirstAsync();

                    if (product != null)
                    {
                        var cartItem = new CartItem
                        {
                            CartItemGUID = Guid.NewGuid().ToString(),
                            CartGUID = cart.CartGUID!,
                            ProductCode = product.ProductCode, // WarehouseProduct使用ProductCode作为主键
                            ItemNumber = localItem.ProductCode,
                            ProductName = localItem.ProductName,
                            ProductImage = localItem.ProductImage,
                            UnitPrice = localItem.UnitPrice,
                            Quantity = localItem.Quantity,
                            TotalPrice = localItem.UnitPrice * localItem.Quantity,
                            Volume = localItem.Volume,
                            Weight = localItem.Weight,
                            MinOrderQuantity = localItem.MinOrderQuantity,
                            AddedAt = localItem.AddedAt,
                            LastUpdated = DateTime.Now,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                            CreatedBy = userGuid,
                            UpdatedBy = userGuid,
                        };

                        await _context.Db.Insertable(cartItem).ExecuteCommandAsync();
                    }
                    else
                    {
                        _logger.LogWarning(
                            "同步购物车时商品不存在: ProductCode={ProductCode}",
                            localItem.ProductCode
                        );
                    }
                }

                // 更新购物车摘要
                await UpdateCartSummaryAsync(cart.CartGUID!);

                // 返回更新后的购物车
                var updatedCart = await GetUserCartAsync(userGuid);

                _logger.LogInformation(
                    "同步购物车完成: UserGuid={UserGuid}, ItemCount={ItemCount}",
                    userGuid,
                    request.LocalCartItems.Count
                );

                return updatedCart ?? cart;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步购物车失败: UserGuid={UserGuid}", userGuid);
                throw;
            }
        }

        public async Task<bool> BatchUpdateCartItemQuantitiesAsync(
            string userGuid,
            Dictionary<string, int> updates
        )
        {
            try
            {
                if (updates == null || !updates.Any())
                {
                    _logger.LogWarning("批量更新购物车项数量: 更新数据为空");
                    return true;
                }

                _logger.LogInformation(
                    "开始批量更新购物车项数量: UserGuid={UserGuid}, UpdateCount={UpdateCount}",
                    userGuid,
                    updates.Count
                );

                // 批量查询需要更新的购物车项
                var cartItemGuids = updates.Keys.ToList();
                var cartItems = await _context
                    .Db.Queryable<CartItem>()
                    .Where(x => cartItemGuids.Contains(x.CartItemGUID))
                    .ToListAsync();

                if (!cartItems.Any())
                {
                    _logger.LogWarning(
                        "未找到需要更新的购物车项: UserGuid={UserGuid}, CartItemCount={CartItemCount}",
                        userGuid,
                        cartItemGuids.Count
                    );
                    return false;
                }

                // 批量更新购物车项
                var updateTime = DateTime.Now;
                var updateList = new List<CartItem>();

                foreach (var cartItem in cartItems)
                {
                    if (updates.TryGetValue(cartItem.CartItemGUID, out var newQuantity))
                    {
                        cartItem.Quantity = newQuantity;
                        cartItem.ActualQuantity = newQuantity;
                        cartItem.TotalPrice = cartItem.UnitPrice * newQuantity;
                        cartItem.LastUpdated = updateTime;
                        cartItem.UpdatedAt = updateTime;
                        cartItem.UpdatedBy = userGuid;
                        updateList.Add(cartItem);
                    }
                }

                if (updateList.Any())
                {
                    // 使用批量更新操作
                    var updateResult = await _context
                        .Db.Updateable(updateList)
                        .UpdateColumns(x => new
                        {
                            x.Quantity,
                            x.ActualQuantity,
                            x.TotalPrice,
                            x.LastUpdated,
                            x.UpdatedAt,
                            x.UpdatedBy,
                        })
                        .ExecuteCommandAsync();

                    _logger.LogInformation(
                        "批量更新购物车项完成: UserGuid={UserGuid}, UpdatedCount={UpdatedCount}, AffectedRows={AffectedRows}",
                        userGuid,
                        updateList.Count,
                        updateResult
                    );

                    var CartGUID = cartItems.First().CartGUID;

                    // 更新购物车摘要
                    await UpdateCartSummaryAsync(CartGUID);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新购物车项数量失败: UserGuid={UserGuid}", userGuid);
                return false;
            }
        }

        public async Task<bool> BatchRemoveCartItemsAsync(
            string userGuid,
            List<string> cartItemGuids
        )
        {
            try
            {
                if (cartItemGuids == null || !cartItemGuids.Any())
                {
                    _logger.LogWarning("批量移除购物车项: 购物车项GUID列表为空");
                    return true;
                }

                _logger.LogInformation(
                    "开始批量移除购物车项: UserGuid={UserGuid}, ItemCount={ItemCount}",
                    userGuid,
                    cartItemGuids.Count
                );

                // 获取用户购物车
                var cart = await GetUserCartAsync(userGuid);
                if (cart == null)
                {
                    _logger.LogWarning("用户购物车不存在: UserGuid={UserGuid}", userGuid);
                    return false;
                }

                // 批量查询需要删除的购物车项
                var cartItems = await _context
                    .Db.Queryable<CartItem>()
                    .Where(x =>
                        cartItemGuids.Contains(x.CartItemGUID) && x.CartGUID == cart.CartGUID
                    )
                    .ToListAsync();

                if (!cartItems.Any())
                {
                    _logger.LogWarning(
                        "未找到需要删除的购物车项: UserGuid={UserGuid}, CartItemCount={CartItemCount}",
                        userGuid,
                        cartItemGuids.Count
                    );
                    return false;
                }

                // 批量删除购物车项
                var deleteResult = await _context
                    .Db.Deleteable<CartItem>()
                    .Where(x =>
                        cartItemGuids.Contains(x.CartItemGUID) && x.CartGUID == cart.CartGUID
                    )
                    .ExecuteCommandAsync();

                _logger.LogInformation(
                    "批量删除购物车项完成: UserGuid={UserGuid}, DeletedCount={DeletedCount}, AffectedRows={AffectedRows}",
                    userGuid,
                    cartItems.Count,
                    deleteResult
                );

                // 更新购物车摘要
                if (!string.IsNullOrEmpty(cart.CartGUID))
                {
                    await UpdateCartSummaryAsync(cart.CartGUID);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量移除购物车项失败: UserGuid={UserGuid}", userGuid);
                return false;
            }
        }

        public async Task<CartSummaryDto> GetCartSummaryAsync(string userGuid)
        {
            try
            {
                var cart = await GetUserCartAsync(userGuid);
                if (cart == null || cart.CartItems == null)
                {
                    return new CartSummaryDto();
                }

                return new CartSummaryDto
                {
                    TotalItems = cart.CartItems.Sum(x => x.Quantity),
                    UniqueItems = cart.CartItems.Count,
                    TotalAmount = cart.CartItems.Sum(x => x.TotalPrice ?? 0),
                    TotalVolume = cart.CartItems.Sum(x =>
                    {
                        var minOrderQuantity = x.MinOrderQuantity;
                        // 防止除零异常
                        if (minOrderQuantity <= 0)
                        {
                            minOrderQuantity = 1;
                        }
                        return (x.Volume ?? 0) * x.Quantity / minOrderQuantity;
                    }),
                    TotalWeight = cart.CartItems.Sum(x => (x.Weight ?? 0) * x.Quantity),
                    LastModified = cart.LastModified,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get cart summary: UserGuid={UserGuid}", userGuid);
                return new CartSummaryDto();
            }
        }

        public async Task<bool> IsProductInCartAsync(string userGuid, string productCode)
        {
            try
            {
                var cart = await GetUserCartAsync(userGuid);
                if (cart == null)
                    return false;

                return await _context
                    .Db.Queryable<CartItem>()
                    .Where(x => x.CartGUID == cart.CartGUID && x.ProductCode == productCode)
                    .AnyAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to check if product is in cart: UserGuid={UserGuid}, ProductCode={ProductCode}",
                    userGuid,
                    productCode
                );
                return false;
            }
        }

        public async Task<int> GetProductQuantityInCartAsync(string userGuid, string productCode)
        {
            try
            {
                var cart = await GetUserCartAsync(userGuid);
                if (cart == null)
                    return 0;

                var cartItem = await _context
                    .Db.Queryable<CartItem>()
                    .Where(x => x.CartGUID == cart.CartGUID && x.ProductCode == productCode)
                    .FirstAsync();

                return cartItem?.Quantity ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to get product quantity in cart: UserGuid={UserGuid}, ProductCode={ProductCode}",
                    userGuid,
                    productCode
                );
                return 0;
            }
        }

        public async Task<
            Dictionary<string, (bool InCart, int Quantity)>
        > BatchCheckProductsInCartAsync(string userGuid, List<string> productCodes)
        {
            var result = new Dictionary<string, (bool InCart, int Quantity)>();

            try
            {
                // 先初始化所有商品为不在购物车中
                foreach (var productCode in productCodes)
                {
                    result[productCode] = (false, 0);
                }

                var cart = await GetUserCartAsync(userGuid);
                if (cart == null)
                {
                    return result;
                }

                // 批量查询购物车中的商品
                var cartItems = await _context
                    .Db.Queryable<CartItem>()
                    .Where(x => x.CartGUID == cart.CartGUID && productCodes.Contains(x.ProductCode))
                    .ToListAsync();

                // 更新结果
                foreach (var cartItem in cartItems)
                {
                    if (!string.IsNullOrEmpty(cartItem.ProductCode))
                    {
                        result[cartItem.ProductCode] = (true, cartItem.Quantity);
                    }
                }

                _logger.LogDebug(
                    "批量检查商品购物车状态: UserGuid={UserGuid}, 商品数量={ProductCount}, 购物车中={InCartCount}",
                    userGuid,
                    productCodes.Count,
                    cartItems.Count
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to batch check products in cart: UserGuid={UserGuid}, ProductCount={ProductCount}",
                    userGuid,
                    productCodes.Count
                );

                // 发生错误时，返回所有商品都不在购物车中
                return result;
            }
        }

        public async Task<int> CleanExpiredCartsAsync()
        {
            try
            {
                var expiredCarts = await _context
                    .Db.Queryable<Cart>()
                    .Where(x => x.ExpiresAt != null && x.ExpiresAt < DateTime.Now)
                    .ToListAsync();

                foreach (var cart in expiredCarts)
                {
                    // 删除购物车项
                    await _context
                        .Db.Deleteable<CartItem>()
                        .Where(x => x.CartGUID == cart.CartGUID)
                        .ExecuteCommandAsync();
                    // 删除购物车
                    await _context
                        .Db.Deleteable<Cart>()
                        .Where(x => x.CartGUID == cart.CartGUID)
                        .ExecuteCommandAsync();
                }

                _logger.LogInformation("清理过期购物车: 数量={Count}", expiredCarts.Count);

                return expiredCarts.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理过期购物车失败");
                return 0;
            }
        }

        public async Task<string?> CreateOrderFromCartAsync(
            string userGuid,
            CreateOrderFromCartRequest request
        )
        {
            try
            {
                var cart = await GetUserCartAsync(userGuid);
                if (cart == null || cart.CartItems == null || cart.CartItems.Count == 0)
                {
                    _logger.LogWarning(
                        "Cart is empty, cannot process: UserGuid={UserGuid}",
                        userGuid
                    );
                    return null;
                }

                // 只清空购物车，不创建订单记录
                await ClearCartAsync(userGuid);

                _logger.LogInformation(
                    "Processed cart clearing for user: UserGuid={UserGuid}, StoreGuid={StoreGuid}",
                    userGuid,
                    request.StoreGUID
                );

                return "Cart processed successfully";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process cart: UserGuid={UserGuid}", userGuid);
                return null;
            }
        }

        #region Private Methods

        /// <summary>
        /// 根据ProductCode获取商品信息
        /// </summary>
        private async Task<WarehouseProduct?> GetProductByGuidAsync(string productCode)
        {
            try
            {
                // 注意：这里假设ProductGUID实际上是ProductCode，因为WarehouseProduct使用ProductCode作为主键
                // 实际项目中需要根据数据模型调整
                return await _context
                    .Db.Queryable<WarehouseProduct>()
                    .Where(x => x.ProductCode == productCode)
                    .FirstAsync();
            }
            catch
            {
                return null;
            }
        }

        private async Task UpdateCartSummaryAsync(string cartGuid)
        {
            try
            {
                var cartItems = await _context
                    .Db.Queryable<CartItem>()
                    .Where(x => x.CartGUID == cartGuid && !x.IsDeleted)
                    .ToListAsync();

                var totalAmount = cartItems.Sum(x => x.TotalPrice ?? 0);
                var totalQuantity = cartItems.Sum(x => x.Quantity);
                //总体积 是 体积数/最小订货数量*订货数量
                decimal totalVolume = cartItems.Sum(x =>
                {
                    var minOrderQuantity = x.MinOrderQuantity ?? 1;
                    // 防止除零异常
                    if (minOrderQuantity <= 0)
                    {
                        minOrderQuantity = 1;
                    }
                    return (x.Volume ?? 0) * x.Quantity / minOrderQuantity;
                });

                await _context
                    .Db.Updateable<Cart>()
                    .SetColumns(x => new Cart
                    {
                        TotalAmount = totalAmount,
                        TotalQuantity = totalQuantity,
                        TotalVolume = totalVolume,
                        LastModified = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                    })
                    .Where(x => x.CartGUID == cartGuid)
                    .ExecuteCommandAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新购物车摘要失败: CartGuid={CartGuid}", cartGuid);
            }
        }

        private async Task<string?> GetUserDefaultStoreAsync(string userGuid)
        {
            try
            {
                var userStore = await _context
                    .Db.Queryable<UserStore>()
                    .Where(x => x.UserGUID == userGuid)
                    .FirstAsync();

                return userStore?.StoreGUID;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户默认门店失败: UserGuid={UserGuid}", userGuid);
                return null;
            }
        }

        private string GenerateOrderNumber()
        {
            var now = DateTime.Now;
            var orderNumber =
                $"ORD-{now:yy}-{now:MMdd}-{now:HHmm}-{Guid.NewGuid().ToString()[..4].ToUpper()}";
            return orderNumber;
        }

        #endregion

        #region 新增购物车状态管理方法

        /// <summary>
        /// 保存购物车状态（更新状态为Save）
        /// </summary>
        public async Task<bool> SaveCartStatusAsync(string userGuid, SaveCartStatusRequest request)
        {
            try
            {
                // 获取用户的活跃购物车
                var cart = await _context
                    .Db.Queryable<Cart>()
                    .Where(x => x.UserGUID == userGuid && x.CartStatus == "Active")
                    .FirstAsync();

                if (cart == null)
                {
                    _logger.LogWarning("Active cart not found for user: {UserGuid}", userGuid);
                    return false;
                }

                // 生成订单号（如果还没有）
                if (string.IsNullOrEmpty(cart.OrderNumber))
                {
                    cart.OrderNumber = await GenerateNextOrderNumberAsync();
                }

                // 更新购物车状态和名称
                await _context
                    .Db.Updateable<Cart>()
                    .SetColumns(x => new Cart
                    {
                        CartStatus = CartStatusConstants.Save,
                        CartName = request.CartName,
                        StoreGUID = request.StoreGUID,
                        OrderNumber = cart.OrderNumber,
                        LastModified = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                    })
                    .Where(x => x.CartGUID == cart.CartGUID)
                    .ExecuteCommandAsync();

                _logger.LogInformation(
                    "Cart saved: CartGuid={CartGuid}, OrderNumber={OrderNumber}",
                    cart.CartGUID,
                    cart.OrderNumber
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save cart status: UserGuid={UserGuid}", userGuid);
                return false;
            }
        }

        /// <summary>
        /// 提交购物车（Checkout - 更新状态为Submitted，不创建订单记录）
        /// </summary>
        public async Task<string?> SubmitCartAsync(string userGuid, SubmitCartRequest request)
        {
            var normalizedUserGuid = userGuid?.Trim();
            var normalizedStoreGuid = request.StoreGUID?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedUserGuid)
                || string.IsNullOrWhiteSpace(normalizedStoreGuid))
            {
                throw new PreorderBusinessException(
                    "用户或分店信息无效",
                    "PREORDER_INVALID_REQUEST",
                    StatusCodes.Status400BadRequest
                );
            }

            var authorization = await ResolveSubmitAuthorizationAsync(normalizedUserGuid);
            // 先按大小写无关规则解析数据库中的 canonical StoreGUID，再使用该值构造所有实例一致的 StoreGate。
            var resolvedStore = await PreorderGateEvaluator.ResolveActiveStoreByGuidFailClosedAsync(
                _context.Db,
                normalizedStoreGuid,
                _logger
            );
            var storeResource = PreorderGateEvaluator.GetStoreLockResourceByStoreGuid(
                resolvedStore.StoreGUID
            );
            await using var storeLock = await PreorderMutationLock.AcquireProcessAsync(
                storeResource
            );

            var transactionStarted = false;
            try
            {
                await _context.Db.Ado.BeginTranAsync();
                transactionStarted = true;

                // 关键逻辑：门禁判断、分店授权和购物车状态更新共用同一个 StoreGate 与数据库事务。
                await PreorderGateEvaluator.AcquireDatabaseLockFailClosedAsync(
                    _context.Db,
                    storeResource,
                    resolvedStore.StoreGUID,
                    resolvedStore.StoreCode,
                    _logger
                );

                var store = await _context.Db.Queryable<Store>()
                    .FirstAsync(item =>
                        item.StoreGUID == resolvedStore.StoreGUID
                        && item.IsActive
                        && !item.IsDeleted
                    );
                if (store == null)
                {
                    throw new PreorderBusinessException(
                        "分店不存在或已停用，无法提交购物车",
                        "PREORDER_GATE_UNAVAILABLE",
                        StatusCodes.Status503ServiceUnavailable
                    );
                }

                if (!authorization.IsWarehouseStaffOnly
                    && !authorization.HasGlobalStoreScope)
                {
                    var canAccessStore = await _context.Db.Queryable<UserStore>()
                        .AnyAsync(item =>
                            item.UserGUID == normalizedUserGuid
                            && item.StoreGUID == resolvedStore.StoreGUID
                            && !item.IsDeleted
                        );
                    if (!canAccessStore)
                    {
                        throw new PreorderBusinessException(
                            "无权提交该分店的购物车",
                            "STORE_ACCESS_DENIED",
                            StatusCodes.Status403Forbidden
                        );
                    }
                }

                if (!authorization.CanBypassGate)
                {
                    var preorderGate = await PreorderGateEvaluator
                        .EvaluateWithHeldStoreGateFailClosedAsync(
                            _context.Db,
                            storeResource,
                            store.StoreCode,
                            _timeProvider,
                            _logger
                        );
                    if (preorderGate.IsBlocked)
                    {
                        throw new PreorderBusinessException(
                            "请先完成当前有效的 Preorder，再提交普通订货",
                            "PREORDER_REQUIRED",
                            StatusCodes.Status409Conflict
                        );
                    }
                }

                // 获取用户的活跃购物车；普通加购、保存和删除链路不受此提交门禁影响。
                var cart = await _context
                    .Db.Queryable<Cart>()
                    .Includes(x => x.CartItems)
                    .Where(x =>
                        x.UserGUID == normalizedUserGuid
                        && x.CartStatus == CartStatusConstants.Active
                    )
                    .FirstAsync();

                if (cart == null)
                {
                    _logger.LogWarning("Active cart not found for user: {UserGuid}", userGuid);
                    await _context.Db.Ado.RollbackTranAsync();
                    transactionStarted = false;
                    return null;
                }

                if (cart.CartItems == null || !cart.CartItems.Any())
                {
                    _logger.LogWarning(
                        "Cannot submit empty cart: CartGuid={CartGuid}",
                        cart.CartGUID
                    );
                    await _context.Db.Ado.RollbackTranAsync();
                    transactionStarted = false;
                    return null;
                }

                // 生成订单号（如果还没有）
                if (string.IsNullOrEmpty(cart.OrderNumber))
                {
                    cart.OrderNumber = await GenerateNextOrderNumberAsync();
                }

                // 更新购物车状态（不创建订单记录）
                var affected = await _context
                    .Db.Updateable<Cart>()
                    .SetColumns(x => new Cart
                    {
                        CartStatus = BlazorApp.Shared.Constants.CartStatusConstants.Submitted,
                        StoreGUID = resolvedStore.StoreGUID,
                        OrderNumber = cart.OrderNumber,
                        LastModified = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                    })
                    // 条件更新避免同一购物车跨分店并发提交时两个请求都成功。
                    .Where(x =>
                        x.CartGUID == cart.CartGUID
                        && x.CartStatus == CartStatusConstants.Active
                    )
                    .ExecuteCommandAsync();
                if (affected != 1)
                {
                    await _context.Db.Ado.RollbackTranAsync();
                    transactionStarted = false;
                    return null;
                }

                await _context.Db.Ado.CommitTranAsync();
                transactionStarted = false;
                _logger.LogInformation(
                    "Cart submitted successfully (status only): CartGuid={CartGuid}, OrderNumber={OrderNumber}",
                    cart.CartGUID,
                    cart.OrderNumber
                );

                return cart.OrderNumber;
            }
            catch (PreorderBusinessException)
            {
                if (transactionStarted)
                {
                    await _context.Db.Ado.RollbackTranAsync();
                }
                throw;
            }
            catch (Exception ex)
            {
                if (transactionStarted)
                {
                    await _context.Db.Ado.RollbackTranAsync();
                }
                _logger.LogError(ex, "Failed to submit cart: UserGuid={UserGuid}", userGuid);
                // 未知数据库或锁错误同样不得让调用方误以为门禁已安全通过。
                throw new PreorderBusinessException(
                    "Preorder 状态暂时无法确认，请稍后重试",
                    "PREORDER_GATE_UNAVAILABLE",
                    StatusCodes.Status503ServiceUnavailable
                );
            }
        }

        private async Task<CartSubmitAuthorization> ResolveSubmitAuthorizationAsync(
            string userGuid
        )
        {
            var principal = _httpContextAccessor.HttpContext?.User;
            var authenticatedUserGuid = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal?.FindFirst("UserGUID")?.Value
                ?? principal?.FindFirst("userId")?.Value;
            if (principal?.Identity?.IsAuthenticated != true
                || !string.Equals(
                    authenticatedUserGuid,
                    userGuid,
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                // 服务层不信任调用方单独传入的 UserGUID，身份上下文不一致时必须 fail-closed。
                throw new PreorderBusinessException(
                    "无法确认当前用户身份",
                    "CART_AUTHORIZATION_UNAVAILABLE",
                    StatusCodes.Status503ServiceUnavailable
                );
            }

            try
            {
                var isAdmin = HasAnyRole(principal, "Admin", "管理员");
                var isWarehouseManager = HasAnyRole(
                    principal,
                    "WarehouseManager",
                    "仓库经理"
                );
                var isWarehouseStaffOnly = HasAnyRole(
                        principal,
                        "WarehouseStaff",
                        "仓库员工"
                    )
                    && !isAdmin
                    && !isWarehouseManager;

                var canSubmit = isWarehouseStaffOnly
                    ? await HasPermissionAsync(principal, Permissions.Orders.Create)
                    : await HasAnyPermissionAsync(principal, CartWritePermissions);
                if (!canSubmit)
                {
                    throw new PreorderBusinessException(
                        "无权提交普通购物车",
                        "CART_SUBMIT_FORBIDDEN",
                        StatusCodes.Status403Forbidden
                    );
                }

                // 与正式 React 路径一致：真实 Admin 或显式仓库管理权限才拥有全分店 scope；WarehouseManager 角色名本身不扩权。
                var hasGlobalStoreScope = isAdmin
                    || await HasAnyPermissionAsync(
                        principal,
                        new[]
                        {
                            Permissions.Warehouse.ManageOrders,
                            Permissions.Warehouse.Manage,
                        }
                    );
                return new CartSubmitAuthorization(
                    isWarehouseStaffOnly,
                    hasGlobalStoreScope,
                    isWarehouseStaffOnly || hasGlobalStoreScope
                );
            }
            catch (PreorderBusinessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "购物车提交权限检查失败: UserGuid={UserGuid}", userGuid);
                throw new PreorderBusinessException(
                    "提交权限暂时无法确认，请稍后重试",
                    "CART_AUTHORIZATION_UNAVAILABLE",
                    StatusCodes.Status503ServiceUnavailable
                );
            }
        }

        private async Task<bool> HasAnyPermissionAsync(
            ClaimsPrincipal principal,
            IEnumerable<string> permissions
        )
        {
            foreach (var permission in permissions)
            {
                if (await HasPermissionAsync(principal, permission))
                {
                    return true;
                }
            }
            return false;
        }

        private async Task<bool> HasPermissionAsync(
            ClaimsPrincipal principal,
            string permission
        ) => (await _authorizationService.AuthorizeAsync(principal, permission)).Succeeded;

        private static bool HasAnyRole(ClaimsPrincipal principal, params string[] roles) =>
            principal.Claims.Any(claim =>
                claim.Type == ClaimTypes.Role
                && roles.Contains(claim.Value, StringComparer.OrdinalIgnoreCase)
            );

        private sealed record CartSubmitAuthorization(
            bool IsWarehouseStaffOnly,
            bool HasGlobalStoreScope,
            bool CanBypassGate
        );

        /// <summary>
        /// 获取用户购物车列表（支持状态过滤和分页）
        /// </summary>
        public async Task<CartListResponse> GetCartListAsync(
            string userGuid,
            CartListRequest request
        )
        {
            try
            {
                var query = _context
                    .Db.Queryable<Cart>()
                    .Includes(x => x.CartItems)
                    .Where(x => x.UserGUID == userGuid);

                // 状态过滤
                if (!string.IsNullOrEmpty(request.Status))
                {
                    query = query.Where(x => x.CartStatus == request.Status);
                }
                else
                {
                    // 默认不显示已删除的购物车
                    query = query.Where(x => x.CartStatus != CartStatusConstants.Deleted);
                }

                // 排除状态过滤
                if (request.ExcludeStatuses != null && request.ExcludeStatuses.Any())
                {
                    query = query.Where(x => !request.ExcludeStatuses.Contains(x.CartStatus));
                }

                // 店铺ID过滤
                if (!string.IsNullOrEmpty(request.StoreId))
                {
                    query = query.Where(x => x.StoreGUID == request.StoreId);
                }

                // 搜索过滤
                if (!string.IsNullOrEmpty(request.SearchKeyword))
                {
                    query = query.Where(x =>
                        (x.CartName != null && x.CartName.Contains(request.SearchKeyword))
                        || (x.OrderNumber != null && x.OrderNumber.Contains(request.SearchKeyword))
                    );
                }

                // 获取总数
                var totalCount = await query.CountAsync();

                // 分页查询
                var carts = await query
                    .OrderBy(x => x.CreatedAt, OrderByType.Desc)
                    .ToPageListAsync(request.Page, request.PageSize);

                var cartDtos = _mapper.Map<List<CartDto>>(carts);

                // 获取门店信息并填充到CartDto中
                var storeGuids = cartDtos
                    .Where(x => !string.IsNullOrEmpty(x.StoreGUID))
                    .Select(x => x.StoreGUID)
                    .Distinct()
                    .ToList();
                if (storeGuids.Any())
                {
                    var stores = await _context
                        .Db.Queryable<Store>()
                        .Where(x => storeGuids.Contains(x.StoreGUID))
                        .ToListAsync();

                    var storeDict = stores.ToDictionary(x => x.StoreGUID, x => x.StoreName);

                    foreach (var cartDto in cartDtos)
                    {
                        if (
                            !string.IsNullOrEmpty(cartDto.StoreGUID)
                            && storeDict.ContainsKey(cartDto.StoreGUID)
                        )
                        {
                            cartDto.StoreName = storeDict[cartDto.StoreGUID];
                        }
                    }
                }

                return new CartListResponse
                {
                    Carts = cartDtos,
                    TotalCount = totalCount,
                    CurrentPage = request.Page,
                    PageSize = request.PageSize,
                    TotalPages = (int)Math.Ceiling((double)totalCount / request.PageSize),
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get cart list: UserGuid={UserGuid}", userGuid);
                return new CartListResponse();
            }
        }

        /// <summary>
        /// 生成下一个订单号（格式：YYYY-NNNN）
        /// </summary>
        public async Task<string> GenerateNextOrderNumberAsync()
        {
            var orderNumber = await _orderNumberGenerator.GetNextOrderNoAsync();
            _logger.LogInformation("Generated order number: {OrderNumber}", orderNumber);
            return orderNumber;
        }

        #endregion

        #region 智能购物车状态管理方法

        /// <summary>
        /// 检查用户是否有Active状态的购物车
        /// </summary>
        public async Task<ActiveCartCheckResponse> CheckActiveCartAsync(string userGuid)
        {
            try
            {
                var activeCart = await _context
                    .Db.Queryable<Cart>()
                    .Includes(x => x.CartItems)
                    .Where(x =>
                        x.UserGUID == userGuid && x.CartStatus == CartStatusConstants.Active
                    )
                    .FirstAsync();

                var response = new ActiveCartCheckResponse
                {
                    HasActiveCart = activeCart != null,
                    ActiveCart = activeCart != null ? _mapper.Map<CartDto>(activeCart) : null,
                };

                _logger.LogInformation(
                    "Checked active cart for user {UserGuid}: HasActiveCart={HasActiveCart}",
                    userGuid,
                    response.HasActiveCart
                );

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check active cart for user {UserGuid}", userGuid);
                return new ActiveCartCheckResponse { HasActiveCart = false };
            }
        }

        /// <summary>
        /// 切换购物车状态
        /// </summary>
        public async Task<bool> SwitchCartStatusAsync(
            string userGuid,
            CartStatusSwitchRequest request
        )
        {
            try
            {
                // 验证购物车属于当前用户
                var cart = await _context
                    .Db.Queryable<Cart>()
                    .Where(x => x.CartGUID == request.FromCartGuid)
                    .FirstAsync();

                if (cart == null)
                {
                    _logger.LogWarning(
                        "Cart not found or access denied: CartGuid={CartGuid}, UserGuid={UserGuid}",
                        request.FromCartGuid,
                        userGuid
                    );
                    return false;
                }

                // 开始事务
                await _context.Db.Ado.BeginTranAsync();

                try
                {
                    // 如果要切换到Active状态，确保只有一个Active购物车
                    if (request.ToStatus == CartStatusConstants.Active)
                    {
                        // 将当前用户的其他Active购物车改为Save状态
                        await _context
                            .Db.Updateable<Cart>()
                            .SetColumns(x => new Cart
                            {
                                CartStatus = CartStatusConstants.Save,
                                LastModified = DateTime.Now,
                                UpdatedAt = DateTime.Now,
                            })
                            .Where(x =>
                                x.UserGUID == userGuid
                                && x.CartStatus == CartStatusConstants.Active
                                && x.CartGUID != request.FromCartGuid
                            )
                            .ExecuteCommandAsync();
                    }

                    // 更新目标购物车状态
                    var updateCount = await _context
                        .Db.Updateable<Cart>()
                        .SetColumns(x => new Cart
                        {
                            CartStatus = request.ToStatus,
                            LastModified = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                            UserGUID = userGuid,
                            UpdatedBy = userGuid,
                        })
                        .Where(x => x.CartGUID == request.FromCartGuid)
                        .ExecuteCommandAsync();

                    if (updateCount > 0)
                    {
                        await _context.Db.Ado.CommitTranAsync();
                        _logger.LogInformation(
                            "Cart status switched: CartGuid={CartGuid}, FromStatus={FromStatus}, ToStatus={ToStatus}",
                            request.FromCartGuid,
                            cart.CartStatus,
                            request.ToStatus
                        );
                        return true;
                    }
                    else
                    {
                        await _context.Db.Ado.RollbackTranAsync();
                        return false;
                    }
                }
                catch
                {
                    await _context.Db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to switch cart status: CartGuid={CartGuid}, ToStatus={ToStatus}",
                    request.FromCartGuid,
                    request.ToStatus
                );
                return false;
            }
        }

        /// <summary>
        /// 合并购物车
        /// </summary>
        public async Task<bool> MergeCartsAsync(string userGuid, CartMergeRequest request)
        {
            try
            {
                // 验证两个购物车都属于当前用户
                var sourceCarts = await _context
                    .Db.Queryable<Cart>()
                    .Includes(x => x.CartItems)
                    .Where(x =>
                        (
                            x.CartGUID == request.SourceCartGuid
                            || x.CartGUID == request.TargetCartGuid
                        )
                        && x.UserGUID == userGuid
                    )
                    .ToListAsync();

                var sourceCart = sourceCarts.FirstOrDefault(x =>
                    x.CartGUID == request.SourceCartGuid
                );
                var targetCart = sourceCarts.FirstOrDefault(x =>
                    x.CartGUID == request.TargetCartGuid
                );

                if (sourceCart == null || targetCart == null)
                {
                    _logger.LogWarning(
                        "Source or target cart not found: SourceGuid={SourceGuid}, TargetGuid={TargetGuid}",
                        request.SourceCartGuid,
                        request.TargetCartGuid
                    );
                    return false;
                }

                if (targetCart.CartStatus != CartStatusConstants.Active)
                {
                    _logger.LogWarning(
                        "Target cart is not active: TargetGuid={TargetGuid}, Status={Status}",
                        request.TargetCartGuid,
                        targetCart.CartStatus
                    );
                    return false;
                }

                // 开始事务
                await _context.Db.Ado.BeginTranAsync();

                try
                {
                    // 合并购物车项
                    if (sourceCart.CartItems?.Any() == true)
                    {
                        foreach (var sourceItem in sourceCart.CartItems)
                        {
                            // 检查目标购物车中是否已有相同商品
                            var existingItem = targetCart.CartItems?.FirstOrDefault(x =>
                                x.ProductCode == sourceItem.ProductCode
                            );

                            if (existingItem != null)
                            {
                                // 处理重复商品
                                switch (request.DuplicateStrategy.ToLower())
                                {
                                    case "add":
                                        // 累加数量
                                        await _context
                                            .Db.Updateable<CartItem>()
                                            .SetColumns(x => new CartItem
                                            {
                                                Quantity =
                                                    existingItem.Quantity + sourceItem.Quantity,
                                                TotalPrice =
                                                    (existingItem.Quantity + sourceItem.Quantity)
                                                    * sourceItem.UnitPrice,
                                                LastUpdated = DateTime.Now,
                                                UpdatedAt = DateTime.Now,
                                                UpdatedBy = userGuid,
                                            })
                                            .Where(x => x.CartItemGUID == existingItem.CartItemGUID)
                                            .ExecuteCommandAsync();
                                        break;

                                    case "replace":
                                        // 替换数量
                                        await _context
                                            .Db.Updateable<CartItem>()
                                            .SetColumns(x => new CartItem
                                            {
                                                Quantity = sourceItem.Quantity,
                                                TotalPrice = sourceItem.TotalPrice,
                                                LastUpdated = DateTime.Now,
                                                UpdatedAt = DateTime.Now,
                                                UpdatedBy = userGuid,
                                            })
                                            .Where(x => x.CartItemGUID == existingItem.CartItemGUID)
                                            .ExecuteCommandAsync();
                                        break;

                                    case "skip":
                                        // 跳过，不处理
                                        break;
                                }
                            }
                            else
                            {
                                // 添加新商品到目标购物车
                                var newItem = new CartItem
                                {
                                    CartItemGUID = Guid.NewGuid().ToString(),
                                    CartGUID = targetCart.CartGUID,
                                    ProductCode = sourceItem.ProductCode,
                                    ItemNumber = sourceItem.ItemNumber,
                                    ProductName = sourceItem.ProductName,
                                    ProductImage = sourceItem.ProductImage,
                                    UnitPrice = sourceItem.UnitPrice,
                                    Quantity = sourceItem.Quantity,
                                    TotalPrice = sourceItem.TotalPrice,
                                    Volume = sourceItem.Volume,
                                    Weight = sourceItem.Weight,
                                    MinOrderQuantity = sourceItem.MinOrderQuantity,
                                    AddedAt = DateTime.Now,
                                    LastUpdated = DateTime.Now,
                                    CreatedAt = DateTime.Now,
                                    UpdatedAt = DateTime.Now,
                                    CreatedBy = userGuid,
                                    UpdatedBy = userGuid,
                                    Remarks = sourceItem.Remarks,
                                };

                                await _context.Db.Insertable(newItem).ExecuteCommandAsync();
                            }
                        }
                    }

                    // 处理源购物车
                    if (request.DeleteSourceCart)
                    {
                        // 删除源购物车及其项目
                        await _context
                            .Db.Deleteable<CartItem>()
                            .Where(x => x.CartGUID == sourceCart.CartGUID)
                            .ExecuteCommandAsync();

                        await _context
                            .Db.Deleteable<Cart>()
                            .Where(x => x.CartGUID == sourceCart.CartGUID)
                            .ExecuteCommandAsync();
                    }
                    else
                    {
                        // 将源购物车状态改为已使用
                        await _context
                            .Db.Updateable<Cart>()
                            .SetColumns(x => new Cart
                            {
                                CartStatus = "Used", // 可以定义一个新状态
                                LastModified = DateTime.Now,
                                UpdatedAt = DateTime.Now,
                            })
                            .Where(x => x.CartGUID == sourceCart.CartGUID)
                            .ExecuteCommandAsync();
                    }

                    // 更新目标购物车的汇总信息
                    await UpdateCartSummaryAsync(targetCart.CartGUID);

                    await _context.Db.Ado.CommitTranAsync();

                    _logger.LogInformation(
                        "Carts merged successfully: SourceGuid={SourceGuid}, TargetGuid={TargetGuid}, Strategy={Strategy}",
                        request.SourceCartGuid,
                        request.TargetCartGuid,
                        request.DuplicateStrategy
                    );

                    return true;
                }
                catch
                {
                    await _context.Db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to merge carts: SourceGuid={SourceGuid}, TargetGuid={TargetGuid}",
                    request.SourceCartGuid,
                    request.TargetCartGuid
                );
                return false;
            }
        }

        public async Task<List<CartItemDto>> GetCartItemsByCartGuidAsync(string cartGuid)
        {
            try
            {
                // 先获取购物车商品和基本商品信息
                var cartItems = await _context
                    .Db.Queryable<CartItem>()
                    .Where(ci => ci.CartGUID == cartGuid)
                    .Includes(ci => ci.Product)
                    .OrderBy(ci => ci.CreatedAt)
                    .ToListAsync();

                if (!cartItems.Any())
                {
                    _logger.LogDebug("No cart items found for cart {CartGuid}", cartGuid);
                    return new List<CartItemDto>();
                }

                // 获取商品代码列表
                var productCodes = cartItems
                    .Where(ci => ci.Product != null)
                    .Select(ci => ci.Product!.ProductCode)
                    .Where(code => !string.IsNullOrEmpty(code))
                    .ToList();

                // 如果有商品，获取货位信息和RRP价格信息
                Dictionary<string, string?> productLocations = new();
                Dictionary<string, decimal?> productRRPPrices = new();
                if (productCodes.Any())
                {
                    // 获取货位信息（LocationType=1 的配货位）
                    var locations = await _context
                        .Db.Queryable<ProductLocation>()
                        .LeftJoin<Location>((pl, l) => pl.LocationGuid == l.LocationGuid)
                        .Where(
                            (pl, l) => productCodes.Contains(pl.ProductCode!) && l.LocationType == 1
                        )
                        .Select(
                            (pl, l) =>
                                new { ProductCode = pl.ProductCode, LocationCode = l.LocationCode }
                        )
                        .ToListAsync();

                    productLocations = locations
                        .Where(x => !string.IsNullOrEmpty(x.ProductCode))
                        .GroupBy(x => x.ProductCode!)
                        .ToDictionary(g => g.Key, g => g.FirstOrDefault()?.LocationCode);

                    // 获取RRP价格信息（来自WarehouseProduct.OEMPrice）
                    var warehouseProducts = await _context
                        .Db.Queryable<WarehouseProduct>()
                        .Where(wp => productCodes.Contains(wp.ProductCode!))
                        .Select(wp => new { ProductCode = wp.ProductCode, OEMPrice = wp.OEMPrice })
                        .ToListAsync();

                    productRRPPrices = warehouseProducts
                        .Where(x => !string.IsNullOrEmpty(x.ProductCode))
                        .GroupBy(x => x.ProductCode!)
                        .ToDictionary(g => g.Key, g => g.FirstOrDefault()?.OEMPrice);
                }

                // 映射到 DTO
                var cartItemDtos = _mapper.Map<List<CartItemDto>>(cartItems);

                // 为每个商品设置货位编码和RRP价格
                foreach (var dto in cartItemDtos)
                {
                    if (!string.IsNullOrEmpty(dto.ProductCode))
                    {
                        // 设置货位编码
                        if (productLocations.TryGetValue(dto.ProductCode, out var locationCode))
                        {
                            dto.LocationCode = locationCode;
                        }

                        // 设置RRP价格
                        if (productRRPPrices.TryGetValue(dto.ProductCode, out var rrpPrice))
                        {
                            dto.RRPPrice = rrpPrice;
                        }
                    }
                }

                _logger.LogDebug(
                    "Retrieved {Count} cart items for cart {CartGuid}",
                    cartItemDtos.Count,
                    cartGuid
                );
                return cartItemDtos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get cart items for cart {CartGuid}", cartGuid);
                throw;
            }
        }

        public async Task<CartDto?> GetCartByIdAsync(string cartGuid)
        {
            try
            {
                var cart = await _context
                    .Db.Queryable<Cart>()
                    .Includes(x => x.CartItems)
                    .Where(x => x.CartGUID == cartGuid)
                    .FirstAsync();

                if (cart == null)
                    return null;

                var cartDto = _mapper.Map<CartDto>(cart);

                // 获取分店信息
                if (!string.IsNullOrEmpty(cart.StoreGUID))
                {
                    var store = await _context
                        .Db.Queryable<Store>()
                        .Where(s => s.StoreGUID == cart.StoreGUID)
                        .FirstAsync();

                    if (store != null)
                    {
                        cartDto.StoreName = store.StoreName;
                        cartDto.StoreAddress = store.Address?.Trim();
                    }
                }

                return cartDto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get cart by ID: CartGuid={CartGuid}", cartGuid);
                return null;
            }
        }

        public async Task<bool> ChangeOrderStoreAsync(
            string userGuid,
            string cartGuid,
            string newStoreGuid,
            string reason
        )
        {
            try
            {
                await _context.Db.Ado.BeginTranAsync();

                try
                {
                    // 验证购物车存在
                    var cart = await _context
                        .Db.Queryable<Cart>()
                        .Where(x => x.CartGUID == cartGuid)
                        .FirstAsync();

                    if (cart == null)
                    {
                        _logger.LogWarning("Cart not found: {CartGuid}", cartGuid);
                        await _context.Db.Ado.RollbackTranAsync();
                        return false;
                    }

                    // 验证新分店存在
                    var newStore = await _context
                        .Db.Queryable<Store>()
                        .Where(s => s.StoreGUID == newStoreGuid && s.IsActive == true)
                        .FirstAsync();

                    if (newStore == null)
                    {
                        _logger.LogWarning(
                            "Store not found or inactive: {StoreGuid}",
                            newStoreGuid
                        );
                        await _context.Db.Ado.RollbackTranAsync();
                        return false;
                    }

                    // 记录历史分店信息
                    var oldStoreGuid = cart.StoreGUID;

                    // 更新购物车分店信息
                    cart.StoreGUID = newStoreGuid;
                    cart.LastModified = DateTime.Now;

                    await _context.Db.Updateable(cart).ExecuteCommandAsync();

                    // 记录更改日志（可选，如果有日志表的话）
                    _logger.LogInformation(
                        "Store changed for cart {CartGuid}: {OldStore} -> {NewStore}, Reason: {Reason}",
                        cartGuid,
                        oldStoreGuid,
                        newStoreGuid,
                        reason
                    );

                    await _context.Db.Ado.CommitTranAsync();
                    return true;
                }
                catch
                {
                    await _context.Db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to change store for cart {CartGuid}", cartGuid);
                return false;
            }
        }

        /// <summary>
        /// 软删除购物车（仅限Saved状态的购物车）
        /// </summary>
        public async Task<bool> SoftDeleteCartAsync(string userGuid, string cartGuid)
        {
            try
            {
                // 验证购物车属于当前用户且状态为Save
                var cart = await _context
                    .Db.Queryable<Cart>()
                    .Where(x => x.CartGUID == cartGuid && x.UserGUID == userGuid)
                    .FirstAsync();

                if (cart == null)
                {
                    _logger.LogWarning(
                        "Cart not found or not eligible for deletion: CartGuid={CartGuid}, UserGuid={UserGuid}",
                        cartGuid,
                        userGuid
                    );
                    return false;
                }

                // 更新状态为Deleted
                var updateCount = await _context
                    .Db.Updateable<Cart>()
                    .SetColumns(x => new Cart
                    {
                        CartStatus = CartStatusConstants.Deleted,
                        LastModified = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                    })
                    .Where(x => x.CartGUID == cartGuid)
                    .ExecuteCommandAsync();

                if (updateCount > 0)
                {
                    _logger.LogInformation(
                        "Cart soft deleted: CartGuid={CartGuid}, UserGuid={UserGuid}",
                        cartGuid,
                        userGuid
                    );
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to soft delete cart: CartGuid={CartGuid}, UserGuid={UserGuid}",
                    cartGuid,
                    userGuid
                );
                return false;
            }
        }

        /// <summary>
        /// 恢复删除的购物车（将状态从Deleted改回Save）
        /// </summary>
        public async Task<bool> RestoreCartAsync(string userGuid, string cartGuid)
        {
            try
            {
                // 验证购物车属于当前用户且状态为Deleted
                var cart = await _context
                    .Db.Queryable<Cart>()
                    .Where(x =>
                        x.CartGUID == cartGuid
                        && x.UserGUID == userGuid
                        && x.CartStatus == CartStatusConstants.Deleted
                    )
                    .FirstAsync();

                if (cart == null)
                {
                    _logger.LogWarning(
                        "Cart not found or not eligible for restore: CartGuid={CartGuid}, UserGuid={UserGuid}",
                        cartGuid,
                        userGuid
                    );
                    return false;
                }

                // 更新状态为Save
                var updateCount = await _context
                    .Db.Updateable<Cart>()
                    .SetColumns(x => new Cart
                    {
                        CartStatus = CartStatusConstants.Save,
                        LastModified = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                    })
                    .Where(x => x.CartGUID == cartGuid)
                    .ExecuteCommandAsync();

                if (updateCount > 0)
                {
                    _logger.LogInformation(
                        "Cart restored: CartGuid={CartGuid}, UserGuid={UserGuid}",
                        cartGuid,
                        userGuid
                    );
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to restore cart: CartGuid={CartGuid}, UserGuid={UserGuid}",
                    cartGuid,
                    userGuid
                );
                return false;
            }
        }

        /// <summary>
        /// 更新购物车备注
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="remarks">备注内容</param>
        /// <returns>操作结果</returns>
        public async Task<bool> UpdateCartRemarksAsync(string userGuid, string? remarks)
        {
            try
            {
                // 验证用户GUID
                if (string.IsNullOrEmpty(userGuid))
                {
                    _logger.LogWarning("UserGuid is empty when updating cart remarks");
                    return false;
                }

                // 查找用户的Active购物车
                var cart = await _context
                    .Db.Queryable<Cart>()
                    .Where(c =>
                        c.UserGUID == userGuid && c.CartStatus == CartStatusConstants.Active
                    )
                    .FirstAsync();

                if (cart == null)
                {
                    _logger.LogWarning(
                        "No active cart found for user: UserGuid={UserGuid}",
                        userGuid
                    );
                    return false;
                }

                // 更新备注
                cart.Remarks = remarks;
                cart.LastModified = DateTime.Now;

                var updated = await _context
                    .Db.Updateable(cart)
                    .UpdateColumns(c => new { c.Remarks, c.LastModified })
                    .ExecuteCommandAsync();

                if (updated > 0)
                {
                    _logger.LogInformation(
                        "Cart remarks updated: CartGuid={CartGuid}, UserGuid={UserGuid}, Remarks={Remarks}",
                        cart.CartGUID,
                        userGuid,
                        remarks
                    );
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to update cart remarks: UserGuid={UserGuid}, Remarks={Remarks}",
                    userGuid,
                    remarks
                );
                return false;
            }
        }

        /// <summary>
        /// 更新购物车项价格
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="request">价格更新请求</param>
        /// <returns>操作结果</returns>
        public async Task<bool> UpdateCartItemPriceAsync(
            string userGuid,
            UpdateCartItemPriceRequest request
        )
        {
            try
            {
                var cartItem = await _context
                    .Db.Queryable<CartItem>()
                    .Includes(x => x.Cart)
                    .Where(x =>
                        x.CartItemGUID == request.CartItemGUID && x.Cart!.UserGUID == userGuid
                    )
                    .FirstAsync();

                if (cartItem == null)
                {
                    _logger.LogWarning(
                        "购物车项不存在或不属于该用户: CartItemGuid={CartItemGuid}, UserGuid={UserGuid}",
                        request.CartItemGUID,
                        userGuid
                    );
                    return false;
                }

                cartItem.ActualPrice = request.ActualPrice;
                cartItem.LastUpdated = DateTime.Now;
                cartItem.UpdatedAt = DateTime.Now;
                cartItem.UpdatedBy = userGuid;

                await _context.Db.Updateable(cartItem).ExecuteCommandAsync();

                // 更新购物车摘要
                await UpdateCartSummaryAsync(cartItem.CartGUID);

                _logger.LogInformation(
                    "更新购物车项价格: UserGuid={UserGuid}, CartItemGuid={CartItemGuid}, ActualPrice={ActualPrice}",
                    userGuid,
                    request.CartItemGUID,
                    request.ActualPrice
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "更新购物车项价格失败: UserGuid={UserGuid}, CartItemGuid={CartItemGuid}",
                    userGuid,
                    request.CartItemGUID
                );
                return false;
            }
        }

        /// <summary>
        /// 批量更新购物车项价格
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="updates">价格更新字典</param>
        /// <returns>操作结果</returns>
        public async Task<bool> BatchUpdateCartItemPricesAsync(
            string userGuid,
            Dictionary<string, decimal?> updates
        )
        {
            try
            {
                if (updates == null || !updates.Any())
                {
                    _logger.LogWarning("批量更新购物车项价格: 更新数据为空");
                    return true;
                }

                _logger.LogInformation(
                    "开始批量更新购物车项价格: UserGuid={UserGuid}, UpdateCount={UpdateCount}",
                    userGuid,
                    updates.Count
                );

                // 批量查询需要更新的购物车项
                var cartItemGuids = updates.Keys.ToList();
                var cartItems = await _context
                    .Db.Queryable<CartItem>()
                    .Where(x => cartItemGuids.Contains(x.CartItemGUID))
                    .ToListAsync();

                if (!cartItems.Any())
                {
                    _logger.LogWarning(
                        "未找到需要更新价格的购物车项: UserGuid={UserGuid}, CartItemCount={CartItemCount}",
                        userGuid,
                        cartItemGuids.Count
                    );
                    return false;
                }

                // 批量更新购物车项价格
                var updateTime = DateTime.Now;
                var updateList = new List<CartItem>();

                foreach (var cartItem in cartItems)
                {
                    if (
                        updates.TryGetValue(cartItem.CartItemGUID, out var newPrice)
                        && newPrice.HasValue
                    )
                    {
                        cartItem.ActualPrice = newPrice.Value;
                        cartItem.TotalPrice = newPrice.Value * cartItem.Quantity;
                        cartItem.LastUpdated = updateTime;
                        cartItem.UpdatedAt = updateTime;
                        cartItem.UpdatedBy = userGuid;
                        updateList.Add(cartItem);
                    }
                }

                if (updateList.Any())
                {
                    // 使用批量更新操作
                    var updateResult = await _context
                        .Db.Updateable(updateList)
                        .UpdateColumns(x => new
                        {
                            x.ActualPrice,
                            x.TotalPrice,
                            x.LastUpdated,
                            x.UpdatedAt,
                            x.UpdatedBy,
                        })
                        .ExecuteCommandAsync();

                    _logger.LogInformation(
                        "批量更新购物车项价格完成: UserGuid={UserGuid}, UpdatedCount={UpdatedCount}, AffectedRows={AffectedRows}",
                        userGuid,
                        updateList.Count,
                        updateResult
                    );

                    var CartGUID = cartItems.First().CartGUID;

                    // 更新购物车摘要
                    await UpdateCartSummaryAsync(CartGUID);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新购物车项价格失败: UserGuid={UserGuid}", userGuid);
                return false;
            }
        }

        /// <summary>
        /// 更新购物车折扣和运费
        /// </summary>
        /// <param name="cartGuid">购物车GUID</param>
        /// <param name="discount">折扣金额</param>
        /// <param name="freightFee">运费</param>
        /// <param name="userGuid">操作用户GUID</param>
        /// <returns>操作结果</returns>
        public async Task<bool> UpdateCartDiscountAndFreightAsync(
            string cartGuid,
            decimal? discount,
            decimal? freightFee,
            string userGuid
        )
        {
            try
            {
                var cart = await _context
                    .Db.Queryable<Cart>()
                    .Where(x => x.CartGUID == cartGuid)
                    .FirstAsync();

                if (cart == null)
                {
                    _logger.LogWarning("购物车不存在: CartGuid={CartGuid}", cartGuid);
                    return false;
                }

                // 更新折扣和运费
                cart.Discount = discount;
                cart.FreightFee = freightFee;
                cart.LastModified = DateTime.Now;

                // 重新计算GST（如果有需要）
                await UpdateCartSummaryAsync(cartGuid);

                // 更新数据库
                var updateResult = await _context
                    .Db.Updateable(cart)
                    .UpdateColumns(x => new
                    {
                        x.Discount,
                        x.FreightFee,
                        x.LastModified,
                    })
                    .ExecuteCommandAsync();

                _logger.LogInformation(
                    "更新购物车折扣和运费: CartGuid={CartGuid}, Discount={Discount}, FreightFee={FreightFee}",
                    cartGuid,
                    discount,
                    freightFee
                );

                return updateResult > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新购物车折扣和运费失败: CartGuid={CartGuid}", cartGuid);
                return false;
            }
        }

        #endregion

        #region 仓库管理员订单管理功能

        public async Task<CartDto?> CreateStoreOrderAsync(
            string userGuid,
            CreateStoreOrderRequest request
        )
        {
            try
            {
                // 验证分店是否存在
                var store = await _context
                    .Db.Queryable<Store>()
                    .Where(s => s.StoreGUID == request.StoreGUID)
                    .FirstAsync();

                if (store == null)
                {
                    _logger.LogWarning(
                        "尝试为不存在的分店创建订单: StoreGUID={StoreGUID}",
                        request.StoreGUID
                    );
                    return null;
                }

                // 生成唯一的订单号
                var orderNumber = await GenerateOrderNumberAsync();

                // 创建新购物车（绑定到指定分店）
                var newCart = new Cart
                {
                    CartGUID = Guid.NewGuid().ToString(),
                    UserGUID = userGuid,
                    StoreGUID = request.StoreGUID,
                    CartName = string.IsNullOrEmpty(request.CartName)
                        ? $"Order for {store.StoreName}"
                        : request.CartName,
                    OrderNumber = orderNumber,
                    CartStatus = CartStatusConstants.Submitted,
                    Remarks = request.Remarks,
                    LastModified = DateTime.Now,
                    ExpiresAt = DateTime.Now.AddDays(30),
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                };

                await _context.Db.Insertable(newCart).ExecuteCommandAsync();

                _logger.LogInformation(
                    "仓库管理员创建新订单: CartGuid={CartGuid}, StoreGuid={StoreGuid}, OrderNumber={OrderNumber}",
                    newCart.CartGUID,
                    request.StoreGUID,
                    orderNumber
                );

                // 映射为DTO并设置分店信息
                var cartDto = _mapper.Map<CartDto>(newCart);
                cartDto.StoreName = store.StoreName;
                cartDto.StoreAddress = store.Address;

                return cartDto;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "创建分店订单失败: UserGuid={UserGuid}, StoreGuid={StoreGuid}",
                    userGuid,
                    request.StoreGUID
                );
                throw;
            }
        }

        public async Task<ExcelImportResult> ImportExcelItemsToCartAsync(
            ExcelImportRequest request,
            string userGuid
        )
        {
            var result = new ExcelImportResult { TotalCount = request.Items.Count };

            try
            {
                _logger.LogInformation(
                    "开始Excel导入: CartGUID={CartGUID}, ItemCount={ItemCount}, ClearExisting={ClearExisting}",
                    request.CartGUID,
                    request.Items.Count,
                    request.ClearExistingItems
                );

                // 验证订单是否存在
                var cart = await _context
                    .Db.Queryable<Cart>()
                    .Where(c => c.CartGUID == request.CartGUID)
                    .FirstAsync();

                if (cart == null)
                {
                    throw new InvalidOperationException($"订单不存在: {request.CartGUID}");
                }

                // 如果需要清除原有商品，先执行清除操作
                if (request.ClearExistingItems)
                {
                    _logger.LogInformation(
                        "清除购物车原有商品: CartGUID={CartGUID}",
                        request.CartGUID
                    );
                    await ClearCartByIdAsync(request.CartGUID, userGuid);
                }

                // 批量验证数据有效性
                var validItems = new List<ExcelImportItem>();
                var invalidItems = new List<ExcelImportItem>();

                foreach (var item in request.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.ItemNumber))
                    {
                        result.Errors.Add(
                            new ExcelImportError
                            {
                                ItemNumber = item.ItemNumber ?? "空值",
                                ErrorMessage = "货号不能为空",
                            }
                        );
                        result.FailureCount++;
                        invalidItems.Add(item);
                        continue;
                    }

                    if (item.Quantity == 0)
                    {
                        result.Errors.Add(
                            new ExcelImportError
                            {
                                ItemNumber = item.ItemNumber,
                                ErrorMessage = "数量必须不为0",
                            }
                        );
                        result.FailureCount++;
                        invalidItems.Add(item);
                        continue;
                    }

                    validItems.Add(item);
                }

                if (!validItems.Any())
                {
                    _logger.LogWarning("所有导入项都无效，跳过商品查询和添加步骤");
                    return result;
                }

                // 批量查询所有有效货号对应的商品
                var itemNumbers = validItems.Select(x => x.ItemNumber).Distinct().ToList();
                var productsDict = await _productService.BatchGetProductsByItemNumbersAsync(
                    itemNumbers
                );

                // 批量查询已存在的购物车项
                var existingCartItems = await _context
                    .Db.Queryable<CartItem>()
                    .Where(ci =>
                        ci.CartGUID == request.CartGUID
                        && validItems.Select(x => x.ItemNumber).Contains(ci.ItemNumber)
                    )
                    .ToListAsync();

                var existingItemsDict = existingCartItems
                    .Where(x => !string.IsNullOrEmpty(x.ItemNumber))
                    .ToDictionary(x => x.ItemNumber!, x => x);

                // 准备批量插入和更新的数据
                var itemsToInsert = new List<CartItem>();
                var itemsToUpdate = new List<CartItem>();

                foreach (var item in validItems)
                {
                    try
                    {
                        // 查找商品
                        if (!productsDict.TryGetValue(item.ItemNumber, out var warehouseProduct))
                        {
                            result.Errors.Add(
                                new ExcelImportError
                                {
                                    ItemNumber = item.ItemNumber,
                                    ErrorMessage = "商品不存在",
                                }
                            );
                            result.FailureCount++;
                            continue;
                        }

                        // 检查是否已存在该商品
                        if (existingItemsDict.TryGetValue(item.ItemNumber, out var existingItem))
                        {
                            // 更新现有商品数量
                            existingItem.Quantity += item.Quantity;
                            existingItem.ActualQuantity = existingItem.Quantity;
                            if (item.Price.HasValue && item.Price.Value > 0)
                            {
                                existingItem.ActualPrice = item.Price.Value;
                            }
                            existingItem.TotalPrice =
                                (existingItem.ActualPrice ?? existingItem.UnitPrice)
                                * existingItem.Quantity;
                            existingItem.LastUpdated = DateTime.Now;
                            existingItem.UpdatedAt = DateTime.Now;
                            existingItem.UpdatedBy = userGuid;

                            itemsToUpdate.Add(existingItem);

                            _logger.LogInformation(
                                "准备更新现有商品: ProductCode={ProductCode}, NewQuantity={NewQuantity}",
                                warehouseProduct.ProductCode,
                                existingItem.Quantity
                            );
                        }
                        else
                        {
                            // 准备添加新商品
                            var unitPrice = warehouseProduct.ImportPrice ?? 0;
                            var actualPrice = item.Price ?? unitPrice;

                            var cartItem = new CartItem
                            {
                                CartItemGUID = Guid.NewGuid().ToString(),
                                CartGUID = request.CartGUID,
                                ProductCode = warehouseProduct.ProductCode!,
                                ProductName = warehouseProduct.Product?.ProductName ?? "未知商品",
                                ItemNumber =
                                    warehouseProduct.Product?.ItemNumber
                                    ?? warehouseProduct.ProductCode,
                                UnitPrice = unitPrice,
                                Quantity = item.Quantity,
                                ActualQuantity = item.Quantity,
                                ActualPrice = actualPrice,
                                TotalPrice = actualPrice * item.Quantity,
                                MinOrderQuantity = warehouseProduct.MinOrderQuantity ?? 1,
                                ProductImage = warehouseProduct.Product?.ProductImage,
                                Volume = warehouseProduct.Volume ?? 0,
                                Weight = 0.1m, // 默认重量
                                AddedAt = DateTime.Now,
                                LastUpdated = DateTime.Now,
                                CreatedAt = DateTime.Now,
                                UpdatedAt = DateTime.Now,
                                CreatedBy = userGuid,
                                UpdatedBy = userGuid,
                            };

                            itemsToInsert.Add(cartItem);

                            _logger.LogInformation(
                                "准备添加新商品: ProductCode={ProductCode}, Quantity={Quantity}",
                                warehouseProduct.ProductCode,
                                item.Quantity
                            );
                        }

                        // 创建成功的CartItemDto
                        var successItem = new CartItemDto
                        {
                            ProductCode = warehouseProduct.ProductCode!,
                            ProductName = warehouseProduct.Product?.ProductName ?? "未知商品",
                            ItemNumber =
                                warehouseProduct.Product?.ItemNumber
                                ?? warehouseProduct.ProductCode,
                            UnitPrice = warehouseProduct.ImportPrice ?? 0,
                            Quantity = item.Quantity,
                            ActualQuantity = item.Quantity,
                            ActualPrice = item.Price ?? warehouseProduct.ImportPrice ?? 0,
                            MinOrderQuantity = warehouseProduct.MinOrderQuantity ?? 1,
                            RRPPrice = warehouseProduct.OEMPrice,
                            ProductImage = warehouseProduct.Product?.ProductImage,
                        };

                        result.SuccessItems.Add(successItem);
                        result.SuccessCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "处理导入项失败: ItemNumber={ItemNumber}",
                            item.ItemNumber
                        );
                        result.Errors.Add(
                            new ExcelImportError
                            {
                                ItemNumber = item.ItemNumber,
                                ErrorMessage = ex.Message,
                            }
                        );
                        result.FailureCount++;
                    }
                }

                // 批量执行数据库操作
                if (itemsToUpdate.Any())
                {
                    var updateResult = await _context
                        .Db.Updateable(itemsToUpdate)
                        .UpdateColumns(x => new
                        {
                            x.Quantity,
                            x.ActualQuantity,
                            x.ActualPrice,
                            x.TotalPrice,
                            x.LastUpdated,
                            x.UpdatedAt,
                            x.UpdatedBy,
                        })
                        .ExecuteCommandAsync();

                    _logger.LogInformation(
                        "批量更新购物车项完成，更新数量: {UpdateCount}, 影响行数: {AffectedRows}",
                        itemsToUpdate.Count,
                        updateResult
                    );
                }

                if (itemsToInsert.Any())
                {
                    var insertResult = await _context
                        .Db.Insertable(itemsToInsert)
                        .ExecuteCommandAsync();

                    _logger.LogInformation(
                        "批量插入购物车项完成，插入数量: {InsertCount}, 影响行数: {AffectedRows}",
                        itemsToInsert.Count,
                        insertResult
                    );
                }

                // 更新购物车摘要
                if (result.SuccessCount > 0)
                {
                    await UpdateCartSummaryAsync(request.CartGUID);
                }

                _logger.LogInformation(
                    "Excel导入完成: CartGUID={CartGUID}, 总计={TotalCount}, 成功={SuccessCount}, 失败={FailureCount}",
                    request.CartGUID,
                    result.TotalCount,
                    result.SuccessCount,
                    result.FailureCount
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excel导入失败: CartGUID={CartGUID}", request.CartGUID);
                throw;
            }
        }

        public async Task<List<ProductSearchResult>> BatchSearchProductsAsync(
            List<string> itemNumbers
        )
        {
            try
            {
                if (itemNumbers == null || !itemNumbers.Any())
                {
                    _logger.LogWarning("批量查询商品：货号列表为空");
                    return new List<ProductSearchResult>();
                }

                _logger.LogInformation("开始批量查询商品，货号数量: {Count}", itemNumbers.Count);

                // 使用批量查询方法
                var productsDict = await _productService.BatchGetProductsByItemNumbersAsync(
                    itemNumbers
                );
                var results = new List<ProductSearchResult>();

                foreach (var itemNumber in itemNumbers)
                {
                    if (productsDict.TryGetValue(itemNumber, out var warehouseProduct))
                    {
                        var searchResult = new ProductSearchResult
                        {
                            ProductCode = warehouseProduct.ProductCode!,
                            ItemNumber =
                                warehouseProduct.Product?.ItemNumber
                                ?? warehouseProduct.ProductCode,
                            ProductName = warehouseProduct.Product?.ProductName ?? "未知商品",
                            CategoryName = warehouseProduct.WarehouseCategory?.CategoryName ?? "",
                            UnitPrice = warehouseProduct.ImportPrice ?? 0,
                            RRPPrice = warehouseProduct.OEMPrice ?? 0,
                            MinOrderQuantity = warehouseProduct.MinOrderQuantity ?? 1,
                            StockQuantity = warehouseProduct.StockQuantity ?? 0,
                            ProductImage = warehouseProduct.Product?.ProductImage ?? "",
                            LocationCode = "",
                            Barcode = warehouseProduct.Barcode ?? "",
                        };

                        results.Add(searchResult);
                    }
                }

                _logger.LogInformation(
                    "批量查询商品完成，找到商品数量: {FoundCount}, 总货号数量: {TotalCount}",
                    results.Count,
                    itemNumbers.Count
                );

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量查询商品失败，货号数量: {Count}", itemNumbers.Count);
                throw;
            }
        }

        public async Task<BatchAddResult> BatchAddItemsToCartAsync(
            BatchAddItemsRequest request,
            string userGuid
        )
        {
            var result = new BatchAddResult { TotalCount = request.Items.Count };

            try
            {
                // 验证订单是否存在
                var cart = await _context
                    .Db.Queryable<Cart>()
                    .Where(c => c.CartGUID == request.CartGUID)
                    .FirstAsync();

                if (cart == null)
                {
                    throw new InvalidOperationException($"订单不存在: {request.CartGUID}");
                }

                // 验证所有商品并准备数据
                var validProducts = new List<(WarehouseProductListDto product, int quantity)>();
                var productCodes = new List<string>();

                foreach (var item in request.Items)
                {
                    // 验证商品代码不为空
                    if (string.IsNullOrWhiteSpace(item.ProductCode))
                    {
                        result.Errors.Add(
                            new BatchAddError
                            {
                                ProductCode = item.ProductCode ?? "未知",
                                ErrorMessage = "商品代码不能为空",
                            }
                        );
                        result.FailureCount++;
                        continue;
                    }

                    // 使用最小订货量作为默认数量
                    var quantity = item.MinOrderQuantity ?? 1;
                    if (quantity <= 0)
                    {
                        result.Errors.Add(
                            new BatchAddError
                            {
                                ProductCode = item.ProductCode,
                                ErrorMessage = "数量必须大于0",
                            }
                        );
                        result.FailureCount++;
                        continue;
                    }

                    validProducts.Add((item, quantity));
                    productCodes.Add(item.ProductCode);
                }

                if (!validProducts.Any())
                {
                    return result;
                }

                // 批量获取购物车中已存在的商品
                var existingCartItems = await _context
                    .Db.Queryable<CartItem>()
                    .Where(x =>
                        x.CartGUID == request.CartGUID && productCodes.Contains(x.ProductCode)
                    )
                    .ToListAsync();

                var existingItemsDict = existingCartItems.ToDictionary(x => x.ProductCode, x => x);

                // 准备批量更新和插入的数据
                var itemsToUpdate = new List<CartItem>();
                var itemsToInsert = new List<CartItem>();

                // 批量处理商品
                foreach (var (item, quantity) in validProducts)
                {
                    // 检查商品是否已在购物车中
                    if (existingItemsDict.TryGetValue(item.ProductCode, out var existingItem))
                    {
                        // 更新现有商品数量
                        existingItem.Quantity += quantity;
                        existingItem.ActualQuantity = existingItem.Quantity;
                        existingItem.TotalPrice = existingItem.UnitPrice * existingItem.Quantity;
                        existingItem.LastUpdated = DateTime.Now;
                        existingItem.UpdatedAt = DateTime.Now;
                        existingItem.UpdatedBy = userGuid;
                        itemsToUpdate.Add(existingItem);
                    }
                    else
                    {
                        // 选择价格（进口价格）
                        var unitPrice = item.ImportPrice ?? 0;

                        // 创建新的购物车项
                        var cartItem = new CartItem
                        {
                            CartItemGUID = Guid.NewGuid().ToString(),
                            CartGUID = cart.CartGUID!,
                            ProductCode = item.ProductCode,
                            ItemNumber = item.ItemNumber,
                            ProductName = item.ProductBaseName ?? "未知商品",
                            ProductImage = item.ProductImage,
                            UnitPrice = unitPrice,
                            Quantity = quantity,
                            ActualQuantity = quantity,
                            ActualPrice = unitPrice,
                            TotalPrice = unitPrice * quantity,
                            Volume = item.Volume ?? 0,
                            Weight = 0.1m, // 默认重量
                            MinOrderQuantity = item.MinOrderQuantity ?? 1,
                            AddedAt = DateTime.Now,
                            LastUpdated = DateTime.Now,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                            CreatedBy = userGuid,
                            UpdatedBy = userGuid,
                        };
                        itemsToInsert.Add(cartItem);

                        // 添加到字典中，防止后续重复添加同一商品
                        existingItemsDict[item.ProductCode] = cartItem;
                    }
                }

                // 批量执行数据库操作
                if (itemsToUpdate.Any())
                {
                    await _context.Db.Updateable(itemsToUpdate).ExecuteCommandAsync();
                }

                if (itemsToInsert.Any())
                {
                    await _context.Db.Insertable(itemsToInsert).ExecuteCommandAsync();
                }

                // 准备返回结果
                var allProcessedItems = new List<CartItem>();
                allProcessedItems.AddRange(itemsToUpdate);
                allProcessedItems.AddRange(itemsToInsert);

                foreach (var item in allProcessedItems)
                {
                    result.SuccessItems.Add(
                        new CartItemDto
                        {
                            CartItemGUID = item.CartItemGUID,
                            CartGUID = item.CartGUID,
                            ProductCode = item.ProductCode,
                            ItemNumber = item.ItemNumber,
                            ProductName = item.ProductName,
                            ProductImage = item.ProductImage,
                            UnitPrice = item.UnitPrice,
                            Quantity = item.Quantity,
                            ActualQuantity = item.ActualQuantity ?? 0,
                            ActualPrice = item.ActualPrice,
                            TotalPrice = item.TotalPrice,
                            Volume = item.Volume,
                            Weight = item.Weight,
                            MinOrderQuantity = item.MinOrderQuantity ?? 0,
                        }
                    );
                }

                result.SuccessCount = allProcessedItems.Count;

                // 更新购物车摘要
                if (result.SuccessCount > 0)
                {
                    await UpdateCartSummaryAsync(cart.CartGUID!);
                }

                _logger.LogInformation(
                    "批量添加商品完成: CartGUID={CartGUID}, 总数={TotalCount}, 成功={SuccessCount}, 失败={FailureCount}",
                    request.CartGUID,
                    result.TotalCount,
                    result.SuccessCount,
                    result.FailureCount
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量添加商品失败: CartGUID={CartGUID}", request.CartGUID);
                throw;
            }
        }

        public async Task<ProductSearchResponse> SearchProductsAsync(ProductSearchRequest request)
        {
            try
            {
                var productQuery = _context.Db.Queryable<Product>();

                // 关键字搜索
                if (!string.IsNullOrWhiteSpace(request.Keyword))
                {
                    var keyword = request.Keyword.Trim();
                    productQuery = productQuery.Where(p =>
                        (!string.IsNullOrEmpty(p.ProductName) && p.ProductName.Contains(keyword))
                        || (!string.IsNullOrEmpty(p.ItemNumber) && p.ItemNumber.Contains(keyword))
                        || (!string.IsNullOrEmpty(p.ProductCode) && p.ProductCode.Contains(keyword))
                        || (!string.IsNullOrEmpty(p.Barcode) && p.Barcode.Contains(keyword))
                    );
                }

                // 分类过滤
                if (!string.IsNullOrEmpty(request.CategoryGUID))
                {
                    productQuery = productQuery.Where(p =>
                        p.ProductCategoryGUID == request.CategoryGUID
                    );
                }

                // 获取总数
                var totalCount = await productQuery.CountAsync();

                // 分页查询商品
                var products = await productQuery
                    .OrderBy(p => p.ProductName)
                    .Skip((request.Page - 1) * request.PageSize)
                    .Take(request.PageSize)
                    .ToListAsync();

                // 构建结果
                var results = new List<ProductSearchResult>();
                foreach (var product in products)
                {
                    // 获取库存信息
                    var warehouseProduct = await _context
                        .Db.Queryable<WarehouseProduct>()
                        .Where(wp => wp.ProductCode == product.ProductCode)
                        .FirstAsync();

                    // 获取分类信息
                    var category = await _context
                        .Db.Queryable<WarehouseCategory>()
                        .Where(wc => wc.CategoryGUID == product.ProductCategoryGUID)
                        .FirstAsync();

                    var searchResult = new ProductSearchResult
                    {
                        ProductCode = product.ProductCode ?? string.Empty,
                        ItemNumber = product.ItemNumber ?? product.ProductCode ?? string.Empty,
                        ProductName = product.ProductName ?? string.Empty,
                        CategoryName = category?.CategoryName ?? "",
                        UnitPrice = product.PurchasePrice ?? 0,
                        RRPPrice = product.RetailPrice ?? 0,
                        MinOrderQuantity = product.MiddlePackageQuantity ?? 1,
                        StockQuantity = warehouseProduct?.StockQuantity ?? 0,
                        ProductImage = product.ProductImage ?? "",
                        LocationCode =
                            warehouseProduct?.Locations?.FirstOrDefault()?.LocationCode
                            ?? string.Empty,
                        Barcode = product.Barcode ?? "",
                    };

                    results.Add(searchResult);
                }

                return new ProductSearchResponse
                {
                    Products = results,
                    TotalCount = totalCount,
                    CurrentPage = request.Page,
                    PageSize = request.PageSize,
                    TotalPages = (int)Math.Ceiling((double)totalCount / request.PageSize),
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "搜索商品失败");
                throw;
            }
        }

        /// <summary>
        /// 生成唯一订单号
        /// </summary>
        private async Task<string> GenerateOrderNumberAsync()
        {
            var today = DateTime.Now.ToString("yyyyMM");
            var prefix = $"ORD-{today}-";

            // 查找本月最大的订单号
            var maxOrderNumber = await _context
                .Db.Queryable<Cart>()
                .Where(c => c.OrderNumber != null && c.OrderNumber.StartsWith(prefix))
                .OrderByDescending(c => c.OrderNumber)
                .Select(c => c.OrderNumber)
                .FirstAsync();

            if (string.IsNullOrEmpty(maxOrderNumber))
            {
                return $"{prefix}001";
            }

            // 提取序号并加1
            var lastNumber = maxOrderNumber.Substring(prefix.Length);
            if (int.TryParse(lastNumber, out var number))
            {
                return $"{prefix}{(number + 1):D3}";
            }

            return $"{prefix}001";
        }

        /// <summary>
        /// 清除指定购物车的所有商品（仓库管理员功能）
        /// </summary>
        /// <param name="cartGuid">购物车GUID</param>
        /// <param name="userGuid">操作用户GUID</param>
        /// <returns>操作结果</returns>
        public async Task<bool> ClearCartByIdAsync(string cartGuid, string userGuid)
        {
            try
            {
                _logger.LogInformation("开始清除购物车: CartGuid={CartGuid}", cartGuid);

                // 验证购物车是否存在
                var cart = await _context
                    .Db.Queryable<Cart>()
                    .Where(x => x.CartGUID == cartGuid)
                    .FirstAsync();

                if (cart == null)
                {
                    _logger.LogWarning("购物车不存在: CartGuid={CartGuid}", cartGuid);
                    return false;
                }

                // 删除所有购物车项（硬删除）
                var deleteResult = await _context
                    .Db.Deleteable<CartItem>()
                    .Where(x => x.CartGUID == cartGuid)
                    .ExecuteCommandAsync();

                _logger.LogInformation(
                    "清除购物车完成: CartGuid={CartGuid}, AffectedRows={AffectedRows}",
                    cartGuid,
                    deleteResult
                );

                // 更新购物车摘要
                await UpdateCartSummaryAsync(cartGuid);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清除购物车失败: CartGuid={CartGuid}", cartGuid);
                return false;
            }
        }

        /// <summary>
        /// 批量删除指定购物车的商品项（仓库管理员功能）
        /// </summary>
        /// <param name="cartGuid">购物车GUID</param>
        /// <param name="cartItemGuids">购物车项GUID列表</param>
        /// <param name="userGuid">操作用户GUID</param>
        /// <returns>操作结果</returns>
        public async Task<bool> BatchRemoveCartItemsByCartIdAsync(
            string cartGuid,
            List<string> cartItemGuids,
            string userGuid
        )
        {
            try
            {
                if (cartItemGuids == null || !cartItemGuids.Any())
                {
                    _logger.LogWarning("批量删除购物车项: 购物车项GUID列表为空");
                    return true;
                }

                _logger.LogInformation(
                    "开始批量删除购物车项: CartGuid={CartGuid}, ItemCount={ItemCount}",
                    cartGuid,
                    cartItemGuids.Count
                );

                // 验证购物车是否存在
                var cart = await _context
                    .Db.Queryable<Cart>()
                    .Where(x => x.CartGUID == cartGuid)
                    .FirstAsync();

                if (cart == null)
                {
                    _logger.LogWarning("购物车不存在: CartGuid={CartGuid}", cartGuid);
                    return false;
                }

                // 批量查询需要删除的购物车项
                var cartItems = await _context
                    .Db.Queryable<CartItem>()
                    .Where(x => cartItemGuids.Contains(x.CartItemGUID) && x.CartGUID == cartGuid)
                    .ToListAsync();

                if (!cartItems.Any())
                {
                    _logger.LogWarning(
                        "未找到需要删除的购物车项: CartGuid={CartGuid}, CartItemCount={CartItemCount}",
                        cartGuid,
                        cartItemGuids.Count
                    );
                    return false;
                }

                // 批量删除购物车项（硬删除）
                var deleteResult = await _context
                    .Db.Deleteable<CartItem>()
                    .Where(x => cartItemGuids.Contains(x.CartItemGUID) && x.CartGUID == cartGuid)
                    .ExecuteCommandAsync();

                _logger.LogInformation(
                    "批量删除购物车项完成: CartGuid={CartGuid}, DeletedCount={DeletedCount}, AffectedRows={AffectedRows}",
                    cartGuid,
                    cartItems.Count,
                    deleteResult
                );

                // 更新购物车摘要
                await UpdateCartSummaryAsync(cartGuid);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除购物车项失败: CartGuid={CartGuid}", cartGuid);
                return false;
            }
        }

        #endregion

        #region 用户订单和仓库订单分离查询

        /// <summary>
        /// 获取用户相关订单列表（用户创建和关联分店的订单）
        /// </summary>
        public async Task<CartListResponse> GetUserRelatedOrdersAsync(
            string userGuid,
            CartListRequest request
        )
        {
            try
            {
                // 获取用户关联的分店GUID列表
                var userStoreGuids = await GetUserStoreGuidsAsync(userGuid);

                var query = _context
                    .Db.Queryable<Cart>()
                    .Includes(x => x.CartItems)
                    .Where(x =>
                        x.UserGUID == userGuid
                        || (x.StoreGUID != null && userStoreGuids.Contains(x.StoreGUID))
                    );

                // 应用过滤条件
                query = ApplyCartListFilters(query, request);

                // 获取总数
                var totalCount = await query.CountAsync();

                // 分页查询
                var carts = await query
                    .OrderBy(x => x.CreatedAt, OrderByType.Desc)
                    .ToPageListAsync(request.Page, request.PageSize);

                var cartDtos = _mapper.Map<List<CartDto>>(carts);

                // 获取门店信息并填充到CartDto中
                await PopulateStoreInfoAsync(cartDtos);

                return new CartListResponse
                {
                    Carts = cartDtos,
                    TotalCount = totalCount,
                    CurrentPage = request.Page,
                    PageSize = request.PageSize,
                    TotalPages = (int)Math.Ceiling((double)totalCount / request.PageSize),
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to get user related orders: UserGuid={UserGuid}",
                    userGuid
                );
                return new CartListResponse();
            }
        }

        /// <summary>
        /// 获取所有订单列表（仓库管理员视图）
        /// </summary>
        public async Task<CartListResponse> GetAllOrdersAsync(CartListRequest request)
        {
            try
            {
                var query = _context.Db.Queryable<Cart>().Includes(x => x.CartItems);

                // 应用过滤条件
                query = ApplyCartListFilters(query, request);

                // 获取总数
                var totalCount = await query.CountAsync();

                // 分页查询
                var carts = await query
                    .OrderBy(x => x.CreatedAt, OrderByType.Desc)
                    .ToPageListAsync(request.Page, request.PageSize);

                var cartDtos = _mapper.Map<List<CartDto>>(carts);

                // 获取门店信息和用户信息
                await PopulateStoreAndUserInfoAsync(cartDtos);

                return new CartListResponse
                {
                    Carts = cartDtos,
                    TotalCount = totalCount,
                    CurrentPage = request.Page,
                    PageSize = request.PageSize,
                    TotalPages = (int)Math.Ceiling((double)totalCount / request.PageSize),
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all orders");
                return new CartListResponse();
            }
        }

        /// <summary>
        /// 应用购物车列表过滤条件
        /// </summary>
        private ISugarQueryable<Cart> ApplyCartListFilters(
            ISugarQueryable<Cart> query,
            CartListRequest request
        )
        {
            // 状态过滤
            if (!string.IsNullOrEmpty(request.Status))
            {
                query = query.Where(x => x.CartStatus == request.Status);
            }
            else
            {
                // 默认不显示已删除的购物车
                query = query.Where(x => x.CartStatus != CartStatusConstants.Deleted);
            }

            // 排除状态过滤
            if (request.ExcludeStatuses != null && request.ExcludeStatuses.Any())
            {
                query = query.Where(x => !request.ExcludeStatuses.Contains(x.CartStatus));
            }

            // 店铺ID过滤
            if (!string.IsNullOrEmpty(request.StoreId))
            {
                query = query.Where(x => x.StoreGUID == request.StoreId);
            }

            // 搜索过滤
            if (!string.IsNullOrEmpty(request.SearchKeyword))
            {
                query = query.Where(x =>
                    (x.CartName != null && x.CartName.Contains(request.SearchKeyword))
                    || (x.OrderNumber != null && x.OrderNumber.Contains(request.SearchKeyword))
                    || (x.UserGUID != null && x.UserGUID.Contains(request.SearchKeyword))
                );
            }

            return query;
        }

        /// <summary>
        /// 获取用户关联的分店GUID列表
        /// </summary>
        private async Task<List<string>> GetUserStoreGuidsAsync(string userGuid)
        {
            try
            {
                var userStores = await _context
                    .Db.Queryable<UserStore>()
                    .Where(x => x.UserGUID == userGuid)
                    .Select(x => x.StoreGUID)
                    .ToListAsync();

                return userStores.Where(x => !string.IsNullOrEmpty(x)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to get user store GUIDs: UserGuid={UserGuid}",
                    userGuid
                );
                return new List<string>();
            }
        }

        /// <summary>
        /// 填充门店信息
        /// </summary>
        private async Task PopulateStoreInfoAsync(List<CartDto> cartDtos)
        {
            try
            {
                var storeGuids = cartDtos
                    .Where(x => !string.IsNullOrEmpty(x.StoreGUID))
                    .Select(x => x.StoreGUID)
                    .Distinct()
                    .ToList();

                if (storeGuids.Any())
                {
                    var stores = await _context
                        .Db.Queryable<Store>()
                        .Where(x => storeGuids.Contains(x.StoreGUID))
                        .ToListAsync();

                    var storeDict = stores.ToDictionary(x => x.StoreGUID, x => x);

                    foreach (var cartDto in cartDtos)
                    {
                        if (
                            !string.IsNullOrEmpty(cartDto.StoreGUID)
                            && storeDict.ContainsKey(cartDto.StoreGUID)
                        )
                        {
                            var store = storeDict[cartDto.StoreGUID];
                            cartDto.StoreName = store.StoreName;
                            cartDto.StoreAddress = store.Address?.Trim();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to populate store info");
            }
        }

        /// <summary>
        /// 填充门店和用户信息
        /// </summary>
        private async Task PopulateStoreAndUserInfoAsync(List<CartDto> cartDtos)
        {
            try
            {
                // 获取门店信息
                await PopulateStoreInfoAsync(cartDtos);

                // 获取用户信息
                var userGuids = cartDtos
                    .Where(x => !string.IsNullOrEmpty(x.UserGUID))
                    .Select(x => x.UserGUID)
                    .Distinct()
                    .ToList();

                if (userGuids.Any())
                {
                    var users = await _context
                        .Db.Queryable<User>()
                        .Where(x => userGuids.Contains(x.UserGUID))
                        .Select(x => new
                        {
                            x.UserGUID,
                            x.Username,
                            x.Email,
                        })
                        .ToListAsync();

                    var userDict = users.ToDictionary(x => x.UserGUID, x => x);

                    foreach (var cartDto in cartDtos)
                    {
                        if (
                            !string.IsNullOrEmpty(cartDto.UserGUID)
                            && userDict.ContainsKey(cartDto.UserGUID)
                        )
                        {
                            var user = userDict[cartDto.UserGUID];
                            cartDto.UserName = user.Username;
                            cartDto.UserEmail = user.Email;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to populate store and user info");
            }
        }

        #endregion

        #region PDA设备专用购物车操作方法

        /// <summary>
        /// PDA设备创建购物车（基于设备ID和分店信息）
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="storeGuid">分店GUID</param>
        /// <param name="cartName">购物车名称</param>
        /// <param name="remarks">备注</param>
        /// <returns>创建的购物车DTO</returns>
        public async Task<CartDto?> CreatePDACartAsync(
            string deviceId,
            string storeGuid,
            string? cartName = null,
            string? remarks = null
        )
        {
            try
            {
                // 验证分店是否存在
                var store = await _context
                    .Db.Queryable<Store>()
                    .Where(s => s.StoreGUID == storeGuid)
                    .FirstAsync();

                if (store == null)
                {
                    _logger.LogWarning(
                        "尝试为不存在的分店创建PDA购物车: StoreGUID={StoreGUID}",
                        storeGuid
                    );
                    return null;
                }

                // 生成唯一的订单号
                var orderNumber = await GenerateNextOrderNumberAsync();

                // 创建新购物车（使用设备ID作为用户标识）
                var newCart = new Cart
                {
                    CartGUID = Guid.NewGuid().ToString(),
                    UserGUID = deviceId, // 使用设备ID作为用户标识
                    StoreGUID = storeGuid,
                    CartName = string.IsNullOrEmpty(cartName)
                        ? $"PDA-{store.StoreName}-{DateTime.Now:MMdd}"
                        : cartName,
                    OrderNumber = orderNumber,
                    CartStatus = CartStatusConstants.Active, // PDA设备创建的购物车默认为Active
                    Remarks = remarks,
                    LastModified = DateTime.Now,
                    ExpiresAt = DateTime.Now.AddDays(30),
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    CreatedBy = deviceId,
                    UpdatedBy = deviceId,
                };

                await _context.Db.Insertable(newCart).ExecuteCommandAsync();

                _logger.LogInformation(
                    "PDA设备创建购物车成功: CartGuid={CartGuid}, DeviceId={DeviceId}, StoreGuid={StoreGuid}, OrderNumber={OrderNumber}",
                    newCart.CartGUID,
                    deviceId,
                    storeGuid,
                    orderNumber
                );

                // 映射为DTO并设置分店信息
                var cartDto = _mapper.Map<CartDto>(newCart);
                cartDto.StoreName = store.StoreName;
                cartDto.StoreAddress = store.Address?.Trim();

                return cartDto;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "PDA设备创建购物车失败: DeviceId={DeviceId}, StoreGuid={StoreGuid}",
                    deviceId,
                    storeGuid
                );
                return null;
            }
        }

        /// <summary>
        /// PDA设备更新购物车信息
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="cartId">购物车ID</param>
        /// <param name="cartName">购物车名称</param>
        /// <param name="remarks">备注</param>
        /// <returns>更新结果</returns>
        public async Task<CartDto?> UpdatePDACartAsync(
            string deviceId,
            string cartId,
            string? cartName = null,
            string? remarks = null
        )
        {
            try
            {
                var existingCart = await _context
                    .Db.Queryable<Cart>()
                    .Where(x => x.CartGUID == cartId && x.UserGUID == deviceId)
                    .FirstAsync();

                if (existingCart == null)
                {
                    _logger.LogWarning(
                        "PDA购物车不存在或不属于该设备: CartId={CartId}, DeviceId={DeviceId}",
                        cartId,
                        deviceId
                    );
                    return null;
                }

                // 更新购物车信息
                if (!string.IsNullOrEmpty(cartName))
                {
                    existingCart.CartName = cartName;
                }

                if (remarks != null) // 允许设置为空字符串
                {
                    existingCart.Remarks = remarks;
                }

                existingCart.UpdatedAt = DateTime.Now;
                existingCart.LastModified = DateTime.Now;
                existingCart.UpdatedBy = deviceId;

                var updateResult = await _context
                    .Db.Updateable(existingCart)
                    .UpdateColumns(x => new
                    {
                        x.CartName,
                        x.Remarks,
                        x.UpdatedAt,
                        x.LastModified,
                        x.UpdatedBy,
                    })
                    .ExecuteCommandAsync();

                if (updateResult > 0)
                {
                    _logger.LogInformation(
                        "PDA购物车更新成功: CartId={CartId}, DeviceId={DeviceId}",
                        cartId,
                        deviceId
                    );

                    // 获取分店信息
                    var cartDto = _mapper.Map<CartDto>(existingCart);
                    if (!string.IsNullOrEmpty(existingCart.StoreGUID))
                    {
                        var store = await _context
                            .Db.Queryable<Store>()
                            .Where(s => s.StoreGUID == existingCart.StoreGUID)
                            .FirstAsync();

                        if (store != null)
                        {
                            cartDto.StoreName = store.StoreName;
                            cartDto.StoreAddress = store.Address?.Trim();
                        }
                    }

                    return cartDto;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "PDA购物车更新失败: CartId={CartId}, DeviceId={DeviceId}",
                    cartId,
                    deviceId
                );
                return null;
            }
        }

        /// <summary>
        /// PDA设备获取购物车列表（基于设备关联的分店）
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="storeGuid">分店GUID（可选，如果不提供则显示设备关联分店的所有购物车）</param>
        /// <param name="request">查询请求</param>
        /// <returns>购物车列表</returns>
        public async Task<CartListResponse> GetPDACartListAsync(
            string deviceId,
            string? storeGuid,
            CartListRequest request
        )
        {
            try
            {
                var query = _context.Db.Queryable<Cart>().Includes(x => x.CartItems);

                // 如果指定了分店，只显示该分店的购物车；否则显示设备创建的所有购物车
                if (!string.IsNullOrEmpty(storeGuid))
                {
                    query = query.Where(x => x.StoreGUID == storeGuid);
                }
                else
                {
                    // 显示设备创建的购物车
                    query = query.Where(x => x.UserGUID == deviceId);
                }

                // 应用过滤条件
                query = ApplyCartListFilters(query, request);

                // 获取总数
                var totalCount = await query.CountAsync();

                // 分页查询
                var carts = await query
                    .OrderBy(x => x.CreatedAt, OrderByType.Desc)
                    .ToPageListAsync(request.Page, request.PageSize);

                var cartDtos = _mapper.Map<List<CartDto>>(carts);

                // 获取门店信息并填充到CartDto中
                await PopulateStoreInfoAsync(cartDtos);

                _logger.LogInformation(
                    "PDA设备获取购物车列表: DeviceId={DeviceId}, StoreGuid={StoreGuid}, Count={Count}",
                    deviceId,
                    storeGuid,
                    cartDtos.Count
                );

                return new CartListResponse
                {
                    Carts = cartDtos,
                    TotalCount = totalCount,
                    CurrentPage = request.Page,
                    PageSize = request.PageSize,
                    TotalPages = (int)Math.Ceiling((double)totalCount / request.PageSize),
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "PDA设备获取购物车列表失败: DeviceId={DeviceId}, StoreGuid={StoreGuid}",
                    deviceId,
                    storeGuid
                );
                return new CartListResponse();
            }
        }

        /// <summary>
        /// PDA设备获取购物车详情
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="cartId">购物车ID</param>
        /// <returns>购物车详情</returns>
        public async Task<CartDto?> GetPDACartByIdAsync(string deviceId, string cartId)
        {
            try
            {
                var cart = await _context
                    .Db.Queryable<Cart>()
                    .Includes(x => x.CartItems)
                    .Where(x => x.CartGUID == cartId)
                    .FirstAsync();

                if (cart == null)
                {
                    _logger.LogWarning("PDA购物车不存在: CartId={CartId}", cartId);
                    return null;
                }

                var cartDto = _mapper.Map<CartDto>(cart);

                // 获取分店信息
                if (!string.IsNullOrEmpty(cart.StoreGUID))
                {
                    var store = await _context
                        .Db.Queryable<Store>()
                        .Where(s => s.StoreGUID == cart.StoreGUID)
                        .FirstAsync();

                    if (store != null)
                    {
                        cartDto.StoreName = store.StoreName;
                        cartDto.StoreAddress = store.Address?.Trim();
                    }
                }

                _logger.LogInformation(
                    "PDA设备获取购物车详情: DeviceId={DeviceId}, CartId={CartId}",
                    deviceId,
                    cartId
                );
                return cartDto;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "PDA设备获取购物车详情失败: DeviceId={DeviceId}, CartId={CartId}",
                    deviceId,
                    cartId
                );
                return null;
            }
        }

        /// <summary>
        /// PDA设备搜索商品（专用方法）
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="keyword">搜索关键词</param>
        /// <param name="storeGuid">分店GUID（用于库存查询）</param>
        /// <param name="pageSize">每页数量</param>
        /// <returns>商品搜索结果</returns>
        public async Task<List<ProductDto>> SearchPDAProductsAsync(
            string deviceId,
            string keyword,
            string? storeGuid = null,
            int pageSize = 50
        )
        {
            try
            {
                var searchRequest = new ProductSearchRequest
                {
                    Keyword = keyword,
                    Page = 1,
                    PageSize = pageSize,
                };

                var searchResponse = await SearchProductsAsync(searchRequest);

                // 转换为PDA需要的ProductDto格式
                var products = searchResponse
                    .Products.Select(p => new ProductDto
                    {
                        ProductCode = p.ProductCode ?? string.Empty,
                        ProductName = p.ProductName ?? string.Empty,
                        ItemNumber = p.ItemNumber ?? p.ProductCode ?? string.Empty,
                        RetailPrice = p.UnitPrice,
                        ProductImage = p.ProductImage ?? string.Empty,
                        Barcode = p.Barcode ?? string.Empty,
                    })
                    .ToList();

                _logger.LogInformation(
                    "PDA设备搜索商品: DeviceId={DeviceId}, Keyword={Keyword}, Results={Count}",
                    deviceId,
                    keyword,
                    products.Count
                );

                return products;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "PDA设备搜索商品失败: DeviceId={DeviceId}, Keyword={Keyword}",
                    deviceId,
                    keyword
                );
                return new List<ProductDto>();
            }
        }

        /// <summary>
        /// PDA设备添加商品到购物车
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="cartId">购物车ID</param>
        /// <param name="productCode">商品代码</param>
        /// <param name="quantity">数量</param>
        /// <param name="unitPrice">单价（可选，如果不提供则使用商品默认价格）</param>
        /// <returns>操作结果</returns>
        public async Task<bool> AddProductToPDACartAsync(
            string deviceId,
            string cartId,
            string productCode,
            int quantity,
            decimal? unitPrice = null
        )
        {
            try
            {
                // 验证购物车是否属于该设备
                var cart = await _context
                    .Db.Queryable<Cart>()
                    .Where(x => x.CartGUID == cartId && x.UserGUID == deviceId)
                    .FirstAsync();

                if (cart == null)
                {
                    _logger.LogWarning(
                        "PDA购物车不存在或不属于该设备: CartId={CartId}, DeviceId={DeviceId}",
                        cartId,
                        deviceId
                    );
                    return false;
                }

                // 获取商品信息
                var warehouseProduct = await _context
                    .Db.Queryable<WarehouseProduct>()
                    .Includes(x => x.Product)
                    .Where(x => x.ProductCode == productCode)
                    .FirstAsync();

                if (warehouseProduct == null)
                {
                    _logger.LogWarning("商品不存在: ProductCode={ProductCode}", productCode);
                    return false;
                }

                // 检查商品是否已在购物车中
                var existingCartItem = await _context
                    .Db.Queryable<CartItem>()
                    .Where(x => x.CartGUID == cartId && x.ProductCode == productCode)
                    .FirstAsync();

                var actualUnitPrice = unitPrice ?? warehouseProduct.ImportPrice ?? 0;

                if (existingCartItem != null)
                {
                    // 更新现有商品数量
                    existingCartItem.Quantity += quantity;
                    existingCartItem.ActualQuantity = existingCartItem.Quantity;
                    existingCartItem.TotalPrice = actualUnitPrice * existingCartItem.Quantity;
                    existingCartItem.LastUpdated = DateTime.Now;
                    existingCartItem.UpdatedAt = DateTime.Now;
                    existingCartItem.UpdatedBy = deviceId;

                    await _context.Db.Updateable(existingCartItem).ExecuteCommandAsync();

                    _logger.LogInformation(
                        "PDA设备更新购物车商品数量: DeviceId={DeviceId}, CartId={CartId}, ProductCode={ProductCode}, NewQuantity={NewQuantity}",
                        deviceId,
                        cartId,
                        productCode,
                        existingCartItem.Quantity
                    );
                }
                else
                {
                    // 创建新的购物车项
                    var cartItem = new CartItem
                    {
                        CartItemGUID = Guid.NewGuid().ToString(),
                        CartGUID = cartId,
                        ProductCode = productCode,
                        ItemNumber = warehouseProduct.Product?.ItemNumber ?? productCode,
                        ProductName = warehouseProduct.Product?.ProductName ?? "未知商品",
                        ProductImage = warehouseProduct.Product?.ProductImage,
                        UnitPrice = actualUnitPrice,
                        Quantity = quantity,
                        ActualQuantity = quantity,
                        ActualPrice = actualUnitPrice,
                        TotalPrice = actualUnitPrice * quantity,
                        Volume = warehouseProduct.Volume ?? 0,
                        Weight = 0.1m, // 默认重量
                        MinOrderQuantity = warehouseProduct.MinOrderQuantity ?? 1,
                        AddedAt = DateTime.Now,
                        LastUpdated = DateTime.Now,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        CreatedBy = deviceId,
                        UpdatedBy = deviceId,
                    };

                    await _context.Db.Insertable(cartItem).ExecuteCommandAsync();

                    _logger.LogInformation(
                        "PDA设备添加商品到购物车: DeviceId={DeviceId}, CartId={CartId}, ProductCode={ProductCode}, Quantity={Quantity}",
                        deviceId,
                        cartId,
                        productCode,
                        quantity
                    );
                }

                // 更新购物车摘要
                await UpdateCartSummaryAsync(cartId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "PDA设备添加商品到购物车失败: DeviceId={DeviceId}, CartId={CartId}, ProductCode={ProductCode}",
                    deviceId,
                    cartId,
                    productCode
                );
                return false;
            }
        }

        /// <summary>
        /// PDA设备批量添加商品到购物车
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="cartId">购物车ID</param>
        /// <param name="items">商品列表</param>
        /// <returns>批量添加结果</returns>
        public async Task<(
            int successCount,
            int failureCount,
            List<string> errors
        )> BatchAddProductsToPDACartAsync(
            string deviceId,
            string cartId,
            List<(string productCode, int quantity, decimal? unitPrice)> items
        )
        {
            var successCount = 0;
            var failureCount = 0;
            var errors = new List<string>();

            try
            {
                // 验证购物车是否属于该设备
                var cart = await _context
                    .Db.Queryable<Cart>()
                    .Where(x => x.CartGUID == cartId && x.UserGUID == deviceId)
                    .FirstAsync();

                if (cart == null)
                {
                    errors.Add("购物车不存在或不属于该设备");
                    return (0, items.Count, errors);
                }

                foreach (var item in items)
                {
                    try
                    {
                        var result = await AddProductToPDACartAsync(
                            deviceId,
                            cartId,
                            item.productCode,
                            item.quantity,
                            item.unitPrice
                        );
                        if (result)
                        {
                            successCount++;
                        }
                        else
                        {
                            failureCount++;
                            errors.Add($"添加商品失败: {item.productCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failureCount++;
                        errors.Add($"添加商品异常: {item.productCode} - {ex.Message}");
                        _logger.LogError(
                            ex,
                            "批量添加商品项失败: ProductCode={ProductCode}",
                            item.productCode
                        );
                    }
                }

                _logger.LogInformation(
                    "PDA设备批量添加商品完成: DeviceId={DeviceId}, CartId={CartId}, Success={Success}, Failure={Failure}",
                    deviceId,
                    cartId,
                    successCount,
                    failureCount
                );

                return (successCount, failureCount, errors);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "PDA设备批量添加商品失败: DeviceId={DeviceId}, CartId={CartId}",
                    deviceId,
                    cartId
                );
                errors.Add($"批量操作异常: {ex.Message}");
                return (0, items.Count, errors);
            }
        }

        /// <summary>
        /// PDA设备更新购物车商品数量
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="cartId">购物车ID</param>
        /// <param name="cartItemId">购物车项ID</param>
        /// <param name="newQuantity">新数量</param>
        /// <returns>操作结果</returns>
        public async Task<bool> UpdatePDACartItemQuantityAsync(
            string deviceId,
            string cartId,
            string cartItemId,
            int newQuantity
        )
        {
            try
            {
                // 验证购物车项是否属于该设备的购物车
                var cartItem = await _context
                    .Db.Queryable<CartItem>()
                    .Includes(x => x.Cart)
                    .Where(x =>
                        x.CartItemGUID == cartItemId
                        && x.CartGUID == cartId
                        && x.Cart!.UserGUID == deviceId
                    )
                    .FirstAsync();

                if (cartItem == null)
                {
                    _logger.LogWarning(
                        "PDA购物车项不存在或不属于该设备: CartItemId={CartItemId}, DeviceId={DeviceId}",
                        cartItemId,
                        deviceId
                    );
                    return false;
                }

                if (newQuantity <= 0)
                {
                    // 如果数量为0或负数，删除该商品
                    await _context
                        .Db.Deleteable<CartItem>()
                        .Where(x => x.CartItemGUID == cartItemId)
                        .ExecuteCommandAsync();

                    _logger.LogInformation(
                        "PDA设备删除购物车商品: DeviceId={DeviceId}, CartItemId={CartItemId}",
                        deviceId,
                        cartItemId
                    );
                }
                else
                {
                    // 更新数量
                    cartItem.Quantity = newQuantity;
                    cartItem.ActualQuantity = newQuantity;
                    cartItem.TotalPrice =
                        (cartItem.ActualPrice ?? cartItem.UnitPrice) * newQuantity;
                    cartItem.LastUpdated = DateTime.Now;
                    cartItem.UpdatedAt = DateTime.Now;
                    cartItem.UpdatedBy = deviceId;

                    await _context.Db.Updateable(cartItem).ExecuteCommandAsync();

                    _logger.LogInformation(
                        "PDA设备更新购物车商品数量: DeviceId={DeviceId}, CartItemId={CartItemId}, NewQuantity={NewQuantity}",
                        deviceId,
                        cartItemId,
                        newQuantity
                    );
                }

                // 更新购物车摘要
                await UpdateCartSummaryAsync(cartId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "PDA设备更新购物车商品数量失败: DeviceId={DeviceId}, CartItemId={CartItemId}",
                    deviceId,
                    cartItemId
                );
                return false;
            }
        }

        /// <summary>
        /// PDA设备从购物车移除商品
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="cartId">购物车ID</param>
        /// <param name="cartItemId">购物车项ID</param>
        /// <returns>操作结果</returns>
        public async Task<bool> RemoveProductFromPDACartAsync(
            string deviceId,
            string cartId,
            string cartItemId
        )
        {
            try
            {
                // 验证购物车项是否属于该设备的购物车
                var cartItem = await _context
                    .Db.Queryable<CartItem>()
                    .Includes(x => x.Cart)
                    .Where(x =>
                        x.CartItemGUID == cartItemId
                        && x.CartGUID == cartId
                        && x.Cart!.UserGUID == deviceId
                    )
                    .FirstAsync();

                if (cartItem == null)
                {
                    _logger.LogWarning(
                        "PDA购物车项不存在或不属于该设备: CartItemId={CartItemId}, DeviceId={DeviceId}",
                        cartItemId,
                        deviceId
                    );
                    return false;
                }

                // 删除购物车项
                await _context
                    .Db.Deleteable<CartItem>()
                    .Where(x => x.CartItemGUID == cartItemId)
                    .ExecuteCommandAsync();

                // 更新购物车摘要
                await UpdateCartSummaryAsync(cartId);

                _logger.LogInformation(
                    "PDA设备移除购物车商品: DeviceId={DeviceId}, CartId={CartId}, CartItemId={CartItemId}",
                    deviceId,
                    cartId,
                    cartItemId
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "PDA设备移除购物车商品失败: DeviceId={DeviceId}, CartItemId={CartItemId}",
                    deviceId,
                    cartItemId
                );
                return false;
            }
        }

        #endregion
    }
}
