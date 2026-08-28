using System.ComponentModel.DataAnnotations;
using BlazorApp.Api.Features.StoreOrders.ProductPicker.Domain;
using BlazorApp.Api.Features.StoreOrders.ProductPicker.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ProductPicker.Application;

internal sealed record GetHomePageCachePageQuery(int PageSize);

internal sealed class GetHomePageCachePageValidator
{
    internal ProductPickerHomePageInput Validate(GetHomePageCachePageQuery query)
    {
        if (query.PageSize <= 0)
        {
            throw new ValidationException("首页缓存页大小必须大于 0");
        }

        return new ProductPickerHomePageInput(
            query.PageSize,
            ProductPickerHomePageMode.AccurateCache
        );
    }
}

internal sealed class GetHomePageCachePageHandler(
    GetHomePageCachePageValidator validator,
    ProductPickerPageQueryStore queryStore
)
{
    internal Task<PagedListReactDto<StoreOrderProductDto>> HandleAsync(
        GetHomePageCachePageQuery query,
        CancellationToken cancellationToken = default
    )
    {
        return queryStore.GetHomePageAsync(
            validator.Validate(query),
            cancellationToken
        );
    }
}
