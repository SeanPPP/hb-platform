using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React;

public interface IWarehouseRetailPriceChangeService
{
    Task<WarehouseRetailPriceChangePage> GetAsync(
        WarehouseRetailPriceChangeQuery query,
        CancellationToken cancellationToken = default
    );
}
