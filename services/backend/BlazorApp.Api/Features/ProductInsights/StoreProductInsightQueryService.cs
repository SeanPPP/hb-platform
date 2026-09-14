using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.ProductHistory.Infrastructure;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace BlazorApp.Api.Features.ProductInsights;

/// <summary>单店商品进销只读查询。所有日期均按调用方已确认的当地业务日含首尾处理。</summary>
public sealed class StoreProductInsightQueryService(
    SqlSugarContext context,
    IServiceProvider serviceProvider
)
{
    private readonly ISqlSugarClient _db = context.Db;

    internal async Task<(DateTime StartDate, DateTime EndDate)?> GetDefaultRangeAsync(
        string storeCode,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 复用既有 ProductSalesHistoryQueryStore 的门店时区推导，避免由设备时区决定业务日期。
        var salesHistoryQueryStore = serviceProvider.GetRequiredService<ProductSalesHistoryQueryStore>();
        var salesContext = await salesHistoryQueryStore.GetActiveStoreSalesContextAsync(storeCode);
        if (salesContext == null)
        {
            return null;
        }

        return (salesContext.EndDate.AddDays(-89), salesContext.EndDate);
    }

    public async Task<StoreProductInsightDto?> GetAsync(
        string storeCode,
        string productCode,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var store = await _db.Queryable<Store>()
            .Where(item => item.StoreCode == storeCode && !item.IsDeleted && item.IsActive)
            .Select(item => new ProductInsightStoreDto
            {
                StoreCode = item.StoreCode,
                StoreName = item.StoreName,
            })
            .FirstAsync();
        var product = await _db.Queryable<Product>()
            .LeftJoin<HBLocalSupplier>((item, supplier) => item.LocalSupplierCode == supplier.LocalSupplierCode && !supplier.IsDeleted)
            .Where((item, supplier) => item.ProductCode == productCode && !item.IsDeleted)
            .Select((item, supplier) => new ProductInsightProductDto
            {
                ProductCode = item.ProductCode!,
                ProductName = item.ProductName,
                ItemNumber = item.ItemNumber,
                Barcode = item.Barcode,
                ProductImage = item.ProductImage,
                LocalSupplierCode = item.LocalSupplierCode,
                LocalSupplierName = supplier.Name,
            })
            .FirstAsync();
        if (store == null || product == null)
        {
            return null;
        }

        var result = new StoreProductInsightDto
        {
            Range = new ProductInsightRangeDto
            {
                StartDate = startDate.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                EndDate = endDate.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            },
            GeneratedAt = DateTime.UtcNow,
            Store = store,
            Product = product,
            SourceType = string.Equals(product.LocalSupplierCode?.Trim(), "200", StringComparison.Ordinal)
                ? "warehouse"
                : "local",
        };

        var sales = await GetSalesAsync(storeCode, productCode, startDate, endDate, cancellationToken);
        result.Sales = sales.Sales;
        result.SalesStatisticLastUpdatedAt = sales.LastUpdatedAt;
        if (result.SourceType == "warehouse")
        {
            result.Warehouse = await GetWarehouseAsync(storeCode, productCode, startDate, endDate, cancellationToken);
        }
        else
        {
            result.Purchases = await GetPurchasesAsync(storeCode, productCode, startDate, endDate, cancellationToken);
        }
        return result;
    }

    private async Task<(ProductInsightSalesDto Sales, DateTime? LastUpdatedAt)> GetSalesAsync(string storeCode, string productCode, DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await _db.Queryable<ProductStoreDailySalesStatistic>()
            .Where(item => item.BranchCode == storeCode && item.ProductCode == productCode && item.Date >= startDate.Date && item.Date < endDate.Date.AddDays(1))
            .GroupBy(item => item.Date)
            .Select(item => new ProductInsightDailySalesDto
            {
                Date = item.Date,
                Quantity = SqlFunc.AggregateSum(item.TotalQuantity),
                Amount = SqlFunc.AggregateSum(item.TotalAmount),
            })
            .OrderBy(item => item.Date, OrderByType.Desc)
            .ToListAsync();
        var lastUpdatedAt = await _db.Queryable<ProductStoreDailySalesStatistic>()
            .Where(item => item.BranchCode == storeCode && item.ProductCode == productCode && item.Date >= startDate.Date && item.Date < endDate.Date.AddDays(1))
            .MaxAsync(item => (DateTime?)item.UpdateTime);
        return (new ProductInsightSalesDto
        {
            Quantity = rows.Sum(item => item.Quantity),
            Amount = rows.Sum(item => item.Amount),
            Records = rows,
        }, lastUpdatedAt);
    }

    private async Task<ProductInsightPurchasesDto> GetPurchasesAsync(string storeCode, string productCode, DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
    {
        var currentRows = await BuildValidPurchaseQuery(storeCode, productCode)
            .Where(item => item.InboundDate >= startDate.Date && item.InboundDate < endDate.Date.AddDays(1))
            .OrderBy(item => item.InboundDate, OrderByType.Desc)
            .OrderBy(item => item.Id, OrderByType.Desc)
            .ToListAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var lastRow = await BuildValidPurchaseQuery(storeCode, productCode)
            .Where(item => item.InboundDate < endDate.Date.AddDays(1))
            .OrderBy(item => item.InboundDate, OrderByType.Desc)
            .OrderBy(item => item.Id, OrderByType.Desc)
            .FirstAsync();
        return new ProductInsightPurchasesDto
        {
            Quantity = currentRows.Sum(item => item.Quantity),
            DocumentCount = currentRows.Count,
            Records = currentRows.Select(ToMovement).ToList(),
            // 最近历史进货不受查询区间限制，且从不混入范围内合计。
            LastRecord = lastRow == null || !StoreProductInsightRules.IsHistoricalRecordEligible(lastRow.InboundDate, endDate)
                ? null
                : ToMovement(lastRow),
        };
    }

    private ISugarQueryable<PurchaseRow> BuildValidPurchaseQuery(string storeCode, string productCode)
    {
        // 明细无全量实收数量；只有完成入库单才能作为准确的实际进货数量，部分入库单不以整行数量冒充实收。
        return _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .InnerJoin<StoreLocalSupplierInvoice>((detail, invoice) => detail.InvoiceGUID == invoice.InvoiceGUID)
            // 历史详情可能只保存 StoreRetailPrice.UUID；回退必须锁定同一分店，允许停用价目但排除软删映射。
            .LeftJoin<StoreRetailPrice>((detail, invoice, price) => detail.StoreProductCode == price.UUID && price.StoreCode == invoice.StoreCode && !price.IsDeleted)
            .LeftJoin<HBLocalSupplier>((detail, invoice, price, supplier) => invoice.SupplierCode == supplier.LocalSupplierCode && !supplier.IsDeleted)
            .Where((detail, invoice, price, supplier) =>
                invoice.StoreCode == storeCode
                && SqlFunc.IIF(SqlFunc.IsNullOrEmpty(detail.ProductCode), price.ProductCode, detail.ProductCode) == productCode
                && !invoice.IsDeleted && !detail.IsDeleted
                && invoice.InboundDate != null && invoice.InboundStatus == 2)
            .GroupBy((detail, invoice, price, supplier) => new { invoice.InvoiceGUID, invoice.InvoiceNo, invoice.InboundDate, invoice.SupplierCode, supplier.Name })
            .Select((detail, invoice, price, supplier) => new PurchaseRow
            {
                Id = invoice.InvoiceGUID,
                DocumentNo = invoice.InvoiceNo,
                InboundDate = invoice.InboundDate!.Value,
                SupplierCode = invoice.SupplierCode,
                SupplierName = supplier.Name,
                Quantity = SqlFunc.AggregateSum(detail.Quantity ?? 0m),
            })
            .MergeTable();
    }

    private async Task<ProductInsightWarehouseDto> GetWarehouseAsync(string storeCode, string productCode, DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
    {
        var rows = await BuildWarehouseOrderQuery(storeCode, productCode)
            .Where(item => item.OrderDate >= startDate.Date && item.OrderDate < endDate.Date.AddDays(1))
            .OrderBy(item => item.OrderDate, OrderByType.Desc)
            .ToListAsync();
        // 送货按实际出库日，而非订货日统计；日期条件在分组前下推，避免 SQLite 对 MergeTable 的 nullable 日期比较漏行。
        var deliveryRows = await BuildWarehouseDeliveryQuery(storeCode, productCode, startDate.Date, endDate.Date.AddDays(1))
            .OrderBy(item => item.OutboundDate, OrderByType.Desc)
            .ToListAsync();
        var deliveries = deliveryRows
            .Where(item => StoreProductInsightRules.IsWarehouseDelivery(item.FlowStatus, item.OutboundDate, item.AllocQuantity, startDate, endDate))
            .Select(ToDeliveryMovement)
            .ToList();
        cancellationToken.ThrowIfCancellationRequested();
        var lastDeliveryRow = await BuildWarehouseDeliveryQuery(storeCode, productCode, null, endDate.Date.AddDays(1))
            .OrderBy(item => item.OutboundDate, OrderByType.Desc)
            .FirstAsync();
        return new ProductInsightWarehouseDto
        {
            OrderedQuantity = rows.Sum(item => item.Quantity),
            DeliveredQuantity = deliveries.Sum(item => item.Quantity),
            Orders = rows.Select(item => new ProductInsightOrderDto
            {
                Id = item.Id,
                Date = item.OrderDate,
                DocumentNo = item.DocumentNo ?? item.Id,
                Quantity = item.Quantity,
                DeliveredQuantity = item.FlowStatus == 2 && item.OutboundDate.HasValue ? item.AllocQuantity : 0,
                DeliveryDate = item.FlowStatus == 2 ? item.OutboundDate : null,
                Status = ToWarehouseStatus(item.FlowStatus),
            }).ToList(),
            Deliveries = deliveries,
            LastDelivery = lastDeliveryRow == null || !StoreProductInsightRules.IsHistoricalRecordEligible(lastDeliveryRow.OutboundDate!.Value, endDate)
                ? null
                : ToDeliveryMovement(lastDeliveryRow),
        };
    }

    private ISugarQueryable<WarehouseOrderRow> BuildWarehouseOrderQuery(string storeCode, string productCode)
    {
        return BuildWarehouseOrderBaseQuery(storeCode, productCode)
            .GroupBy((detail, order) => new { order.OrderGUID, order.OrderNo, order.OrderDate, order.OutboundDate, order.FlowStatus })
            .Select((detail, order) => new WarehouseOrderRow
            {
                Id = order.OrderGUID,
                DocumentNo = order.OrderNo,
                OrderDate = order.OrderDate!.Value,
                OutboundDate = order.OutboundDate,
                FlowStatus = order.FlowStatus,
                Quantity = SqlFunc.AggregateSum(detail.Quantity ?? 0m),
                AllocQuantity = SqlFunc.AggregateSum(detail.AllocQuantity ?? 0m),
            })
            .MergeTable();
    }

    private ISugarQueryable<WarehouseOrderRow> BuildWarehouseDeliveryQuery(string storeCode, string productCode, DateTime? startDate, DateTime endExclusive)
    {
        var query = BuildWarehouseOrderBaseQuery(storeCode, productCode)
            .Where((detail, order) => order.FlowStatus == 2 && order.OutboundDate != null && detail.AllocQuantity > 0);
        if (startDate.HasValue)
        {
            query = query.Where((detail, order) => order.OutboundDate >= startDate.Value);
        }
        return query
            .Where((detail, order) => order.OutboundDate < endExclusive)
            .GroupBy((detail, order) => new { order.OrderGUID, order.OrderNo, order.OrderDate, order.OutboundDate, order.FlowStatus })
            .Select((detail, order) => new WarehouseOrderRow
            {
                Id = order.OrderGUID,
                DocumentNo = order.OrderNo,
                OrderDate = order.OrderDate!.Value,
                OutboundDate = order.OutboundDate,
                FlowStatus = order.FlowStatus,
                Quantity = SqlFunc.AggregateSum(detail.Quantity ?? 0m),
                AllocQuantity = SqlFunc.AggregateSum(detail.AllocQuantity ?? 0m),
            })
            .MergeTable();
    }

    private ISugarQueryable<WareHouseOrderDetails, WareHouseOrder> BuildWarehouseOrderBaseQuery(string storeCode, string productCode)
    {
        return _db.Queryable<WareHouseOrderDetails>()
            .InnerJoin<WareHouseOrder>((detail, order) => detail.OrderGUID == order.OrderGUID)
            .Where((detail, order) => order.StoreCode == storeCode && detail.ProductCode == productCode && order.FlowStatus > 0 && !order.IsDeleted && !detail.IsDeleted && order.OrderDate != null);
    }

    private static ProductInsightMovementDto ToMovement(PurchaseRow row) => new()
    {
        Id = row.Id,
        Date = row.InboundDate,
        DocumentNo = string.IsNullOrWhiteSpace(row.DocumentNo) ? row.Id : row.DocumentNo,
        Quantity = row.Quantity,
        SupplierName = row.SupplierName ?? row.SupplierCode,
    };

    private static ProductInsightMovementDto ToDeliveryMovement(WarehouseOrderRow row) => new()
    {
        Id = row.Id,
        Date = row.OutboundDate!.Value,
        DocumentNo = string.IsNullOrWhiteSpace(row.DocumentNo) ? row.Id : row.DocumentNo,
        Quantity = row.AllocQuantity,
        SupplierName = "200",
    };

    private static string ToWarehouseStatus(int? flowStatus) => flowStatus switch
    {
        1 => "submitted",
        2 => "completed",
        3 => "allocating",
        _ => "unknown",
    };

    private sealed class PurchaseRow { public string Id { get; set; } = string.Empty; public string? DocumentNo { get; set; } public DateTime InboundDate { get; set; } public string? SupplierCode { get; set; } public string? SupplierName { get; set; } public decimal Quantity { get; set; } }
    private sealed class WarehouseOrderRow { public string Id { get; set; } = string.Empty; public string? DocumentNo { get; set; } public DateTime OrderDate { get; set; } public DateTime? OutboundDate { get; set; } public int? FlowStatus { get; set; } public decimal Quantity { get; set; } public decimal AllocQuantity { get; set; } }
}
