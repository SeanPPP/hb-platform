using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Services;

/// <summary>
/// 仅对商品分页且 checksumVersion=2 的响应启用 gzip 压缩。
/// v1/WPF 即使发送 Accept-Encoding 头也保持未压缩，保证 WPF v1 下载响应
/// 与修改前字节级一致；其他端点保持原有未压缩行为。
/// </summary>
public sealed class CatalogV2ResponseCompressionProvider : ResponseCompressionProvider
{
    public CatalogV2ResponseCompressionProvider(
        IServiceProvider services,
        IOptions<ResponseCompressionOptions> options)
        : base(services, options)
    {
    }

    public override bool ShouldCompressResponse(HttpContext context)
    {
        return IsCatalogV2PageRequest(context) && base.ShouldCompressResponse(context);
    }

    private static bool IsCatalogV2PageRequest(HttpContext context)
    {
        return context.Request.Path.StartsWithSegments(
                   "/api/v1/catalog/sellable-items/page",
                   StringComparison.OrdinalIgnoreCase)
               && string.Equals(
                   context.Request.Query["checksumVersion"].ToString(),
                   "2",
                   StringComparison.Ordinal);
    }
}
