using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 分店订货发票邮件后台发送 job 服务。
    /// </summary>
    public interface IStoreOrderInvoiceEmailJobService
    {
        /// <summary>
        /// 创建发票邮件发送 job。
        /// </summary>
        Task<StoreOrderInvoiceEmailJobDto> StartJobAsync(
            SendStoreOrderInvoiceEmailDto request,
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// 获取发票邮件发送 job 状态。
        /// </summary>
        Task<StoreOrderInvoiceEmailJobDto?> GetJobAsync(
            string jobId,
            CancellationToken cancellationToken = default
        );
    }
}
