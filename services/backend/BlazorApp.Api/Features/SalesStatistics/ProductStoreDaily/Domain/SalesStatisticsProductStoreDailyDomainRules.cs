using System.Security.Cryptography;
using System.Text;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services;

/// <summary>商品分店日统计的纯领域规则；不访问数据库或事务。</summary>
internal static class SalesStatisticsProductStoreDailyDomainRules
{
    internal static string ResolveStatisticSupplierCode(
        string? mappedSupplierCode,
        string? detailSupplierCode)
    {
        var supplierCode = SalesStatisticsCodeRules.Normalize(mappedSupplierCode);
        if (!string.IsNullOrWhiteSpace(supplierCode))
            return supplierCode;

        supplierCode = SalesStatisticsCodeRules.Normalize(detailSupplierCode);
        return !string.IsNullOrWhiteSpace(supplierCode)
            ? supplierCode
            : SalesStatisticsCodeRules.UnknownSupplierCode;
    }

    internal static Dictionary<string, decimal> BuildOrderAmountMap(IEnumerable<OrderAmountRow> rows) =>
        rows
            .Where(row => !string.IsNullOrWhiteSpace(row.OrderGuid))
            .GroupBy(row => row.OrderGuid!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(row => row.Amount),
                StringComparer.OrdinalIgnoreCase
            );

    internal static decimal ResolveStatisticAmount(
        string? orderGuid,
        decimal detailAmount,
        Dictionary<string, decimal> paymentAmounts,
        Dictionary<string, decimal> detailAmounts)
    {
        var key = SalesStatisticsCodeRules.Normalize(orderGuid);
        if (string.IsNullOrWhiteSpace(key)
            || !paymentAmounts.TryGetValue(key, out var paymentAmount)
            || !detailAmounts.TryGetValue(key, out var detailTotal)
            || detailTotal == 0m)
        {
            // 无支付记录时必须按支付口径计 0，不能回退明细金额掩盖对账异常。
            return 0m;
        }

        return paymentAmount * detailAmount / detailTotal;
    }

    internal static decimal? ResolveUnitCost(
        string branchCode,
        string supplierCode,
        string productCode,
        Dictionary<string, decimal?> storeCostMap,
        Dictionary<string, decimal?> productCostMap,
        Dictionary<string, decimal?> warehouseCostMap,
        out string costSource)
    {
        if (storeCostMap.TryGetValue(
                $"{branchCode}|{supplierCode}|{productCode}",
                out var storeCost)
            && storeCost is > 0)
        {
            costSource = "StoreRetailPrice";
            return storeCost;
        }

        if (productCostMap.TryGetValue(productCode, out var productCost)
            && productCost is > 0)
        {
            costSource = "ProductPurchasePrice";
            return productCost;
        }

        if (warehouseCostMap.TryGetValue(productCode, out var warehouseCost)
            && warehouseCost is > 0)
        {
            costSource = "WarehouseImportPrice";
            return warehouseCost;
        }

        costSource = "Missing";
        return null;
    }

    internal static string SelectDeterministicProductCode(IEnumerable<string> candidates) =>
        candidates
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(code => code, StringComparer.Ordinal)
            .First();

    internal static DateTime? GetLatestSourceTime(params DateTime?[] timestamps)
    {
        var values = timestamps
            .Where(timestamp => timestamp.HasValue)
            .Select(timestamp => timestamp!.Value)
            .ToList();
        return values.Count == 0 ? null : values.Max();
    }

    internal static DateTime? GetLatestSourceTime(IEnumerable<ProductStoreDailySourceRow> rows) =>
        GetLatestSourceTime(rows
            .SelectMany(row => new[] { row.OrderLastUploadTime, row.DetailLastUploadTime })
            .ToArray());

    internal static DateTime? GetHBSalesSourceWatermark(
        IEnumerable<ProductStoreDailySourceRow> hbSalesRows)
    {
        // 四列必须分别取 MAX；LastModify 有值时不能遮蔽 Create。
        return GetLatestSourceTime(
            GetLatestSourceTime(hbSalesRows.Select(row => row.HBSalesMainLastModifiedAt).ToArray()),
            GetLatestSourceTime(hbSalesRows.Select(row => row.HBSalesMainCreatedAt).ToArray()),
            GetLatestSourceTime(hbSalesRows.Select(row => row.HBSalesDetailLastModifiedAt).ToArray()),
            GetLatestSourceTime(hbSalesRows.Select(row => row.HBSalesDetailCreatedAt).ToArray())
        );
    }

    internal static DateTime? GetPosmSnapshotWatermark(Posm2025DailySnapshot snapshot)
    {
        var values = snapshot.OrderRows.Select(row => row.LastUploadTime)
            .Concat(snapshot.DetailRows.Select(row =>
                GetLatestSourceTime(row.OrderLastUploadTime, row.DetailLastUploadTime)))
            .Concat(snapshot.PaymentRows.Select(row => row.LastUploadTime))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();
        return values.Count == 0 ? null : values.Max();
    }

    internal static HBSales2025DailySnapshotSignature CreateHBSales2025DailySnapshotSignature(
        DateTime date,
        IEnumerable<ProductStoreDailySourceRow> rows)
    {
        var dayRows = rows.Where(row => row.Date.Date == date.Date).ToList();
        var mainLastModifiedAt = dayRows.Max(row => row.HBSalesMainLastModifiedAt);
        var mainCreatedAt = dayRows.Max(row => row.HBSalesMainCreatedAt);
        var detailLastModifiedAt = dayRows.Max(row => row.HBSalesDetailLastModifiedAt);
        var detailCreatedAt = dayRows.Max(row => row.HBSalesDetailCreatedAt);
        using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var row in dayRows.OrderBy(row => row.HBSalesOrderNumber, StringComparer.Ordinal)
                     .ThenBy(row => row.DetailGuid, StringComparer.Ordinal)
                     .ThenBy(row => row.BranchCode, StringComparer.Ordinal)
                     .ThenBy(row => row.ProductCode, StringComparer.Ordinal))
        {
            AppendSignatureValue(checksum, row.HBSalesOrderNumber);
            AppendSignatureValue(checksum, row.DetailGuid);
            AppendSignatureValue(checksum, row.Date);
            AppendSignatureValue(checksum, row.BranchCode);
            AppendSignatureValue(checksum, row.ProductCode);
            AppendSignatureValue(checksum, row.ItemNumber);
            AppendSignatureValue(checksum, row.Barcode);
            AppendSignatureValue(checksum, row.SupplierCode);
            AppendSignatureValue(checksum, row.ProductName);
            AppendSignatureValue(checksum, row.Quantity);
            AppendSignatureValue(checksum, row.ActualAmount);
            AppendSignatureValue(checksum, row.DocumentType);
            AppendSignatureValue(checksum, row.HBSalesMainLastModifiedAt);
            AppendSignatureValue(checksum, row.HBSalesMainCreatedAt);
            AppendSignatureValue(checksum, row.HBSalesDetailLastModifiedAt);
            AppendSignatureValue(checksum, row.HBSalesDetailCreatedAt);
        }

        return new HBSales2025DailySnapshotSignature(
            date.Date,
            dayRows.Count,
            mainLastModifiedAt,
            mainCreatedAt,
            detailLastModifiedAt,
            detailCreatedAt,
            Convert.ToHexString(checksum.GetHashAndReset())
        );
    }

    internal static void AppendSignatureValue(IncrementalHash checksum, object? value)
    {
        var text = value switch
        {
            null => "<null>",
            DateTime dateTime => dateTime.ToString(
                "O",
                System.Globalization.CultureInfo.InvariantCulture
            ),
            decimal decimalValue => decimalValue.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
                ?? string.Empty,
        };
        var bytes = Encoding.UTF8.GetBytes(text);
        checksum.AppendData(BitConverter.GetBytes(bytes.Length));
        checksum.AppendData(bytes);
    }

    internal static Posm2025DailySnapshotSignature CreatePosm2025DailySnapshotSignature(
        DateTime date,
        IEnumerable<StoreStatisticOrderRow> orders,
        IEnumerable<ProductStoreDailySourceRow> details,
        IEnumerable<StoreStatisticPaymentRow> payments,
        IEnumerable<ProductStoreDailySourceRow> salesReturns)
    {
        var orderRows = orders.OrderBy(row => row.OrderGuid, StringComparer.Ordinal).ToList();
        var detailRows = details.OrderBy(row => row.DetailGuid, StringComparer.Ordinal).ToList();
        var paymentRows = payments.OrderBy(row => row.PaymentGuid, StringComparer.Ordinal).ToList();
        var returnRows = salesReturns.OrderBy(row => row.DetailGuid, StringComparer.Ordinal).ToList();
        return new Posm2025DailySnapshotSignature(
            date.Date,
            CreatePosmTableSignature(orderRows, row => row.UpdatedAt, row => row.CreatedAt, row =>
                [row.OrderGuid, row.OrderTime, row.BranchCode, row.DeviceCode, row.Status,
                    row.LastUploadTime, row.CreatedAt, row.UpdatedAt]),
            CreatePosmTableSignature(detailRows, row => row.SourceUpdatedAt, row => row.SourceCreatedAt, row =>
                [row.OrderGuid, row.DetailGuid, row.ProductCode, row.SupplierCode, row.ProductName,
                    row.Barcode, row.Quantity, row.ActualAmount, row.DetailLastUploadTime,
                    row.SourceCreatedAt, row.SourceUpdatedAt]),
            CreatePosmTableSignature(paymentRows, row => row.UpdatedAt, row => row.CreatedAt, row =>
                [row.PaymentGuid, row.OrderGuid, row.Amount, row.LastUploadTime,
                    row.CreatedAt, row.UpdatedAt]),
            CreatePosmTableSignature(returnRows, row => row.SourceUpdatedAt, row => row.SourceCreatedAt, row =>
                [row.OrderGuid, row.DetailGuid, row.ProductCode, row.Quantity, row.ActualAmount,
                    row.SourceCreatedAt, row.SourceUpdatedAt])
        );
    }

    internal static Posm2025DailyTableSignature CreatePosmTableSignature<T>(
        IReadOnlyList<T> rows,
        Func<T, DateTime?> lastModifiedSelector,
        Func<T, DateTime?> createdSelector,
        Func<T, object?[]> valuesSelector)
    {
        using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var row in rows)
        {
            foreach (var value in valuesSelector(row))
                AppendSignatureValue(checksum, value);
        }

        return new Posm2025DailyTableSignature(
            rows.Count,
            rows.Max(lastModifiedSelector),
            rows.Max(createdSelector),
            Convert.ToHexString(checksum.GetHashAndReset())
        );
    }

    internal static List<HBSalesStoreAggregateRow> BuildHBSalesStoreAggregates(
        IReadOnlyList<ProductStoreDailySourceRow> hbSalesRows)
    {
        return hbSalesRows
            .Where(row => row.IsHBSalesSource)
            .Select(row => new
            {
                Row = row,
                BranchCode = SalesStatisticsCodeRules.Normalize(row.BranchCode),
            })
            .Where(row => !string.IsNullOrWhiteSpace(row.BranchCode))
            .GroupBy(row => row.BranchCode)
            .Select(group => new HBSalesStoreAggregateRow
            {
                BranchCode = group.Key,
                TotalAmount = group.Sum(row => row.Row.ActualAmount),
                TotalQuantity = group.Sum(row => row.Row.Quantity),
                OrderCount = group
                    .Select(row => row.Row.HBSalesOrderNumber)
                    .Where(orderNumber => orderNumber != null)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
            })
            .ToList();
    }
}
