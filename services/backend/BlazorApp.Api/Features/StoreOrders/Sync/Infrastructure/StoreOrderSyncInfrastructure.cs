using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Sync.Domain;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Extensions.Configuration;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.Sync.Infrastructure;

internal interface IStoreOrderSyncInfrastructure
{
    Task<StoreOrderSyncQueryResult> PrepareAsync(List<string> storeCodes);

    Task<StoreOrderSyncWriteResult> PersistAsync(StoreOrderSyncPreparation preparation);
}

internal sealed class StoreOrderSyncInfrastructure : IStoreOrderSyncInfrastructure
{
    private const int WriteBatchSize = 200;
    private const int HqReadBatchSize = 500;

    private readonly ISqlSugarClient _db;
    private readonly IMapper _mapper;
    private readonly ILogger<StoreOrderSyncInfrastructure> _logger;
    private readonly Func<ISqlSugarClient> _createHqConnection;

    public StoreOrderSyncInfrastructure(
        SqlSugarContext context,
        IMapper mapper,
        IConfiguration configuration,
        ILogger<StoreOrderSyncInfrastructure> logger
    )
        : this(
            context.Db,
            mapper,
            logger,
            () => HqSqlSugarContext.CreateConcurrentConnection(configuration)
        ) { }

    internal StoreOrderSyncInfrastructure(
        ISqlSugarClient db,
        IMapper mapper,
        ILogger<StoreOrderSyncInfrastructure> logger,
        Func<ISqlSugarClient> createHqConnection
    )
    {
        _db = db;
        _mapper = mapper;
        _logger = logger;
        _createHqConnection = createHqConnection;
    }

    public async Task<StoreOrderSyncQueryResult> PrepareAsync(List<string> storeCodes)
    {
        var hasStoreFilter = storeCodes.Count > 0;
        var localOrders = await _db.Queryable<WareHouseOrder>()
            .WhereIF(hasStoreFilter, order => storeCodes.Contains(order.StoreCode!))
            .Select(order => new StoreOrderSyncLocalOrderSnapshot
            {
                OrderGUID = order.OrderGUID,
                StoreCode = order.StoreCode,
                UpdatedAt = order.UpdatedAt,
                IsDeleted = order.IsDeleted,
            })
            .ToListAsync();

        var activeOrderGuids = localOrders
            .Where(order => !order.IsDeleted)
            .Select(order => order.OrderGUID)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deletedOrderGuids = localOrders
            .Where(order => order.IsDeleted)
            .Select(order => order.OrderGUID)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allLocalOrderGuids = localOrders
            .Select(order => order.OrderGUID)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var localUpdatedAtMap = localOrders
            .Where(order => order.UpdatedAt.HasValue)
            .ToDictionary(
                order => order.OrderGUID,
                order => order.UpdatedAt!.Value,
                StringComparer.OrdinalIgnoreCase
            );

        _logger.LogInformation(
            "本地已存在订单数量: {Count}, 分店代码: {StoreCodes}",
            allLocalOrderGuids.Count,
            hasStoreFilter ? string.Join(",", storeCodes) : "全部"
        );

        using var hqDb = _createHqConnection();
        var allHqOrders = await hqDb.Queryable<CBP_RED_分店订货单主表Store>()
            .Where(order => SqlFunc.HasValue(order.HGUID))
            .WhereIF(hasStoreFilter, order => storeCodes.Contains(order.分店代码!))
            .ToListAsync();

        if (!allHqOrders.Any())
        {
            _logger.LogInformation(
                "HQ 订单读取完成：总数 0，数字分店订单 0，外购客户订单 0，分店筛选 {StoreCodes}",
                hasStoreFilter ? string.Join(",", storeCodes) : "全部"
            );
            return StoreOrderSyncQueryResult.NoChanges("没有需要同步的订单");
        }

        allHqOrders = allHqOrders
            .GroupBy(order => order.HGUID, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var hqExternalCustomerOrderCount = allHqOrders.Count(order =>
            Guid.TryParse(order.分店代码, out _)
        );
        _logger.LogInformation(
            "HQ 订单读取完成：总数 {Total}，数字分店订单 {StoreOrderCount}，外购客户订单 {ExternalCustomerOrderCount}，分店筛选 {StoreCodes}",
            allHqOrders.Count,
            allHqOrders.Count - hqExternalCustomerOrderCount,
            hqExternalCustomerOrderCount,
            hasStoreFilter ? string.Join(",", storeCodes) : "全部"
        );

        var allHqOrderGuids = allHqOrders
            .Select(order => order.HGUID!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 先读取轻量明细指纹；完整明细只在确定目标订单后拉取。
        var hqDetailFingerprints = await QueryHqOrderDetailFingerprintsAsync(
            hqDb,
            allHqOrderGuids
        );
        var hqOrderStoreCodeMap = allHqOrders.ToDictionary(
            order => order.HGUID!,
            order => order.分店代码,
            StringComparer.OrdinalIgnoreCase
        );
        var hqDetailOrderCount = hqDetailFingerprints
            .Where(detail => !string.IsNullOrWhiteSpace(detail.OrderGuid))
            .Select(detail => detail.OrderGuid!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var hqExternalCustomerFingerprintCount = hqDetailFingerprints.Count(detail =>
            !string.IsNullOrWhiteSpace(detail.OrderGuid)
            && hqOrderStoreCodeMap.TryGetValue(detail.OrderGuid, out var storeCode)
            && Guid.TryParse(storeCode, out _)
        );
        _logger.LogInformation(
            "HQ 轻量明细指纹读取完成：订单 {OrderCount} 个，有明细订单 {DetailOrderCount} 个，轻量明细 {DetailCount} 条，外购客户轻量明细 {ExternalCustomerDetailCount} 条",
            allHqOrderGuids.Count,
            hqDetailOrderCount,
            hqDetailFingerprints.Count,
            hqExternalCustomerFingerprintCount
        );

        var localDetailFingerprints = await QueryLocalOrderDetailFingerprintsAsync(
            allHqOrderGuids
        );
        _logger.LogInformation(
            "本地轻量明细指纹读取完成：轻量明细 {DetailCount} 条",
            localDetailFingerprints.Count
        );

        var detailChangedOrderGuids = StoreOrderSyncRules.GetDetailChangedOrderGuids(
            hqDetailFingerprints,
            localDetailFingerprints
        );
        var missingOrderGuids = allHqOrders
            .Where(order => !allLocalOrderGuids.Contains(order.HGUID!))
            .Select(order => order.HGUID!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reactivatedOrderGuids = allHqOrders
            .Where(order => deletedOrderGuids.Contains(order.HGUID!))
            .Select(order => order.HGUID!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var updatedOrderGuids = allHqOrders
            .Where(order => activeOrderGuids.Contains(order.HGUID!))
            .Where(order =>
                StoreOrderSyncRules.IsHqOrderNewerThanLocal(order, localUpdatedAtMap)
            )
            .Select(order => order.HGUID!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var detailOnlyOrderGuids = detailChangedOrderGuids
            .Where(orderGuid =>
                activeOrderGuids.Contains(orderGuid) && !updatedOrderGuids.Contains(orderGuid)
            )
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetOrderGuids = missingOrderGuids
            .Concat(reactivatedOrderGuids)
            .Concat(updatedOrderGuids)
            .Concat(detailOnlyOrderGuids)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation(
            "分店订货同步目标判定完成：新增 {NewCount} 个，恢复 {RestoreCount} 个，主表更新 {OrderUpdatedCount} 个，明细变更 {DetailOnlyCount} 个，目标订单 {TargetCount} 个",
            missingOrderGuids.Count,
            reactivatedOrderGuids.Count,
            updatedOrderGuids.Count,
            detailOnlyOrderGuids.Count,
            targetOrderGuids.Count
        );

        if (targetOrderGuids.Count == 0)
        {
            _logger.LogInformation(
                "分店订货同步无需拉取完整明细：目标订单为空，分店筛选 {StoreCodes}",
                hasStoreFilter ? string.Join(",", storeCodes) : "全部"
            );
            return StoreOrderSyncQueryResult.NoChanges("所有订单已是最新，无需同步");
        }

        var targetHqOrders = allHqOrders
            .Where(order => targetOrderGuids.Contains(order.HGUID!))
            .ToList();
        var targetOrderGuidList = targetHqOrders
            .Select(order => order.HGUID!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var hqDetails = await QueryHqOrderDetailsAsync(hqDb, targetOrderGuidList);
        var hqExternalCustomerDetailCount = hqDetails.Count(detail =>
            Guid.TryParse(detail.分店代码, out _)
        );
        _logger.LogInformation(
            "HQ 目标订单完整明细读取完成：目标订单 {OrderCount} 个，明细 {DetailCount} 条，外购客户明细 {ExternalCustomerDetailCount} 条",
            targetOrderGuidList.Count,
            hqDetails.Count,
            hqExternalCustomerDetailCount
        );
        var hqDetailsByOrder = hqDetails
            .GroupBy(detail => detail.主表GUID!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToList(),
                StringComparer.OrdinalIgnoreCase
            );

        var localDetails = await _db.Queryable<WareHouseOrderDetails>()
            .Where(detail => targetOrderGuidList.Contains(detail.OrderGUID!))
            .ToListAsync();
        var localDetailByGuid = localDetails
            .GroupBy(detail => detail.DetailGUID, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase
            );

        return StoreOrderSyncQueryResult.Ready(
            new StoreOrderSyncPreparation(
                targetHqOrders,
                hqDetailsByOrder,
                localDetailByGuid,
                missingOrderGuids,
                reactivatedOrderGuids,
                updatedOrderGuids
            )
        );
    }

    public async Task<StoreOrderSyncWriteResult> PersistAsync(
        StoreOrderSyncPreparation preparation
    )
    {
        var ordersSynced = 0;
        var ordersUpdated = 0;
        var detailsSynced = 0;
        var detailsUpdated = 0;

        // Command 的唯一事务边界：四类批量写入统一提交或回滚。
        var transactionResult = await _db.Ado.UseTranAsync(async () =>
        {
            var ordersToInsert = new List<WareHouseOrder>();
            var ordersToUpdate = new List<WareHouseOrder>();

            foreach (var hqOrder in preparation.TargetHqOrders)
            {
                var order = MapHqOrder(hqOrder);
                if (preparation.MissingOrderGuids.Contains(hqOrder.HGUID!))
                {
                    ordersToInsert.Add(order);
                }
                else if (
                    preparation.ReactivatedOrderGuids.Contains(hqOrder.HGUID!)
                    || preparation.UpdatedOrderGuids.Contains(hqOrder.HGUID!)
                )
                {
                    ordersToUpdate.Add(order);
                }
            }

            var detailsToInsert = new List<WareHouseOrderDetails>();
            var detailsToUpdate = new List<WareHouseOrderDetails>();
            foreach (var hqDetail in preparation.TargetHqOrders
                .SelectMany(order =>
                    preparation.HqDetailsByOrder.TryGetValue(order.HGUID!, out var details)
                        ? details
                        : new List<CBP_RED_分店订单详情表Store>()
                )
                .GroupBy(detail => detail.HGUID, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()))
            {
                var detail = MapHqDetail(hqDetail);
                if (!preparation.LocalDetailByGuid.TryGetValue(detail.DetailGUID, out var localDetail))
                {
                    detailsToInsert.Add(detail);
                }
                else if (StoreOrderSyncRules.IsHqDetailChanged(hqDetail, localDetail))
                {
                    detailsToUpdate.Add(detail);
                }
            }

            ordersSynced = ordersToInsert.Count + preparation.ReactivatedOrderGuids.Count;
            ordersUpdated = ordersToUpdate.Count - preparation.ReactivatedOrderGuids.Count;
            detailsSynced = detailsToInsert.Count;
            detailsUpdated = detailsToUpdate.Count;

            _logger.LogInformation(
                "分店订货同步准备写入：新增订单 {InsertOrderCount} 个，更新订单 {UpdateOrderCount} 个，新增明细 {InsertDetailCount} 条，更新明细 {UpdateDetailCount} 条，批大小 {BatchSize}",
                ordersToInsert.Count,
                ordersToUpdate.Count,
                detailsToInsert.Count,
                detailsToUpdate.Count,
                WriteBatchSize
            );

            await ExecuteInsertInBatchesAsync(ordersToInsert, WriteBatchSize);
            await ExecuteUpdateInBatchesAsync(ordersToUpdate, WriteBatchSize);
            await ExecuteInsertInBatchesAsync(detailsToInsert, WriteBatchSize);
            await ExecuteUpdateInBatchesAsync(detailsToUpdate, WriteBatchSize);
        });

        if (!transactionResult.IsSuccess)
        {
            var transactionError = transactionResult.ErrorException;
            throw new InvalidOperationException(
                transactionError?.Message ?? "同步订单事务失败",
                transactionError
            );
        }

        return new StoreOrderSyncWriteResult(
            ordersSynced,
            ordersUpdated,
            detailsSynced,
            detailsUpdated
        );
    }

    private async Task<List<CBP_RED_分店订单详情表Store>> QueryHqOrderDetailsAsync(
        ISqlSugarClient hqDb,
        List<string> orderGuids
    )
    {
        if (orderGuids.Count == 0)
        {
            return new List<CBP_RED_分店订单详情表Store>();
        }

        var result = new List<CBP_RED_分店订单详情表Store>();
        foreach (var batch in orderGuids.Chunk(HqReadBatchSize))
        {
            var batchOrderGuids = batch.ToList();
            var rows = await hqDb.Queryable<CBP_RED_分店订单详情表Store>()
                .Where(detail =>
                    SqlFunc.HasValue(detail.HGUID) && SqlFunc.HasValue(detail.主表GUID)
                )
                .Where(detail => batchOrderGuids.Contains(detail.主表GUID!))
                .ToListAsync();
            result.AddRange(rows);
        }

        return result;
    }

    private async Task<List<HqOrderDetailFingerprint>> QueryHqOrderDetailFingerprintsAsync(
        ISqlSugarClient hqDb,
        List<string> orderGuids
    )
    {
        if (orderGuids.Count == 0)
        {
            return new List<HqOrderDetailFingerprint>();
        }

        var result = new List<HqOrderDetailFingerprint>();
        foreach (var batch in orderGuids.Chunk(HqReadBatchSize))
        {
            var batchOrderGuids = batch.ToList();
            var rows = await hqDb.Queryable<CBP_RED_分店订单详情表Store>()
                .Where(detail =>
                    SqlFunc.HasValue(detail.HGUID) && SqlFunc.HasValue(detail.主表GUID)
                )
                .Where(detail => batchOrderGuids.Contains(detail.主表GUID!))
                .Select(detail => new HqOrderDetailFingerprint
                {
                    DetailGuid = detail.HGUID,
                    OrderGuid = detail.主表GUID,
                    StoreCode = detail.分店代码,
                    StoreProductCode = detail.分店商品编码,
                    ProductCode = detail.商品编码,
                    Quantity = detail.数量,
                    AllocQuantity = detail.配货数量,
                    LastCost = detail.上次成本,
                    ImportPrice = detail.进口价格,
                    ImportAmount = detail.合计进口金额,
                    OemPrice = detail.贴牌价格,
                    OemAmount = detail.合计贴牌金额,
                    UpdatedAt = detail.FGC_LastModifyDate,
                })
                .ToListAsync();
            result.AddRange(rows);
        }

        return result;
    }

    private async Task<List<LocalOrderDetailFingerprint>> QueryLocalOrderDetailFingerprintsAsync(
        List<string> orderGuids
    )
    {
        if (orderGuids.Count == 0)
        {
            return new List<LocalOrderDetailFingerprint>();
        }

        var result = new List<LocalOrderDetailFingerprint>();
        foreach (var batch in orderGuids.Chunk(HqReadBatchSize))
        {
            var batchOrderGuids = batch.ToList();
            var rows = await _db.Queryable<WareHouseOrderDetails>()
                .Where(detail => batchOrderGuids.Contains(detail.OrderGUID!))
                .Select(detail => new LocalOrderDetailFingerprint
                {
                    DetailGuid = detail.DetailGUID,
                    OrderGuid = detail.OrderGUID,
                    StoreCode = detail.StoreCode,
                    StoreProductCode = detail.StoreProductCode,
                    ProductCode = detail.ProductCode,
                    Quantity = detail.Quantity,
                    AllocQuantity = detail.AllocQuantity,
                    LastCost = detail.LastCost,
                    ImportPrice = detail.ImportPrice,
                    ImportAmount = detail.ImportAmount,
                    OemPrice = detail.OEMPrice,
                    OemAmount = detail.OEMAmount,
                    UpdatedAt = detail.UpdatedAt,
                    IsDeleted = detail.IsDeleted,
                })
                .ToListAsync();
            result.AddRange(rows);
        }

        return result;
    }

    private WareHouseOrder MapHqOrder(CBP_RED_分店订货单主表Store hqOrder)
    {
        var order = _mapper.Map<WareHouseOrder>(hqOrder);
        order.IsDeleted = false;
        order.CreatedAt = hqOrder.FGC_CreateDate ?? DateTime.Now;
        order.UpdatedAt = hqOrder.FGC_LastModifyDate ?? DateTime.Now;
        order.CreatedBy = hqOrder.FGC_Creator ?? "HQ同步";
        order.UpdatedBy = hqOrder.FGC_LastModifier ?? "HQ同步";
        return order;
    }

    private WareHouseOrderDetails MapHqDetail(CBP_RED_分店订单详情表Store hqDetail)
    {
        var detail = _mapper.Map<WareHouseOrderDetails>(hqDetail);
        detail.IsDeleted = false;
        detail.CreatedAt = hqDetail.FGC_CreateDate ?? DateTime.Now;
        detail.UpdatedAt = hqDetail.FGC_LastModifyDate ?? DateTime.Now;
        detail.CreatedBy = hqDetail.FGC_Creator ?? "HQ同步";
        detail.UpdatedBy = hqDetail.FGC_LastModifier ?? "HQ同步";
        return detail;
    }

    private async Task ExecuteInsertInBatchesAsync<T>(List<T> entities, int size)
        where T : class, new()
    {
        foreach (var batch in entities.Chunk(size))
        {
            await _db.Insertable(batch.ToList()).ExecuteCommandAsync();
        }
    }

    private async Task ExecuteUpdateInBatchesAsync<T>(List<T> entities, int size)
        where T : class, new()
    {
        foreach (var batch in entities.Chunk(size))
        {
            await _db.Updateable(batch.ToList()).ExecuteCommandAsync();
        }
    }
}
