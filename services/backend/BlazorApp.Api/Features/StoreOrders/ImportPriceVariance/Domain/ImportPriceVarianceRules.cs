using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Domain;

internal readonly record struct ImportPriceVariancePage(int PageNumber, int PageSize);

internal sealed record ImportPriceVarianceQueryInput(
    StoreOrderImportPriceVarianceQueryDto Query,
    ImportPriceVariancePage Page,
    IReadOnlyList<string> RequestedStoreCodes
);

internal sealed record ImportPriceVarianceDetailQueryInput(
    StoreOrderImportPriceVarianceDetailQueryDto Query,
    ImportPriceVariancePage Page,
    IReadOnlyList<string> RequestedStoreCodes,
    string? ProductCode
);

internal sealed record ImportPriceVarianceDomesticPriceInput(
    string ProductCode,
    decimal DomesticPrice
);

internal sealed record ImportPriceVarianceWarehouseImportPriceInput(
    string ProductCode,
    decimal WarehouseImportPrice
);

internal sealed record ImportPriceVarianceWarehouseImportPriceBatchInput(
    IReadOnlyList<string> ProductCodes,
    decimal WarehouseImportPrice
);

internal sealed record ImportPriceVarianceValidationResult<T>(
    bool IsValid,
    T? Value,
    string? ErrorMessage
)
    where T : class
{
    internal static ImportPriceVarianceValidationResult<T> Valid(T value) =>
        new(true, value, null);

    internal static ImportPriceVarianceValidationResult<T> Invalid(string errorMessage) =>
        new(false, null, errorMessage);
}

internal sealed record ImportPriceVarianceWriteResult<T>(
    bool Success,
    T? Data,
    string? ErrorMessage
)
    where T : class
{
    internal static ImportPriceVarianceWriteResult<T> Ok(T data) => new(true, data, null);

    internal static ImportPriceVarianceWriteResult<T> Fail(string errorMessage) =>
        new(false, null, errorMessage);
}

internal readonly record struct ImportPriceVarianceStoreSelection(
    IReadOnlyList<string> StoreCodes,
    bool NoAccessibleStores
);

internal static class ImportPriceVarianceRules
{
    internal const int DefaultPageSize = 20;
    internal const int MaximumPageSize = 500;
    internal const int WarehouseImportPriceBatchLimit = 500;

    internal static ImportPriceVariancePage NormalizePage(int pageNumber, int pageSize)
    {
        return new ImportPriceVariancePage(
            Math.Max(1, pageNumber),
            Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaximumPageSize)
        );
    }

    internal static IReadOnlyList<string> NormalizeRequestedStoreCodes(
        StoreOrderImportPriceVarianceQueryDto query
    )
    {
        var storeCodes = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.StoreCode))
        {
            storeCodes.Add(query.StoreCode.Trim());
        }

        if (query.StoreCodes != null)
        {
            storeCodes.AddRange(
                query.StoreCodes
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code.Trim())
            );
        }

        return storeCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static IReadOnlyList<string> NormalizeSelectedProductCodes(
        IEnumerable<string?> productCodes
    )
    {
        // 选择结果保留首次出现的大小写和顺序；套装锁自己的稳定规范化由 Common 协调器负责。
        return productCodes
            .Select(code => code?.Trim())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(code => code!)
            .ToList();
    }

    internal static ImportPriceVarianceStoreSelection ApplyAccessScope(
        IReadOnlyList<string> requestedStoreCodes,
        IReadOnlyList<string>? accessibleStoreCodes
    )
    {
        if (accessibleStoreCodes == null)
        {
            return new ImportPriceVarianceStoreSelection(requestedStoreCodes, false);
        }

        var selectedStoreCodes = requestedStoreCodes.Count > 0
            ? requestedStoreCodes
                .Intersect(accessibleStoreCodes, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : accessibleStoreCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        return new ImportPriceVarianceStoreSelection(
            selectedStoreCodes,
            selectedStoreCodes.Count == 0
        );
    }

    internal static decimal NormalizePrice(decimal price) =>
        Math.Round(price, 2, MidpointRounding.AwayFromZero);

    internal static StoreOrderImportPriceVarianceResultDto CreateEmptyResult(
        ImportPriceVariancePage page
    )
    {
        return new StoreOrderImportPriceVarianceResultDto
        {
            Items = new List<StoreOrderImportPriceVarianceItemDto>(),
            Total = 0,
            PageNumber = page.PageNumber,
            PageSize = page.PageSize,
            Summary = new StoreOrderImportPriceVarianceSummaryDto(),
            SupplierSummaries = new List<StoreOrderImportPriceVarianceSupplierSummaryDto>(),
        };
    }

    internal static StoreOrderImportPriceVarianceDetailResultDto CreateEmptyDetailResult(
        ImportPriceVariancePage page
    )
    {
        return new StoreOrderImportPriceVarianceDetailResultDto
        {
            Items = new List<StoreOrderImportPriceVarianceDetailItemDto>(),
            Total = 0,
            PageNumber = page.PageNumber,
            PageSize = page.PageSize,
            Summary = new StoreOrderImportPriceVarianceSummaryDto(),
        };
    }
}
