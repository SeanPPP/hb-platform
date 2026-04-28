using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Extensions.Configuration;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class StoreOrderReactService : IStoreOrderReactService
    {
        private readonly ISqlSugarClient _db;
        private readonly ILogger<StoreOrderReactService> _logger;
        private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _httpContextAccessor;
        private readonly IOrderNumberGenerator _orderNumberGenerator;
        private readonly IConfiguration _configuration;
        private readonly IMapper _mapper;

        public StoreOrderReactService(
            SqlSugarContext context,
            ILogger<StoreOrderReactService> logger,
            Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor,
            IOrderNumberGenerator orderNumberGenerator,
            IConfiguration configuration,
            IMapper mapper
        )
        {
            _db = context.Db;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _orderNumberGenerator = orderNumberGenerator;
            _configuration = configuration;
            _mapper = mapper;
        }

        public async Task<PagedListReactDto<StoreOrderProductDto>> GetPagedListAsync(
            StoreOrderFilterDto filter
        )
        {
            var q = _db.Queryable<Product>()
                .InnerJoin<WarehouseProduct>((p, wp) => p.ProductCode == wp.ProductCode)
                .LeftJoin<WarehouseCategory>(
                    (p, wp, wc) => p.WarehouseCategoryGUID == wc.CategoryGUID
                )
                .Where((p, wp, wc) => p.IsActive && !p.IsDeleted && !wp.IsDeleted && wp.IsActive);

            // 1. 分类筛选
            if (!string.IsNullOrWhiteSpace(filter.CategoryGUID))
            {
                var categoryIds = GetAllSubCategoryIds(filter.CategoryGUID);
                _logger.LogInformation(
                    "Category Filter: Found {Count} categories (including self) for root {CategoryGUID}",
                    categoryIds.Count,
                    filter.CategoryGUID
                );
                q = q.Where(
                    (p, wp, wc) =>
                        p.WarehouseCategoryGUID != null
                        && categoryIds.Contains(p.WarehouseCategoryGUID)
                );
            }

            // 2. 货号搜索 (兼容商品名称搜索)
            if (!string.IsNullOrWhiteSpace(filter.ItemNumber))
            {
                var keyword = filter.ItemNumber.Trim().ToLower();
                q = q.Where(
                    (p, wp, wc) =>
                        (p.ItemNumber != null && p.ItemNumber.ToLower().Contains(keyword))
                        || (p.ProductName != null && p.ProductName.ToLower().Contains(keyword))
                );
            }

            // 2.1 商品名称搜索
            if (!string.IsNullOrWhiteSpace(filter.ProductName))
            {
                var keyword = filter.ProductName.Trim().ToLower();
                q = q.Where(
                    (p, wp, wc) =>
                        p.ProductName != null && p.ProductName.ToLower().Contains(keyword)
                );
            }

            // 3. 排序
            if (!string.IsNullOrWhiteSpace(filter.SortBy))
            {
                switch (filter.SortBy.ToLower())
                {
                    case "priceasc":
                        q = q.OrderBy((p, wp, wc) => wp.OEMPrice, OrderByType.Asc);
                        break;
                    case "pricedesc":
                        q = q.OrderBy((p, wp, wc) => wp.OEMPrice, OrderByType.Desc);
                        break;
                    case "name":
                        q = q.OrderBy((p, wp, wc) => p.ProductName, OrderByType.Asc);
                        break;
                    default:
                        // 默认按货号排序
                        q = q.OrderBy((p, wp, wc) => p.ItemNumber, OrderByType.Asc);
                        break;
                }
            }
            else
            {
                q = q.OrderBy((p, wp, wc) => p.ItemNumber, OrderByType.Asc);
            }

            var total = await q.CountAsync();

            var items = await q.Select(
                    (p, wp, wc) =>
                        new StoreOrderProductDto
                        {
                            ProductCode = p.ProductCode ?? string.Empty,
                            ItemNumber = p.ItemNumber,
                            ProductName = p.ProductName,
                            ProductImage = p.ProductImage,
                            CategoryName = wc.CategoryName,
                            WarehouseCategoryGUID = p.WarehouseCategoryGUID,
                            OEMPrice = wp.OEMPrice,
                            MinOrderQuantity = wp.MinOrderQuantity ?? 1,
                            StockQuantity = wp.StockQuantity ?? 0,
                            PackQty = p.MiddlePackageQuantity,
                            ImportPrice = wp.ImportPrice,
                        }
                )
                .Skip((filter.PageNumber - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();

            return new PagedListReactDto<StoreOrderProductDto>
            {
                Items = items,
                Total = total,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize,
            };
        }

        private List<string> GetAllSubCategoryIds(string categoryGuid)
        {
            try
            {
                var allCategories = _db.Queryable<WarehouseCategory>().ToList();
                var result = new List<string> { categoryGuid };
                GetSubCategoriesRecursive(categoryGuid, allCategories, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to get subcategories for {CategoryGuid}",
                    categoryGuid
                );
                return new List<string> { categoryGuid };
            }
        }

        private void GetSubCategoriesRecursive(
            string parentGuid,
            List<WarehouseCategory> allCategories,
            List<string> result
        )
        {
            var children = allCategories.Where(c => c.ParentGUID == parentGuid).ToList();
            foreach (var child in children)
            {
                if (
                    !string.IsNullOrEmpty(child.CategoryGUID)
                    && !result.Contains(child.CategoryGUID)
                )
                {
                    result.Add(child.CategoryGUID);
                    GetSubCategoriesRecursive(child.CategoryGUID, allCategories, result);
                }
            }
        }

        public async Task<ApiResponse<StoreOrderCartDto?>> GetActiveCartAsync(string storeCode)
        {
            // FlowStatus = 0 代表购物车
            var order = await _db.Queryable<WareHouseOrder>()
                .Where(o => o.StoreCode == storeCode && o.FlowStatus == 0 && !o.IsDeleted)
                .FirstAsync();

            if (order == null)
            {
                return new ApiResponse<StoreOrderCartDto?> { Success = true, Data = null };
            }

            var details = await _db.Queryable<WareHouseOrderDetails>()
                .LeftJoin<Product>((d, p) => d.ProductCode == p.ProductCode)
                .LeftJoin<WarehouseProduct>((d, p, wp) => d.ProductCode == wp.ProductCode)
                .LeftJoin<DomesticProduct>((d, p, wp, dp) => wp.ProductCode == dp.ProductCode)
                .Where(d => d.OrderGUID == order.OrderGUID)
                .Select(
                    (d, p, wp, dp) =>
                        new StoreOrderCartItemDto
                        {
                            DetailGUID = d.DetailGUID,
                            ProductCode = d.ProductCode ?? string.Empty,
                            ItemNumber = p.ItemNumber,
                            Barcode = p.Barcode,
                            ProductName = p.ProductName,
                            ProductImage = p.ProductImage,
                            Price = d.OEMPrice ?? 0,
                            Quantity = d.Quantity ?? 0,
                            AllocQuantity = d.AllocQuantity,
                            Amount = d.OEMAmount ?? 0,
                            ImportPrice = d.ImportPrice ?? (wp.ImportPrice ?? 0),
                            ImportAmount =
                                d.ImportAmount
                                ?? ((d.ImportPrice ?? (wp.ImportPrice ?? 0)) * (d.Quantity ?? 0)),
                            // 计算单件体积: 如果装箱数 > 0，则用箱体积 / 装箱数，否则直接用 UnitVolume
                            Volume =
                                (dp.PackingQuantity > 0)
                                    ? (dp.UnitVolume / dp.PackingQuantity)
                                    : dp.UnitVolume,
                            MinOrderQuantity = wp.MinOrderQuantity ?? 1,
                        }
                )
                .ToListAsync();

            // 计算小计体积和总计
            foreach (var item in details)
            {
                if (item.Volume.HasValue)
                {
                    item.TotalVolume = item.Volume * item.Quantity;
                }
            }

            var dto = new StoreOrderCartDto
            {
                OrderGUID = order.OrderGUID,
                OrderNo = order.OrderNo,
                StoreCode = order.StoreCode,
                TotalAmount = order.OEMTotalAmount ?? 0,
                TotalQuantity = (int)details.Sum(x => x.Quantity),
                TotalImportAmount = details.Sum(x => x.ImportAmount),
                TotalVolume = details.Sum(x => x.TotalVolume ?? 0),
                Remarks = order.Remarks,
                ShippingFee = order.ShippingFee,
                Items = details,
            };

            return new ApiResponse<StoreOrderCartDto?> { Success = true, Data = dto };
        }

        public async Task<ApiResponse<bool>> AddToCartAsync(AddToCartRequestDto request)
        {
            try
            {
                var now = DateTime.Now;
                var currentUser =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                // 1. 获取或创建订单 (FlowStatus = 0)
                var order = await _db.Queryable<WareHouseOrder>()
                    .Where(o =>
                        o.StoreCode == request.StoreCode && o.FlowStatus == 0 && !o.IsDeleted
                    )
                    .FirstAsync();

                if (order == null)
                {
                    order = new WareHouseOrder
                    {
                        OrderGUID = UuidHelper.GenerateUuid7(),
                        StoreCode = request.StoreCode,
                        OrderDate = now,
                        FlowStatus = 0, // 购物车状态
                        IsDeleted = false,
                        CreatedAt = now,
                        UpdatedAt = now,
                        UpdatedBy = currentUser,
                        OEMTotalAmount = 0,
                        ImportTotalAmount = 0,
                        ShippingFee = 0,
                    };
                    await _db.Insertable(order).ExecuteCommandAsync();
                }

                // 2. 获取商品信息 (获取贴牌价)
                var warehouseProduct = await _db.Queryable<WarehouseProduct>()
                    .Where(wp => wp.ProductCode == request.ProductCode)
                    .FirstAsync();

                var product = await _db.Queryable<Product>()
                    .Where(p => p.ProductCode == request.ProductCode)
                    .FirstAsync();

                if (warehouseProduct == null || product == null)
                {
                    return new ApiResponse<bool> { Success = false, Message = "商品不存在" };
                }

                decimal price = warehouseProduct.OEMPrice ?? 0;
                decimal importPrice = warehouseProduct.ImportPrice ?? 0; // 记录ImportPrice以便统计

                // 3. 检查明细是否已存在
                var detail = await _db.Queryable<WareHouseOrderDetails>()
                    .Where(d =>
                        d.OrderGUID == order.OrderGUID && d.ProductCode == request.ProductCode
                    )
                    .FirstAsync();

                if (detail == null)
                {
                    // 新增明细
                    detail = new WareHouseOrderDetails
                    {
                        DetailGUID = UuidHelper.GenerateUuid7(),
                        OrderGUID = order.OrderGUID,
                        StoreCode = request.StoreCode,
                        ProductCode = request.ProductCode,
                        Quantity = request.Quantity,
                        OEMPrice = price,
                        OEMAmount = price * request.Quantity,
                        ImportPrice = importPrice,
                        ImportAmount = importPrice * request.Quantity,
                        IsDeleted = false,
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = currentUser,
                        UpdatedBy = currentUser,
                    };
                    await _db.Insertable(detail).ExecuteCommandAsync();
                }
                else
                {
                    // 更新明细
                    detail.Quantity += request.Quantity;
                    // 如果数量 <= 0，则删除
                    if (detail.Quantity <= 0)
                    {
                        await _db.Deleteable(detail).ExecuteCommandAsync();
                    }
                    else
                    {
                        detail.OEMAmount = detail.Quantity * detail.OEMPrice;
                        detail.ImportAmount = detail.Quantity * detail.ImportPrice;
                        detail.UpdatedAt = now;
                        detail.UpdatedBy = currentUser;
                        await _db.Updateable(detail).ExecuteCommandAsync();
                    }
                }

                // 4. 更新主表总金额
                await UpdateOrderTotalAsync(order.OrderGUID);

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddToCart failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> UpdateCartItemAsync(AddToCartRequestDto request)
        {
            try
            {
                var now = DateTime.Now;
                var currentUser =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                // 1. 获取购物车
                var order = await _db.Queryable<WareHouseOrder>()
                    .Where(o =>
                        o.StoreCode == request.StoreCode && o.FlowStatus == 0 && !o.IsDeleted
                    )
                    .FirstAsync();

                if (order == null)
                {
                    // 如果购物车不存在，则尝试当作添加处理 (或返回错误，取决于业务)
                    // 这里我们选择直接调用 AddToCart
                    return await AddToCartAsync(request);
                }

                // 2. 获取商品信息 (获取贴牌价)
                var warehouseProduct = await _db.Queryable<WarehouseProduct>()
                    .Where(wp => wp.ProductCode == request.ProductCode)
                    .FirstAsync();

                if (warehouseProduct == null)
                {
                    return new ApiResponse<bool> { Success = false, Message = "商品不存在" };
                }

                decimal price = warehouseProduct.OEMPrice ?? 0;
                decimal importPrice = warehouseProduct.ImportPrice ?? 0;

                // 3. 检查明细
                var detail = await _db.Queryable<WareHouseOrderDetails>()
                    .Where(d =>
                        d.OrderGUID == order.OrderGUID && d.ProductCode == request.ProductCode
                    )
                    .FirstAsync();

                if (detail == null)
                {
                    // 如果明细不存在，创建新的
                    detail = new WareHouseOrderDetails
                    {
                        DetailGUID = UuidHelper.GenerateUuid7(),
                        OrderGUID = order.OrderGUID,
                        StoreCode = request.StoreCode,
                        ProductCode = request.ProductCode,
                        Quantity = request.Quantity,
                        OEMPrice = price,
                        OEMAmount = price * request.Quantity,
                        ImportPrice = importPrice,
                        ImportAmount = importPrice * request.Quantity,
                        IsDeleted = false,
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = currentUser,
                        UpdatedBy = currentUser,
                    };
                    await _db.Insertable(detail).ExecuteCommandAsync();
                }
                else
                {
                    // 更新数量
                    detail.Quantity = request.Quantity;
                    // 如果数量 <= 0，则删除
                    if (detail.Quantity <= 0)
                    {
                        await _db.Deleteable(detail).ExecuteCommandAsync();
                    }
                    else
                    {
                        detail.OEMAmount = detail.Quantity * detail.OEMPrice;
                        detail.ImportAmount = detail.Quantity * detail.ImportPrice;
                        detail.UpdatedAt = now;
                        detail.UpdatedBy = currentUser;
                        await _db.Updateable(detail).ExecuteCommandAsync();
                    }
                }

                // 4. 更新主表总金额
                await UpdateOrderTotalAsync(order.OrderGUID);

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateCartItemAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> RemoveFromCartAsync(RemoveFromCartRequestDto request)
        {
            try
            {
                var detail = await _db.Queryable<WareHouseOrderDetails>()
                    .Where(d => d.DetailGUID == request.DetailGUID)
                    .FirstAsync();

                if (detail == null)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Cart item not found",
                    };
                }

                var orderGuid = detail.OrderGUID;
                await _db.Deleteable(detail).ExecuteCommandAsync();

                // 更新主表
                if (!string.IsNullOrEmpty(orderGuid))
                {
                    await UpdateOrderTotalAsync(orderGuid);
                }

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RemoveFromCart failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<StoreOrderCartDto?>> ClearCartAsync(string storeCode)
        {
            try
            {
                var cart = await _db.Queryable<WareHouseOrder>()
                    .Where(o => o.StoreCode == storeCode && o.FlowStatus == 0 && !o.IsDeleted)
                    .FirstAsync();

                if (cart == null)
                {
                    return new ApiResponse<StoreOrderCartDto?>
                    {
                        Success = true,
                        Data = null,
                        Message = "Cart is already empty",
                    };
                }

                await _db.Deleteable<WareHouseOrderDetails>()
                    .Where(d => d.OrderGUID == cart.OrderGUID)
                    .ExecuteCommandAsync();

                await _db.Deleteable<WareHouseOrder>()
                    .Where(o => o.OrderGUID == cart.OrderGUID)
                    .ExecuteCommandAsync();

                _logger.LogInformation("Cleared cart for store: {StoreCode}", storeCode);

                return new ApiResponse<StoreOrderCartDto?>
                {
                    Success = true,
                    Data = null,
                    Message = "Cart cleared successfully",
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear cart for store: {StoreCode}", storeCode);
                return new ApiResponse<StoreOrderCartDto?>
                {
                    Success = false,
                    Message = "Failed to clear cart",
                };
            }
        }

        public async Task<ApiResponse<bool>> SubmitOrderAsync(SubmitStoreOrderRequestDto request)
        {
            try
            {
                var order = await _db.Queryable<WareHouseOrder>()
                    .Where(o =>
                        o.StoreCode == request.StoreCode && o.FlowStatus == 0 && !o.IsDeleted
                    )
                    .FirstAsync();

                if (order == null)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "No active cart found",
                    };
                }

                // 检查是否有明细
                var count = await _db.Queryable<WareHouseOrderDetails>()
                    .Where(d => d.OrderGUID == order.OrderGUID)
                    .CountAsync();

                if (count == 0)
                {
                    return new ApiResponse<bool> { Success = false, Message = "Cart is empty" };
                }

                // 更新状态 0 -> 1 (审核中/已提交)
                order.FlowStatus = 1;
                order.Remarks = request.Remarks;
                order.OrderDate = DateTime.Now; // 更新下单时间
                order.UpdatedAt = DateTime.Now;
                order.UpdatedBy =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                // 生成正式订单号 (ORD-YYYY-NNNN 格式，从1000开始递增)
                order.OrderNo = await _orderNumberGenerator.GetNextOrderNoAsync();

                await _db.Updateable(order).ExecuteCommandAsync();

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SubmitOrder failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        private async Task UpdateOrderTotalAsync(string orderGuid)
        {
            var details = await _db.Queryable<WareHouseOrderDetails>()
                .Where(d => d.OrderGUID == orderGuid)
                .ToListAsync();

            var totalOEM = details.Sum(d => d.OEMAmount) ?? 0;
            var totalImport = details.Sum(d => d.ImportAmount) ?? 0;

            await _db.Updateable<WareHouseOrder>()
                .SetColumns(o => new WareHouseOrder
                {
                    OEMTotalAmount = totalOEM,
                    ImportTotalAmount = totalImport,
                    UpdatedAt = DateTime.Now,
                })
                .Where(o => o.OrderGUID == orderGuid)
                .ExecuteCommandAsync();
        }

        public async Task<ApiResponse<List<StoreOrderDynamicDataDto>>> GetProductsDynamicDataAsync(
            StoreOrderDynamicDataRequestDto request
        )
        {
            try
            {
                if (request.ProductCodes == null || !request.ProductCodes.Any())
                {
                    return new ApiResponse<List<StoreOrderDynamicDataDto>>
                    {
                        Success = true,
                        Data = new List<StoreOrderDynamicDataDto>(),
                    };
                }

                // 1. 获取购物车数量 (FlowStatus = 0)
                var cartItems = await _db.Queryable<WareHouseOrderDetails>()
                    .InnerJoin<WareHouseOrder>((d, o) => d.OrderGUID == o.OrderGUID)
                    .Where(
                        (d, o) =>
                            o.StoreCode == request.StoreCode
                            && o.FlowStatus == 0
                            && !o.IsDeleted
                            && !d.IsDeleted
                    )
                    .Where(
                        (d, o) =>
                            d.ProductCode != null && request.ProductCodes.Contains(d.ProductCode)
                    )
                    .Select((d, o) => new { d.ProductCode, d.Quantity })
                    .ToListAsync();

                // 2. 获取最近历史订单 (FlowStatus > 0)
                // 策略: 先取出所有相关明细按时间倒序，然后在内存分组
                var historyItems = await _db.Queryable<WareHouseOrderDetails>()
                    .InnerJoin<WareHouseOrder>((d, o) => d.OrderGUID == o.OrderGUID)
                    .Where(
                        (d, o) =>
                            o.StoreCode == request.StoreCode
                            && o.FlowStatus > 0
                            && !o.IsDeleted
                            && !d.IsDeleted
                    )
                    .Where(
                        (d, o) =>
                            d.ProductCode != null && request.ProductCodes.Contains(d.ProductCode)
                    )
                    .OrderBy((d, o) => o.OrderDate, OrderByType.Desc)
                    .Select(
                        (d, o) =>
                            new
                            {
                                d.ProductCode,
                                o.OrderDate,
                                d.Quantity,
                                d.AllocQuantity,
                            }
                    )
                    .ToListAsync();

                // 3. 组装结果
                var result = new List<StoreOrderDynamicDataDto>();
                foreach (var code in request.ProductCodes)
                {
                    var dto = new StoreOrderDynamicDataDto { ProductCode = code };

                    // 填充购物车数量
                    var cartItem = cartItems.FirstOrDefault(x => x.ProductCode == code);
                    if (cartItem != null)
                    {
                        dto.CartQuantity = cartItem.Quantity ?? 0;
                    }

                    // 填充历史信息
                    var historyItem = historyItems.FirstOrDefault(x => x.ProductCode == code);
                    if (historyItem != null)
                    {
                        dto.LastOrderDate = historyItem.OrderDate;
                        dto.LastQuantity = historyItem.Quantity;
                        dto.LastAllocQuantity = historyItem.AllocQuantity;
                    }

                    result.Add(dto);
                }

                return new ApiResponse<List<StoreOrderDynamicDataDto>>
                {
                    Success = true,
                    Data = result,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetProductsDynamicDataAsync failed");
                return new ApiResponse<List<StoreOrderDynamicDataDto>>
                {
                    Success = false,
                    Message = ex.Message,
                };
            }
        }

        public async Task<PagedListReactDto<StoreOrderListItemDto>> GetOrderListAsync(
            StoreOrderListFilterDto filter
        )
        {
            try
            {
                ISugarQueryable<WareHouseOrder> q;

                // 0. 关键字筛选 (订单号 或 分店代码 或 商品货号)
                // 使用 Union 优化查询性能：分别查询主表和关联表，利用各自的索引
                if (!string.IsNullOrWhiteSpace(filter.Keyword))
                {
                    var keyword = filter.Keyword.Trim();

                    // Query 1: 匹配订单号或分店代码
                    var q1 = _db.Queryable<WareHouseOrder>()
                        .Where(o =>
                            !o.IsDeleted
                            && (
                                (o.OrderNo != null && o.OrderNo.Contains(keyword))
                                || (o.StoreCode != null && o.StoreCode.Contains(keyword))
                            )
                        );

                    // Query 2: 匹配商品货号
                    var q2 = _db.Queryable<WareHouseOrder>()
                        .InnerJoin<WareHouseOrderDetails>((o, d) => o.OrderGUID == d.OrderGUID)
                        .InnerJoin<Product>((o, d, p) => d.ProductCode == p.ProductCode)
                        .Where(
                            (o, d, p) =>
                                !o.IsDeleted
                                && p.ItemNumber != null
                                && p.ItemNumber.Contains(keyword)
                        )
                        .Select(o => o); // 只选择主表字段以匹配 q1

                    // 合并查询 (Union 会自动去重)
                    // 使用 MergeTable() 将 Union 结果作为派生表处理，避免 "无法绑定由多个部分组成的标识符" 错误
                    q = _db.Union(q1, q2).MergeTable();
                }
                else
                {
                    q = _db.Queryable<WareHouseOrder>().Where(o => !o.IsDeleted);
                }

                // 1. 分店筛选
                if (filter.StoreCodes != null && filter.StoreCodes.Any())
                {
                    q = q.Where(o =>
                        o.StoreCode != null && filter.StoreCodes!.Contains(o.StoreCode)
                    );
                }
                else if (!string.IsNullOrWhiteSpace(filter.StoreCode))
                {
                    q = q.Where(o => o.StoreCode == filter.StoreCode);
                }
                else
                {
                    // 如果未指定 StoreCode，则只返回当前用户关联的分店订单
                    var currentUser = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
                    if (!string.IsNullOrEmpty(currentUser))
                    {
                        // 假设从 UserStore 表获取用户关联的分店
                        // 注意：这里需要注入 IUserService 或直接查询 UserStore 表
                        // 由于未直接注入，这里使用 _db 查询
                        var userGuid = await _db.Queryable<User>()
                            .Where(u => u.Username == currentUser)
                            .Select(u => u.UserGUID)
                            .FirstAsync();

                        if (!string.IsNullOrEmpty(userGuid))
                        {
                            var userRoles = await _db.Queryable<UserRole>()
                                .InnerJoin<Role>((ur, r) => ur.RoleGUID == r.RoleGUID)
                                .Where((ur, r) => ur.UserGUID == userGuid && r.IsActive)
                                .Select((ur, r) => r.RoleName)
                                .ToListAsync();

                            bool isAdminOrManager = userRoles.Any(role =>
                                role == "Admin" || role == "Manager"
                            );

                            if (!isAdminOrManager)
                            {
                                var userStoreCodes = await _db.Queryable<UserStore>()
                                    .InnerJoin<Store>((us, s) => us.StoreGUID == s.StoreGUID)
                                    .Where((us, s) => us.UserGUID == userGuid)
                                    .Select((us, s) => s.StoreCode)
                                    .ToListAsync();

                                if (userStoreCodes.Any())
                                {
                                    q = q.Where(o => userStoreCodes.Contains(o.StoreCode));
                                }
                            }
                        }
                    }
                }

                // 2. 状态筛选
                if (filter.StatusList != null && filter.StatusList.Any())
                {
                    // 不包含状态 2，直接使用 StatusList
                    q = q.Where(o =>
                        o.FlowStatus != null && filter.StatusList.Contains(o.FlowStatus.Value)
                    );
                }

                // 3. 日期筛选
                if (filter.StartDate.HasValue)
                {
                    var start = filter.StartDate.Value.Date;
                    q = q.Where(o => o.OrderDate >= start);
                }
                if (filter.EndDate.HasValue)
                {
                    var end = filter.EndDate.Value.Date.AddDays(1).AddMilliseconds(-1);
                    q = q.Where(o => o.OrderDate <= end);
                }

                var total = await q.Clone().CountAsync();

                // 动态排序处理
                var sortBy = (filter.SortBy ?? "default").Trim().ToLower();
                var orderType =
                    (filter.SortDescending ?? true) ? OrderByType.Desc : OrderByType.Asc;

                ISugarQueryable<WareHouseOrder> orderedQuery = q;

                switch (sortBy)
                {
                    case "orderno":
                        orderedQuery = q.OrderBy(o => o.OrderNo, orderType)
                            .OrderBy(o => o.OrderGUID, orderType);
                        break;
                    case "orderdate":
                        orderedQuery = q.OrderBy(
                            $"OrderDate {(orderType == OrderByType.Desc ? "DESC" : "ASC")}, OrderNo {(orderType == OrderByType.Desc ? "DESC" : "ASC")}, OrderGUID {(orderType == OrderByType.Desc ? "DESC" : "ASC")}"
                        );
                        break;
                    case "storecode":
                        orderedQuery = q.OrderBy(o => o.StoreCode, orderType)
                            .OrderBy(o => o.OrderGUID, orderType);
                        break;
                    case "flowstatus":
                        orderedQuery = q.OrderBy(o => o.FlowStatus, orderType)
                            .OrderByDescending(o => o.OrderDate)
                            .OrderBy(o => o.OrderGUID, orderType);
                        break;
                    case "totalamount":
                        orderedQuery = q.OrderBy(o => o.ImportTotalAmount ?? 0, orderType)
                            .OrderBy(o => o.OrderGUID, orderType);
                        break;
                    case "oemtotalamount":
                        orderedQuery = q.OrderBy(o => o.OEMTotalAmount ?? 0, orderType)
                            .OrderBy(o => o.OrderGUID, orderType);
                        break;
                    case "importtotalamount":
                        orderedQuery = q.OrderBy(o => o.ImportTotalAmount ?? 0, orderType)
                            .OrderBy(o => o.OrderGUID, orderType);
                        break;
                    case "totalorderamount":
                        orderedQuery = q.OrderBy(
                            $"(SELECT ISNULL(SUM(d.Quantity * d.ImportPrice), 0) FROM WareHouseOrderDetails d WHERE d.OrderGUID = WareHouseOrder.OrderGUID) {(orderType == OrderByType.Desc ? "DESC" : "ASC")}, OrderGUID {(orderType == OrderByType.Desc ? "DESC" : "ASC")}"
                        );
                        break;
                    case "totalquantity":
                        orderedQuery = q.OrderBy(
                            $"(SELECT ISNULL(SUM(d.Quantity), 0) FROM WareHouseOrderDetails d WHERE d.OrderGUID = WareHouseOrder.OrderGUID) {(orderType == OrderByType.Desc ? "DESC" : "ASC")}, OrderGUID {(orderType == OrderByType.Desc ? "DESC" : "ASC")}"
                        );
                        break;
                    case "totalallocquantity":
                        orderedQuery = q.OrderBy(
                            $"(SELECT ISNULL(SUM(d.AllocQuantity), 0) FROM WareHouseOrderDetails d WHERE d.OrderGUID = WareHouseOrder.OrderGUID) {(orderType == OrderByType.Desc ? "DESC" : "ASC")}, OrderGUID {(orderType == OrderByType.Desc ? "DESC" : "ASC")}"
                        );
                        break;
                    case "remarks":
                        orderedQuery = q.OrderBy(o => o.Remarks, orderType)
                            .OrderBy(o => o.OrderGUID, orderType);
                        break;
                    default:
                        orderedQuery = q.OrderBy("FlowStatus ASC, OrderDate DESC, OrderNo DESC");
                        break;
                }

                var items = await orderedQuery
                    .Skip((filter.PageNumber - 1) * filter.PageSize)
                    .Take(filter.PageSize)
                    .Select(o => new StoreOrderListItemDto
                    {
                        OrderGUID = o.OrderGUID,
                        OrderNo = o.OrderNo,
                        StoreCode = o.StoreCode,
                        StoreName = SqlFunc
                            .Subqueryable<Store>()
                            .Where(s => s.StoreCode == o.StoreCode)
                            .Select(s => s.StoreName),
                        OrderDate = o.OrderDate,
                        FlowStatus = o.FlowStatus ?? 0,

                        // TotalAmount -> 实际发货金额
                        TotalAmount = SqlFunc
                            .Subqueryable<WareHouseOrderDetails>()
                            .Where(d => d.OrderGUID == o.OrderGUID)
                            .Sum(d => (d.AllocQuantity ?? 0) * (d.ImportPrice ?? 0)),
                        //发货 预计销售sales
                        OEMTotalAmount = SqlFunc
                            .Subqueryable<WareHouseOrderDetails>()
                            .Where(d => d.OrderGUID == o.OrderGUID)
                            .Sum(d => (d.AllocQuantity ?? 0) * (d.OEMAmount ?? 0)),

                        // ImportTotalAmount -> 发货金额 (Alloc Qty * OEMPrice)
                        ImportTotalAmount = SqlFunc
                            .Subqueryable<WareHouseOrderDetails>()
                            .Where(d => d.OrderGUID == o.OrderGUID)
                            .Sum(d => (d.AllocQuantity ?? 0) * (d.ImportPrice ?? 0)),

                        // TotalOrderAmount -> 订货金额 (Order Qty * OEMPrice)
                        TotalOrderAmount = SqlFunc
                            .Subqueryable<WareHouseOrderDetails>()
                            .Where(d => d.OrderGUID == o.OrderGUID)
                            .Sum(d => (d.Quantity ?? 0) * (d.ImportPrice ?? 0)),
                        //订货数量
                        TotalQuantity = (int)(
                            SqlFunc
                                .Subqueryable<WareHouseOrderDetails>()
                                .Where(d => d.OrderGUID == o.OrderGUID)
                                .Sum(d => d.Quantity)
                            ?? 0
                        ),

                        // TotalAllocQuantity -> 发货数量 (Alloc Qty)
                        TotalAllocQuantity = (int)(
                            SqlFunc
                                .Subqueryable<WareHouseOrderDetails>()
                                .Where(d => d.OrderGUID == o.OrderGUID)
                                .Sum(d => d.AllocQuantity)
                            ?? 0
                        ),

                        Remarks = o.Remarks,
                    })
                    .ToListAsync();

                // 内存排序兜底，解决 SQL Server 分页后外层无 Order By 导致的乱序问题
                if (items.Any())
                {
                    switch (sortBy)
                    {
                        case "orderno":
                            items =
                                orderType == OrderByType.Desc
                                    ? items
                                        .OrderByDescending(x => x.OrderNo)
                                        .ThenByDescending(x => x.OrderGUID)
                                        .ToList()
                                    : items
                                        .OrderBy(x => x.OrderNo)
                                        .ThenBy(x => x.OrderGUID)
                                        .ToList();
                            break;
                        case "orderdate":
                            items =
                                orderType == OrderByType.Desc
                                    ? items
                                        .OrderByDescending(x => x.OrderDate)
                                        .ThenByDescending(x => x.OrderNo)
                                        .ThenByDescending(x => x.OrderGUID)
                                        .ToList()
                                    : items
                                        .OrderBy(x => x.OrderDate)
                                        .ThenBy(x => x.OrderNo)
                                        .ThenBy(x => x.OrderGUID)
                                        .ToList();
                            break;
                        case "storecode":
                            items =
                                orderType == OrderByType.Desc
                                    ? items
                                        .OrderByDescending(x => x.StoreCode)
                                        .ThenByDescending(x => x.OrderGUID)
                                        .ToList()
                                    : items
                                        .OrderBy(x => x.StoreCode)
                                        .ThenBy(x => x.OrderGUID)
                                        .ToList();
                            break;
                        case "flowstatus":
                            items =
                                orderType == OrderByType.Desc
                                    ? items
                                        .OrderByDescending(x => x.FlowStatus)
                                        .ThenByDescending(x => x.OrderGUID)
                                        .ToList()
                                    : items
                                        .OrderBy(x => x.FlowStatus)
                                        .ThenBy(x => x.OrderGUID)
                                        .ToList();
                            break;
                        case "totalamount":
                            items =
                                orderType == OrderByType.Desc
                                    ? items
                                        .OrderByDescending(x => x.TotalAmount)
                                        .ThenByDescending(x => x.OrderGUID)
                                        .ToList()
                                    : items
                                        .OrderBy(x => x.TotalAmount)
                                        .ThenBy(x => x.OrderGUID)
                                        .ToList();
                            break;
                        case "oemtotalamount":
                            items =
                                orderType == OrderByType.Desc
                                    ? items
                                        .OrderByDescending(x => x.OEMTotalAmount)
                                        .ThenByDescending(x => x.OrderGUID)
                                        .ToList()
                                    : items
                                        .OrderBy(x => x.OEMTotalAmount)
                                        .ThenBy(x => x.OrderGUID)
                                        .ToList();
                            break;
                        case "importtotalamount":
                            items =
                                orderType == OrderByType.Desc
                                    ? items
                                        .OrderByDescending(x => x.ImportTotalAmount)
                                        .ThenByDescending(x => x.OrderGUID)
                                        .ToList()
                                    : items
                                        .OrderBy(x => x.ImportTotalAmount)
                                        .ThenBy(x => x.OrderGUID)
                                        .ToList();
                            break;
                        default:
                            items = items
                                .OrderByDescending(x => x.OrderDate)
                                .ThenByDescending(x => x.OrderNo)
                                .ThenByDescending(x => x.OrderGUID)
                                .ToList();
                            break;
                    }
                }

                return new PagedListReactDto<StoreOrderListItemDto>
                {
                    Items = items,
                    Total = total,
                    PageNumber = filter.PageNumber,
                    PageSize = filter.PageSize,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetOrderListAsync failed");
                throw;
            }
        }

        public async Task<ApiResponse<StoreOrderCartDto?>> GetOrderDetailAsync(string orderGuid)
        {
            var order = await _db.Queryable<WareHouseOrder>()
                .InnerJoin<Store>((o, s) => o.StoreCode == s.StoreCode)
                .Where(o => o.OrderGUID == orderGuid && !o.IsDeleted)
                .Select((o, s) => new { Order = o, StoreAddress = s.Address })
                .FirstAsync();

            if (order == null)
            {
                return new ApiResponse<StoreOrderCartDto?>
                {
                    Success = false,
                    Message = "Order not found",
                };
            }

            // 1. 获取基本明细
            var baseDetails = await _db.Queryable<WareHouseOrderDetails>()
                .LeftJoin<Product>((d, p) => d.ProductCode == p.ProductCode)
                .LeftJoin<WarehouseProduct>((d, p, wp) => d.ProductCode == wp.ProductCode)
                .LeftJoin<DomesticProduct>((d, p, wp, dp) => wp.ProductCode == dp.ProductCode)
                .Where(d => d.OrderGUID == order.Order.OrderGUID)
                .Select(
                    (d, p, wp, dp) =>
                        new StoreOrderCartItemDto
                        {
                            DetailGUID = d.DetailGUID,
                            ProductCode = d.ProductCode ?? string.Empty,
                            ItemNumber = p.ItemNumber,
                            Barcode = p.Barcode,
                            ProductName = p.ProductName,
                            ProductImage = p.ProductImage,
                            Price = d.OEMPrice ?? 0,
                            Quantity = d.Quantity ?? 0,
                            AllocQuantity = d.AllocQuantity,
                            Amount = d.OEMAmount ?? 0,
                            ImportPrice = d.ImportPrice ?? (wp.ImportPrice ?? 0),
                            ImportAmount =
                                d.ImportAmount
                                ?? (
                                    (d.ImportPrice ?? (wp.ImportPrice ?? 0))
                                    * (d.AllocQuantity ?? d.Quantity ?? 0)
                                ),
                            Volume =
                                (dp.PackingQuantity > 0)
                                    ? (dp.UnitVolume / dp.PackingQuantity)
                                    : dp.UnitVolume,
                            MinOrderQuantity = wp.MinOrderQuantity ?? 1,
                            IsActive = p.IsActive,
                            RRP = p.RetailPrice,
                        }
                )
                .ToListAsync();

            // 2. 批量获取 LocationCode (LocationType = 1)
            var productCodes = baseDetails.Select(x => x.ProductCode).Distinct().ToList();
            if (productCodes.Any())
            {
                var locations = await _db.Queryable<ProductLocation>()
                    .InnerJoin<Location>((pl, l) => pl.LocationGuid == l.LocationGuid)
                    .Where(
                        (pl, l) =>
                            pl.ProductCode != null
                            && productCodes.Contains(pl.ProductCode)
                            && l.LocationType == 1
                    )
                    .Select((pl, l) => new { pl.ProductCode, l.LocationCode })
                    .ToListAsync();

                foreach (var item in baseDetails)
                {
                    var locs = locations
                        .Where(x => x.ProductCode == item.ProductCode)
                        .Select(x => x.LocationCode)
                        .Distinct();
                    item.LocationCode = string.Join(", ", locs);
                }
            }

            foreach (var item in baseDetails)
            {
                if (item.Volume.HasValue)
                {
                    item.TotalVolume = item.Volume * item.Quantity;
                }
            }

            var dto = new StoreOrderCartDto
            {
                OrderGUID = order.Order.OrderGUID,
                OrderNo = order.Order.OrderNo,
                StoreCode = order.Order.StoreCode,
                OrderDate = order.Order.OrderDate,
                TotalAmount = order.Order.OEMTotalAmount ?? 0,
                TotalQuantity = (int)baseDetails.Sum(x => x.Quantity),
                TotalAllocQuantity = (int)baseDetails.Sum(x => x.AllocQuantity ?? 0),
                TotalSKU = baseDetails.Select(x => x.ProductCode).Distinct().Count(),
                TotalImportAmount = baseDetails.Sum(x => x.ImportAmount),
                TotalVolume = baseDetails.Sum(x => x.TotalVolume ?? 0),
                Remarks = order.Order.Remarks,
                StoreAddress = order.StoreAddress,
                ShippingFee = order.Order.ShippingFee,
                FlowStatus = order.Order.FlowStatus,
                Items = baseDetails,
            };

            return new ApiResponse<StoreOrderCartDto?> { Success = true, Data = dto };
        }

        public async Task<ApiResponse<List<BranchDto>>> GetUsedBranchesAsync()
        {
            try
            {
                // 1. 从订单表获取所有使用过的分店代码 (distinct)
                var usedStoreCodes = await _db.Queryable<WareHouseOrder>()
                    .Where(o => !o.IsDeleted && !string.IsNullOrEmpty(o.StoreCode))
                    .Select(o => o.StoreCode)
                    .Distinct()
                    .ToListAsync();

                if (!usedStoreCodes.Any())
                {
                    return new ApiResponse<List<BranchDto>>
                    {
                        Success = true,
                        Data = new List<BranchDto>(),
                    };
                }

                // 2. 根据分店代码批量查询分店表获取详细信息
                var branches = await _db.Queryable<Store>()
                    .Where(s => usedStoreCodes.Contains(s.StoreCode))
                    .Select(s => new
                    {
                        s.StoreGUID,
                        s.StoreCode,
                        s.StoreName,
                    })
                    .ToListAsync();

                // 3. 构建结果列表
                var result = new List<BranchDto>();

                foreach (var code in usedStoreCodes)
                {
                    var branch = branches.FirstOrDefault(b => b.StoreCode == code);

                    if (branch != null)
                    {
                        // 分店表中找到的记录
                        result.Add(
                            new BranchDto
                            {
                                Guid = branch.StoreGUID,
                                Code = branch.StoreCode,
                                Name = branch.StoreName,
                            }
                        );
                    }
                    // 不返回找不到的记录，只记录警告日志
                    else
                    {
                        _logger.LogWarning("分店代码 '{Code}' 在订单中存在但分店表中未找到", code);
                    }
                }

                // 4. 按分店代码排序
                result = result.OrderBy(b => b.Code).ToList();

                return new ApiResponse<List<BranchDto>> { Success = true, Data = result };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetUsedBranchesAsync failed");
                return new ApiResponse<List<BranchDto>> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<string>> CreateOrderAsync(CreateStoreOrderDto request)
        {
            try
            {
                var now = DateTime.Now;
                var currentUser =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                var order = new WareHouseOrder
                {
                    OrderGUID = UuidHelper.GenerateUuid7(),
                    StoreCode = request.StoreCode,
                    OrderDate = now,
                    FlowStatus = 1, // Submitted
                    IsDeleted = false,
                    CreatedAt = now,
                    UpdatedAt = now,
                    UpdatedBy = currentUser,
                    OEMTotalAmount = 0,
                    ImportTotalAmount = 0,
                    ShippingFee = 0,
                    OrderNo = await _orderNumberGenerator.GetNextOrderNoAsync(),
                    Remarks = request.Remarks,
                };

                await _db.Insertable(order).ExecuteCommandAsync();
                return new ApiResponse<string> { Success = true, Data = order.OrderGUID };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateOrderAsync failed");
                return new ApiResponse<string> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> AddOrderLineAsync(AddOrderLineDto request)
        {
            try
            {
                var order = await GetEditableOrderAsync(request.OrderGUID);
                if (order == null)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Order not found or not editable",
                    };
                }

                await AddOrUpdateDetailAsync(
                    order,
                    request.ProductCode,
                    request.Quantity,
                    null, // importPrice
                    isUpdate: false
                );
                await UpdateOrderTotalAsync(order.OrderGUID);

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddOrderLineAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> BatchAddOrderLineAsync(BatchAddOrderLineDto request)
        {
            try
            {
                var order = await GetEditableOrderAsync(request.OrderGUID);
                if (order == null)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Order not found or not editable",
                    };
                }

                foreach (var item in request.Items)
                {
                    await AddOrUpdateDetailAsync(
                        order,
                        item.ProductCode,
                        item.Quantity,
                        item.ImportPrice, // importPrice
                        isUpdate: false
                    );
                }

                await UpdateOrderTotalAsync(order.OrderGUID);

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchAddOrderLineAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> UpdateOrderLineAsync(UpdateOrderLineDto request)
        {
            try
            {
                var order = await GetEditableOrderAsync(request.OrderGUID);
                if (order == null)
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Order not found or not editable",
                    };

                await AddOrUpdateDetailAsync(
                    order,
                    request.ProductCode,
                    request.Quantity,
                    request.ImportPrice,
                    isUpdate: true
                );
                await UpdateOrderTotalAsync(order.OrderGUID);

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateOrderLineAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> BatchUpdateOrderLineAsync(
            BatchUpdateOrderLineDto request
        )
        {
            try
            {
                var order = await GetEditableOrderAsync(request.OrderGUID);
                if (order == null)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Order not found or not editable",
                    };
                }

                foreach (var item in request.Items)
                {
                    await AddOrUpdateDetailAsync(
                        order,
                        item.ProductCode,
                        item.Quantity ?? 0, // 如果未传Quantity，在AddOrUpdateDetailAsync内部需要处理（或者不允许单独更新价格而不传数量？）
                        // 注意：AddOrUpdateDetailAsync 目前设计是接收 Quantity。如果是只更新价格，需要重构。
                        // 重构 AddOrUpdateDetailAsync 以支持可选参数。
                        item.ImportPrice,
                        isUpdate: true,
                        isBatch: true, // 标记为批量操作
                        originalQuantity: item.Quantity // 传递原始数量，如果是null则表示不更新数量
                    );
                }

                await UpdateOrderTotalAsync(order.OrderGUID);
                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchUpdateOrderLineAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> UpdateOrderHeaderAsync(UpdateOrderHeaderDto request)
        {
            try
            {
                var order = await GetEditableOrderAsync(request.OrderGuid);
                if (order == null)
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Order not found or not editable",
                    };

                order.Remarks = request.Remarks;
                order.ShippingFee = request.ShippingFee;
                if (request.OrderDate.HasValue)
                {
                    order.OrderDate = request.OrderDate.Value;
                }

                // 处理 StoreCode 更新
                if (
                    !string.IsNullOrEmpty(request.StoreCode)
                    && order.StoreCode != request.StoreCode
                )
                {
                    order.StoreCode = request.StoreCode;

                    // 更新明细中的 StoreCode
                    await _db.Updateable<WareHouseOrderDetails>()
                        .SetColumns(d => d.StoreCode == request.StoreCode)
                        .Where(d => d.OrderGUID == request.OrderGuid)
                        .ExecuteCommandAsync();
                }

                order.UpdatedAt = DateTime.Now;
                order.UpdatedBy =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                await _db.Updateable(order)
                    .UpdateColumns(o => new
                    {
                        o.Remarks,
                        o.ShippingFee,
                        o.OrderDate,
                        o.StoreCode,
                        o.UpdatedAt,
                        o.UpdatedBy,
                    })
                    .ExecuteCommandAsync();

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateOrderHeaderAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> DeleteOrderAsync(string orderGuid)
        {
            try
            {
                var currentUser =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";
                var order = await _db.Queryable<WareHouseOrder>()
                    .Where(o => o.OrderGUID == orderGuid && !o.IsDeleted)
                    .FirstAsync();

                if (order == null)
                {
                    return new ApiResponse<bool> { Success = false, Message = "Order not found" };
                }

                // 软删除主表
                order.IsDeleted = true;
                order.UpdatedBy = currentUser;
                order.UpdatedAt = DateTime.Now;

                // 开启事务，同时软删除明细
                try
                {
                    _db.Ado.BeginTran();

                    await _db.Updateable(order).ExecuteCommandAsync();

                    await _db.Updateable<WareHouseOrderDetails>()
                        .SetColumns(d => new WareHouseOrderDetails
                        {
                            IsDeleted = true,
                            UpdatedBy = currentUser,
                            UpdatedAt = DateTime.Now,
                        })
                        .Where(d => d.OrderGUID == orderGuid)
                        .ExecuteCommandAsync();

                    _db.Ado.CommitTran();
                    return new ApiResponse<bool> { Success = true, Data = true };
                }
                catch (Exception)
                {
                    _db.Ado.RollbackTran();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteOrderAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> UpdateProductStatusAsync(
            UpdateProductStatusDto request
        )
        {
            try
            {
                var now = DateTime.Now;
                var currentUser =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                // 更新 Product 表
                var result = await _db.Updateable<Product>()
                    .SetColumns(p => new Product
                    {
                        IsActive = request.IsActive,
                        UpdatedAt = now,
                        UpdatedBy = currentUser,
                    })
                    .Where(p => p.ProductCode == request.ProductCode)
                    .ExecuteCommandAsync();

                if (result > 0)
                {
                    return new ApiResponse<bool> { Success = true, Data = true };
                }
                return new ApiResponse<bool> { Success = false, Message = "Product not found" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateProductStatusAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> BatchUpdateProductStatusAsync(
            BatchUpdateProductStatusDto request
        )
        {
            try
            {
                var now = DateTime.Now;
                var currentUser =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                if (request.ProductCodes == null || !request.ProductCodes.Any())
                {
                    return new ApiResponse<bool> { Success = true, Data = true };
                }

                // 批量更新 Product 表
                await _db.Updateable<Product>()
                    .SetColumns(p => new Product
                    {
                        IsActive = request.IsActive,
                        UpdatedAt = now,
                        UpdatedBy = currentUser,
                    })
                    .Where(p =>
                        p.ProductCode != null && request.ProductCodes.Contains(p.ProductCode)
                    )
                    .ExecuteCommandAsync();

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchUpdateProductStatusAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<CopyOrderResultDto>> CopyOrderAsync(CopyOrderDto request)
        {
            try
            {
                var sourceOrder = await _db.Queryable<WareHouseOrder>()
                    .Where(o => o.OrderGUID == request.SourceOrderGUID && !o.IsDeleted)
                    .FirstAsync();

                if (sourceOrder == null)
                {
                    return new ApiResponse<CopyOrderResultDto>
                    {
                        Success = false,
                        Message = "Source order not found",
                    };
                }

                var sourceDetails = await _db.Queryable<WareHouseOrderDetails>()
                    .Where(d => d.OrderGUID == request.SourceOrderGUID && !d.IsDeleted)
                    .ToListAsync();

                if (sourceDetails == null || !sourceDetails.Any())
                {
                    return new ApiResponse<CopyOrderResultDto>
                    {
                        Success = false,
                        Message = "Source order has no items",
                    };
                }

                var now = DateTime.Now;
                var currentUser =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                var newOrder = new WareHouseOrder
                {
                    OrderGUID = UuidHelper.GenerateUuid7(),
                    StoreCode = request.TargetStoreCode,
                    OrderDate = now,
                    FlowStatus = 1,
                    IsDeleted = false,
                    CreatedAt = now,
                    UpdatedAt = now,
                    UpdatedBy = currentUser,
                    OEMTotalAmount = 0,
                    ImportTotalAmount = 0,
                    ShippingFee = 0,
                    OrderNo = await _orderNumberGenerator.GetNextOrderNoAsync(),
                    Remarks = $"Copied from {sourceOrder.OrderNo}",
                };

                var newDetails = new List<WareHouseOrderDetails>();
                foreach (var srcDetail in sourceDetails)
                {
                    var newDetail = new WareHouseOrderDetails
                    {
                        DetailGUID = UuidHelper.GenerateUuid7(),
                        OrderGUID = newOrder.OrderGUID,
                        StoreCode = request.TargetStoreCode,
                        ProductCode = srcDetail.ProductCode,
                        Quantity = request.CopyOrderQuantity ? srcDetail.Quantity : 0,
                        OEMPrice = srcDetail.OEMPrice,
                        OEMAmount = 0,
                        AllocQuantity = request.CopyAllocQuantity ? srcDetail.AllocQuantity : 0,
                        ImportPrice = srcDetail.ImportPrice,
                        ImportAmount = 0,
                        IsDeleted = false,
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = currentUser,
                        UpdatedBy = currentUser,
                    };

                    newDetail.OEMAmount = newDetail.AllocQuantity * newDetail.OEMPrice;
                    newDetail.ImportAmount = newDetail.AllocQuantity * newDetail.ImportPrice;

                    newDetails.Add(newDetail);
                }

                newOrder.OEMTotalAmount = newDetails.Sum(d => d.OEMAmount);
                newOrder.ImportTotalAmount = newDetails.Sum(d => d.ImportAmount);

                try
                {
                    _db.Ado.BeginTran();

                    await _db.Insertable(newOrder).ExecuteCommandAsync();
                    await _db.Insertable(newDetails).ExecuteCommandAsync();

                    _db.Ado.CommitTran();
                }
                catch (Exception)
                {
                    _db.Ado.RollbackTran();
                    throw;
                }

                return new ApiResponse<CopyOrderResultDto>
                {
                    Success = true,
                    Data = new CopyOrderResultDto
                    {
                        OrderGUID = newOrder.OrderGUID,
                        OrderNo = newOrder.OrderNo,
                    },
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CopyOrderAsync failed");
                return new ApiResponse<CopyOrderResultDto>
                {
                    Success = false,
                    Message = ex.Message,
                };
            }
        }

        private async Task<WareHouseOrder?> GetEditableOrderAsync(string orderGuid)
        {
            var order = await _db.Queryable<WareHouseOrder>()
                .Where(o => o.OrderGUID == orderGuid && !o.IsDeleted)
                .FirstAsync();

            // 允许编辑 FlowStatus 0 (购物车) 和 1 (已提交)
            if (order != null && (order.FlowStatus == 0 || order.FlowStatus == 1))
            {
                return order;
            }
            return null;
        }

        private async Task AddOrUpdateDetailAsync(
            WareHouseOrder order,
            string productCode,
            decimal quantity,
            decimal? importPrice, // 新增参数
            bool isUpdate,
            bool isBatch = false, // 新增参数
            decimal? originalQuantity = null // 新增参数
        )
        {
            var now = DateTime.Now;
            var currentUser = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

            var warehouseProduct = await _db.Queryable<WarehouseProduct>()
                .Where(wp => wp.ProductCode == productCode)
                .FirstAsync();

            if (warehouseProduct == null)
            {
                // 如果找不到 WarehouseProduct，尝试找 Product
                var prod = await _db.Queryable<Product>()
                    .Where(p => p.ProductCode == productCode)
                    .FirstAsync();
                if (prod == null)
                    throw new Exception($"Product {productCode} not found");
                // 如果没有 WarehouseProduct 数据，使用默认值
                warehouseProduct = new WarehouseProduct
                {
                    OEMPrice = 0,
                    ImportPrice = 0,
                    MinOrderQuantity = 1,
                };
            }

            // 检查最小起订量
            var minQty = warehouseProduct.MinOrderQuantity ?? 1;
            if (minQty < 1)
                minQty = 1;

            if (isUpdate)
            {
                // 如果是批量操作且未提供数量，跳过数量检查
                if (isBatch && originalQuantity == null)
                {
                    // Do nothing for quantity check
                }
                else if (quantity < 0)
                {
                    throw new Exception($"商品数量 {quantity}");
                }
            }
            else
            {
                // 添加模式（累加）
                // ... (原有逻辑保持不变，但要注意 importPrice)
            }

            decimal price = warehouseProduct.OEMPrice ?? 0;
            // 优先使用传入的 importPrice，否则使用 warehouseProduct 的
            decimal finalImportPrice = importPrice ?? warehouseProduct.ImportPrice ?? 0;

            var detail = await _db.Queryable<WareHouseOrderDetails>()
                .Where(d => d.OrderGUID == order.OrderGUID && d.ProductCode == productCode)
                .FirstAsync();

            if (detail == null)
            {
                // 新增
                detail = new WareHouseOrderDetails
                {
                    DetailGUID = UuidHelper.GenerateUuid7(),
                    OrderGUID = order.OrderGUID,
                    StoreCode = order.StoreCode,
                    ProductCode = productCode,
                    Quantity = 0,
                    OEMPrice = price,
                    OEMAmount = price * minQty,
                    AllocQuantity = minQty,
                    ImportPrice = finalImportPrice,
                    ImportAmount = finalImportPrice * minQty,
                    IsDeleted = false,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedBy = currentUser,
                    UpdatedBy = currentUser,
                };
                await _db.Insertable(detail).ExecuteCommandAsync();
            }
            else
            {
                // 更新
                if (isUpdate)
                {
                    // 如果传入了数量 (对于批量操作，originalQuantity 不为 null)
                    if (!isBatch || originalQuantity != null)
                    {
                        detail.AllocQuantity = quantity;
                    }

                    // 如果传入了 importPrice，更新它
                    if (importPrice.HasValue)
                    {
                        detail.ImportPrice = importPrice.Value;
                    }
                }
                else
                {
                    detail.AllocQuantity += minQty;
                }

                if (detail.AllocQuantity <= 0 && detail.Quantity <= 0)
                { //订货数量和发货数量都为空 可以删除
                    await _db.Deleteable(detail).ExecuteCommandAsync();
                }
                else
                {
                    detail.OEMAmount = detail.AllocQuantity * detail.OEMPrice;
                    // 使用最新的 ImportPrice 计算
                    detail.ImportAmount = detail.AllocQuantity * detail.ImportPrice;
                    detail.UpdatedAt = now;
                    detail.UpdatedBy = currentUser;
                    await _db.Updateable(detail).ExecuteCommandAsync();
                }
            }
        }

        public async Task<SyncMissingOrdersResultDto> SyncMissingOrdersFromHqAsync(
            string? storeCode
        )
        {
            var result = new SyncMissingOrdersResultDto { Success = true, Message = string.Empty };

            try
            {
                var existingOrders = await _db.Queryable<WareHouseOrder>()
                    .WhereIF(!string.IsNullOrEmpty(storeCode), x => x.StoreCode == storeCode)
                    .Where(x => !x.IsDeleted)
                    .Select(x => new { x.OrderGUID, x.UpdatedAt })
                    .ToListAsync();

                var existingOrderGuids = existingOrders.Select(x => x.OrderGUID).ToList();
                var localUpdatedAtMap = existingOrders
                    .Where(x => x.UpdatedAt.HasValue)
                    .ToDictionary(x => x.OrderGUID, x => x.UpdatedAt!.Value);

                _logger.LogInformation(
                    "本地已存在订单数量: {Count}, 分店代码: {StoreCode}",
                    existingOrderGuids.Count,
                    storeCode ?? "全部"
                );

                using var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                var allHqOrders = await hqDb.Queryable<CBP_RED_分店订货单主表Store>()
                    .Where(x => SqlFunc.HasValue(x.HGUID) && x.流程状态 == 0)
                    .WhereIF(!string.IsNullOrEmpty(storeCode), x => x.分店代码 == storeCode)
                    .ToListAsync();

                if (!allHqOrders.Any())
                {
                    result.Message = "没有需要同步的订单";
                    return result;
                }

                var missingHqOrders = allHqOrders
                    .Where(x => !existingOrderGuids.Contains(x.HGUID!))
                    .ToList();

                var updatedHqOrders = allHqOrders
                    .Where(x => existingOrderGuids.Contains(x.HGUID!))
                    .Where(x =>
                    {
                        if (!localUpdatedAtMap.TryGetValue(x.HGUID!, out var localUpdated))
                            return true;
                        var hqUpdated = x.FGC_LastModifyDate;
                        if (!hqUpdated.HasValue)
                            return false;
                        return hqUpdated.Value > localUpdated;
                    })
                    .ToList();

                _logger.LogInformation(
                    "HQ 订单总数: {Total}, 新增: {New}, 更新: {Updated}",
                    allHqOrders.Count,
                    missingHqOrders.Count,
                    updatedHqOrders.Count
                );

                if (missingHqOrders.Any())
                {
                    var newOrders = new List<WareHouseOrder>();
                    var newOrderGuids = new List<string>();

                    foreach (var hqOrder in missingHqOrders)
                    {
                        var localOrder = _mapper.Map<WareHouseOrder>(hqOrder);
                        localOrder.IsDeleted = false;
                        localOrder.CreatedAt = hqOrder.FGC_CreateDate ?? DateTime.Now;
                        localOrder.UpdatedAt = hqOrder.FGC_LastModifyDate ?? DateTime.Now;
                        localOrder.CreatedBy = hqOrder.FGC_Creator ?? "HQ同步";
                        localOrder.UpdatedBy = hqOrder.FGC_LastModifier ?? "HQ同步";
                        newOrders.Add(localOrder);
                        newOrderGuids.Add(localOrder.OrderGUID);
                    }

                    await _db.Insertable(newOrders).ExecuteCommandAsync();
                    result.OrdersSynced = newOrders.Count;

                    _logger.LogInformation("新增订单主表数量: {Count}", result.OrdersSynced);

                    var hqDetails = await hqDb.Queryable<CBP_RED_分店订单详情表Store>()
                        .Where(x => SqlFunc.HasValue(x.HGUID) && SqlFunc.HasValue(x.主表GUID))
                        .WhereIF(newOrderGuids.Any(), x => newOrderGuids.Contains(x.主表GUID!))
                        .ToListAsync();

                    if (hqDetails.Any())
                    {
                        var newDetails = new List<WareHouseOrderDetails>();
                        foreach (var hqDetail in hqDetails)
                        {
                            var localDetail = _mapper.Map<WareHouseOrderDetails>(hqDetail);
                            localDetail.IsDeleted = false;
                            localDetail.CreatedAt = hqDetail.FGC_CreateDate ?? DateTime.Now;
                            localDetail.UpdatedAt = hqDetail.FGC_LastModifyDate ?? DateTime.Now;
                            localDetail.CreatedBy = hqDetail.FGC_Creator ?? "HQ同步";
                            localDetail.UpdatedBy = hqDetail.FGC_LastModifier ?? "HQ同步";
                            newDetails.Add(localDetail);
                        }
                        await _db.Insertable(newDetails).ExecuteCommandAsync();
                        result.DetailsSynced = newDetails.Count;
                    }
                }

                if (updatedHqOrders.Any())
                {
                    var updatedOrderGuids = updatedHqOrders.Select(x => x.HGUID!).ToList();

                    foreach (var hqOrder in updatedHqOrders)
                    {
                        var mapped = _mapper.Map<WareHouseOrder>(hqOrder);
                        mapped.UpdatedAt = hqOrder.FGC_LastModifyDate ?? DateTime.Now;
                        mapped.UpdatedBy = hqOrder.FGC_LastModifier ?? "HQ同步";

                        await _db.Updateable<WareHouseOrder>()
                            .SetColumns(o => new WareHouseOrder
                            {
                                StoreCode = mapped.StoreCode,
                                OrderNo = mapped.OrderNo,
                                OrderDate = mapped.OrderDate,
                                OutboundDate = mapped.OutboundDate,
                                ShippingFee = mapped.ShippingFee,
                                ImportTotalAmount = mapped.ImportTotalAmount,
                                OEMTotalAmount = mapped.OEMTotalAmount,
                                Remarks = mapped.Remarks,
                                FlowStatus = mapped.FlowStatus,
                                InboundStatus = mapped.InboundStatus,
                                UpdatedAt = mapped.UpdatedAt,
                                UpdatedBy = mapped.UpdatedBy,
                            })
                            .Where(o => o.OrderGUID == hqOrder.HGUID)
                            .ExecuteCommandAsync();
                    }
                    result.OrdersUpdated = updatedHqOrders.Count;

                    _logger.LogInformation("更新订单主表数量: {Count}", result.OrdersUpdated);

                    var hqUpdatedDetails = await hqDb.Queryable<CBP_RED_分店订单详情表Store>()
                        .Where(x => SqlFunc.HasValue(x.HGUID) && SqlFunc.HasValue(x.主表GUID))
                        .WhereIF(
                            updatedOrderGuids.Any(),
                            x => updatedOrderGuids.Contains(x.主表GUID!)
                        )
                        .ToListAsync();

                    if (hqUpdatedDetails.Any())
                    {
                        var existingDetailGuids = await _db.Queryable<WareHouseOrderDetails>()
                            .Where(d => updatedOrderGuids.Contains(d.OrderGUID))
                            .Select(d => d.DetailGUID)
                            .ToListAsync();

                        var existingDetailSet = new HashSet<string>(existingDetailGuids);

                        var detailsToInsert = new List<WareHouseOrderDetails>();
                        var detailsToUpdate = new List<WareHouseOrderDetails>();

                        foreach (var hqDetail in hqUpdatedDetails)
                        {
                            var localDetail = _mapper.Map<WareHouseOrderDetails>(hqDetail);
                            localDetail.IsDeleted = false;
                            localDetail.UpdatedAt = hqDetail.FGC_LastModifyDate ?? DateTime.Now;
                            localDetail.UpdatedBy = hqDetail.FGC_LastModifier ?? "HQ同步";

                            if (existingDetailSet.Contains(localDetail.DetailGUID))
                            {
                                localDetail.CreatedAt = hqDetail.FGC_CreateDate ?? DateTime.Now;
                                localDetail.CreatedBy = hqDetail.FGC_Creator ?? "HQ同步";
                                detailsToUpdate.Add(localDetail);
                            }
                            else
                            {
                                localDetail.CreatedAt = hqDetail.FGC_CreateDate ?? DateTime.Now;
                                localDetail.CreatedBy = hqDetail.FGC_Creator ?? "HQ同步";
                                detailsToInsert.Add(localDetail);
                            }
                        }

                        if (detailsToInsert.Any())
                        {
                            await _db.Insertable(detailsToInsert).ExecuteCommandAsync();
                        }

                        foreach (var detail in detailsToUpdate)
                        {
                            await _db.Updateable<WareHouseOrderDetails>()
                                .SetColumns(d => new WareHouseOrderDetails
                                {
                                    StoreCode = detail.StoreCode,
                                    ProductCode = detail.ProductCode,
                                    Quantity = detail.Quantity,
                                    OEMPrice = detail.OEMPrice,
                                    OEMAmount = detail.OEMAmount,
                                    AllocQuantity = detail.AllocQuantity,
                                    ImportPrice = detail.ImportPrice,
                                    ImportAmount = detail.ImportAmount,
                                    UpdatedAt = detail.UpdatedAt,
                                    UpdatedBy = detail.UpdatedBy,
                                })
                                .Where(d => d.DetailGUID == detail.DetailGUID)
                                .ExecuteCommandAsync();
                        }

                        result.DetailsUpdated = detailsToInsert.Count + detailsToUpdate.Count;
                    }
                }

                var hasChanges =
                    result.OrdersSynced > 0
                    || result.DetailsSynced > 0
                    || result.OrdersUpdated > 0
                    || result.DetailsUpdated > 0;

                if (hasChanges)
                {
                    result.Message =
                        $"同步成功：新增订单 {result.OrdersSynced} 条、详情 {result.DetailsSynced} 条；"
                        + $"更新订单 {result.OrdersUpdated} 条、详情 {result.DetailsUpdated} 条";
                }
                else
                {
                    result.Message = "所有订单已是最新，无需同步";
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"同步失败：{ex.Message}";
                _logger.LogError(ex, "同步缺失订单失败，分店代码：{StoreCode}", storeCode);
                return result;
            }
        }

        public async Task<ApiResponse<bool>> CompleteOrderAsync(string orderGuid)
        {
            try
            {
                var order = await _db.Queryable<WareHouseOrder>()
                    .Where(o => o.OrderGUID == orderGuid && !o.IsDeleted)
                    .FirstAsync();

                if (order == null)
                {
                    return new ApiResponse<bool> { Success = false, Message = "订单不存在" };
                }

                if (order.FlowStatus != 1)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "只有已提交状态的订单才能标记为完成",
                    };
                }

                order.FlowStatus = 2;
                order.UpdatedAt = DateTime.Now;
                order.UpdatedBy =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                await _db.Updateable(order)
                    .UpdateColumns(o => new
                    {
                        o.FlowStatus,
                        o.UpdatedAt,
                        o.UpdatedBy,
                    })
                    .ExecuteCommandAsync();

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CompleteOrderAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> StartPickingAsync(string orderGuid)
        {
            try
            {
                var order = await _db.Queryable<WareHouseOrder>()
                    .Where(o => o.OrderGUID == orderGuid && !o.IsDeleted)
                    .FirstAsync();

                if (order == null)
                {
                    return new ApiResponse<bool> { Success = false, Message = "订单不存在" };
                }

                if (order.FlowStatus == 2 || order.FlowStatus == 3)
                {
                    return new ApiResponse<bool> { Success = true, Data = true };
                }

                if (order.FlowStatus != 1)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "只有已提交状态的订单才能开始配货",
                    };
                }

                order.FlowStatus = 3;
                order.UpdatedAt = DateTime.Now;
                order.UpdatedBy =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                await _db.Updateable(order)
                    .UpdateColumns(o => new
                    {
                        o.FlowStatus,
                        o.UpdatedAt,
                        o.UpdatedBy,
                    })
                    .ExecuteCommandAsync();

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StartPickingAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }
    }
}
