using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Repositories.Interfaces
{
    public interface IStoreRetailPriceRepository : IRepository<StoreRetailPrice>
    {
        ISugarQueryable<StoreRetailPrice> QueryActive();

        Task<StoreRetailPriceDetailDto?> GetDetailByUuidAsync(string uuid);

        Task<List<StoreRetailPriceListDto>> GetListByUuidsAsync(List<string> uuids);

        Task<List<string>> GetActiveStoreCodesAsync();

        Task<int> SoftDeleteByUuidAsync(string uuid, string updatedBy);

        Task<int> SoftDeleteByUuidsAsync(List<string> uuids, string updatedBy);
    }
}
