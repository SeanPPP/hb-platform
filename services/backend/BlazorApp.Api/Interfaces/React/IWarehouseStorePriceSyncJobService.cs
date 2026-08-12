using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React;

public interface IWarehouseStorePriceSyncJobService
{
    Task<WarehouseStorePriceSyncJobDto> StartJobAsync(
        WarehouseStorePriceSyncRequestDto request,
        string updatedBy,
        CancellationToken cancellationToken = default
    );

    Task<WarehouseStorePriceSyncJobDto?> GetJobAsync(
        string jobId,
        CancellationToken cancellationToken = default
    );
}
