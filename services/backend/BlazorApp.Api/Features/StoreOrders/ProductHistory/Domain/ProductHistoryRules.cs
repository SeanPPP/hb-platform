using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ProductHistory.Domain;

internal sealed record ProductsDynamicDataQueryInput(
    string? StoreCode,
    List<string> ProductCodes,
    bool IncludeSales
);

internal sealed record ProductOrderHistoryQueryInput(
    string StoreCode,
    string ProductCode,
    int PageNumber,
    int PageSize
);

internal sealed record ProductActivityHistoryQueryInput(
    string StoreCode,
    string ProductCode,
    int PageNumber,
    int PageSize,
    string RecordType
);

internal sealed record SalesSinceLastArrivalQueryInput(
    string StoreCode,
    string ProductCode,
    int PageNumber,
    int PageSize
);

internal sealed record SalesSinceLastArrivalSummaryQueryInput(
    string? StoreCode,
    List<string> ProductCodes
);

internal sealed record ProductsDynamicDataReadResult(
    List<StoreOrderDynamicDataDto> Items,
    int CartRows,
    int LatestDateRows,
    int HistoryRows,
    long CartMilliseconds,
    long LatestDateMilliseconds,
    long HistoryMilliseconds,
    bool SalesContextLoaded,
    int SalesRows,
    long SalesMilliseconds
);

internal static class ProductHistoryRules
{
    internal const int ProductOrderHistoryDefaultPageSize = 20;
    internal const int ProductActivityHistoryDefaultPageSize = 30;
    internal const int MaximumHistoryPageSize = 50;
    internal const int SalesSinceLastArrivalPageSize = 20;
    internal const int SalesStatisticsMaxProductCodesPerCutoffGroup = 500;

    internal static List<string> NormalizeProductCodes(IEnumerable<string>? productCodes)
    {
        return (productCodes ?? Array.Empty<string>())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static int NormalizePageNumber(int pageNumber) => pageNumber < 1 ? 1 : pageNumber;

    internal static int NormalizePageSize(int pageSize, int defaultPageSize)
    {
        return pageSize < 1 ? defaultPageSize : Math.Min(pageSize, MaximumHistoryPageSize);
    }

    internal static string NormalizeActivityRecordType(string? recordType)
    {
        return recordType?.Trim().ToLowerInvariant() switch
        {
            "order" => "order",
            "sales" => "sales",
            _ => "all",
        };
    }

    internal static PagedListReactDto<StoreOrderProductOrderHistoryItemDto> CreateOrderHistoryResult(
        ProductOrderHistoryQueryInput input
    )
    {
        return new PagedListReactDto<StoreOrderProductOrderHistoryItemDto>
        {
            PageNumber = input.PageNumber,
            PageSize = input.PageSize,
            Items = new List<StoreOrderProductOrderHistoryItemDto>(),
        };
    }

    internal static StoreOrderProductActivityHistoryResultDto CreateActivityHistoryResult(
        ProductActivityHistoryQueryInput input
    )
    {
        return new StoreOrderProductActivityHistoryResultDto
        {
            StoreCode = input.StoreCode,
            ProductCode = input.ProductCode,
            PageNumber = input.PageNumber,
            PageSize = input.PageSize,
            Items = new List<StoreOrderProductActivityHistoryItemDto>(),
        };
    }

    internal static StoreOrderSalesSinceLastArrivalResultDto CreateSalesResult(
        SalesSinceLastArrivalQueryInput input
    )
    {
        return new StoreOrderSalesSinceLastArrivalResultDto
        {
            StoreCode = input.StoreCode,
            ProductCode = input.ProductCode,
            PageNumber = input.PageNumber,
            PageSize = input.PageSize,
            Items = new List<StoreOrderSalesSinceLastArrivalItemDto>(),
        };
    }

    internal static List<StoreOrderSalesSinceLastArrivalSummaryItemDto> CreateSalesSummaryResult(
        IReadOnlyList<string> productCodes
    )
    {
        return productCodes
            .Select(productCode => new StoreOrderSalesSinceLastArrivalSummaryItemDto
            {
                ProductCode = productCode,
            })
            .ToList();
    }
}
