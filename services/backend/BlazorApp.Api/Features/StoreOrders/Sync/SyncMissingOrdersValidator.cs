using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Sync;

internal sealed class SyncMissingOrdersValidator
{
    internal List<string> NormalizeStoreCodes(SyncMissingOrdersRequestDto? request)
    {
        var source =
            request?.StoreCodes?.Where(item => !string.IsNullOrWhiteSpace(item))
            ?? Enumerable.Empty<string>();
        var storeCodes = source
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (storeCodes.Count == 0 && !string.IsNullOrWhiteSpace(request?.StoreCode))
        {
            storeCodes.Add(request.StoreCode.Trim());
        }

        return storeCodes;
    }
}
