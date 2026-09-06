using System.Security.Cryptography;
using System.Text;

namespace BlazorApp.Shared.Models;

/// <summary>
/// 商品分店日统计到供应商汇总之间的纯版本契约。
/// </summary>
public static class SupplierStatisticVersion
{
    public static string? GetProductVersion(SalesStatisticRefreshState? state)
    {
        var version = state?.SourceProductVersion?.Trim();
        return string.IsNullOrWhiteSpace(version) ? null : version;
    }

    public static string ComputeProductVersion(IEnumerable<ProductStoreDailySalesStatistic> rows)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var row in rows
                     .OrderBy(row => row.Date)
                     .ThenBy(row => row.BranchCode, StringComparer.Ordinal)
                     .ThenBy(row => row.SupplierCode, StringComparer.Ordinal)
                     .ThenBy(row => row.ProductCode, StringComparer.Ordinal))
        {
            Append(hash, row.Date);
            Append(hash, row.BranchCode);
            Append(hash, row.SupplierCode);
            Append(hash, row.ProductCode);
            Append(hash, row.TotalQuantity);
            Append(hash, row.TotalAmount);
            Append(hash, row.OrderCount);
            Append(hash, row.UnitCostSnapshot);
            Append(hash, row.TotalCost);
            Append(hash, row.GrossProfit);
            Append(hash, row.GrossMarginRate);
            Append(hash, row.CostSource);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, object? value)
    {
        var text = value switch
        {
            null => "<null>",
            // 统计日期是业务本地日期；版本计算不能依赖运行主机时区或 DateTime.Kind。
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            decimal number => number.ToString("G29", System.Globalization.CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        };
        var bytes = Encoding.UTF8.GetBytes(text);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }
}
