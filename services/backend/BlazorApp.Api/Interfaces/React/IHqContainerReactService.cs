using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IHqContainerReactService
    {
        Task<ContainerListResponse> GetContainersAsync(ContainerQueryRequest request);
        Task<ContainerMainDto?> GetContainerDetailAsync(string containerGuid);
    }
}
