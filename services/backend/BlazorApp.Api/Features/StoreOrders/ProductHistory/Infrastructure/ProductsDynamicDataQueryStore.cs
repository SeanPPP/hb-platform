using System.Diagnostics;
using System.Security.Claims;
using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.ProductHistory.Domain;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.ProductHistory.Infrastructure;

internal sealed class ProductsDynamicDataQueryStore(
    SqlSugarContext context,
    IStoreOrderActorContext actorContext,
    ProductSalesHistoryQueryStore salesHistoryQueryStore,
    ILogger<ProductsDynamicDataQueryStore> logger
)
{
    private readonly ISqlSugarClient _db = context.Db;

    internal async Task<ProductsDynamicDataReadResult> GetProductsDynamicDataAsync(
        ProductsDynamicDataQueryInput input
    )
    {
        ProductHistorySalesContext? salesContext = null;
        if (input.IncludeSales)
        {
            try
            {
                // 门店不存在或停用会在此短路，后续不会查询来货或销售统计。
                salesContext = await salesHistoryQueryStore.GetActiveStoreSalesContextAsync(
                    input.StoreCode
                );
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "GetProductsDynamicDataAsync check store active failed; skip sales-since-last-arrival"
                );
            }
        }

        var cartSw = Stopwatch.StartNew();
        var cartOwnerUserGuid = ResolveActiveCartOwnerUserGuid();
        // 购物车数量保持数据库侧聚合，并严格保留仓库员工独立购物车范围。
        var cartQuery = _db.Queryable<WareHouseOrderDetails>()
            .InnerJoin<WareHouseOrder>((detail, order) => detail.OrderGUID == order.OrderGUID)
            .Where((detail, order) =>
                order.StoreCode == input.StoreCode
                && order.FlowStatus == 0
                && !order.IsDeleted
                && !detail.IsDeleted
            )
            .Where((detail, order) =>
                detail.ProductCode != null
                && input.ProductCodes.Contains(detail.ProductCode)
            );
        cartQuery = string.IsNullOrWhiteSpace(cartOwnerUserGuid)
            ? cartQuery.Where((detail, order) =>
                SqlFunc.IsNullOrEmpty(order.CartOwnerUserGuid)
            )
            : cartQuery.Where((detail, order) =>
                order.CartOwnerUserGuid == cartOwnerUserGuid
            );
        var cartItems = await cartQuery
            .GroupBy((detail, order) => detail.ProductCode)
            .Select((detail, order) => new
            {
                ProductCode = detail.ProductCode,
                CartQuantity = SqlFunc.AggregateSum(detail.Quantity),
            })
            .ToListAsync();
        cartSw.Stop();

        var latestDateSw = Stopwatch.StartNew();
        var latestOrderDates = await _db.Queryable<WareHouseOrderDetails>()
            .InnerJoin<WareHouseOrder>((detail, order) => detail.OrderGUID == order.OrderGUID)
            .Where((detail, order) =>
                order.StoreCode == input.StoreCode
                && order.FlowStatus > 0
                && !order.IsDeleted
                && !detail.IsDeleted
            )
            .Where((detail, order) =>
                detail.ProductCode != null
                && input.ProductCodes.Contains(detail.ProductCode)
            )
            .GroupBy((detail, order) => detail.ProductCode)
            .Select((detail, order) => new
            {
                ProductCode = detail.ProductCode,
                LastOrderDate = SqlFunc.AggregateMax(order.OrderDate),
            })
            .ToListAsync();
        latestDateSw.Stop();

        var historySw = Stopwatch.StartNew();
        var latestDateMap = latestOrderDates
            .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
            .GroupBy(item => item.ProductCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().LastOrderDate,
                StringComparer.OrdinalIgnoreCase
            );
        var historyCandidates = latestDateMap.Count == 0
            ? new List<ProductHistoryDynamicHistoryRow>()
            : await _db.Queryable<WareHouseOrderDetails>()
                .InnerJoin<WareHouseOrder>((detail, order) =>
                    detail.OrderGUID == order.OrderGUID
                )
                .Where((detail, order) =>
                    order.StoreCode == input.StoreCode
                    && order.FlowStatus > 0
                    && !order.IsDeleted
                    && !detail.IsDeleted
                )
                .Where((detail, order) =>
                    detail.ProductCode != null
                    && input.ProductCodes.Contains(detail.ProductCode)
                )
                .Select((detail, order) => new ProductHistoryDynamicHistoryRow
                {
                    ProductCode = detail.ProductCode,
                    OrderGUID = detail.OrderGUID,
                    OrderDate = order.OrderDate,
                    CreatedAt = order.CreatedAt,
                    Quantity = detail.Quantity,
                    AllocQuantity = detail.AllocQuantity,
                })
                .ToListAsync();
        // 同订单同商品先聚合，再按与历史弹窗完全相同的稳定顺序选最新行。
        var historyItems = historyCandidates
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.ProductCode)
                && !string.IsNullOrWhiteSpace(item.OrderGUID)
                && latestDateMap.TryGetValue(item.ProductCode!, out var latestDate)
                && item.OrderDate == latestDate
            )
            .GroupBy(item => new { item.ProductCode, item.OrderGUID })
            .Select(orderGroup => new ProductHistoryDynamicHistoryRow
            {
                ProductCode = orderGroup.Key.ProductCode,
                OrderGUID = orderGroup.Key.OrderGUID,
                OrderDate = orderGroup.First().OrderDate,
                CreatedAt = orderGroup.First().CreatedAt,
                Quantity = orderGroup.Sum(item => item.Quantity ?? 0m),
                AllocQuantity = orderGroup.Sum(item => item.AllocQuantity ?? 0m),
            })
            .GroupBy(item => item.ProductCode!, StringComparer.OrdinalIgnoreCase)
            .Select(productGroup =>
                productGroup
                    .OrderByDescending(item => item.OrderDate)
                    .ThenByDescending(item => item.CreatedAt)
                    .ThenByDescending(item => item.OrderGUID)
                    .First()
            )
            .ToList();
        historySw.Stop();

        var cartQuantityMap = cartItems
            .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
            .GroupBy(item => item.ProductCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item => item.CartQuantity ?? 0m),
                StringComparer.OrdinalIgnoreCase
            );
        var latestHistoryMap = historyItems
            .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
            .GroupBy(item => item.ProductCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase
            );

        var result = new List<StoreOrderDynamicDataDto>();
        foreach (var productCode in input.ProductCodes)
        {
            var item = new StoreOrderDynamicDataDto { ProductCode = productCode };
            if (cartQuantityMap.TryGetValue(productCode, out var cartQuantity))
            {
                item.CartQuantity = cartQuantity;
            }

            if (latestHistoryMap.TryGetValue(productCode, out var historyItem))
            {
                item.LastOrderDate = historyItem.OrderDate;
                item.LastQuantity = historyItem.Quantity;
                item.LastAllocQuantity = historyItem.AllocQuantity;
            }

            result.Add(item);
        }

        var salesSw = Stopwatch.StartNew();
        var salesRows = 0;
        if (input.IncludeSales && salesContext != null)
        {
            try
            {
                var salesQuantityResult =
                    await salesHistoryQueryStore.GetSalesQuantitySinceLastArrivalMapAsync(
                        salesContext.StoreCode,
                        input.ProductCodes,
                        salesContext.EndDate
                    );
                salesRows = salesQuantityResult.SalesQuantityMap.Count;
                foreach (var item in result)
                {
                    item.SalesQuantitySinceLastArrival =
                        salesQuantityResult.SalesQuantityMap.TryGetValue(
                            item.ProductCode,
                            out var salesQuantity
                        )
                            ? salesQuantity
                            : null;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "GetProductsDynamicDataAsync fill sales-since-last-arrival failed; new field remains null"
                );
            }
        }
        salesSw.Stop();

        return new ProductsDynamicDataReadResult(
            result,
            cartItems.Count,
            latestOrderDates.Count,
            historyItems.Count,
            cartSw.ElapsedMilliseconds,
            latestDateSw.ElapsedMilliseconds,
            historySw.ElapsedMilliseconds,
            salesContext != null,
            salesRows,
            salesSw.ElapsedMilliseconds
        );
    }

    private string? ResolveActiveCartOwnerUserGuid()
    {
        var isWarehouseStaff = actorContext.HasRole("WarehouseStaff")
            || actorContext.HasRole("仓库员工");
        var hasSuperAdminRole = Permissions.SuperAdminRoleNames.Any(actorContext.HasRole);
        var hasWarehouseManagerRole = Permissions.WarehouseManagerRoleNames.Any(
            actorContext.HasRole
        );
        if (!isWarehouseStaff || hasSuperAdminRole || hasWarehouseManagerRole)
        {
            return null;
        }

        var user = actorContext.User;
        var userGuid = (
            user?.FindFirst("userId")?.Value
            ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user?.FindFirst("userGuid")?.Value
            ?? user?.FindFirst("userGUID")?.Value
            ?? user?.FindFirst("UserGuid")?.Value
            ?? user?.FindFirst("sub")?.Value
            ?? string.Empty
        ).Trim();
        if (string.IsNullOrWhiteSpace(userGuid))
        {
            throw new InvalidOperationException("无法识别当前仓库员工");
        }

        return userGuid;
    }
}
