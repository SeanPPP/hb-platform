using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBSalesRecord;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.POSM;

namespace BlazorApp.Api.Services;

/// <summary>商品分店日统计的来源读取边界：只装载输入，不承担聚合或写入。</summary>
internal sealed class SalesStatisticsProductStoreDailySourceReader
{
    private const int CommandTimeoutSeconds = 1800;
    private const int HBSalesMainCheckoutDateWindowDays = 7;
    private const int StoreCostProductQueryBatchSize = 500;
    private const int PosmSupplierMappingQueryBatchSize = 500;

    internal async Task<ProductStoreDailyRefreshInput> LoadAsync(
        SqlSugarContext context,
        POSMSqlSugarContext posmContext,
        HBSalesRecordSqlSugarContext? hbSalesContext,
        ILogger logger,
        DateTime date,
        IReadOnlyList<ProductStoreDailySourceRow>? preloadedHBSalesRows,
        Posm2025DailySnapshot? preloadedPosmSnapshot)
    {
        var targetDate = date.Date;
        var nextDate = targetDate.AddDays(1);
        var posmWatermark = preloadedPosmSnapshot == null
            ? await posmContext.Db.Queryable<SalesOrder>()
                .Where(order => order.Status != null
                    && (order.Status == 1 || order.Status == 4)
                    && order.OrderTime != null
                    && order.OrderTime >= targetDate
                    && order.OrderTime < nextDate)
                .MaxAsync(order => order.LastUploadTime)
            : SalesStatisticsProductStoreDailyDomainRules.GetPosmSnapshotWatermark(
                preloadedPosmSnapshot
            );

        var hbSalesRows = targetDate.Year == 2025
            ? preloadedHBSalesRows?.ToList()
                ?? await LoadHBSalesProductStoreDailyRowsAsync(
                    hbSalesContext ?? throw new InvalidOperationException("2025 年商品统计缺少 HBSalesRecord 上下文"),
                    targetDate,
                    nextDate)
            : new List<ProductStoreDailySourceRow>();
        if (targetDate.Year == 2025)
            await ValidateAndResolveHBSalesRowsAsync(context, hbSalesRows, targetDate);

        var lastSourceUploadTime = SalesStatisticsProductStoreDailyDomainRules.GetLatestSourceTime(
            posmWatermark,
            SalesStatisticsProductStoreDailyDomainRules.GetLatestSourceTime(hbSalesRows)
        );
        var missingHBSalesBranchCount = hbSalesRows.Count(row => string.IsNullOrWhiteSpace(row.BranchCode));
        if (missingHBSalesBranchCount > 0)
        {
            // 缺分店来源仅记录诊断，不能在装载阶段篡改既有口径。
            logger.LogWarning(
                "2025 HBSales 有 {Count} 条明细缺少分店编码，未写入商品分店统计: {Date}",
                missingHBSalesBranchCount,
                targetDate.ToString("yyyy-MM-dd"));
        }

        var detailRows = preloadedPosmSnapshot?.DetailRows.ToList() ?? await posmContext.Db.Queryable<SalesOrder>()
            .LeftJoin<SalesOrderDetail>((order, detail) => order.OrderGuid == detail.OrderGuid)
            .Where(order => order.Status != null
                && (order.Status == 1 || order.Status == 4)
                && order.OrderTime != null
                && order.OrderTime >= targetDate
                && order.OrderTime < nextDate)
            .Select((order, detail) => new ProductStoreDailySourceRow
            {
                Date = order.OrderTime!.Value.Date,
                OrderGuid = order.OrderGuid,
                DetailGuid = detail.OrderDetailGuid,
                BranchCode = order.BranchCode,
                DeviceCode = order.DeviceCode,
                OrderLastUploadTime = order.LastUploadTime,
                ProductCode = detail.ProductCode,
                SupplierCode = detail.SupplierCode,
                ProductName = detail.ProductName,
                Barcode = detail.Barcode,
                Quantity = detail.Quantity ?? 0m,
                ActualAmount = detail.ActualAmount ?? 0m,
                DetailLastUploadTime = detail.LastUploadTime,
            })
            .ToListAsync();
        var detailGuidSet = detailRows.Select(row => row.DetailGuid)
            .Where(guid => !string.IsNullOrWhiteSpace(guid))
            .Select(guid => guid!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var supplementalReturnRows = preloadedPosmSnapshot?.SupplementalReturnRows.ToList()
            ?? await SalesStatisticsProductStoreDailySourceQueries.LoadSupplementalReturnRowsAsync(
                posmContext,
                targetDate,
                nextDate,
                detailGuidSet
            );
        var rawRows = detailRows.Concat(supplementalReturnRows).Concat(hbSalesRows).ToList();
        var orderAmountMaps = preloadedPosmSnapshot == null
            ? await SalesStatisticsProductStoreDailySourceQueries.LoadOrderAmountMapsAsync(
                posmContext,
                targetDate,
                nextDate,
                detailRows,
                row => row.OrderGuid,
                row => row.ActualAmount
            )
            : (
                PaymentAmounts: SalesStatisticsProductStoreDailyDomainRules.BuildOrderAmountMap(
                    preloadedPosmSnapshot.PaymentRows.Select(row => new OrderAmountRow
                    {
                        OrderGuid = row.OrderGuid,
                        Amount = row.Amount,
                    })
                ),
                DetailAmounts: SalesStatisticsProductStoreDailyDomainRules.BuildOrderAmountMap(
                    detailRows.Select(row => new OrderAmountRow
                    {
                        OrderGuid = row.OrderGuid,
                        Amount = row.ActualAmount,
                    })
                )
            );
        var deviceBranchMap = preloadedPosmSnapshot?.DeviceBranchMap.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.OrdinalIgnoreCase
            )
            ?? await SalesStatisticsProductStoreDailySourceQueries.LoadDeviceBranchMapAsync(
                posmContext,
                rawRows.Where(row => string.IsNullOrWhiteSpace(row.BranchCode))
                    .Select(row => row.DeviceCode)
            );
        var productCodes = rawRows.Select(row => row.ProductCode)
            .Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code!).Distinct().ToList();
        var posmSupplierMapping = await LoadPosmSupplierMappingInBatchesAsync(
            posmContext,
            rawRows.Where(row => !row.IsHBSalesSource && string.IsNullOrWhiteSpace(row.SupplierCode))
                .Select(row => row.ProductCode));
        foreach (var row in rawRows.Where(row =>
            !row.IsHBSalesSource
            && string.IsNullOrWhiteSpace(row.SupplierCode)
            && !string.IsNullOrWhiteSpace(row.ProductCode)
            && posmSupplierMapping.ContainsKey(row.ProductCode.Trim())))
        {
            // HBSales 供应商是权威事实；仅 POSM 明细缺值时使用商品映射补齐。
            row.SupplierCode = posmSupplierMapping[row.ProductCode!.Trim()];
        }
        var branchCodes = rawRows.Select(row => SalesStatisticsCodeRules.ResolveBranchCode(
                row.BranchCode,
                row.DeviceCode,
                deviceBranchMap
            ))
            .Where(code => !string.IsNullOrWhiteSpace(code)).Distinct().ToList();
        var storeCosts = await LoadStoreCostsInBatchesAsync(context, productCodes, branchCodes);
        var productCosts = productCodes.Count == 0 ? new List<ProductCostRow>() : await context.Db.Queryable<Product>()
            .Where(product => product.ProductCode != null && productCodes.Contains(product.ProductCode) && product.IsDeleted == false)
            .Select(product => new ProductCostRow { ProductCode = product.ProductCode, PurchasePrice = product.PurchasePrice })
            .ToListAsync();
        var warehouseCosts = productCodes.Count == 0 ? new List<WarehouseCostRow>() : await context.Db.Queryable<WarehouseProduct>()
            .Where(product => productCodes.Contains(product.ProductCode) && product.IsDeleted == false)
            .Select(product => new WarehouseCostRow { ProductCode = product.ProductCode, ImportPrice = product.ImportPrice })
            .ToListAsync();

        return new ProductStoreDailyRefreshInput(
            targetDate, rawRows, supplementalReturnRows.ToHashSet(), orderAmountMaps.PaymentAmounts,
            orderAmountMaps.DetailAmounts, deviceBranchMap, storeCosts, productCosts, warehouseCosts,
            lastSourceUploadTime);
    }

    private static async Task ValidateAndResolveHBSalesRowsAsync(
        SqlSugarContext context,
        List<ProductStoreDailySourceRow> hbSalesRows,
        DateTime targetDate)
    {
        hbSalesRows.RemoveAll(row => row.Quantity == 0m && row.ActualAmount == 0m);
        await ResolveMissingHBSalesProductCodesAsync(context, hbSalesRows);
        var invalidRows = hbSalesRows.Where(row => string.IsNullOrWhiteSpace(row.BranchCode)
                || string.IsNullOrWhiteSpace(row.ProductCode))
            .ToList();
        if (invalidRows.Count == 0)
            return;

        var missingFields = new List<string>();
        if (invalidRows.Any(row => string.IsNullOrWhiteSpace(row.BranchCode)))
            missingFields.Add("分店编码");
        if (invalidRows.Any(row => string.IsNullOrWhiteSpace(row.ProductCode)))
            missingFields.Add("商品编码且无法获得唯一商品候选");
        throw new InvalidOperationException(
            $"2025 HBSales 存在 {invalidRows.Count} 条非零来源行缺少{string.Join("或", missingFields)}，不能替换双表统计: {targetDate:yyyy-MM-dd}");
    }

    internal static async Task<List<StoreCostRow>> LoadStoreCostsInBatchesAsync(
    SqlSugarContext context,
    IReadOnlyCollection<string> productCodes,
    IReadOnlyCollection<string> branchCodes
)
{
    if (productCodes.Count == 0 || branchCodes.Count == 0)
        return new List<StoreCostRow>();

    var normalizedBranchCodes = branchCodes.Distinct(StringComparer.Ordinal).ToList();
    var rows = new List<StoreCostRow>();
    foreach (var productCodeBatch in productCodes
        .Distinct(StringComparer.Ordinal)
        .Chunk(StoreCostProductQueryBatchSize))
    {
        // 超大 IN 条件在 460 万行分店价格表上会导致 SQL Server 优化和并发超时；
        // 小批量查询继续命中现有 ProductCode + StoreCode 索引，统计口径保持不变。
        var batch = productCodeBatch.ToList();
        var batchRows = await context.Db.Queryable<StoreRetailPrice>()
            .Where(p =>
                p.ProductCode != null
                && p.StoreCode != null
                && batch.Contains(p.ProductCode)
                && normalizedBranchCodes.Contains(p.StoreCode)
                && p.SupplierCode != null
                && p.IsDeleted == false
                && p.IsActive == true
            )
            .Select(p => new StoreCostRow
            {
                StoreCode = p.StoreCode,
                SupplierCode = p.SupplierCode,
                ProductCode = p.ProductCode,
                PurchasePrice = p.PurchasePrice,
            })
            .ToListAsync();
        rows.AddRange(batchRows);
    }

    return rows;
}

internal static async Task<Dictionary<string, string>> LoadPosmSupplierMappingInBatchesAsync(
    POSMSqlSugarContext posmContext,
    IEnumerable<string?> productCodes)
{
    var targetProductCodes = productCodes
        .Where(code => !string.IsNullOrWhiteSpace(code))
        .Select(code => code!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    var mappings = new List<PosmProductSupplierMapping>();
    foreach (var batch in targetProductCodes.Chunk(PosmSupplierMappingQueryBatchSize))
    {
        var batchCodes = batch.ToList();
        mappings.AddRange(await posmContext.Db.Queryable<PosmProductSupplierMapping>()
            .Where(mapping => batchCodes.Contains(mapping.ProductCode))
            .ToListAsync());
    }
    return mappings
        .Where(mapping => !string.IsNullOrWhiteSpace(mapping.ProductCode)
            && !string.IsNullOrWhiteSpace(mapping.LocalSupplierCode))
        .GroupBy(mapping => mapping.ProductCode.Trim(), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            group => group.Key,
            group => group.Select(mapping => mapping.LocalSupplierCode.Trim()).First(),
            StringComparer.OrdinalIgnoreCase);
}


internal static async Task<List<ProductStoreDailySourceRow>> LoadHBSalesProductStoreDailyRowsAsync(
    HBSalesRecordSqlSugarContext hbSalesContext,
    DateTime targetDate,
    DateTime nextDate,
    int? maxRows = null
)
{
    var originalCommandTimeout = hbSalesContext.Db.Ado.CommandTimeOut;
    var mainCheckoutDateWindowStart = targetDate.AddDays(
        -HBSalesMainCheckoutDateWindowDays
    );
    var mainCheckoutDateWindowEnd = nextDate.AddDays(HBSalesMainCheckoutDateWindowDays);
    hbSalesContext.Db.Ado.CommandTimeOut = Math.Max(
        originalCommandTimeout,
        CommandTimeoutSeconds
    );
    List<ProductStoreDailySourceRow> rows;
    try
    {
        var query = hbSalesContext.Db.Queryable<SalesOrderMain>()
            .LeftJoin<SalesOrderDetailRecord>((main, detail) =>
                main.B销售单号 == detail.B销售单号
            )
            .Where((main, detail) =>
                detail.B结账日期.HasValue
                && detail.B结账日期.Value >= targetDate
                && detail.B结账日期.Value < nextDate
                && main.B结账日期.HasValue
                && main.B结账日期.Value >= mainCheckoutDateWindowStart
                && main.B结账日期.Value < mainCheckoutDateWindowEnd
                &&
                // SQL 的 != 会丢掉 NULL；年度口径是只排除类型 2。
                (main.B单据类型 == null || main.B单据类型.Trim() != "2")
            )
            .Select((main, detail) => new ProductStoreDailySourceRow
            {
                IsHBSalesSource = true,
                Date = detail.B结账日期!.Value.Date,
                // 前缀避免与 POSM 的订单号偶然相同而误合并订单数。
                OrderGuid = "HBSALES:" + main.B销售单号,
                // 分店统计的订单数必须保留源单号的 null 语义，不能从带前缀的显示键反推。
                HBSalesOrderNumber = main.B销售单号,
                DetailGuid = $"HBSALES:{detail.ID}",
                // 与既有 2025 年度统计保持一致：分店以明细记录为准，不从主表补写。
                BranchCode = detail.B分店代码,
                ProductCode = detail.B产品编号,
                ItemNumber = detail.B货号,
                SupplierCode = detail.B供应商ID,
                ProductName = detail.B商品名,
                Barcode = detail.B条形码,
                Quantity = detail.B数量 ?? 0m,
                ActualAmount = detail.B合计金额 ?? 0m,
                // HBSales 使用明细/主表的最后修改时间；创建时间仅作为旧记录的可靠回退。
                // 同时保留四个原始时间，供 pre 水位按字段独立 MAX，不能由回退值反推。
                HBSalesMainLastModifiedAt = main.FGC_LastModifyDate,
                HBSalesMainCreatedAt = main.FGC_CreateDate,
                HBSalesDetailLastModifiedAt = detail.FGC_LastModifyDate,
                HBSalesDetailCreatedAt = detail.FGC_CreateDate,
                OrderLastUploadTime = main.FGC_LastModifyDate ?? main.FGC_CreateDate,
                DetailLastUploadTime = detail.FGC_LastModifyDate ?? detail.FGC_CreateDate,
                DocumentType = main.B单据类型,
            });
        rows = maxRows.HasValue
            ? await query.Take(maxRows.Value + 1).ToListAsync()
            : await query.ToListAsync();
    }
    finally
    {
        // 共享上下文可能被后续查询复用，必须还原调用方原有超时。
        hbSalesContext.Db.Ado.CommandTimeOut = originalCommandTimeout;
    }

    if (maxRows.HasValue && rows.Count > maxRows.Value)
    {
        throw new InvalidOperationException(
            $"2025 HBSales 批量快照超过 {maxRows.Value:N0} 行内存保护上限，请缩小日期范围"
        );
    }

    foreach (var row in rows.Where(row =>
        SalesStatisticsCodeRules.Normalize(row.DocumentType) == "3"
        || SalesStatisticsCodeRules.Normalize(row.DocumentType) == "4"
    ))
    {
        // HBSales 年度统计口径：类型 3/4 为退货/退款，数量和金额统一取反。
        row.Quantity = -row.Quantity;
        row.ActualAmount = -row.ActualAmount;
    }

    return rows;
}


internal static async Task ResolveMissingHBSalesProductCodesAsync(
    SqlSugarContext context,
    IReadOnlyList<ProductStoreDailySourceRow> hbSalesRows
)
{
    var rowsToResolve = hbSalesRows
        .Where(row => string.IsNullOrWhiteSpace(row.ProductCode))
        .Where(row =>
            !string.IsNullOrWhiteSpace(SalesStatisticsCodeRules.Normalize(row.ItemNumber))
            || !string.IsNullOrWhiteSpace(SalesStatisticsCodeRules.Normalize(row.Barcode))
        )
        .ToList();
    if (!rowsToResolve.Any())
    {
        return;
    }

    var globalLookupCodes = rowsToResolve
        .SelectMany(row => new[]
        {
            SalesStatisticsCodeRules.Normalize(row.ItemNumber),
            SalesStatisticsCodeRules.Normalize(row.Barcode),
        })
        .Where(code => !string.IsNullOrWhiteSpace(code))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    var barcodeLookupCodes = rowsToResolve
        .Select(row => SalesStatisticsCodeRules.Normalize(row.Barcode))
        .Where(code => !string.IsNullOrWhiteSpace(code))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    var globalCandidates = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    var branchCandidates = new Dictionary<(string StoreCode, string Barcode), HashSet<string>>();
    var crossStoreCandidates = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

    void AddGlobalCandidate(string? lookupCode, string? productCode)
    {
        var normalizedLookupCode = SalesStatisticsCodeRules.Normalize(lookupCode);
        var normalizedProductCode = SalesStatisticsCodeRules.Normalize(productCode);
        if (string.IsNullOrWhiteSpace(normalizedLookupCode)
            || string.IsNullOrWhiteSpace(normalizedProductCode))
        {
            return;
        }

        if (!globalCandidates.TryGetValue(normalizedLookupCode, out var candidates))
        {
            candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            globalCandidates[normalizedLookupCode] = candidates;
        }
        candidates.Add(normalizedProductCode);
    }

    void AddStoreCandidate(string? storeCode, string? lookupCode, string? productCode)
    {
        var normalizedStoreCode = SalesStatisticsCodeRules.Normalize(storeCode);
        var normalizedLookupCode = SalesStatisticsCodeRules.Normalize(lookupCode);
        var normalizedProductCode = SalesStatisticsCodeRules.Normalize(productCode);
        if (string.IsNullOrWhiteSpace(normalizedStoreCode)
            || string.IsNullOrWhiteSpace(normalizedLookupCode)
            || string.IsNullOrWhiteSpace(normalizedProductCode))
        {
            return;
        }

        var branchKey = (
            normalizedStoreCode.ToUpperInvariant(),
            normalizedLookupCode.ToUpperInvariant()
        );
        if (!branchCandidates.TryGetValue(branchKey, out var candidates))
        {
            candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            branchCandidates[branchKey] = candidates;
        }
        candidates.Add(normalizedProductCode);

        if (!crossStoreCandidates.TryGetValue(normalizedLookupCode, out var crossStore))
        {
            crossStore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            crossStoreCandidates[normalizedLookupCode] = crossStore;
        }
        crossStore.Add(normalizedProductCode);
    }

    foreach (var lookupCodeBatch in globalLookupCodes.Chunk(StoreCostProductQueryBatchSize))
    {
        // 只查询当前缺码行携带的货号/条码，分批限制 IN 条件大小，避免不受控参数量。
        var batch = lookupCodeBatch.ToList();
        var productRows = await context.Db.Queryable<Product>()
            .Where(product =>
                product.IsDeleted == false
                && product.IsActive
                && product.ProductCode != null
                && (
                    (product.ItemNumber != null && batch.Contains(product.ItemNumber))
                    || (product.Barcode != null && batch.Contains(product.Barcode))
                )
            )
            .Select(product => new { product.ItemNumber, product.Barcode, product.ProductCode })
            .ToListAsync();
        foreach (var product in productRows)
        {
            AddGlobalCandidate(product.ItemNumber, product.ProductCode);
            AddGlobalCandidate(product.Barcode, product.ProductCode);
        }
    }

    foreach (var barcodeBatch in barcodeLookupCodes.Chunk(StoreCostProductQueryBatchSize))
    {
        // ProductSetCode 和 StoreMultiCodeProduct 都以条码为强键，后者还必须匹配分店。
        var batch = barcodeBatch.ToList();
        var productSetCodeRows = await context.Db.Queryable<ProductSetCode>()
            .Where(setCode =>
                setCode.IsDeleted == false
                && setCode.IsActive
                && setCode.ProductCode != null
                && setCode.SetBarcode != null
                && batch.Contains(setCode.SetBarcode)
            )
            .Select(setCode => new { setCode.SetBarcode, setCode.ProductCode })
            .ToListAsync();
        foreach (var setCode in productSetCodeRows)
        {
            AddGlobalCandidate(setCode.SetBarcode, setCode.ProductCode);
        }

        var storeMultiCodeRows = await context.Db.Queryable<StoreMultiCodeProduct>()
            .Where(multiCode =>
                multiCode.IsDeleted == false
                && multiCode.IsActive
                && multiCode.StoreCode != null
                && multiCode.ProductCode != null
                && multiCode.MultiBarcode != null
                && batch.Contains(multiCode.MultiBarcode)
            )
            .Select(multiCode => new
            {
                multiCode.StoreCode,
                multiCode.MultiBarcode,
                multiCode.ProductCode,
            })
            .ToListAsync();
        foreach (var multiCode in storeMultiCodeRows)
        {
            AddStoreCandidate(multiCode.StoreCode, multiCode.MultiBarcode, multiCode.ProductCode);
        }
    }

    foreach (var row in rowsToResolve)
    {
        var barcode = SalesStatisticsCodeRules.Normalize(row.Barcode);
        var branchKey = (
            SalesStatisticsCodeRules.Normalize(row.BranchCode).ToUpperInvariant(),
            barcode.ToUpperInvariant()
        );
        if (!string.IsNullOrWhiteSpace(barcode)
            && branchCandidates.TryGetValue(branchKey, out var branch))
        {
            // 第一层命中后不可再被全局候选推翻；多候选直接保留为空，不能降级。
            if (branch.Count == 1)
            {
                row.ProductCode = SalesStatisticsProductStoreDailyDomainRules
                    .SelectDeterministicProductCode(branch);
            }
            continue;
        }

        var global = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lookupCode in new[]
            {
                SalesStatisticsCodeRules.Normalize(row.ItemNumber),
                SalesStatisticsCodeRules.Normalize(row.Barcode),
            }
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (globalCandidates.TryGetValue(lookupCode, out var candidates))
            {
                global.UnionWith(candidates);
            }
        }

        // 第二层将 Product 的货号/条码候选与 ProductSetCode 条码候选合并；冲突同样不能降级。
        if (global.Count == 1)
        {
            row.ProductCode = SalesStatisticsProductStoreDailyDomainRules
                .SelectDeterministicProductCode(global);
            continue;
        }
        if (global.Count > 1 || string.IsNullOrWhiteSpace(barcode))
        {
            continue;
        }

        // 仅前两层均无候选时，才允许跨分店多码条码回退；多候选保持空并由原子路径失败。
        if (crossStoreCandidates.TryGetValue(barcode, out var crossStore)
            && crossStore.Count == 1)
        {
            row.ProductCode = SalesStatisticsProductStoreDailyDomainRules
                .SelectDeterministicProductCode(crossStore);
        }
    }
}

}
