using System.Data;
using System.Data.Common;
using System.Text.Json;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBSalesRecord;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Data.SqlClient;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

/// <summary>
/// 批量货号销量的事实读取边界。每个来源都在 SQL Server 端按日期、分店、商品和折扣类别聚合；
/// 不把交易明细搬回 API 进程，也不为每个商品或门店重新查询。
/// </summary>
internal sealed class BatchProductSalesAnalysisFactReader
{
    private readonly ISqlSugarClient _catalogDb;
    private readonly ISqlSugarClient _posmDb;
    private readonly ISqlSugarClient _hbSalesDb;

    internal BatchProductSalesAnalysisFactReader(
        ISqlSugarClient catalogDb,
        ISqlSugarClient posmDb,
        ISqlSugarClient hbSalesDb)
    {
        _catalogDb = catalogDb;
        _posmDb = posmDb;
        _hbSalesDb = hbSalesDb;
    }

    internal async Task<List<BatchProductSalesAggregateRow>> ReadAsync(
        IReadOnlyCollection<string> productCodes,
        DateTime startDate,
        DateTime endDate,
        IReadOnlyCollection<string> storeCodes,
        CancellationToken cancellationToken)
    {
        var products = Normalize(productCodes);
        var stores = Normalize(storeCodes);
        if (products.Count == 0 || stores.Count == 0)
            return [];
        EnsureSqlServer(_posmDb, "POSM");
        EnsureSqlServer(_hbSalesDb, "HBSales");
        EnsureSqlServer(_catalogDb, "HBweb");

        var endExclusive = endDate.Date.AddDays(1);
        cancellationToken.ThrowIfCancellationRequested();
        var posmTask = ReadPosmAsync(products, stores, startDate.Date, endExclusive, cancellationToken);
        var hbTask = startDate.Year <= 2025 && endDate.Year >= 2025
            ? ReadHBSalesAsync(products, stores, Max(startDate.Date, new DateTime(2025, 1, 1)),
                Min(endDate.Date, new DateTime(2025, 12, 31)).AddDays(1), cancellationToken)
            : Task.FromResult(new List<SqlAggregateRow>());
        await Task.WhenAll(posmTask, hbTask);
        cancellationToken.ThrowIfCancellationRequested();

        // 两个数据库不能 UNION；这里只合并已经在各自 SQL 端聚合的至多日期×门店×商品×类别行。
        return posmTask.Result.Concat(hbTask.Result)
            .GroupBy(row => new { row.Date, BranchCode = row.BranchCode.ToUpperInvariant(), ProductCode = row.ProductCode.ToUpperInvariant(), row.DiscountKind })
            .Select(group => ToAggregateRow(group.Key.Date, group.Key.BranchCode, group.Key.ProductCode, group.Key.DiscountKind, group))
            .OrderBy(row => row.Date).ThenBy(row => row.BranchCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ProductCode, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<List<SqlAggregateRow>> ReadPosmAsync(
        IReadOnlyList<string> products, IReadOnlyList<string> stores, DateTime startDate, DateTime endExclusive,
        CancellationToken cancellationToken)
    {
        const string sql = """
WITH ProductScope AS (SELECT [value] AS ProductCode FROM OPENJSON(@products)),
StoreScope AS (SELECT [value] AS BranchCode FROM OPENJSON(@stores)),
DeviceBranch AS (
    SELECT UPPER(LTRIM(RTRIM([系统设备编号]))) AS DeviceCode,
           MAX(NULLIF(LTRIM(RTRIM([分店代码])), '')) AS BranchCode
    FROM [POSM_设备注册信息表]
    WHERE [系统设备编号] IS NOT NULL
    GROUP BY UPPER(LTRIM(RTRIM([系统设备编号])))
),
EligibleOrders AS (
    SELECT o.[OrderGuid], o.[OrderTime], o.[BranchCode], o.[DeviceCode]
    FROM [sales_order] o
    LEFT JOIN DeviceBranch db ON db.DeviceCode = UPPER(LTRIM(RTRIM(o.[DeviceCode])))
    INNER JOIN StoreScope ss ON ss.BranchCode = COALESCE(NULLIF(LTRIM(RTRIM(o.[BranchCode])), ''), db.BranchCode)
    WHERE o.[Status] IN (1, 4) AND o.[OrderTime] >= @startDate AND o.[OrderTime] < @endExclusive
),
TargetSaleOrders AS (
    -- 只保留实际含目标商品的订单；随后仍读取该订单全部行，保证支付分摊分母正确。
    SELECT DISTINCT o.[OrderGuid]
    FROM EligibleOrders o INNER JOIN [sales_order_detail] d ON d.[OrderGuid] = o.[OrderGuid]
    INNER JOIN ProductScope ps ON ps.ProductCode = LTRIM(RTRIM(d.[ProductCode]))
),
PaymentTotals AS (
    SELECT p.[OrderGuid], SUM(COALESCE(p.[Amount], 0)) AS PaymentAmount
    FROM [payment_detail] p INNER JOIN TargetSaleOrders o ON o.[OrderGuid] = p.[OrderGuid]
    GROUP BY p.[OrderGuid]
),
AllDetails AS (
    SELECT o.[OrderGuid], o.[OrderTime], o.[BranchCode], o.[DeviceCode], d.[OrderDetailGuid],
           d.[ProductCode], d.[Quantity], d.[ActualAmount], d.[Price], d.[Subtotal], d.[DiscountAmount], d.[DiscountRate],
           SUM(COALESCE(d.[ActualAmount], 0)) OVER (PARTITION BY o.[OrderGuid]) AS OrderDetailAmount
    FROM EligibleOrders o INNER JOIN TargetSaleOrders tso ON tso.[OrderGuid] = o.[OrderGuid]
    INNER JOIN [sales_order_detail] d ON d.[OrderGuid] = o.[OrderGuid]
),
SaleFacts AS (
    SELECT CONVERT(date, d.[OrderTime]) AS [Date],
           COALESCE(NULLIF(LTRIM(RTRIM(d.[BranchCode])), ''), db.BranchCode) AS BranchCode,
           LTRIM(RTRIM(d.[ProductCode])) AS ProductCode,
           COALESCE(d.[Quantity], 0) AS Quantity,
           CASE WHEN pt.PaymentAmount IS NULL OR d.OrderDetailAmount = 0 THEN CAST(0 AS decimal(19,4))
                ELSE pt.PaymentAmount * COALESCE(d.[ActualAmount], 0) / d.OrderDetailAmount END AS SalesAmount,
           d.[Price], d.[ActualAmount], d.[Subtotal], d.[DiscountAmount], d.[DiscountRate],
           CAST(CASE WHEN COALESCE(d.[Quantity], 0) < 0 OR COALESCE(d.[ActualAmount], 0) < 0 THEN 1 ELSE 0 END AS bit) AS IsReturn
    FROM AllDetails d
    LEFT JOIN PaymentTotals pt ON pt.[OrderGuid] = d.[OrderGuid]
    LEFT JOIN DeviceBranch db ON db.DeviceCode = UPPER(LTRIM(RTRIM(d.[DeviceCode])))
    INNER JOIN ProductScope ps ON ps.ProductCode = LTRIM(RTRIM(d.[ProductCode]))
),
ReturnFacts AS (
    SELECT CONVERT(date, o.[OrderTime]) AS [Date],
           COALESCE(NULLIF(LTRIM(RTRIM(o.[BranchCode])), ''), db.BranchCode) AS BranchCode,
           LTRIM(RTRIM(COALESCE(NULLIF(LTRIM(RTRIM(r.[ProductCode])), ''), od.[ProductCode]))) AS ProductCode,
           -ABS(COALESCE(r.[ReturnQuantity], 0)) AS Quantity,
           -ABS(COALESCE(r.[ReturnAmount], 0)) AS SalesAmount,
           CASE WHEN LTRIM(RTRIM(r.[OriginalOrderGuid])) = LTRIM(RTRIM(od.[OrderGuid]))
                     AND (NULLIF(LTRIM(RTRIM(r.[ProductCode])), '') IS NULL OR NULLIF(LTRIM(RTRIM(od.[ProductCode])), '') IS NULL OR LTRIM(RTRIM(r.[ProductCode])) = LTRIM(RTRIM(od.[ProductCode])))
                THEN od.[Price] END AS Price,
           CASE WHEN LTRIM(RTRIM(r.[OriginalOrderGuid])) = LTRIM(RTRIM(od.[OrderGuid]))
                     AND (NULLIF(LTRIM(RTRIM(r.[ProductCode])), '') IS NULL OR NULLIF(LTRIM(RTRIM(od.[ProductCode])), '') IS NULL OR LTRIM(RTRIM(r.[ProductCode])) = LTRIM(RTRIM(od.[ProductCode])))
                THEN od.[ActualAmount] END AS OriginalActualAmount,
           CASE WHEN LTRIM(RTRIM(r.[OriginalOrderGuid])) = LTRIM(RTRIM(od.[OrderGuid]))
                     AND (NULLIF(LTRIM(RTRIM(r.[ProductCode])), '') IS NULL OR NULLIF(LTRIM(RTRIM(od.[ProductCode])), '') IS NULL OR LTRIM(RTRIM(r.[ProductCode])) = LTRIM(RTRIM(od.[ProductCode])))
                THEN od.[Quantity] END AS OriginalQuantity,
           CASE WHEN LTRIM(RTRIM(r.[OriginalOrderGuid])) = LTRIM(RTRIM(od.[OrderGuid]))
                     AND (NULLIF(LTRIM(RTRIM(r.[ProductCode])), '') IS NULL OR NULLIF(LTRIM(RTRIM(od.[ProductCode])), '') IS NULL OR LTRIM(RTRIM(r.[ProductCode])) = LTRIM(RTRIM(od.[ProductCode])))
                THEN od.[Subtotal] END AS Subtotal,
           CASE WHEN LTRIM(RTRIM(r.[OriginalOrderGuid])) = LTRIM(RTRIM(od.[OrderGuid]))
                     AND (NULLIF(LTRIM(RTRIM(r.[ProductCode])), '') IS NULL OR NULLIF(LTRIM(RTRIM(od.[ProductCode])), '') IS NULL OR LTRIM(RTRIM(r.[ProductCode])) = LTRIM(RTRIM(od.[ProductCode])))
                THEN od.[DiscountAmount] END AS DiscountAmount,
           CASE WHEN LTRIM(RTRIM(r.[OriginalOrderGuid])) = LTRIM(RTRIM(od.[OrderGuid]))
                     AND (NULLIF(LTRIM(RTRIM(r.[ProductCode])), '') IS NULL OR NULLIF(LTRIM(RTRIM(od.[ProductCode])), '') IS NULL OR LTRIM(RTRIM(r.[ProductCode])) = LTRIM(RTRIM(od.[ProductCode])))
                THEN od.[DiscountRate] END AS DiscountRate,
           CAST(1 AS bit) AS IsReturn
    FROM [sales_return_record] r
    INNER JOIN EligibleOrders o ON o.[OrderGuid] = r.[ReturnOrderGuid]
    LEFT JOIN [sales_order_detail] od ON od.[OrderDetailGuid] = r.[OriginalOrderDetailGuid]
    LEFT JOIN DeviceBranch db ON db.DeviceCode = UPPER(LTRIM(RTRIM(o.[DeviceCode])))
    INNER JOIN ProductScope ps ON ps.ProductCode = LTRIM(RTRIM(COALESCE(NULLIF(LTRIM(RTRIM(r.[ProductCode])), ''), od.[ProductCode])))
    WHERE NULLIF(LTRIM(RTRIM(r.[ReturnDetailGuid])), '') IS NULL
       OR NOT EXISTS (SELECT 1 FROM [sales_order_detail] ad WHERE ad.[OrderDetailGuid] = r.[ReturnDetailGuid])
),
Facts AS (
    SELECT s.[Date], s.BranchCode, s.ProductCode, s.Quantity, s.SalesAmount, s.[Price], s.[ActualAmount],
           NULL AS OriginalActualAmount, NULL AS OriginalQuantity, s.[Subtotal], s.[DiscountAmount], s.[DiscountRate], s.IsReturn FROM SaleFacts s
    UNION ALL
    SELECT r.[Date], r.BranchCode, r.ProductCode, r.Quantity, r.SalesAmount, r.[Price], NULL,
           r.OriginalActualAmount, r.OriginalQuantity, r.[Subtotal], r.[DiscountAmount], r.[DiscountRate], r.IsReturn FROM ReturnFacts r
),
Classified AS (
    SELECT f.*,
      CASE
        WHEN f.IsReturn = 1 AND (f.[Price] IS NULL OR f.OriginalQuantity IS NULL OR f.OriginalQuantity = 0
             OR ABS(ABS(f.SalesAmount) / NULLIF(ABS(f.Quantity), 0) - ABS(f.OriginalActualAmount) / NULLIF(ABS(f.OriginalQuantity), 0)) > 0.0001) THEN 2
        WHEN f.[DiscountAmount] IS NULL AND f.[DiscountRate] IS NULL AND f.[Subtotal] IS NULL THEN 2
        WHEN COALESCE(f.[DiscountAmount], 0) <> 0 OR COALESCE(f.[DiscountRate], 0) <> 0
             OR (f.[Subtotal] IS NOT NULL AND ABS(COALESCE(f.[ActualAmount], f.OriginalActualAmount, 0)) < ABS(f.[Subtotal])) THEN 1
        ELSE 0 END AS DiscountKind
    FROM Facts f INNER JOIN StoreScope ss ON ss.BranchCode = f.BranchCode
)
SELECT [Date], BranchCode, ProductCode, DiscountKind,
       SUM(Quantity) AS Quantity, SUM(SalesAmount) AS SalesAmount,
       SUM(CASE WHEN IsReturn = 1 THEN ABS(Quantity) ELSE 0 END) AS ReturnQuantity,
       SUM(CASE WHEN DiscountKind = 2 THEN 1 ELSE 0 END) AS UnknownRowCount,
       MIN(CASE WHEN DiscountKind <> 2 AND [Price] > 0 THEN [Price] END) AS OriginalPriceMin,
       MAX(CASE WHEN DiscountKind <> 2 AND [Price] > 0 THEN [Price] END) AS OriginalPriceMax,
       MIN(CASE WHEN DiscountKind = 1 AND Quantity <> 0 THEN ABS(SalesAmount / Quantity) END) AS DiscountPriceMin,
       MAX(CASE WHEN DiscountKind = 1 AND Quantity <> 0 THEN ABS(SalesAmount / Quantity) END) AS DiscountPriceMax
FROM Classified
WHERE BranchCode IS NOT NULL AND BranchCode <> '' AND ProductCode IS NOT NULL AND ProductCode <> ''
GROUP BY [Date], BranchCode, ProductCode, DiscountKind;
""";
        var returnTableName = _posmDb.EntityMaintenance.GetTableName(typeof(SalesReturnRecord));
        var hasReturnTable = _posmDb.DbMaintenance.GetTableInfoList(false)
            .Any(table => string.Equals(table.Name, returnTableName, StringComparison.OrdinalIgnoreCase));
        var effectiveSql = hasReturnTable ? sql : sql.Replace("[sales_return_record] r", """
(SELECT CAST(NULL AS nvarchar(100)) [ReturnDetailGuid], CAST(NULL AS nvarchar(100)) [ReturnOrderGuid],
        CAST(NULL AS nvarchar(100)) [OriginalOrderGuid], CAST(NULL AS nvarchar(100)) [OriginalOrderDetailGuid],
        CAST(NULL AS nvarchar(100)) [ProductCode], CAST(NULL AS decimal(19,4)) [ReturnQuantity],
        CAST(NULL AS decimal(19,4)) [ReturnAmount] WHERE 1 = 0) r
""", StringComparison.Ordinal);
        return await ExecuteAggregateQueryAsync(_posmDb, effectiveSql,
            [new("@products", JsonSerializer.Serialize(products), SqlDbType.NVarChar),
             new("@stores", JsonSerializer.Serialize(stores), SqlDbType.NVarChar),
             new("@startDate", startDate, SqlDbType.DateTime2), new("@endExclusive", endExclusive, SqlDbType.DateTime2)],
            cancellationToken);
    }

    private async Task<List<SqlAggregateRow>> ReadHBSalesAsync(IReadOnlyList<string> products,
        IReadOnlyList<string> stores, DateTime startDate, DateTime endExclusive, CancellationToken cancellationToken)
    {
        // 先从本次范围内缺产品码的 HBSales 明细取得实际货号/条码，再做目录消歧。
        // 不能从当前 Product 的条码反推历史别名，否则会漏掉 ProductSetCode/一品多码。
        var aliases = await BuildHBSalesAliasesAsync(products, stores, cancellationToken);
        const string sql = """
WITH ProductScope AS (SELECT [value] AS ProductCode FROM OPENJSON(@products)),
StoreScope AS (SELECT [value] AS BranchCode FROM OPENJSON(@stores)),
Aliases AS (SELECT Alias, BranchCode, ProductCode, Scope FROM OPENJSON(@aliases)
    WITH (Alias nvarchar(100), BranchCode nvarchar(100), ProductCode nvarchar(100), Scope nvarchar(16))),
Raw AS (
 SELECT CONVERT(date, d.[B结账日期]) [Date], LTRIM(RTRIM(d.[B分店代码])) BranchCode,
        LTRIM(RTRIM(d.[B产品编号])) RawProductCode, d.[B货号] ItemNumber, d.[B条形码] Barcode,
        d.[B数量] Quantity, d.[B合计金额] SalesAmount,
        CASE WHEN LTRIM(RTRIM(m.[B单据类型])) IN ('3','4') THEN origEvidence.[B单价] ELSE d.[B单价] END OriginalPrice,
        CASE WHEN LTRIM(RTRIM(m.[B单据类型])) IN ('3','4') THEN origEvidence.[B原价合计金额] ELSE d.[B原价合计金额] END OriginalAmount,
        CASE WHEN LTRIM(RTRIM(m.[B单据类型])) IN ('3','4') THEN origEvidence.[B合计金额] ELSE d.[B合计金额] END OriginalSaleAmount,
        CASE WHEN LTRIM(RTRIM(m.[B单据类型])) IN ('3','4') THEN origEvidence.[B数量] ELSE d.[B数量] END OriginalSaleQuantity,
        CASE WHEN LTRIM(RTRIM(m.[B单据类型])) IN ('3','4') THEN origEvidence.[B折扣率] ELSE d.[B折扣率] END DiscountRate,
        CASE WHEN LTRIM(RTRIM(m.[B单据类型])) IN ('3','4') THEN origEvidence.CandidateCount ELSE 1 END OriginalCandidateCount,
        origEvidence.OriginalProductCode,
        m.[B单据类型] DocumentType
 FROM [B销售清单主表副本] m INNER JOIN [B销售清单详情表副本] d ON d.[B销售单号] = m.[B销售单号]
 INNER JOIN StoreScope scope ON scope.BranchCode = LTRIM(RTRIM(d.[B分店代码]))
 OUTER APPLY (
    SELECT COUNT(*) CandidateCount, MIN(o.[B产品编号]) OriginalProductCode, MIN(o.[B单价]) [B单价], MIN(o.[B原价合计金额]) [B原价合计金额],
           MIN(o.[B合计金额]) [B合计金额], MIN(o.[B数量]) [B数量], MIN(o.[B折扣率]) [B折扣率]
    FROM [B销售清单详情表副本] o
    WHERE LTRIM(RTRIM(m.[B单据类型])) IN ('3', '4')
      AND LTRIM(RTRIM(o.[B销售单号])) = LTRIM(RTRIM(m.[B原销售单号]))
      AND ((NULLIF(LTRIM(RTRIM(d.[B退货码])), '') IS NOT NULL
               AND LTRIM(RTRIM(o.[B退货码])) = LTRIM(RTRIM(d.[B退货码]))
               AND (NULLIF(LTRIM(RTRIM(d.[B产品编号])), '') IS NULL OR NULLIF(LTRIM(RTRIM(o.[B产品编号])), '') IS NULL OR LTRIM(RTRIM(o.[B产品编号])) = LTRIM(RTRIM(d.[B产品编号]))))
           OR (NULLIF(LTRIM(RTRIM(d.[B退货码])), '') IS NULL
               AND (NULLIF(LTRIM(RTRIM(d.[B产品编号])), '') IS NULL OR LTRIM(RTRIM(o.[B产品编号])) = LTRIM(RTRIM(d.[B产品编号])))
               AND (NULLIF(LTRIM(RTRIM(d.[B条形码])), '') IS NULL OR LTRIM(RTRIM(o.[B条形码])) = LTRIM(RTRIM(d.[B条形码])))))
 ) origEvidence
 WHERE d.[B结账日期] >= @startDate AND d.[B结账日期] < @endExclusive
   AND m.[B结账日期] >= @mainWindowStart AND m.[B结账日期] < @mainWindowEnd
   AND (m.[B单据类型] IS NULL OR LTRIM(RTRIM(m.[B单据类型])) <> '2')
   AND (EXISTS (SELECT 1 FROM ProductScope ps WHERE ps.ProductCode = LTRIM(RTRIM(d.[B产品编号])) )
        OR (NULLIF(LTRIM(RTRIM(d.[B产品编号])), '') IS NULL
            AND EXISTS (SELECT 1 FROM Aliases a WHERE a.Alias = LTRIM(RTRIM(d.[B货号])) OR a.Alias = LTRIM(RTRIM(d.[B条形码])))))
),
Resolved AS (
 SELECT r.*, CASE WHEN NULLIF(LTRIM(RTRIM(r.RawProductCode)), '') IS NOT NULL THEN ps.ProductCode
                  ELSE COALESCE(branchAlias.ProductCode, globalAlias.ProductCode, crossAlias.ProductCode) END ProductCode,
   CASE WHEN branchAlias.CandidateCount > 0 THEN branchAlias.CandidateCount
        WHEN globalAlias.CandidateCount > 0 THEN globalAlias.CandidateCount ELSE crossAlias.CandidateCount END CandidateCount
 FROM Raw r
 LEFT JOIN ProductScope ps ON ps.ProductCode = LTRIM(RTRIM(r.RawProductCode))
 OUTER APPLY (SELECT COUNT(DISTINCT ProductCode) CandidateCount, MIN(ProductCode) ProductCode FROM Aliases a
   WHERE a.Scope = 'branch' AND a.BranchCode = r.BranchCode AND a.Alias = LTRIM(RTRIM(r.Barcode))) branchAlias
 OUTER APPLY (SELECT COUNT(DISTINCT ProductCode) CandidateCount, MIN(ProductCode) ProductCode FROM Aliases a
   WHERE a.Scope = 'global' AND (a.Alias = LTRIM(RTRIM(r.ItemNumber)) OR a.Alias = LTRIM(RTRIM(r.Barcode)))) globalAlias
 OUTER APPLY (SELECT COUNT(DISTINCT ProductCode) CandidateCount, MIN(ProductCode) ProductCode FROM Aliases a
   WHERE a.Scope = 'cross' AND a.Alias = LTRIM(RTRIM(r.Barcode))) crossAlias
),
Classified AS (
 SELECT [Date], BranchCode, ProductCode,
   CASE WHEN LTRIM(RTRIM(DocumentType)) IN ('3','4') THEN -COALESCE(Quantity,0) ELSE COALESCE(Quantity,0) END Quantity,
   CASE WHEN LTRIM(RTRIM(DocumentType)) IN ('3','4') THEN -COALESCE(SalesAmount,0) ELSE COALESCE(SalesAmount,0) END SalesAmount,
   CASE WHEN LTRIM(RTRIM(DocumentType)) IN ('3','4') THEN 1 ELSE 0 END IsReturn,
   OriginalPrice,
   CASE WHEN LTRIM(RTRIM(DocumentType)) IN ('3','4')
             AND (OriginalCandidateCount <> 1 OR OriginalSaleQuantity IS NULL OR OriginalSaleQuantity = 0
                  OR (NULLIF(LTRIM(RTRIM(OriginalProductCode)), '') IS NOT NULL
                      AND LTRIM(RTRIM(OriginalProductCode)) <> LTRIM(RTRIM(ProductCode)))
                  OR Quantity IS NULL OR Quantity = 0
                  OR ABS(ABS(COALESCE(SalesAmount, 0)) / ABS(Quantity)
                         - ABS(COALESCE(OriginalSaleAmount, 0)) / ABS(OriginalSaleQuantity)) > 0.0001) THEN 2
        WHEN OriginalAmount IS NULL AND DiscountRate IS NULL THEN 2
        WHEN COALESCE(DiscountRate,0) <> 0
             OR (OriginalAmount IS NOT NULL AND ABS(COALESCE(OriginalSaleAmount, SalesAmount,0)) < ABS(OriginalAmount)) THEN 1
        ELSE 0 END DiscountKind
 FROM Resolved
 WHERE ProductCode IS NOT NULL AND ProductCode <> ''
   -- 权威 B产品编号 只需精确命中目标商品；别名歧义只能限制缺产品编号行。
   AND (NULLIF(LTRIM(RTRIM(RawProductCode)), '') IS NOT NULL OR CandidateCount IS NULL OR CandidateCount <= 1)
)
SELECT c.[Date], c.BranchCode, c.ProductCode, c.DiscountKind, SUM(Quantity) Quantity, SUM(SalesAmount) SalesAmount,
 SUM(CASE WHEN IsReturn=1 THEN ABS(Quantity) ELSE 0 END) ReturnQuantity,
 SUM(CASE WHEN DiscountKind=2 THEN 1 ELSE 0 END) UnknownRowCount,
 MIN(CASE WHEN DiscountKind<>2 AND OriginalPrice>0 THEN OriginalPrice END) OriginalPriceMin,
 MAX(CASE WHEN DiscountKind<>2 AND OriginalPrice>0 THEN OriginalPrice END) OriginalPriceMax,
 MIN(CASE WHEN DiscountKind=1 AND Quantity<>0 THEN ABS(SalesAmount/Quantity) END) DiscountPriceMin,
 MAX(CASE WHEN DiscountKind=1 AND Quantity<>0 THEN ABS(SalesAmount/Quantity) END) DiscountPriceMax
FROM Classified c INNER JOIN StoreScope s ON s.BranchCode=c.BranchCode
GROUP BY c.[Date], c.BranchCode, c.ProductCode, c.DiscountKind;
""";
        return await ExecuteAggregateQueryAsync(_hbSalesDb, sql,
            [new("@products", JsonSerializer.Serialize(products), SqlDbType.NVarChar), new("@stores", JsonSerializer.Serialize(stores), SqlDbType.NVarChar),
             new("@aliases", JsonSerializer.Serialize(aliases), SqlDbType.NVarChar), new("@startDate", startDate, SqlDbType.DateTime2),
             new("@endExclusive", endExclusive, SqlDbType.DateTime2), new("@mainWindowStart", startDate.AddDays(-7), SqlDbType.DateTime2),
             new("@mainWindowEnd", endExclusive.AddDays(7), SqlDbType.DateTime2)], cancellationToken);
    }

    private async Task<List<AliasRow>> BuildHBSalesAliasesAsync(IReadOnlyList<string> products,
        IReadOnlyList<string> stores, CancellationToken cancellationToken)
    {
        var productScope = JsonSerializer.Serialize(products);
        var target = await ExecuteCatalogRowsAsync(_catalogDb, """
SELECT p.[ProductCode], p.[ItemNumber], p.[Barcode] FROM [Product] p
INNER JOIN OPENJSON(@codes) s ON s.[value] = p.[ProductCode] WHERE p.[IsDeleted] = 0;
""", productScope, cancellationToken, reader => new CatalogProductRow
        {
            ProductCode = GetNullableString(reader, 0), ItemNumber = GetNullableString(reader, 1), Barcode = GetNullableString(reader, 2),
        });
        // 正向读取目标商品的所有三类历史身份，再查同名候选做全局/分店歧义判定；
        // 这一集合与查询商品数绑定，避免为一个商品把全年所有缺码交易拉入内存。
        var targetSetAliases = await ExecuteCatalogRowsAsync(_catalogDb, """
SELECT p.[SetBarcode] FROM [ProductSetCode] p INNER JOIN OPENJSON(@codes) s ON s.[value] = p.[ProductCode]
WHERE p.[IsDeleted] = 0 AND p.[SetBarcode] IS NOT NULL;
""", productScope, cancellationToken, reader => GetNullableString(reader, 0));
        var targetMultiAliases = await ExecuteCatalogRowsAsync(_catalogDb, """
SELECT p.[MultiBarcode] FROM [StoreMultiCodeProduct] p INNER JOIN OPENJSON(@codes) s ON s.[value] = p.[ProductCode]
WHERE p.[IsDeleted] = 0 AND p.[MultiBarcode] IS NOT NULL;
""", productScope, cancellationToken, reader => GetNullableString(reader, 0));
        var aliases = target.SelectMany(p => new[] { p.ItemNumber, p.Barcode }).Concat(targetSetAliases).Concat(targetMultiAliases)
            .Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (aliases.Count == 0) return [];
        // 旧年份缺码可能跨越很多条码；按 500 个代码分批，避免生成无界 IN 参数列表。
        var allProducts = new List<CatalogProductRow>();
        var sets = new List<AliasCatalogRow>();
        var multi = new List<StoreAliasCatalogRow>();
        foreach (var chunk in aliases.Chunk(500))
        {
            var batch = JsonSerializer.Serialize(chunk);
            allProducts.AddRange(await ExecuteCatalogRowsAsync(_catalogDb, """
SELECT p.[ProductCode], p.[ItemNumber], p.[Barcode] FROM [Product] p INNER JOIN OPENJSON(@codes) s
ON s.[value] = p.[ItemNumber] OR s.[value] = p.[Barcode] WHERE p.[IsDeleted] = 0 AND p.[ProductCode] IS NOT NULL;
""", batch, cancellationToken, reader => new CatalogProductRow { ProductCode = GetNullableString(reader, 0), ItemNumber = GetNullableString(reader, 1), Barcode = GetNullableString(reader, 2) }));
            sets.AddRange(await ExecuteCatalogRowsAsync(_catalogDb, """
SELECT p.[SetBarcode], p.[ProductCode] FROM [ProductSetCode] p INNER JOIN OPENJSON(@codes) s ON s.[value] = p.[SetBarcode]
WHERE p.[IsDeleted] = 0 AND p.[ProductCode] IS NOT NULL;
""", batch, cancellationToken, reader => new AliasCatalogRow { Alias = GetNullableString(reader, 0), ProductCode = GetNullableString(reader, 1) }));
            multi.AddRange(await ExecuteCatalogRowsAsync(_catalogDb, """
SELECT p.[MultiBarcode], p.[StoreCode], p.[ProductCode] FROM [StoreMultiCodeProduct] p INNER JOIN OPENJSON(@codes) s ON s.[value] = p.[MultiBarcode]
WHERE p.[IsDeleted] = 0 AND p.[StoreCode] IS NOT NULL AND p.[ProductCode] IS NOT NULL;
""", batch, cancellationToken, reader => new StoreAliasCatalogRow { Alias = GetNullableString(reader, 0), BranchCode = GetNullableString(reader, 1), ProductCode = GetNullableString(reader, 2) }));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var targetSet = products.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<AliasRow>();
        foreach (var value in aliases)
        {
            // 非目标商品也必须参加歧义判定，不能只看命中目标的行。
            var globalAll = allProducts.Where(p => Same(p.ItemNumber, value) || Same(p.Barcode, value)).Select(p => p.ProductCode!).Concat(sets.Where(s => Same(s.Alias, value)).Select(s => s.ProductCode!)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (globalAll.Count == 1 && targetSet.Contains(globalAll[0]))
                result.Add(new AliasRow(value, null, globalAll[0], "global"));
            else if (globalAll.Count > 0)
                // 空值占位使 SQL 不会把已存在的全局歧义降级到跨店多码候选。
                result.Add(new AliasRow(value, null, string.Empty, "global"));
            foreach (var branch in stores)
            {
                var candidates = multi.Where(m => Same(m.Alias, value) && Same(m.BranchCode, branch)).Select(m => m.ProductCode!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (candidates.Count == 1 && targetSet.Contains(candidates[0]))
                    result.Add(new AliasRow(value, branch, candidates[0], "branch"));
                else if (candidates.Count > 0)
                    // 分店多码有候选但不唯一时，稳定口径不允许退回全局候选。
                    result.Add(new AliasRow(value, branch, string.Empty, "branch"));
            }
            var cross = multi.Where(m => Same(m.Alias, value)).Select(m => m.ProductCode!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (globalAll.Count == 0 && cross.Count == 1 && targetSet.Contains(cross[0])) result.Add(new AliasRow(value, null, cross[0], "cross"));
        }
        return result;
    }

    internal static BatchProductSalesAggregateRow ToAggregateRow(DateTime date, string branchCode, string productCode, int kind, IEnumerable<SqlAggregateRow> rows)
    {
        var list = rows.ToList(); var quantity = list.Sum(r => r.Quantity); var amount = list.Sum(r => r.SalesAmount);
        var row = new BatchProductSalesAggregateRow { Date = date.Date, BranchCode = branchCode.Trim(), ProductCode = productCode.Trim(), ReturnQuantity = list.Sum(r => r.ReturnQuantity), SalesAmount = amount,
            UnknownRowCount = list.Sum(r => r.UnknownRowCount), OriginalPriceMin = Min(list.Select(r => r.OriginalPriceMin)), OriginalPriceMax = Max(list.Select(r => r.OriginalPriceMax)), DiscountPriceMin = Min(list.Select(r => r.DiscountPriceMin)), DiscountPriceMax = Max(list.Select(r => r.DiscountPriceMax)) };
        if (kind == 0) row.RegularQuantity = quantity; else if (kind == 1) row.DiscountQuantity = quantity; else row.UnknownQuantity = quantity;
        row.Quantity = row.RegularQuantity + row.DiscountQuantity + row.UnknownQuantity;
        return row;
    }

    /// <summary>与 HBSales 日统计一致：类型 3/4 仅乘 -1，其它单据保留源数据符号。</summary>
    internal static decimal ApplyHBSalesDocumentSign(decimal value, string? documentType) =>
        documentType?.Trim() is "3" or "4" ? -value : value;

    private static async Task<List<SqlAggregateRow>> ExecuteAggregateQueryAsync(
        ISqlSugarClient db, string sql, IReadOnlyList<BatchSqlParameter> parameters, CancellationToken cancellationToken)
    {
        // 不使用 Ado.SqlQueryAsync：该调用不能接收请求取消令牌，且会继承 POSM 的长全局 timeout。
        await using var connection = new SqlConnection(db.CurrentConnectionConfig.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 60;
        foreach (var item in parameters)
        {
            var parameter = new SqlParameter(item.Name, item.Type) { Value = item.Value ?? DBNull.Value };
            if (item.Type == SqlDbType.NVarChar) parameter.Size = -1;
            command.Parameters.Add(parameter);
        }
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<SqlAggregateRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SqlAggregateRow
            {
                Date = reader.GetDateTime(0), BranchCode = reader.GetString(1), ProductCode = reader.GetString(2),
                DiscountKind = reader.GetInt32(3), Quantity = reader.GetDecimal(4), SalesAmount = reader.GetDecimal(5),
                ReturnQuantity = reader.GetDecimal(6), UnknownRowCount = reader.GetInt32(7),
                OriginalPriceMin = GetNullableDecimal(reader, 8), OriginalPriceMax = GetNullableDecimal(reader, 9),
                DiscountPriceMin = GetNullableDecimal(reader, 10), DiscountPriceMax = GetNullableDecimal(reader, 11),
            });
        }
        return rows;
    }

    private static decimal? GetNullableDecimal(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    private static string? GetNullableString(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static async Task<List<T>> ExecuteCatalogRowsAsync<T>(ISqlSugarClient db, string sql, string codesJson,
        CancellationToken cancellationToken, Func<DbDataReader, T> map)
    {
        // 目录别名也必须响应取消；不能在聚合 SQL 可取消后仍由 SqlSugar 的 ToListAsync 长时间占用请求。
        await using var connection = new SqlConnection(db.CurrentConnectionConfig.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 60;
        command.Parameters.Add(new SqlParameter("@codes", SqlDbType.NVarChar, -1) { Value = codesJson });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(map(reader));
        return rows;
    }

    private static List<string> Normalize(IEnumerable<string> values) => values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private static bool Same(string? left, string? right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static decimal? Min(IEnumerable<decimal?> values) { var a = values.Where(v => v.HasValue).Select(v => v!.Value).ToList(); return a.Count == 0 ? null : a.Min(); }
    private static decimal? Max(IEnumerable<decimal?> values) { var a = values.Where(v => v.HasValue).Select(v => v!.Value).ToList(); return a.Count == 0 ? null : a.Max(); }
    private static DateTime Max(DateTime left, DateTime right) => left > right ? left : right;
    private static DateTime Min(DateTime left, DateTime right) => left < right ? left : right;
    private static void EnsureSqlServer(ISqlSugarClient db, string source) { if (db.CurrentConnectionConfig.DbType != SqlSugar.DbType.SqlServer) throw new NotSupportedException($"批量货号销量仅支持 SQL Server {source} 数据源。"); }

    internal sealed class SqlAggregateRow { public DateTime Date { get; set; } public string BranchCode { get; set; } = string.Empty; public string ProductCode { get; set; } = string.Empty; public int DiscountKind { get; set; } public decimal Quantity { get; set; } public decimal SalesAmount { get; set; } public decimal ReturnQuantity { get; set; } public int UnknownRowCount { get; set; } public decimal? OriginalPriceMin { get; set; } public decimal? OriginalPriceMax { get; set; } public decimal? DiscountPriceMin { get; set; } public decimal? DiscountPriceMax { get; set; } }
    private sealed record BatchSqlParameter(string Name, object? Value, SqlDbType Type);
    private sealed class CatalogProductRow { public string? ProductCode { get; set; } public string? ItemNumber { get; set; } public string? Barcode { get; set; } }
    private class AliasCatalogRow { public string? Alias { get; set; } public string? ProductCode { get; set; } }
    private sealed class StoreAliasCatalogRow : AliasCatalogRow { public string? BranchCode { get; set; } }
    private sealed record AliasRow(string Alias, string? BranchCode, string ProductCode, string Scope);
}

/// <summary>一行对应一个折扣类别的 SQL 聚合；UnknownRowCount 使净未知量为零时仍保持 partial/unknown。</summary>
internal sealed class BatchProductSalesAggregateRow
{
    public DateTime Date { get; init; }
    public string BranchCode { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal RegularQuantity { get; set; }
    public decimal DiscountQuantity { get; set; }
    public decimal UnknownQuantity { get; set; }
    public decimal ReturnQuantity { get; set; }
    public decimal SalesAmount { get; set; }
    public int UnknownRowCount { get; set; }
    public decimal? OriginalPriceMin { get; set; }
    public decimal? OriginalPriceMax { get; set; }
    public decimal? DiscountPriceMin { get; set; }
    public decimal? DiscountPriceMax { get; set; }
    public BatchProductSalesMetricsDto Metrics => new()
    {
        Quantity = Quantity, RegularQuantity = RegularQuantity, DiscountQuantity = DiscountQuantity, UnknownQuantity = UnknownQuantity,
        ReturnQuantity = ReturnQuantity, SalesAmount = SalesAmount,
        DiscountStatus = UnknownRowCount > 0 ? (RegularQuantity != 0 || DiscountQuantity != 0 ? "partial" : "unknown") : "complete",
        OriginalPriceMin = OriginalPriceMin, OriginalPriceMax = OriginalPriceMax, DiscountPriceMin = DiscountPriceMin, DiscountPriceMax = DiscountPriceMax,
    };
}
