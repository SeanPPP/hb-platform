using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 供应商商品图片批量更新后台任务服务。
    /// </summary>
    public interface IProductSupplierImageBatchUpdateJobService
    {
        Task<BatchUpdateSupplierImagesJobDto> StartJobAsync(
            BatchUpdateSupplierImagesJobRequest request,
            CancellationToken cancellationToken = default
        );

        Task<BatchUpdateSupplierImagesJobDto?> GetJobAsync(
            string jobId,
            CancellationToken cancellationToken = default
        );
    }
}
