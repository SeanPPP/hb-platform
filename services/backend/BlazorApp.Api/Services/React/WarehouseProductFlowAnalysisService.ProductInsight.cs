using BlazorApp.Api.Features.ProductInsights;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System.Diagnostics;
using System.Text.Json;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 移动端仓库商品进销查询：单商品一次返回全仓库合计与分店分布。
    /// 与 Web 仓库商品流转分析共用同一组事实表口径，只在"进货是否含在途货柜"上按移动端语义收紧。
    /// </summary>
    public partial class WarehouseProductFlowAnalysisService
    {
        /// <summary>单据明细返回上限；合计始终按全量计算，列表只截断展示以保证移动端首屏预算。</summary>
        private const int InsightMaxDetailRows = 200;
        private const int InsightDeadlockMaxAttempts = 3;

        public async Task<ApiResponse<WarehouseProductInsightDto>> GetProductInsightAsync(
            WarehouseProductInsightQuery query,
            List<string>? branchCodes
        )
        {
            ArgumentNullException.ThrowIfNull(query);
            var productCode = query.ProductCode?.Trim() ?? string.Empty;
            if (productCode.Length == 0)
                throw new ArgumentException("商品编码不能为空。", nameof(query));

            var startDate = query.StartDate.Date;
            var endDate = query.EndDate.Date;
            if (!WarehouseProductInsightRules.IsChronological(startDate, endDate))
                throw new ArgumentException("开始日期不能晚于结束日期。", nameof(query));
            if (!WarehouseProductInsightRules.IsWithinMaxRange(startDate, endDate))
                throw new ArgumentException(
                    $"查询区间不能超过 {WarehouseProductInsightRules.MaxRangeDays} 天。",
                    nameof(query)
                );

            var cacheKey = BuildProductInsightCacheKey(productCode, startDate, endDate, branchCodes);
            return await GetOrCreateAsync(
                cacheKey,
                query.ForceRefresh,
                () => BuildProductInsightAsync(productCode, startDate, endDate, branchCodes)
            );
        }

        private async Task<WarehouseProductInsightDto> BuildProductInsightAsync(
            string productCode,
            DateTime startDate,
            DateTime endDate,
            List<string>? branchCodes
        )
        {
            var stopwatch = Stopwatch.StartNew();
            // 空授权集合表示当前用户没有任何分店，直接跳过分店相关查询而不是扫全表。
            var noAuthorizedStore = branchCodes is { Count: 0 };
            // 货柜进货固定看最近一年：仓库靠库存供货，查询区间内常常没有到货货柜。
            var inboundStartDate = WarehouseProductInsightRules.InboundStartDate(endDate);

            (InsightCatalogSegment Value, long ElapsedMs) catalog;
            (InsightOrderShipmentSegment Value, long ElapsedMs) movements;
            (InsightSalesSegment Value, long ElapsedMs) sales;
            Dictionary<string, string> storeNames;
            if (_context.Db.CurrentConnectionConfig.DbType == DbType.SqlServer)
            {
                // 同一 scoped SqlSugar 连接不能并发读；三段只读查询各用 CopyNew 出的独立连接并行执行，
                // 是本仓库既有的并行姿势，也是移动端首屏 3 秒预算的主要保障。
                using var catalogDb = _context.Db.CopyNew();
                using var movementDb = _context.Db.CopyNew();
                using var salesDb = _context.Db.CopyNew();
                using var storeDb = _context.Db.CopyNew();
                var catalogTask = TimeInsightSegmentAsync(
                    () => QueryInsightCatalogAsync(catalogDb, productCode, inboundStartDate, endDate)
                );
                var movementTask = TimeInsightSegmentAsync(
                    () => QueryInsightMovementsAsync(movementDb, productCode, branchCodes, startDate, endDate)
                );
                var salesTask = TimeInsightSegmentAsync(
                    () =>
                        noAuthorizedStore
                            ? Task.FromResult(new InsightSalesSegment())
                            : QueryInsightSalesAsync(salesDb, productCode, branchCodes, startDate, endDate)
                );
                // 门店名映射只依赖门店主档，不依赖前三段结果，必须一起并行；
                // 否则它会串在并行之后，把总耗时抬高近一秒。
                var storeTask = QueryInsightStoreNamesAsync(storeDb);
                await Task.WhenAll(catalogTask, movementTask, salesTask, storeTask);
                catalog = await catalogTask;
                movements = await movementTask;
                sales = await salesTask;
                storeNames = await storeTask;
            }
            else
            {
                // SQLite 内存测试库不能复制连接，保持确定性的串行执行。
                catalog = await TimeInsightSegmentAsync(
                    () => QueryInsightCatalogAsync(_context.Db, productCode, inboundStartDate, endDate)
                );
                movements = await TimeInsightSegmentAsync(
                    () => QueryInsightMovementsAsync(_context.Db, productCode, branchCodes, startDate, endDate)
                );
                sales = await TimeInsightSegmentAsync(
                    () =>
                        noAuthorizedStore
                            ? Task.FromResult(new InsightSalesSegment())
                            : QueryInsightSalesAsync(_context.Db, productCode, branchCodes, startDate, endDate)
                );
                storeNames = await QueryInsightStoreNamesAsync(_context.Db);
            }

            var result = ComposeProductInsight(
                productCode,
                startDate,
                endDate,
                branchCodes,
                inboundStartDate,
                catalog.Value,
                movements.Value,
                sales.Value,
                storeNames
            );
            stopwatch.Stop();
            // 分段耗时用于定位缺失索引：三段并行时总耗时约等于最慢的一段。
            _logger.LogInformation(
                "仓库商品进销查询完成 ProductCode={ProductCode} Days={Days} Branches={Branches} Containers={Containers} "
                    + "CatalogMs={CatalogMs} MovementMs={MovementMs} SalesMs={SalesMs} ElapsedMs={ElapsedMs}",
                productCode,
                result.Range.DayCount,
                result.Branches.Count,
                result.Containers.Count,
                catalog.ElapsedMs,
                movements.ElapsedMs,
                sales.ElapsedMs,
                stopwatch.ElapsedMilliseconds
            );
            return result;
        }

        private static WarehouseProductInsightDto ComposeProductInsight(
            string productCode,
            DateTime startDate,
            DateTime endDate,
            List<string>? branchCodes,
            DateTime inboundStartDate,
            InsightCatalogSegment catalog,
            InsightOrderShipmentSegment movements,
            InsightSalesSegment sales,
            Dictionary<string, string> storeNames
        )
        {
            var product = catalog.Product ?? new WarehouseProductInsightProductDto { ProductCode = productCode };

            var orderedByStore = SumByStore(movements.Orders);
            var shippedByStore = SumByStore(movements.Shipments);
            var salesByStore = sales
                .Rows.GroupBy(row => row.BranchCode.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (Quantity: group.Sum(row => row.Quantity), Amount: group.Sum(row => row.Amount)),
                    StringComparer.OrdinalIgnoreCase
                );

            var branches = orderedByStore
                .Keys.Concat(shippedByStore.Keys)
                .Concat(salesByStore.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(storeCode =>
                {
                    var ordered = orderedByStore.GetValueOrDefault(storeCode);
                    var shipped = shippedByStore.GetValueOrDefault(storeCode);
                    salesByStore.TryGetValue(storeCode, out var sale);
                    return new WarehouseProductInsightBranchDto
                    {
                        StoreCode = storeCode,
                        StoreName = storeNames.GetValueOrDefault(storeCode) ?? storeCode,
                        OrderedQuantity = ordered,
                        ShippedQuantity = shipped,
                        PendingQuantity = WarehouseProductInsightRules.PendingQuantity(ordered, shipped),
                        SalesQuantity = sale.Quantity,
                        SalesAmount = sale.Amount,
                        SellThroughRate = WarehouseProductInsightRules.SellThroughRate(shipped, sale.Quantity),
                    };
                })
                .OrderByDescending(row => row.SalesQuantity)
                .ThenByDescending(row => row.ShippedQuantity)
                .ThenBy(row => row.StoreCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var arrived = catalog.Containers.Where(row => !row.IsEstimatedArrival).ToList();
            var inTransit = catalog.Containers.Where(row => row.IsEstimatedArrival).ToList();
            var totals = new WarehouseProductInsightTotalsDto
            {
                InboundQuantity = arrived.Sum(row => row.Quantity),
                ContainerCount = arrived.Count,
                InTransitQuantity = inTransit.Sum(row => row.Quantity),
                InTransitContainerCount = inTransit.Count,
                OrderedQuantity = branches.Sum(row => row.OrderedQuantity),
                OrderedStoreCount = branches.Count(row => row.OrderedQuantity > 0),
                OrderDocumentCount = movements.Orders.Count,
                ShippedQuantity = branches.Sum(row => row.ShippedQuantity),
                ShippedStoreCount = branches.Count(row => row.ShippedQuantity > 0),
                ShipmentDocumentCount = movements.Shipments.Count,
                SalesQuantity = branches.Sum(row => row.SalesQuantity),
                SalesAmount = branches.Sum(row => row.SalesAmount),
                SalesStoreCount = branches.Count(row => row.SalesQuantity > 0),
            };
            totals.PendingQuantity = branches.Sum(row => row.PendingQuantity);
            totals.PendingStoreCount = branches.Count(row => row.PendingQuantity > 0);

            return new WarehouseProductInsightDto
            {
                Range = new WarehouseProductInsightRangeDto
                {
                    StartDate = startDate.ToString("yyyy-MM-dd"),
                    EndDate = endDate.ToString("yyyy-MM-dd"),
                    DayCount = WarehouseProductInsightRules.CountDays(startDate, endDate),
                },
                InboundRange = new WarehouseProductInsightRangeDto
                {
                    StartDate = inboundStartDate.ToString("yyyy-MM-dd"),
                    EndDate = endDate.ToString("yyyy-MM-dd"),
                    DayCount = WarehouseProductInsightRules.CountDays(inboundStartDate, endDate),
                },
                // 响应生成时间与统计更新时间是两回事，不能互相冒充。
                GeneratedAt = DateTime.UtcNow,
                SalesStatisticLastUpdatedAt = sales.LastUpdatedAt,
                Scope = branchCodes == null ? "all-stores" : "authorized-stores",
                Product = product,
                Totals = totals,
                Branches = branches,
                Containers = catalog
                    .Containers.OrderByDescending(row => row.ArrivalDate)
                    .ThenBy(row => row.ContainerNumber, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Orders = TakeRecentMovements(movements.Orders, storeNames),
                Shipments = TakeRecentMovements(movements.Shipments, storeNames),
                DailySales = sales
                    .Rows.GroupBy(row => row.Date.Date)
                    .Select(group => new WarehouseProductInsightDailySalesDto
                    {
                        Date = group.Key,
                        Quantity = group.Sum(row => row.Quantity),
                        Amount = group.Sum(row => row.Amount),
                    })
                    .OrderByDescending(row => row.Date)
                    .ToList(),
            };
        }

        private static Dictionary<string, decimal> SumByStore(IEnumerable<InsightMovementRow> rows) =>
            rows.Where(row => !string.IsNullOrWhiteSpace(row.StoreCode))
                .GroupBy(row => row.StoreCode.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(row => row.Quantity),
                    StringComparer.OrdinalIgnoreCase
                );

        private static List<WarehouseProductInsightMovementDto> TakeRecentMovements(
            List<InsightMovementRow> rows,
            Dictionary<string, string> storeNames
        ) =>
            rows.OrderByDescending(row => row.Date)
                .ThenBy(row => row.DocumentNo, StringComparer.OrdinalIgnoreCase)
                .Take(InsightMaxDetailRows)
                .Select(row => new WarehouseProductInsightMovementDto
                {
                    DocumentNo = row.DocumentNo,
                    StoreCode = row.StoreCode,
                    StoreName = storeNames.GetValueOrDefault(row.StoreCode.Trim()) ?? row.StoreCode,
                    Date = row.Date,
                    Quantity = row.Quantity,
                })
                .ToList();

        /// <summary>商品主档与货柜进货同属小结果集，放在同一连接里串行读取。</summary>
        private async Task<InsightCatalogSegment> QueryInsightCatalogAsync(
            ISqlSugarClient db,
            string productCode,
            DateTime startDate,
            DateTime endDate
        )
        {
            var product = await ExecuteInsightReadAsync(
                async () =>
                    await db.Queryable<Product>()
                        .LeftJoin<WarehouseProduct>((item, warehouse) => item.ProductCode == warehouse.ProductCode)
                        .Where((item, warehouse) => item.ProductCode == productCode && !item.IsDeleted)
                        .Select(
                            (item, warehouse) =>
                                new WarehouseProductInsightProductDto
                                {
                                    ProductCode = item.ProductCode!,
                                    ProductName = item.ProductName,
                                    ItemNumber = item.ItemNumber,
                                    Barcode = item.Barcode,
                                    ProductImage = item.ProductImage,
                                    SupplierCode = item.LocalSupplierCode,
                                    StockQuantity = warehouse.StockQuantity,
                                }
                        )
                        .FirstAsync(),
                "商品主档"
            );
            if (product != null)
            {
                product.LocationCode = await ExecuteInsightReadAsync(
                    async () =>
                        await db.Queryable<ProductLocation>()
                            .LeftJoin<Location>((bind, location) => bind.LocationGuid == location.LocationGuid)
                            .Where((bind, location) => bind.ProductCode == productCode && !bind.IsDeleted && !location.IsDeleted)
                            .OrderBy((bind, location) => location.LocationCode)
                            .Select((bind, location) => location.LocationCode)
                            .FirstAsync(),
                    "商品仓位"
                );
                // 供应商名与商品主档同属小查询，放在同一段里避免并行结束后再串一次往返。
                product.SupplierName = await ExecuteInsightReadAsync(
                    () => QueryInsightSupplierNameAsync(db, productCode),
                    "商品供应商"
                );
            }

            var exclusiveEnd = endDate.AddDays(1);
            var containerRows = await ExecuteInsightReadAsync(
                async () =>
                    await db.Queryable<ContainerDetail>()
                        .InnerJoin<Container>((detail, container) => detail.ContainerCode == container.ContainerCode)
                        .Where(
                            (detail, container) =>
                                detail.ProductCode == productCode
                                && !detail.IsDeleted
                                && !container.IsDeleted
                                // 已取消的货柜与已取消的明细不算进货。
                                && (container.Status == null || container.Status != 7)
                                && (detail.Status == null || detail.Status != 6)
                                && (
                                    (container.ActualArrivalDate != null
                                        && container.ActualArrivalDate >= startDate
                                        && container.ActualArrivalDate < exclusiveEnd)
                                    || (container.ActualArrivalDate == null
                                        && container.EstimatedArrivalDate != null
                                        && container.EstimatedArrivalDate >= startDate
                                        && container.EstimatedArrivalDate < exclusiveEnd)
                                )
                        )
                        .Select(
                            (detail, container) =>
                                new InsightContainerRow
                                {
                                    ContainerNumber = container.ContainerNumber,
                                    ActualArrivalDate = container.ActualArrivalDate,
                                    EstimatedArrivalDate = container.EstimatedArrivalDate,
                                    ContainerStatus = container.Status,
                                    Quantity = detail.LoadingQuantity,
                                    Pieces = detail.LoadingPieces,
                                }
                        )
                        .ToListAsync(),
                "货柜进货"
            );

            var containers = containerRows
                .GroupBy(
                    row => new
                    {
                        Number = row.ContainerNumber ?? string.Empty,
                        row.ActualArrivalDate,
                        row.EstimatedArrivalDate,
                        row.ContainerStatus,
                    }
                )
                .Select(group => new WarehouseProductInsightContainerDto
                {
                    ContainerNumber = group.Key.Number,
                    ArrivalDate = (group.Key.ActualArrivalDate ?? group.Key.EstimatedArrivalDate)!.Value.Date,
                    IsEstimatedArrival = group.Key.ActualArrivalDate == null,
                    Quantity = group.Sum(row => row.Quantity ?? 0m),
                    Pieces = group.Sum(row => row.Pieces ?? 0m),
                    Status = DescribeContainerStatus(group.Key.ContainerStatus, group.Key.ActualArrivalDate != null),
                })
                .ToList();
            return new InsightCatalogSegment(product, containers);
        }

        /// <summary>订货与发货读同一组表，放在同一连接里串行读取。</summary>
        private async Task<InsightOrderShipmentSegment> QueryInsightMovementsAsync(
            ISqlSugarClient db,
            string productCode,
            List<string>? branchCodes,
            DateTime startDate,
            DateTime endDate
        )
        {
            if (branchCodes is { Count: 0 })
                return new InsightOrderShipmentSegment([], []);

            var exclusiveEnd = endDate.AddDays(1);
            var orders = await ExecuteInsightReadAsync(
                async () =>
                {
                    // 订货按订单日归集，口径与 Web 仓库商品流转分析的 QueryOrderRowsAsync 一致。
                    var query = db.Queryable<WareHouseOrderDetails>()
                        .InnerJoin<WareHouseOrder>((detail, order) => detail.OrderGUID == order.OrderGUID)
                        .Where(
                            (detail, order) =>
                                detail.ProductCode == productCode
                                && !detail.IsDeleted
                                && !order.IsDeleted
                                && order.FlowStatus > 0
                                && order.OrderDate >= startDate
                                && order.OrderDate < exclusiveEnd
                        );
                    if (branchCodes is { Count: > 0 })
                        query = query.Where((detail, order) => order.StoreCode != null && branchCodes.Contains(order.StoreCode));
                    return await query
                        .GroupBy((detail, order) => new { order.OrderGUID, order.OrderNo, order.OrderDate, order.StoreCode })
                        .Select(
                            (detail, order) =>
                                new InsightMovementRow
                                {
                                    DocumentNo = order.OrderNo ?? order.OrderGUID,
                                    StoreCode = order.StoreCode ?? string.Empty,
                                    Date = order.OrderDate!.Value,
                                    Quantity = SqlFunc.AggregateSum(detail.Quantity ?? 0m),
                                }
                        )
                        .ToListAsync();
                },
                "分店订货"
            );

            var shipments = await ExecuteInsightReadAsync(
                async () =>
                {
                    // 发货按实际出库日归集，只认已分配数量，口径与 QueryShipmentRowsAsync 一致。
                    var query = db.Queryable<WareHouseOrderDetails>()
                        .InnerJoin<WareHouseOrder>((detail, order) => detail.OrderGUID == order.OrderGUID)
                        .Where(
                            (detail, order) =>
                                detail.ProductCode == productCode
                                && !detail.IsDeleted
                                && !order.IsDeleted
                                && order.OutboundDate != null
                                && detail.AllocQuantity > 0
                                && order.OutboundDate >= startDate
                                && order.OutboundDate < exclusiveEnd
                        );
                    if (branchCodes is { Count: > 0 })
                        query = query.Where((detail, order) => order.StoreCode != null && branchCodes.Contains(order.StoreCode));
                    return await query
                        .GroupBy((detail, order) => new { order.OrderGUID, order.OrderNo, order.OutboundDate, order.StoreCode })
                        .Select(
                            (detail, order) =>
                                new InsightMovementRow
                                {
                                    DocumentNo = order.OrderNo ?? order.OrderGUID,
                                    StoreCode = order.StoreCode ?? string.Empty,
                                    Date = order.OutboundDate!.Value,
                                    Quantity = SqlFunc.AggregateSum(detail.AllocQuantity ?? 0m),
                                }
                        )
                        .ToListAsync();
                },
                "分店发货"
            );
            return new InsightOrderShipmentSegment(orders, shipments);
        }

        /// <summary>POS 日销售统计按分店与业务日在数据库聚合，避免把明细行拉回应用层。</summary>
        private async Task<InsightSalesSegment> QueryInsightSalesAsync(
            ISqlSugarClient db,
            string productCode,
            List<string>? branchCodes,
            DateTime startDate,
            DateTime endDate
        )
        {
            var exclusiveEnd = endDate.AddDays(1);
            var rows = await ExecuteInsightReadAsync(
                async () =>
                {
                    var query = db.Queryable<ProductStoreDailySalesStatistic>()
                        .Where(row => row.ProductCode == productCode && row.Date >= startDate && row.Date < exclusiveEnd);
                    if (branchCodes is { Count: > 0 })
                        query = query.Where(row => branchCodes.Contains(row.BranchCode));
                    return await query
                        .GroupBy(row => new { row.BranchCode, row.Date })
                        .Select(row => new InsightSalesRow
                        {
                            BranchCode = row.BranchCode,
                            Date = row.Date,
                            Quantity = SqlFunc.AggregateSum(row.TotalQuantity),
                            Amount = SqlFunc.AggregateSum(row.TotalAmount),
                            LastUpdatedAt = SqlFunc.AggregateMax(row.UpdateTime),
                        })
                        .ToListAsync();
                },
                "分店销售"
            );
            return new InsightSalesSegment
            {
                Rows = rows,
                LastUpdatedAt = rows.Count == 0 ? null : rows.Max(row => row.LastUpdatedAt),
            };
        }

        /// <summary>
        /// 门店名映射一次取回全部未删除门店。门店主档很小，
        /// 这样它就不依赖订货/发货/销售的结果，可以和三段事实查询一起并行。
        /// </summary>
        private static async Task<Dictionary<string, string>> QueryInsightStoreNamesAsync(ISqlSugarClient db)
        {
            var rows = await db.Queryable<Store>()
                .Where(store => !store.IsDeleted)
                .Select(store => new { store.StoreCode, store.StoreName })
                .ToListAsync();
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var code = row.StoreCode?.Trim();
                if (string.IsNullOrWhiteSpace(code) || map.ContainsKey(code))
                    continue;
                map[code] = string.IsNullOrWhiteSpace(row.StoreName) ? code : row.StoreName.Trim();
            }
            return map;
        }

        /// <summary>供应商名口径与 GetDomesticSupplierNameAsync 一致，只是改为接受指定连接以便并行。</summary>
        private static async Task<string?> QueryInsightSupplierNameAsync(ISqlSugarClient db, string productCode)
        {
            var domestic = await db.Queryable<DomesticProduct>()
                .Where(item => !item.IsDeleted && item.ProductCode == productCode)
                .Select(item => new { item.SupplierCode })
                .FirstAsync();
            if (domestic?.SupplierCode == null)
                return null;

            var supplierCode = domestic.SupplierCode.Trim();
            var suppliers = await db.Queryable<ChinaSupplier>()
                .Where(item => item.SupplierCode != null && item.SupplierCode.Trim() == supplierCode)
                .OrderBy(item => item.IsDeleted ? 1 : 0)
                .OrderBy(item => item.Guid)
                .Select(item => new { item.SupplierName })
                .ToListAsync();
            return suppliers
                    .Select(item => item.SupplierName?.Trim())
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                ?? supplierCode;
        }

        /// <summary>记录单段查询耗时，用于定位缺失索引；不改变任何查询语义。</summary>
        private static async Task<(T Value, long ElapsedMs)> TimeInsightSegmentAsync<T>(Func<Task<T>> read)
        {
            var stopwatch = Stopwatch.StartNew();
            var value = await read();
            stopwatch.Stop();
            return (value, stopwatch.ElapsedMilliseconds);
        }

        /// <summary>只读报表段遇到 SQL Server 死锁牺牲品时重试，不把并发冲突伪装成零数据。</summary>
        private async Task<T> ExecuteInsightReadAsync<T>(Func<Task<T>> read, string segment)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await read();
                }
                catch (SqlException exception) when (exception.Number == 1205 && attempt < InsightDeadlockMaxAttempts)
                {
                    var delay = TimeSpan.FromMilliseconds(200 * attempt);
                    _logger.LogWarning(
                        exception,
                        "仓库商品进销查询 {Segment} 段发生死锁，第 {Attempt}/{MaxAttempts} 次重试将在 {DelayMs}ms 后执行",
                        segment,
                        attempt,
                        InsightDeadlockMaxAttempts,
                        delay.TotalMilliseconds
                    );
                    await Task.Delay(delay);
                }
            }
        }

        private static string DescribeContainerStatus(int? containerStatus, bool hasActualArrival) =>
            containerStatus switch
            {
                6 => "completed",
                5 => "cleared",
                4 => "arrived",
                3 => "shipping",
                _ => hasActualArrival ? "arrived" : "shipping",
            };

        private static string BuildProductInsightCacheKey(
            string productCode,
            DateTime startDate,
            DateTime endDate,
            List<string>? branchCodes
        ) =>
            JsonSerializer.Serialize(
                new object?[]
                {
                    "wfpa",
                    "product-insight",
                    productCode.ToUpperInvariant(),
                    $"{startDate:yyyyMMdd}:{endDate:yyyyMMdd}",
                    BuildBranchScopeCachePart(branchCodes),
                }
            );

        private sealed record InsightCatalogSegment(
            WarehouseProductInsightProductDto? Product,
            List<WarehouseProductInsightContainerDto> Containers
        );

        private sealed record InsightOrderShipmentSegment(
            List<InsightMovementRow> Orders,
            List<InsightMovementRow> Shipments
        );

        private sealed class InsightSalesSegment
        {
            public List<InsightSalesRow> Rows { get; init; } = [];
            public DateTime? LastUpdatedAt { get; init; }
        }

        private sealed class InsightContainerRow
        {
            public string? ContainerNumber { get; set; }
            public DateTime? ActualArrivalDate { get; set; }
            public DateTime? EstimatedArrivalDate { get; set; }
            public int? ContainerStatus { get; set; }
            public decimal? Quantity { get; set; }
            public decimal? Pieces { get; set; }
        }

        private sealed class InsightMovementRow
        {
            public string DocumentNo { get; set; } = string.Empty;
            public string StoreCode { get; set; } = string.Empty;
            public DateTime Date { get; set; }
            public decimal Quantity { get; set; }
        }

        private sealed class InsightSalesRow
        {
            public string BranchCode { get; set; } = string.Empty;
            public DateTime Date { get; set; }
            public int Quantity { get; set; }
            public decimal Amount { get; set; }
            public DateTime? LastUpdatedAt { get; set; }
        }
    }
}
