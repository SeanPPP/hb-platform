using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Features.LocalSupplierInvoices;

/// <summary>商品审核 DTO 与持久化实体的映射边界，不包含业务规则或数据库访问。</summary>
internal static class LocalSupplierInvoicesProductReviewAssembler
{
    public static List<StoreLocalSupplierInvoiceDetails> CreateUpdateEntities(
        LocalSupplierInvoicesProductReviewEvaluation evaluation,
        IReadOnlyCollection<StoreLocalSupplierInvoiceDetails> details,
        DateTime updatedAt)
    {
        var detailsByGuid = details.ToDictionary(detail => detail.DetailGUID);
        return evaluation.Results.Select(result => new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = result.DetailGuid,
            ProductCode = result.ProductInfo?.ProductCode,
            StoreProductCode = result.ProductInfo?.StoreProductCode,
            // 已保存的上次进货价是历史快照，只允许在其为空时由检测结果补齐。
            LastPurchasePrice = detailsByGuid.TryGetValue(result.DetailGuid, out var detail)
                ? detail.LastPurchasePrice ?? result.LastPurchasePrice
                : result.LastPurchasePrice,
            AutoPricing = result.AutoPricing,
            IsSpecialProduct = result.IsSpecialProduct,
            DiscountRate = result.DiscountRate,
            ExistingProductCount = result.ExistingProductCount,
            BarcodeStatus = result.BarcodeStatus,
            BarcodeMatchCount = result.BarcodeMatchCount,
            PricingFloatRate = result.PricingFloatRate,
            NewAutoRetailPrice = result.NewAutoRetailPrice,
            ActivityType = result.DefaultAction,
            UpdatedAt = updatedAt,
        }).ToList();
    }
}
