using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;

namespace BlazorApp.Api.Features.ProductWarehouse;

/// <summary>
/// 批量写入开始前一次性固化操作人、批次号和图片配置，避免工作流内重复解释请求。
/// </summary>
internal sealed record WarehouseProductBatchUpdatePlan(
    string UpdatedBy,
    Guid BatchGuid,
    string? NormalizedImageBaseUrl
)
{
    internal static bool TryCreate(
        WarehouseProductBatchUpdateOptionsDto options,
        string? updatedBy,
        out WarehouseProductBatchUpdatePlan? plan,
        out string error
    )
    {
        error = string.Empty;
        if (options.SyncImageToHq && !options.GenerateImageUrls)
        {
            plan = null;
            error = "同步 HQ 图片前必须启用图片地址生成";
            return false;
        }

        string? normalizedImageBaseUrl = null;
        if (
            options.GenerateImageUrls
            && !WarehouseProductBatchImageUrlBuilder.TryNormalizeBaseUrl(
                options.ImageBaseUrl,
                out normalizedImageBaseUrl,
                out error
            )
        )
        {
            plan = null;
            return false;
        }

        plan = new WarehouseProductBatchUpdatePlan(
            ProductWarehouseSliceBase.ResolveUpdatedBy(updatedBy),
            Guid.NewGuid(),
            normalizedImageBaseUrl
        );
        return true;
    }
}

/// <summary>
/// 服务层防御性校验：即使调用方绕过 DTO 注解，也不能造成部分字段写入。
/// </summary>
internal static class WarehouseProductBatchUpdateValidator
{
    internal static string? Validate(UpdateItemDto item)
    {
        if (
            (item.PackingQuantity.HasValue && item.PackingQuantity.Value < 0)
            || (item.MinOrderQuantity.HasValue && item.MinOrderQuantity.Value < 0)
        )
        {
            return $"装箱数和最小起订量不能为负数: ProductCode={item.ProductCode}, ItemNumber={item.ItemNumber}";
        }

        return null;
    }
}
