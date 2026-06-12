using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// Excel 粘贴导入后台 job 服务。
    /// </summary>
    public interface IStoreOrderPasteReplaceJobService
    {
        /// <summary>
        /// 创建 Excel 粘贴导入 job。
        /// </summary>
        Task<StoreOrderPasteReplaceJobDto> StartJobAsync(
            PasteReplaceOrderLinesDto request,
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// 获取 Excel 粘贴导入 job 状态。
        /// </summary>
        Task<StoreOrderPasteReplaceJobDto?> GetJobAsync(
            string jobId,
            CancellationToken cancellationToken = default
        );
    }
}
