using BlazorApp.Api.Services.React;
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

        /// <summary>
        /// 使用临时 SMTP 配置发送带附件的发票邮件。
        /// </summary>
        Task<ApiResponse<bool>> SendInvoiceAsync(
            StoreOrderInvoiceEmailMessage message,
            InvoiceEmailOptions options
        );
    }
}
