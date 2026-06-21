using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services.React
{
    public class NoopLocalSupplierInvoiceOcrService : ILocalSupplierInvoiceOcrService
    {
        public Task<ApiResponse<string>> ExtractTextAsync(
            byte[] fileBytes,
            string fileName,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(
                ApiResponse<string>.Error(
                    "PDF 文本为空，且未配置 OCR 服务，无法继续导入",
                    "OCR_NOT_CONFIGURED"
                )
            );
        }
    }
}
