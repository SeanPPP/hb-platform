using System.ComponentModel.DataAnnotations;
using BlazorApp.Api.Features.StoreOrders.ProductPicker.Domain;
using BlazorApp.Api.Features.StoreOrders.ProductPicker.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ProductPicker.Application;

internal sealed record GetHomePageWarmUpPageQuery(int PageSize);

internal sealed class GetHomePageWarmUpPageValidator
{
    internal ProductPickerHomePageInput Validate(GetHomePageWarmUpPageQuery query)
    {
        if (query.PageSize <= 0)
        {
            throw new ValidationException("首页预热页大小必须大于 0");
        }

        return new ProductPickerHomePageInput(
            query.PageSize,
            ProductPickerHomePageMode.LightweightWarmUp
        );
    }
}

internal sealed class GetHomePageWarmUpPageHandler(
    GetHomePageWarmUpPageValidator validator,
    ProductPickerPageQueryStore queryStore
)
{
    internal Task<PagedListReactDto<StoreOrderProductDto>> HandleAsync(
        GetHomePageWarmUpPageQuery query,
        CancellationToken cancellationToken = default
    )
    {
        return queryStore.GetHomePageAsync(
            validator.Validate(query),
            cancellationToken
        );
    }
}
