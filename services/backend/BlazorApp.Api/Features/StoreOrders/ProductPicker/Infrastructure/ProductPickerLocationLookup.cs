using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;

namespace BlazorApp.Api.Features.StoreOrders.ProductPicker.Infrastructure;

internal interface IProductPickerLocationLookup
{
    bool IsEnabled { get; }

    Task<StoreOrderLocationProductLookupResult?> LookupAsync(
        string identifier,
        CancellationToken cancellationToken = default
    );
}

internal sealed class ProductPickerLocationLookup(
    IStoreOrderActorContext actorContext,
    IStoreOrderLocationProductLookupService locationProductLookupService
) : IProductPickerLocationLookup
{
    private static readonly string[] AllowedRoleNames = Permissions.SuperAdminRoleNames
        .Concat(Permissions.WarehouseManagerRoleNames)
        .Concat(new[] { "WarehouseStaff", "仓库员工" })
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public bool IsEnabled => AllowedRoleNames.Any(actorContext.HasRole);

    public Task<StoreOrderLocationProductLookupResult?> LookupAsync(
        string identifier,
        CancellationToken cancellationToken = default
    )
    {
        return locationProductLookupService.LookupAsync(identifier, cancellationToken);
    }
}
