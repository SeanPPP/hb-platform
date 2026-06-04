using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 分店订货发票邮件附件生成服务。
    /// </summary>
    public interface IStoreOrderInvoiceAttachmentService
    {
        /// <summary>
        /// 生成发票邮件 PDF 和 Excel 附件。
        /// </summary>
        Task<ApiResponse<StoreOrderInvoiceAttachmentBundle>> GenerateAttachmentsAsync(
            string orderGuid,
            CancellationToken cancellationToken = default
        );
    }
}
