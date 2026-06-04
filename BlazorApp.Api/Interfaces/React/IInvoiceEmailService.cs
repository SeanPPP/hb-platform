using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 发票邮件发送服务接口。
    /// </summary>
    public interface IInvoiceEmailService
    {
        /// <summary>
        /// 发送带 PDF 附件的发票邮件。
        /// </summary>
        Task<ApiResponse<bool>> SendInvoiceAsync(StoreOrderInvoiceEmailMessage message);
    }
}
