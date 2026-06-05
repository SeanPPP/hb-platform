using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 发票邮件弹窗文本翻译服务。
    /// </summary>
    public class StoreOrderInvoiceEmailTextTranslationService
        : IStoreOrderInvoiceEmailTextTranslationService
    {
        private readonly ITranslationService _translationService;
        private readonly ILogger<StoreOrderInvoiceEmailTextTranslationService> _logger;

        public StoreOrderInvoiceEmailTextTranslationService(
            ITranslationService translationService,
            ILogger<StoreOrderInvoiceEmailTextTranslationService> logger
        )
        {
            _translationService = translationService;
            _logger = logger;
        }

        public async Task<ApiResponse<StoreOrderInvoiceEmailTextTranslationResultDto>> TranslateAsync(
            StoreOrderInvoiceEmailTextTranslationRequestDto request,
            CancellationToken cancellationToken = default
        )
        {
            var targetLanguage = NormalizeTargetLanguage(request.TargetLanguage);
            if (targetLanguage == null)
            {
                return ApiResponse<StoreOrderInvoiceEmailTextTranslationResultDto>.Error(
                    "目标语言无效",
                    "STORE_ORDER_INVOICE_EMAIL_TRANSLATION_INVALID_LANGUAGE"
                );
            }

            try
            {
                var result = new StoreOrderInvoiceEmailTextTranslationResultDto
                {
                    Subject = await TranslateOptionalAsync(request.Subject, targetLanguage),
                    Body = await TranslateOptionalAsync(request.Body, targetLanguage),
                };

                return ApiResponse<StoreOrderInvoiceEmailTextTranslationResultDto>.OK(
                    result,
                    "邮件内容翻译成功"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "翻译发票邮件内容失败，目标语言：{TargetLanguage}", targetLanguage);
                return ApiResponse<StoreOrderInvoiceEmailTextTranslationResultDto>.Error(
                    "翻译邮件内容失败，请稍后重试",
                    "STORE_ORDER_INVOICE_EMAIL_TRANSLATION_FAILED"
                );
            }
        }

        private async Task<string?> TranslateOptionalAsync(string? text, string targetLanguage)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            return await _translationService.TranslateAsync(text.Trim(), targetLanguage);
        }

        private static string? NormalizeTargetLanguage(string? targetLanguage)
        {
            return targetLanguage?.Trim().ToLowerInvariant() switch
            {
                "zh" or "zh-cn" => "zh",
                "en" or "en-us" or "en-au" => "en",
                _ => null,
            };
        }
    }
}
