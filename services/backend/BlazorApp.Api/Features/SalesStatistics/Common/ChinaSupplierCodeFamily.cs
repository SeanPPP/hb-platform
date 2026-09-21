using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services;

/// <summary>
/// 国内货在商品分店日统计里的供应商编码族。
/// 旧写法把国内货统一记成本地供应商 200，再靠 POSM 商品映射还原国内供应商；
/// 新写法直接把国内供应商编码写进 SupplierCode。只认 200 的读取方会漏掉直写行，
/// 必须按「200 加全部国内供应商编码」过滤。
/// </summary>
internal static class ChinaSupplierCodeFamily
{
    /// <summary>国内货在澳洲供应商口径下的固定汇总编码。</summary>
    internal const string LocalSupplierCode = "200";

    /// <summary>
    /// 读取全部国内供应商编码，包含停用和软删除的供应商。
    /// 直写行只能靠「编码是否属于这个集合」来识别；供应商日后被删除，也不能让它的历史销售
    /// 在澳洲侧报表里变成一个普通澳洲供应商。
    /// </summary>
    internal static async Task<HashSet<string>> LoadChinaSupplierCodesAsync(ISqlSugarClient db)
    {
        var codes = await db.Queryable<ChinaSupplier>()
            .Where(supplier => supplier.SupplierCode != null && supplier.SupplierCode != "")
            .Select(supplier => supplier.SupplierCode ?? string.Empty)
            .ToListAsync();

        return codes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 生成日统计 SupplierCode 的过滤列表：200 在前，国内供应商编码按序排列。
    /// 列表会被内联成 SQL 字面量，顺序固定才能让同一份供应商目录每次生成相同的 SQL 文本、复用执行计划。
    /// </summary>
    internal static List<string> BuildStatisticFilterCodes(IEnumerable<string> chinaSupplierCodes)
    {
        var filterCodes = new List<string> { LocalSupplierCode };
        filterCodes.AddRange(chinaSupplierCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Where(code => !IsLocalSupplierCode(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.Ordinal));
        return filterCodes;
    }

    internal static bool IsLocalSupplierCode(string? supplierCode) =>
        string.Equals(supplierCode?.Trim(), LocalSupplierCode, StringComparison.OrdinalIgnoreCase);
}
