using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.ProductWarehouse;

internal sealed record WarehouseProductSingleCreationPlan(string UpdatedBy)
{
    internal static WarehouseProductSingleCreationPlan Create(string? updatedBy) =>
        new(ProductWarehouseSliceBase.ResolveUpdatedBy(updatedBy));
}

internal static class WarehouseProductSingleCreationValidator
{
    internal static string? ValidateIdentityPrerequisites(CreateSingleProductRequestDto request)
    {
        if (
            string.IsNullOrWhiteSpace(request.ItemNumber)
            && string.IsNullOrWhiteSpace(request.SupplierCode)
            && !request.SupplierId.HasValue
        )
        {
            return "货号为空时需提供供应商编码以自动生成";
        }
        return null;
    }

    internal static string? ValidatePricing(CreateSingleProductRequestDto request) =>
        request.OEMPrice <= 0
            ? "零售价必须大于0"
            : request.ImportPrice <= 0
                ? "进口价格必须大于0"
                : null;
}
