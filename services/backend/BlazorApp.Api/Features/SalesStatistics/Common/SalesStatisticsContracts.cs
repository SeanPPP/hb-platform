using System.Collections.ObjectModel;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services;

/// <summary>
/// 后台重算在新作用域中只需要这两个动作，不暴露完整兼容服务。
/// </summary>
internal interface ISalesStatisticsRecalculationExecutor
{
    Task MarkProductStatisticJobRunningAsync(Guid jobId, DateTime date);
    Task UpdateProductStoreDailyStatisticsAsync(DateTime? date);
}

/// <summary>
/// 报表缺口修复只需要两类刷新动作；调用方不得解析兼容门面或完整应用协调器。
/// </summary>
internal interface ISalesStatisticsRefreshOperations
{
    Task UpdateStoreStatisticsAsync(DateTime date, List<string>? branchCodes = null);
    Task UpdateHourlyStatisticsAsync(DateTime date, int? hour = null);
}

internal sealed record HBSales2025DailySnapshotSignature(
    DateTime Date,
    int RowCount,
    DateTime? MainLastModifiedAt,
    DateTime? MainCreatedAt,
    DateTime? DetailLastModifiedAt,
    DateTime? DetailCreatedAt,
    string Checksum
);

internal sealed record Posm2025DailyTableSignature(
    int RowCount,
    DateTime? LastModifiedAt,
    DateTime? CreatedAt,
    string Checksum
);

internal sealed record Posm2025DailySnapshotSignature(
    DateTime Date,
    Posm2025DailyTableSignature Orders,
    Posm2025DailyTableSignature Details,
    Posm2025DailyTableSignature Payments,
    Posm2025DailyTableSignature SalesReturns
);

internal sealed class HBSales2025BatchSnapshot
{
    private readonly IReadOnlyDictionary<DateTime, IReadOnlyList<ProductStoreDailySourceRow>> _rowsByDate;

    internal HBSales2025BatchSnapshot(
        IReadOnlyDictionary<DateTime, List<ProductStoreDailySourceRow>> rowsByDate,
        IReadOnlyDictionary<DateTime, HBSales2025DailySnapshotSignature> signatures
    )
    {
        _rowsByDate = new ReadOnlyDictionary<DateTime, IReadOnlyList<ProductStoreDailySourceRow>>(
            rowsByDate.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<ProductStoreDailySourceRow>)Array.AsReadOnly(entry.Value.ToArray())
            )
        );
        Signatures = new ReadOnlyDictionary<DateTime, HBSales2025DailySnapshotSignature>(
            signatures.ToDictionary(entry => entry.Key, entry => entry.Value)
        );
    }

    internal IReadOnlyDictionary<DateTime, HBSales2025DailySnapshotSignature> Signatures { get; }

    internal IReadOnlyList<ProductStoreDailySourceRow> GetRows(DateTime date) =>
        _rowsByDate.TryGetValue(date.Date, out var rows)
            ? rows
            : throw new InvalidOperationException($"批量快照不包含日期: {date:yyyy-MM-dd}");

    internal HBSales2025DailySnapshotSignature GetSignature(DateTime date) =>
        Signatures.TryGetValue(date.Date, out var signature)
            ? signature
            : throw new InvalidOperationException($"批量快照不包含签名: {date:yyyy-MM-dd}");
}

internal sealed class Posm2025DailySnapshot
{
    internal Posm2025DailySnapshot(
        IReadOnlyList<ProductStoreDailySourceRow> detailRows,
        IReadOnlyList<ProductStoreDailySourceRow> supplementalReturnRows,
        IReadOnlyList<StoreStatisticPaymentRow> paymentRows,
        IReadOnlyList<StoreStatisticOrderRow> orderRows,
        Dictionary<string, string> deviceBranchMap,
        Posm2025DailySnapshotSignature signature
    )
    {
        DetailRows = Array.AsReadOnly(detailRows.ToArray());
        SupplementalReturnRows = Array.AsReadOnly(supplementalReturnRows.ToArray());
        PaymentRows = Array.AsReadOnly(paymentRows.ToArray());
        OrderRows = Array.AsReadOnly(orderRows.ToArray());
        DeviceBranchMap = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(deviceBranchMap, StringComparer.Ordinal)
        );
        Signature = signature;
    }

    internal IReadOnlyList<ProductStoreDailySourceRow> DetailRows { get; }
    internal IReadOnlyList<ProductStoreDailySourceRow> SupplementalReturnRows { get; }
    internal IReadOnlyList<StoreStatisticPaymentRow> PaymentRows { get; }
    internal IReadOnlyList<StoreStatisticOrderRow> OrderRows { get; }
    internal IReadOnlyDictionary<string, string> DeviceBranchMap { get; }
    internal Posm2025DailySnapshotSignature Signature { get; }
}

internal class StoreCostRow
{
    public string? StoreCode { get; set; }
    public string? SupplierCode { get; set; }
    public string? ProductCode { get; set; }
    public decimal? PurchasePrice { get; set; }
}

internal class ProductCostRow
{
    public string? ProductCode { get; set; }
    public decimal? PurchasePrice { get; set; }
}

internal class WarehouseCostRow
{
    public string ProductCode { get; set; } = string.Empty;
    public decimal? ImportPrice { get; set; }
}

internal class ProductStatisticDiagnosticRow
{
    public string BranchCode { get; set; } = string.Empty;
    public decimal UnmatchedSupplierAmount { get; set; }
    public int UnmatchedSupplierQuantity { get; set; }
    public int UnmatchedSupplierProductCount { get; set; }
}

internal class ProductStatisticDiagnostics
{
    public decimal UnmatchedSupplierAmount { get; set; }
    public int UnmatchedSupplierQuantity { get; set; }
    public int UnmatchedSupplierProductCount { get; set; }
    public Dictionary<string, ProductStatisticDiagnosticRow> BranchDiagnostics { get; set; } = new();
}

internal class ProductStoreDailySourceRow
{
    public bool IsHBSalesSource { get; set; }
    public DateTime Date { get; set; }
    public string? OrderGuid { get; set; }
    public string? HBSalesOrderNumber { get; set; }
    public string? DetailGuid { get; set; }
    public string? BranchCode { get; set; }
    public string? DeviceCode { get; set; }
    public DateTime? HBSalesMainLastModifiedAt { get; set; }
    public DateTime? HBSalesMainCreatedAt { get; set; }
    public DateTime? HBSalesDetailLastModifiedAt { get; set; }
    public DateTime? HBSalesDetailCreatedAt { get; set; }
    public DateTime? OrderLastUploadTime { get; set; }
    public string? ProductCode { get; set; }
    public string? ItemNumber { get; set; }
    public string? SupplierCode { get; set; }
    public string? ProductName { get; set; }
    public string? Barcode { get; set; }
    public decimal Quantity { get; set; }
    public decimal ActualAmount { get; set; }
    public DateTime? DetailLastUploadTime { get; set; }
    public DateTime? SourceCreatedAt { get; set; }
    public DateTime? SourceUpdatedAt { get; set; }
    public string? DocumentType { get; set; }
}

internal class StoreStatisticPaymentRow
{
    public string? PaymentGuid { get; set; }
    public string? OrderGuid { get; set; }
    public string? BranchCode { get; set; }
    public string? DeviceCode { get; set; }
    public decimal Amount { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastUploadTime { get; set; }
}

internal class StoreStatisticQuantityRow
{
    public string? OrderGuid { get; set; }
    public string? BranchCode { get; set; }
    public string? DeviceCode { get; set; }
    public int Quantity { get; set; }
}

internal class StoreStatisticOrderRow
{
    public string? OrderGuid { get; set; }
    public string? BranchCode { get; set; }
    public string? DeviceCode { get; set; }
    public DateTime? OrderTime { get; set; }
    public int? Status { get; set; }
    public DateTime? LastUploadTime { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

internal class HBSalesSourceWatermarkRow
{
    public DateTime? MainLastModifiedAt { get; set; }
    public DateTime? MainCreatedAt { get; set; }
    public DateTime? DetailLastModifiedAt { get; set; }
    public DateTime? DetailCreatedAt { get; set; }
}

internal class HBSalesStoreAggregateRow
{
    public string? BranchCode { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalQuantity { get; set; }
    public int OrderCount { get; set; }
}

internal class HourlyStatisticSourceRow
{
    public DateTime Date { get; set; }
    public int Hour { get; set; }
    public string? BranchCode { get; set; }
    public string? DeviceCode { get; set; }
    public decimal TotalAmount { get; set; }
    public int TotalQuantity { get; set; }
    public int OrderCount { get; set; }
    public int CustomerCount { get; set; }
}

internal class OrderAmountRow
{
    public string? OrderGuid { get; set; }
    public decimal Amount { get; set; }
}

internal class StoreSupplierSourceRow
{
    public DateTime Date { get; set; }
    public string? BranchCode { get; set; }
    public string? DeviceCode { get; set; }
    public string? OrderGuid { get; set; }
    public string? DetailSupplierCode { get; set; }
    public string? LocalSupplierCode { get; set; }
    public string? ChinaSupplierCode { get; set; }
    public decimal ActualAmount { get; set; }
    public decimal Quantity { get; set; }
}

internal class StoreSupplierResolvedRow
{
    public DateTime Date { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public bool IsDomestic { get; set; }
    public string? OrderGuid { get; set; }
    public decimal TotalAmount { get; set; }
    public int TotalQuantity { get; set; }
}
