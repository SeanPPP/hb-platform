using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React;

public interface IWarehouseStorePriceSyncService
{
    Task<List<WarehouseStorePriceSyncTargetStoreDto>> GetTargetStoresAsync(
        CancellationToken cancellationToken = default
    );

    Task<int> GetAllProductCountAsync(CancellationToken cancellationToken = default);

    Task<ApiResponse<WarehouseStorePriceSyncResultDto>> ExecuteAsync(
        WarehouseStorePriceSyncRequestDto request,
        string updatedBy,
        CancellationToken cancellationToken = default
    );
}
