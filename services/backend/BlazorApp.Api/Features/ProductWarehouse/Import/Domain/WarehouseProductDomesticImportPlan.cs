using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.ProductWarehouse;

/// <summary>
/// 国内导入在打开事务前固定批次和目标编码，后续锁、历史和写入共享同一计划。
/// </summary>
internal sealed record WarehouseProductDomesticImportPlan(
    string UpdatedBy,
    Guid BatchGuid,
    List<string> ProductCodes
)
{
    internal static bool TryCreate(
        ImportFromDomesticRequestDto request,
        string? updatedBy,
        out WarehouseProductDomesticImportPlan? plan,
        out string error
    )
    {
        if (request.ProductCodes == null || !request.ProductCodes.Any())
        {
            plan = null;
            error = "请选择要导入的商品";
            return false;
        }

        plan = new WarehouseProductDomesticImportPlan(
            ProductWarehouseSliceBase.ResolveUpdatedBy(updatedBy),
            Guid.NewGuid(),
            request.ProductCodes.Distinct().ToList()
        );
        error = string.Empty;
        return true;
    }
}
