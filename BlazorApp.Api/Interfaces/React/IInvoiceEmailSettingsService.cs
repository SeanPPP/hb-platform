using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 发票邮件 SMTP 配置服务接口。
    /// </summary>
    public interface IInvoiceEmailSettingsService
    {
        Task<ApiResponse<InvoiceEmailSettingsDto>> GetSettingsAsync(
            CancellationToken cancellationToken = default
        );

        Task<ApiResponse<InvoiceEmailSettingsDto>> UpdateSettingsAsync(
            UpdateInvoiceEmailSettingsDto request,
            string? updatedBy,
            CancellationToken cancellationToken = default
        );

        Task<InvoiceEmailOptions> GetEffectiveOptionsAsync(
            CancellationToken cancellationToken = default
        );

        Task<InvoiceEmailOptions> BuildTransientOptionsAsync(
            UpdateInvoiceEmailSettingsDto request,
            CancellationToken cancellationToken = default
        );
    }
}
