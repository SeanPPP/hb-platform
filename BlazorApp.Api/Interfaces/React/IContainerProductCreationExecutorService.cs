using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 货柜明细创建新商品执行服务。
    /// </summary>
    public interface IContainerProductCreationExecutorService
    {
        Task<ContainerProductCreationResultDto> ExecuteAsync(
            ContainerProductCreationJobRequestDto request,
            CancellationToken cancellationToken = default
        );
    }
}
