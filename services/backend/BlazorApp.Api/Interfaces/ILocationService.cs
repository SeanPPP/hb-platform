using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces
{
    public interface ILocationService
    {
        Task<LocationDto> GetByGuidAsync(string locationGuid);
        Task<LocationDto> GetByCodeAsync(string locationCode);
        Task<PagedResult<LocationDto>> GetAllAsync(LocationFilterDto filter);
        Task<LocationDto> CreateAsync(CreateLocationDto createDto);
        Task<LocationDto> UpdateAsync(UpdateLocationDto updateDto);
        Task<bool> DeleteAsync(string locationGuid);
        Task<List<LocationDto>> GetByTypeAsync(int locationType);
        Task<List<LocationDto>> GetActiveLocationsAsync();
        Task<bool> ExistsAsync(string locationCode);
        Task<bool> ToggleStatusAsync(string locationGuid);
        Task<List<LocationWithProductCountDto>> GetLocationsWithProductCountsAsync();
    }
}