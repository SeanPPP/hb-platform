using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services
{
    public interface IProductStoreSyncService
    {
        Task<ApiResponse<SyncProductsToStoresResult>> SyncProductsToStoresAsync(SyncProductsToStoresRequest request);
    }
}
