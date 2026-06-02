using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 货柜明细创建新商品 job 服务。
    /// </summary>
    public interface IContainerProductCreationJobService
    {
        Task<ContainerProductCreationJobDto> StartJobAsync(
            string userId,
            ContainerProductCreationJobRequestDto request,
            CancellationToken cancellationToken = default
        );

        Task<ContainerProductCreationJobDto?> GetJobAsync(
            string userId,
            string jobId,
            CancellationToken cancellationToken = default
        );
    }
}
