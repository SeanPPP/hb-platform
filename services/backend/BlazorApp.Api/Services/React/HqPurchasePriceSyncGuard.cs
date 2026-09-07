namespace BlazorApp.Api.Services.React;

/// <summary>
/// HQ 到 Web 同步的进货价保护规则。
/// HQ 的 0/null 表示没有可用成本时，不能抹掉 Web 已有的正数成本。
/// </summary>
internal static class HqPurchasePriceSyncGuard
{
    public static decimal? PreservePositive(decimal? existing, decimal? incoming)
    {
        return existing is > 0m && incoming.GetValueOrDefault() <= 0m
            ? existing
            : incoming;
    }

    public static decimal? PreservePositiveForOrdinaryProduct(
        decimal? existing,
        decimal? incoming,
        int? existingProductType,
        int? incomingProductType
    )
    {
        return IsOrdinaryProduct(existingProductType)
                && IsOrdinaryProduct(incomingProductType)
            ? PreservePositive(existing, incoming)
            : incoming;
    }

    private static bool IsOrdinaryProduct(int? productType) =>
        !productType.HasValue || productType.Value == 0;
}
