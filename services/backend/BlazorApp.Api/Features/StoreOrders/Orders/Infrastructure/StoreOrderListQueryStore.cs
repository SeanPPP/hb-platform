using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.Orders.Infrastructure;

internal sealed class StoreOrderListQueryStore(
    SqlSugarContext context,
    IStoreOrderAccessScope accessScope,
    IStoreOrderActorContext actorContext
)
{
    private const int AggregateChunkSize = 500;
    private readonly ISqlSugarClient _db = context.Db;

    internal async Task<PagedListReactDto<StoreOrderListItemDto>> GetAsync(
        StoreOrderListFilterDto filter
    )
    {
        var accessibleStoreCodes = await accessScope.GetAccessibleStoreCodesAsync();
        ISugarQueryable<WareHouseOrder> query;

        // 关键字分别命中订单主表和商品主档，保留原实现可利用各表索引的两段查询。
        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = filter.Keyword.Trim();
            var matchedGuids = await _db.Queryable<WareHouseOrder>()
                .Where(order =>
                    !order.IsDeleted
                    && (
                        (order.OrderNo != null && order.OrderNo.Contains(keyword))
                        || (order.StoreCode != null && order.StoreCode.Contains(keyword))
                    )
                )
                .Select(order => order.OrderGUID)
                .ToListAsync();
            var detailMatchedGuids = await _db.Queryable<WareHouseOrderDetails>()
                .InnerJoin<Product>((detail, product) =>
                    detail.ProductCode == product.ProductCode
                )
                .Where((detail, product) =>
                    !detail.IsDeleted
                    && !product.IsDeleted
                    && detail.OrderGUID != null
                    && product.ItemNumber != null
                    && product.ItemNumber.Contains(keyword)
                )
                .Select(detail => detail.OrderGUID)
                .Distinct()
                .ToListAsync();

            matchedGuids.AddRange(
                detailMatchedGuids
                    .Where(guid => !string.IsNullOrWhiteSpace(guid))
                    .Select(guid => guid!)
            );
            matchedGuids = matchedGuids.Distinct().ToList();
            query = _db.Queryable<WareHouseOrder>()
                .Where(order =>
                    !order.IsDeleted && matchedGuids.Contains(order.OrderGUID)
                );
        }
        else
        {
            query = _db.Queryable<WareHouseOrder>().Where(order => !order.IsDeleted);
        }

        if (filter.StoreCodes != null && filter.StoreCodes.Any())
        {
            var requestedStoreCodes = filter.StoreCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (accessibleStoreCodes != null)
            {
                requestedStoreCodes = requestedStoreCodes
                    .Intersect(accessibleStoreCodes, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (requestedStoreCodes.Count == 0)
            {
                return CreateEmptyPage(filter);
            }

            query = query.Where(order =>
                order.StoreCode != null && requestedStoreCodes.Contains(order.StoreCode)
            );
        }
        else if (!string.IsNullOrWhiteSpace(filter.StoreCode))
        {
            var requestedStoreCode = filter.StoreCode.Trim();
            if (
                accessibleStoreCodes != null
                && !accessibleStoreCodes.Contains(
                    requestedStoreCode,
                    StringComparer.OrdinalIgnoreCase
                )
            )
            {
                return CreateEmptyPage(filter);
            }

            query = query.Where(order => order.StoreCode == requestedStoreCode);
        }
        else
        {
            // 未指定门店时保留旧入口的用户角色与 UserStore 范围逻辑。
            var currentUser = actorContext.User?.Identity?.Name;
            if (!string.IsNullOrEmpty(currentUser))
            {
                var userGuid = await _db.Queryable<User>()
                    .Where(user => user.Username == currentUser)
                    .Select(user => user.UserGUID)
                    .FirstAsync();
                if (!string.IsNullOrEmpty(userGuid))
                {
                    var userRoles = await _db.Queryable<UserRole>()
                        .InnerJoin<Role>((userRole, role) =>
                            userRole.RoleGUID == role.RoleGUID
                        )
                        .Where((userRole, role) =>
                            userRole.UserGUID == userGuid && role.IsActive
                        )
                        .Select((userRole, role) => role.RoleName)
                        .ToListAsync();
                    var isAdminOrManager = userRoles.Any(role =>
                        role == "Admin" || role == "Manager"
                    );
                    if (!isAdminOrManager)
                    {
                        var userStoreCodes = await _db.Queryable<UserStore>()
                            .InnerJoin<Store>((userStore, store) =>
                                userStore.StoreGUID == store.StoreGUID
                            )
                            .Where((userStore, store) => userStore.UserGUID == userGuid)
                            .Select((userStore, store) => store.StoreCode)
                            .ToListAsync();
                        userStoreCodes = userStoreCodes
                            .Where(code => !string.IsNullOrWhiteSpace(code))
                            .Select(code => code!)
                            .ToList();
                        if (userStoreCodes.Count > 0)
                        {
                            query = query.Where(order =>
                                order.StoreCode != null
                                && userStoreCodes.Contains(order.StoreCode)
                            );
                        }
                    }
                }
            }
        }

        if (filter.StatusList != null && filter.StatusList.Any())
        {
            query = query.Where(order =>
                order.FlowStatus != null
                && filter.StatusList.Contains(order.FlowStatus.Value)
            );
        }

        if (filter.StartDate.HasValue)
        {
            var start = filter.StartDate.Value.Date;
            query = query.Where(order => order.OrderDate >= start);
        }
        if (filter.EndDate.HasValue)
        {
            var end = filter.EndDate.Value.Date.AddDays(1).AddMilliseconds(-1);
            query = query.Where(order => order.OrderDate <= end);
        }

        var sortBy = (filter.SortBy ?? "default").Trim().ToLower();
        var orderType = (filter.SortDescending ?? true)
            ? OrderByType.Desc
            : OrderByType.Asc;
        query = ApplyMainColumnFilters(query, filter.ColumnFilters);

        if (ShouldUseAggregatePipeline(filter.ColumnFilters, sortBy))
        {
            return await BuildAggregatePageAsync(query, filter, sortBy, orderType);
        }

        var total = await query.Clone().CountAsync();
        var orderedQuery = ApplyDatabaseOrder(query, sortBy, orderType);
        var items = await orderedQuery
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(order => new StoreOrderListItemDto
            {
                OrderGUID = order.OrderGUID,
                OrderNo = order.OrderNo ?? string.Empty,
                StoreCode = order.StoreCode,
                StoreName = SqlFunc.Subqueryable<Store>()
                    .Where(store =>
                        store.StoreCode == order.StoreCode
                        || store.StoreGUID == order.StoreCode
                    )
                    .Select(store => store.StoreName),
                OrderDate = order.OrderDate,
                OutboundDate = order.OutboundDate,
                FlowStatus = order.FlowStatus ?? 0,
                TotalAmount = SqlFunc.Subqueryable<WareHouseOrderDetails>()
                    .Where(detail =>
                        detail.OrderGUID == order.OrderGUID && !detail.IsDeleted
                    )
                    .Sum(detail =>
                        (detail.AllocQuantity ?? 0) * (detail.ImportPrice ?? 0)
                    ),
                OEMTotalAmount = SqlFunc.Subqueryable<WareHouseOrderDetails>()
                    .Where(detail =>
                        detail.OrderGUID == order.OrderGUID && !detail.IsDeleted
                    )
                    .Sum(detail =>
                        (detail.AllocQuantity ?? 0) * (detail.OEMAmount ?? 0)
                    ),
                ImportTotalAmount = SqlFunc.Subqueryable<WareHouseOrderDetails>()
                    .Where(detail =>
                        detail.OrderGUID == order.OrderGUID && !detail.IsDeleted
                    )
                    .Sum(detail =>
                        (detail.AllocQuantity ?? 0) * (detail.ImportPrice ?? 0)
                    ),
                TotalOrderAmount = SqlFunc.Subqueryable<WareHouseOrderDetails>()
                    .Where(detail =>
                        detail.OrderGUID == order.OrderGUID && !detail.IsDeleted
                    )
                    .Sum(detail =>
                        (detail.Quantity ?? 0) * (detail.ImportPrice ?? 0)
                    ),
                TotalQuantity = (int)(
                    SqlFunc.Subqueryable<WareHouseOrderDetails>()
                        .Where(detail =>
                            detail.OrderGUID == order.OrderGUID && !detail.IsDeleted
                        )
                        .Sum(detail => detail.Quantity) ?? 0
                ),
                TotalAllocQuantity = (int)(
                    SqlFunc.Subqueryable<WareHouseOrderDetails>()
                        .Where(detail =>
                            detail.OrderGUID == order.OrderGUID && !detail.IsDeleted
                        )
                        .Sum(detail => detail.AllocQuantity) ?? 0
                ),
                Remarks = order.Remarks,
                CreatedAt = order.CreatedAt,
                CreatedBy = order.CreatedBy,
                UpdatedAt = order.UpdatedAt,
                UpdatedBy = order.UpdatedBy,
            })
            .ToListAsync();

        if (items.Count > 0)
        {
            items = SortPagedItems(items, sortBy, orderType);
            await FillVolumesAsync(items);
        }

        return new PagedListReactDto<StoreOrderListItemDto>
        {
            Items = items,
            Total = total,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize,
        };
    }

    private static PagedListReactDto<StoreOrderListItemDto> CreateEmptyPage(
        StoreOrderListFilterDto filter
    )
    {
        return new PagedListReactDto<StoreOrderListItemDto>
        {
            Items = new List<StoreOrderListItemDto>(),
            Total = 0,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize,
        };
    }

    private static ISugarQueryable<WareHouseOrder> ApplyMainColumnFilters(
        ISugarQueryable<WareHouseOrder> query,
        StoreOrderListColumnFilterDto? filters
    )
    {
        if (filters == null)
        {
            return query;
        }

        if (!string.IsNullOrWhiteSpace(filters.OrderNo))
        {
            var keyword = filters.OrderNo.Trim();
            query = query.Where(order =>
                order.OrderNo != null && order.OrderNo.Contains(keyword)
            );
        }
        if (filters.OutboundDateStart.HasValue)
        {
            var start = filters.OutboundDateStart.Value.Date;
            query = query.Where(order => order.OutboundDate >= start);
        }
        if (filters.OutboundDateEnd.HasValue)
        {
            var end = filters.OutboundDateEnd.Value.Date.AddDays(1).AddMilliseconds(-1);
            query = query.Where(order => order.OutboundDate <= end);
        }
        if (!string.IsNullOrWhiteSpace(filters.Remarks))
        {
            var keyword = filters.Remarks.Trim();
            query = query.Where(order =>
                order.Remarks != null && order.Remarks.Contains(keyword)
            );
        }
        if (filters.CreatedAtStart.HasValue)
        {
            var start = filters.CreatedAtStart.Value.Date;
            query = query.Where(order => order.CreatedAt >= start);
        }
        if (filters.CreatedAtEnd.HasValue)
        {
            var end = filters.CreatedAtEnd.Value.Date.AddDays(1).AddMilliseconds(-1);
            query = query.Where(order => order.CreatedAt <= end);
        }
        if (!string.IsNullOrWhiteSpace(filters.UpdatedBy))
        {
            var keyword = filters.UpdatedBy.Trim();
            query = query.Where(order =>
                order.UpdatedBy != null && order.UpdatedBy.Contains(keyword)
            );
        }
        if (filters.UpdatedAtStart.HasValue)
        {
            var start = filters.UpdatedAtStart.Value.Date;
            query = query.Where(order => order.UpdatedAt >= start);
        }
        if (filters.UpdatedAtEnd.HasValue)
        {
            var end = filters.UpdatedAtEnd.Value.Date.AddDays(1).AddMilliseconds(-1);
            query = query.Where(order => order.UpdatedAt <= end);
        }

        return query;
    }

    private static bool ShouldUseAggregatePipeline(
        StoreOrderListColumnFilterDto? filters,
        string sortBy
    )
    {
        return IsAggregateSortField(sortBy) || HasAggregateColumnFilters(filters);
    }

    private static bool HasAggregateColumnFilters(StoreOrderListColumnFilterDto? filters)
    {
        return filters != null
            && (
                filters.TotalQuantityMin.HasValue
                || filters.TotalQuantityMax.HasValue
                || filters.TotalOrderAmountMin.HasValue
                || filters.TotalOrderAmountMax.HasValue
                || filters.TotalOrderVolumeMin.HasValue
                || filters.TotalOrderVolumeMax.HasValue
                || filters.TotalAllocVolumeMin.HasValue
                || filters.TotalAllocVolumeMax.HasValue
                || filters.TotalAllocQuantityMin.HasValue
                || filters.TotalAllocQuantityMax.HasValue
                || filters.ImportTotalAmountMin.HasValue
                || filters.ImportTotalAmountMax.HasValue
            );
    }

    private static bool HasVolumeColumnFilters(StoreOrderListColumnFilterDto? filters)
    {
        return filters != null
            && (
                filters.TotalOrderVolumeMin.HasValue
                || filters.TotalOrderVolumeMax.HasValue
                || filters.TotalAllocVolumeMin.HasValue
                || filters.TotalAllocVolumeMax.HasValue
            );
    }

    private static bool IsAggregateSortField(string sortBy)
    {
        return sortBy
            is "totalorderamount"
                or "totalquantity"
                or "totalallocquantity"
                or "importtotalamount";
    }

    private async Task<PagedListReactDto<StoreOrderListItemDto>> BuildAggregatePageAsync(
        ISugarQueryable<WareHouseOrder> query,
        StoreOrderListFilterDto filter,
        string sortBy,
        OrderByType orderType
    )
    {
        var orders = await query.ToListAsync();
        var items = await BuildItemsFromOrdersAsync(orders);
        var needsVolumeFilters = HasVolumeColumnFilters(filter.ColumnFilters);
        if (needsVolumeFilters)
        {
            await FillVolumesAsync(items);
        }

        items = ApplyAggregateColumnFilters(items, filter.ColumnFilters).ToList();
        var total = items.Count;
        items = SortAggregateItems(items, sortBy, orderType)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToList();
        if (!needsVolumeFilters)
        {
            await FillVolumesAsync(items);
        }

        return new PagedListReactDto<StoreOrderListItemDto>
        {
            Items = items,
            Total = total,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize,
        };
    }

    private static ISugarQueryable<WareHouseOrder> ApplyDatabaseOrder(
        ISugarQueryable<WareHouseOrder> query,
        string sortBy,
        OrderByType orderType
    )
    {
        return sortBy switch
        {
            "orderno" => query.OrderBy(order => order.OrderNo, orderType)
                .OrderBy(order => order.OrderGUID, orderType),
            "orderdate" => query.OrderBy(order => order.OrderDate, orderType)
                .OrderBy(order => order.OrderNo, orderType)
                .OrderBy(order => order.OrderGUID, orderType),
            "storecode" => query.OrderBy(order => order.StoreCode, orderType)
                .OrderBy(order => order.OrderGUID, orderType),
            "flowstatus" => query.OrderBy(order => order.FlowStatus, orderType)
                .OrderByDescending(order => order.OrderDate)
                .OrderBy(order => order.OrderGUID, orderType),
            "createdat" => query.OrderBy(order => order.CreatedAt, orderType)
                .OrderBy(order => order.OrderGUID, orderType),
            "totalamount" => query.OrderBy(
                    order => order.ImportTotalAmount ?? 0,
                    orderType
                )
                .OrderBy(order => order.OrderGUID, orderType),
            "oemtotalamount" => query.OrderBy(
                    order => order.OEMTotalAmount ?? 0,
                    orderType
                )
                .OrderBy(order => order.OrderGUID, orderType),
            "importtotalamount" => query.OrderBy(
                    order => order.ImportTotalAmount ?? 0,
                    orderType
                )
                .OrderBy(order => order.OrderGUID, orderType),
            "remarks" => query.OrderBy(order => order.Remarks, orderType)
                .OrderBy(order => order.OrderGUID, orderType),
            _ => query.OrderBy(order => order.FlowStatus, OrderByType.Asc)
                .OrderBy(order => order.OrderDate, OrderByType.Desc)
                .OrderBy(order => order.OrderNo, OrderByType.Desc),
        };
    }

    private static List<StoreOrderListItemDto> SortPagedItems(
        List<StoreOrderListItemDto> items,
        string sortBy,
        OrderByType orderType
    )
    {
        return (sortBy, orderType) switch
        {
            ("orderno", OrderByType.Desc) => items
                .OrderByDescending(item => item.OrderNo)
                .ThenByDescending(item => item.OrderGUID)
                .ToList(),
            ("orderno", _) => items.OrderBy(item => item.OrderNo)
                .ThenBy(item => item.OrderGUID)
                .ToList(),
            ("orderdate", OrderByType.Desc) => items
                .OrderByDescending(item => item.OrderDate)
                .ThenByDescending(item => item.OrderNo)
                .ThenByDescending(item => item.OrderGUID)
                .ToList(),
            ("orderdate", _) => items.OrderBy(item => item.OrderDate)
                .ThenBy(item => item.OrderNo)
                .ThenBy(item => item.OrderGUID)
                .ToList(),
            ("storecode", OrderByType.Desc) => items
                .OrderByDescending(item => item.StoreCode)
                .ThenByDescending(item => item.OrderGUID)
                .ToList(),
            ("storecode", _) => items.OrderBy(item => item.StoreCode)
                .ThenBy(item => item.OrderGUID)
                .ToList(),
            ("flowstatus", OrderByType.Desc) => items
                .OrderByDescending(item => item.FlowStatus)
                .ThenByDescending(item => item.OrderGUID)
                .ToList(),
            ("flowstatus", _) => items.OrderBy(item => item.FlowStatus)
                .ThenBy(item => item.OrderGUID)
                .ToList(),
            ("createdat", OrderByType.Desc) => items
                .OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.OrderGUID)
                .ToList(),
            ("createdat", _) => items.OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.OrderGUID)
                .ToList(),
            ("totalamount", OrderByType.Desc) => items
                .OrderByDescending(item => item.TotalAmount)
                .ThenByDescending(item => item.OrderGUID)
                .ToList(),
            ("totalamount", _) => items.OrderBy(item => item.TotalAmount)
                .ThenBy(item => item.OrderGUID)
                .ToList(),
            ("oemtotalamount", OrderByType.Desc) => items
                .OrderByDescending(item => item.OEMTotalAmount)
                .ThenByDescending(item => item.OrderGUID)
                .ToList(),
            ("oemtotalamount", _) => items.OrderBy(item => item.OEMTotalAmount)
                .ThenBy(item => item.OrderGUID)
                .ToList(),
            ("importtotalamount", OrderByType.Desc) => items
                .OrderByDescending(item => item.ImportTotalAmount)
                .ThenByDescending(item => item.OrderGUID)
                .ToList(),
            ("importtotalamount", _) => items.OrderBy(item => item.ImportTotalAmount)
                .ThenBy(item => item.OrderGUID)
                .ToList(),
            _ => items.OrderByDescending(item => item.OrderDate)
                .ThenByDescending(item => item.OrderNo)
                .ThenByDescending(item => item.OrderGUID)
                .ToList(),
        };
    }

    private static IEnumerable<StoreOrderListItemDto> SortAggregateItems(
        List<StoreOrderListItemDto> items,
        string sortBy,
        OrderByType orderType
    )
    {
        return (sortBy, orderType) switch
        {
            ("totalorderamount", OrderByType.Desc) => items
                .OrderByDescending(item => item.TotalOrderAmount)
                .ThenByDescending(item => item.OrderGUID),
            ("totalorderamount", _) => items.OrderBy(item => item.TotalOrderAmount)
                .ThenBy(item => item.OrderGUID),
            ("totalquantity", OrderByType.Desc) => items
                .OrderByDescending(item => item.TotalQuantity)
                .ThenByDescending(item => item.OrderGUID),
            ("totalquantity", _) => items.OrderBy(item => item.TotalQuantity)
                .ThenBy(item => item.OrderGUID),
            ("totalallocquantity", OrderByType.Desc) => items
                .OrderByDescending(item => item.TotalAllocQuantity)
                .ThenByDescending(item => item.OrderGUID),
            ("totalallocquantity", _) => items.OrderBy(item => item.TotalAllocQuantity)
                .ThenBy(item => item.OrderGUID),
            ("importtotalamount", OrderByType.Desc) => items
                .OrderByDescending(item => item.ImportTotalAmount)
                .ThenByDescending(item => item.OrderGUID),
            ("importtotalamount", _) => items.OrderBy(item => item.ImportTotalAmount)
                .ThenBy(item => item.OrderGUID),
            ("orderno", OrderByType.Desc) => items
                .OrderByDescending(item => item.OrderNo)
                .ThenByDescending(item => item.OrderGUID),
            ("orderno", _) => items.OrderBy(item => item.OrderNo)
                .ThenBy(item => item.OrderGUID),
            ("orderdate", OrderByType.Desc) => items
                .OrderByDescending(item => item.OrderDate)
                .ThenByDescending(item => item.OrderNo)
                .ThenByDescending(item => item.OrderGUID),
            ("orderdate", _) => items.OrderBy(item => item.OrderDate)
                .ThenBy(item => item.OrderNo)
                .ThenBy(item => item.OrderGUID),
            ("storecode", OrderByType.Desc) => items
                .OrderByDescending(item => item.StoreCode)
                .ThenByDescending(item => item.OrderGUID),
            ("storecode", _) => items.OrderBy(item => item.StoreCode)
                .ThenBy(item => item.OrderGUID),
            ("flowstatus", OrderByType.Desc) => items
                .OrderByDescending(item => item.FlowStatus)
                .ThenByDescending(item => item.OrderGUID),
            ("flowstatus", _) => items.OrderBy(item => item.FlowStatus)
                .ThenBy(item => item.OrderGUID),
            ("remarks", OrderByType.Desc) => items
                .OrderByDescending(item => item.Remarks)
                .ThenByDescending(item => item.OrderGUID),
            ("remarks", _) => items.OrderBy(item => item.Remarks)
                .ThenBy(item => item.OrderGUID),
            _ => items.OrderByDescending(item => item.OrderDate)
                .ThenByDescending(item => item.OrderNo)
                .ThenByDescending(item => item.OrderGUID),
        };
    }

    private static IEnumerable<StoreOrderListItemDto> ApplyAggregateColumnFilters(
        IEnumerable<StoreOrderListItemDto> items,
        StoreOrderListColumnFilterDto? filters
    )
    {
        if (filters == null)
        {
            return items;
        }

        if (filters.TotalQuantityMin.HasValue)
        {
            items = items.Where(item => item.TotalQuantity >= filters.TotalQuantityMin.Value);
        }
        if (filters.TotalQuantityMax.HasValue)
        {
            items = items.Where(item => item.TotalQuantity <= filters.TotalQuantityMax.Value);
        }
        if (filters.TotalOrderAmountMin.HasValue)
        {
            items = items.Where(item =>
                item.TotalOrderAmount >= filters.TotalOrderAmountMin.Value
            );
        }
        if (filters.TotalOrderAmountMax.HasValue)
        {
            items = items.Where(item =>
                item.TotalOrderAmount <= filters.TotalOrderAmountMax.Value
            );
        }
        if (filters.TotalOrderVolumeMin.HasValue)
        {
            items = items.Where(item =>
                item.TotalOrderVolume >= filters.TotalOrderVolumeMin.Value
            );
        }
        if (filters.TotalOrderVolumeMax.HasValue)
        {
            items = items.Where(item =>
                item.TotalOrderVolume <= filters.TotalOrderVolumeMax.Value
            );
        }
        if (filters.TotalAllocVolumeMin.HasValue)
        {
            items = items.Where(item =>
                item.TotalAllocVolume >= filters.TotalAllocVolumeMin.Value
            );
        }
        if (filters.TotalAllocVolumeMax.HasValue)
        {
            items = items.Where(item =>
                item.TotalAllocVolume <= filters.TotalAllocVolumeMax.Value
            );
        }
        if (filters.TotalAllocQuantityMin.HasValue)
        {
            items = items.Where(item =>
                item.TotalAllocQuantity >= filters.TotalAllocQuantityMin.Value
            );
        }
        if (filters.TotalAllocQuantityMax.HasValue)
        {
            items = items.Where(item =>
                item.TotalAllocQuantity <= filters.TotalAllocQuantityMax.Value
            );
        }
        if (filters.ImportTotalAmountMin.HasValue)
        {
            items = items.Where(item =>
                item.ImportTotalAmount >= filters.ImportTotalAmountMin.Value
            );
        }
        if (filters.ImportTotalAmountMax.HasValue)
        {
            items = items.Where(item =>
                item.ImportTotalAmount <= filters.ImportTotalAmountMax.Value
            );
        }

        return items;
    }

    private async Task<List<StoreOrderListItemDto>> BuildItemsFromOrdersAsync(
        List<WareHouseOrder> orders
    )
    {
        var orderGuids = orders.Select(order => order.OrderGUID).Distinct().ToList();
        var storeCodes = orders
            .Select(order => order.StoreCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var stores = storeCodes.Count == 0
            ? new List<Store>()
            : await _db.Queryable<Store>()
                .Where(store =>
                    (store.StoreCode != null && storeCodes.Contains(store.StoreCode))
                    || (store.StoreGUID != null && storeCodes.Contains(store.StoreGUID))
                )
                .ToListAsync();
        var storeNameMap = stores
            .SelectMany(store =>
                new[]
                {
                    new { Key = store.StoreCode, store.StoreName },
                    new { Key = store.StoreGUID, store.StoreName },
                }
            )
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().StoreName,
                StringComparer.OrdinalIgnoreCase
            );
        var details = await QueryActiveDetailsAsync(orderGuids);
        var totalsMap = details
            .Where(detail => !string.IsNullOrWhiteSpace(detail.OrderGUID))
            .GroupBy(detail => detail.OrderGUID!)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    TotalAmount = group.Sum(detail =>
                        (detail.AllocQuantity ?? 0) * (detail.ImportPrice ?? 0)
                    ),
                    OEMTotalAmount = group.Sum(detail =>
                        (detail.AllocQuantity ?? 0) * (detail.OEMAmount ?? 0)
                    ),
                    ImportTotalAmount = group.Sum(detail =>
                        (detail.AllocQuantity ?? 0) * (detail.ImportPrice ?? 0)
                    ),
                    TotalOrderAmount = group.Sum(detail =>
                        (detail.Quantity ?? 0) * (detail.ImportPrice ?? 0)
                    ),
                    TotalQuantity = (int)(group.Sum(detail => detail.Quantity) ?? 0),
                    TotalAllocQuantity = (int)(
                        group.Sum(detail => detail.AllocQuantity) ?? 0
                    ),
                }
            );

        return orders
            .Select(order =>
            {
                totalsMap.TryGetValue(order.OrderGUID, out var totals);
                var storeName = !string.IsNullOrWhiteSpace(order.StoreCode)
                    && storeNameMap.TryGetValue(order.StoreCode, out var name)
                        ? name
                        : null;
                return new StoreOrderListItemDto
                {
                    OrderGUID = order.OrderGUID,
                    OrderNo = order.OrderNo ?? string.Empty,
                    StoreCode = order.StoreCode,
                    StoreName = storeName,
                    OrderDate = order.OrderDate,
                    OutboundDate = order.OutboundDate,
                    FlowStatus = order.FlowStatus ?? 0,
                    TotalAmount = totals?.TotalAmount ?? 0,
                    OEMTotalAmount = totals?.OEMTotalAmount ?? 0,
                    ImportTotalAmount = totals?.ImportTotalAmount ?? 0,
                    TotalOrderAmount = totals?.TotalOrderAmount ?? 0,
                    TotalQuantity = totals?.TotalQuantity ?? 0,
                    TotalAllocQuantity = totals?.TotalAllocQuantity ?? 0,
                    Remarks = order.Remarks,
                    CreatedAt = order.CreatedAt,
                    CreatedBy = order.CreatedBy,
                    UpdatedAt = order.UpdatedAt,
                    UpdatedBy = order.UpdatedBy,
                };
            })
            .ToList();
    }

    private async Task<List<WareHouseOrderDetails>> QueryActiveDetailsAsync(
        List<string> orderGuids
    )
    {
        var result = new List<WareHouseOrderDetails>();
        foreach (var chunk in orderGuids.Chunk(AggregateChunkSize))
        {
            var chunkGuids = chunk.ToList();
            var rows = await _db.Queryable<WareHouseOrderDetails>()
                .Where(detail =>
                    detail.OrderGUID != null
                    && chunkGuids.Contains(detail.OrderGUID)
                    && !detail.IsDeleted
                )
                .ToListAsync();
            result.AddRange(rows);
        }

        return result;
    }

    private async Task FillVolumesAsync(List<StoreOrderListItemDto> items)
    {
        var orderGuids = items.Select(item => item.OrderGUID).Distinct().ToList();
        if (orderGuids.Count == 0)
        {
            return;
        }

        var volumeRows = await _db.Queryable<WareHouseOrderDetails>()
            .LeftJoin<WarehouseProduct>((detail, warehouseProduct) =>
                detail.ProductCode == warehouseProduct.ProductCode
            )
            .LeftJoin<DomesticProduct>((detail, warehouseProduct, domesticProduct) =>
                warehouseProduct.ProductCode == domesticProduct.ProductCode
            )
            .Where((detail, warehouseProduct, domesticProduct) =>
                detail.OrderGUID != null
                && orderGuids.Contains(detail.OrderGUID)
                && !detail.IsDeleted
            )
            .Select((detail, warehouseProduct, domesticProduct) => new
            {
                detail.OrderGUID,
                UnitVolume = domesticProduct.PackingQuantity > 0
                    ? domesticProduct.UnitVolume / domesticProduct.PackingQuantity
                    : domesticProduct.UnitVolume,
                detail.Quantity,
                detail.AllocQuantity,
            })
            .ToListAsync();
        var volumeMap = volumeRows
            .Where(row => !string.IsNullOrWhiteSpace(row.OrderGUID))
            .GroupBy(row => row.OrderGUID)
            .ToDictionary(
                group => group.Key!,
                group => new
                {
                    TotalOrderVolume = group.Sum(row =>
                        (row.UnitVolume ?? 0) * (row.Quantity ?? 0)
                    ),
                    TotalAllocVolume = group.Sum(row =>
                        (row.UnitVolume ?? 0) * (row.AllocQuantity ?? 0)
                    ),
                }
            );
        foreach (var item in items)
        {
            if (volumeMap.TryGetValue(item.OrderGUID, out var totals))
            {
                item.TotalOrderVolume = totals.TotalOrderVolume;
                item.TotalAllocVolume = totals.TotalAllocVolume;
            }
        }
    }
}
