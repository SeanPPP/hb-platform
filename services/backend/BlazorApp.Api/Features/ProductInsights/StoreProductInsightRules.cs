namespace BlazorApp.Api.Features.ProductInsights;

/// <summary>查询口径中不依赖数据库的事实判断，供服务与回归测试共享。</summary>
public static class StoreProductInsightRules
{
    public static bool CanAccessStore(IReadOnlyCollection<string>? allowedStoreCodes, string requestedStoreCode) =>
        allowedStoreCodes == null || allowedStoreCodes.Contains(requestedStoreCode, StringComparer.Ordinal);

    public static bool IsValidRange(DateTime startDate, DateTime endDate) => startDate.Date <= endDate.Date;

    public static bool IsValidLocalInbound(bool isInvoiceDeleted, bool isDetailDeleted, DateTime? inboundDate, int? inboundStatus) =>
        !isInvoiceDeleted && !isDetailDeleted && inboundDate.HasValue && inboundStatus == 2;

    public static bool IsWarehouseDelivery(int? flowStatus, DateTime? outboundDate, decimal allocatedQuantity, DateTime startDate, DateTime endDate) =>
        flowStatus == 2 && outboundDate.HasValue && allocatedQuantity > 0
        && outboundDate.Value >= startDate.Date && outboundDate.Value < endDate.Date.AddDays(1);

    public static bool IsHistoricalRecordEligible(DateTime recordDate, DateTime endDate) =>
        recordDate < endDate.Date.AddDays(1);
}
