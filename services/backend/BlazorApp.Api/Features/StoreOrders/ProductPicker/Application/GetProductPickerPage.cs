using BlazorApp.Api.Features.StoreOrders.ProductPicker.Domain;
using BlazorApp.Api.Features.StoreOrders.ProductPicker.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ProductPicker.Application;

internal sealed record GetProductPickerPageQuery(StoreOrderFilterDto? Filter);

internal sealed class GetProductPickerPageValidator
{
    internal ProductPickerPageInput Validate(GetProductPickerPageQuery query)
    {
        var filter = query.Filter ?? throw new ArgumentNullException(nameof(query.Filter));
        return new ProductPickerPageInput(
            filter,
            ProductPickerRules.NormalizeGrades(filter.Grade)
        );
    }
}

internal sealed class GetProductPickerPageHandler(
    GetProductPickerPageValidator validator,
    ProductPickerPageQueryStore queryStore,
    ProductPickerPageCacheStore cacheStore
)
{
    internal async Task<PagedListReactDto<StoreOrderProductDto>> HandleAsync(
        GetProductPickerPageQuery query
    )
    {
        var input = validator.Validate(query);
        if (cacheStore.TryGet(input.Filter, out var cachedResult))
        {
            return cachedResult!;
        }

        var result = await queryStore.GetPagedListAsync(input);
        cacheStore.Set(input.Filter, result);
        return result;
    }
}
