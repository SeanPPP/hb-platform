using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 发票邮件弹窗文本翻译服务。
    /// </summary>
    public interface IStoreOrderInvoiceEmailTextTranslationService
    {
        /// <summary>
        /// 翻译邮件主题和正文到目标语言。
        /// </summary>
        Task<ApiResponse<StoreOrderInvoiceEmailTextTranslationResultDto>> TranslateAsync(
            StoreOrderInvoiceEmailTextTranslationRequestDto request,
            CancellationToken cancellationToken = default
        );
    }
}
