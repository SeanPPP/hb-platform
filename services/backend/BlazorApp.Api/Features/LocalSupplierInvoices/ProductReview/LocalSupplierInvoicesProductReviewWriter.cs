using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Features.LocalSupplierInvoices;

/// <summary>商品审核唯一写入边界；命令只在此处建立一个事务。</summary>
internal sealed class LocalSupplierInvoicesProductReviewWriter
{
    private readonly SqlSugarContext _context;

    public LocalSupplierInvoicesProductReviewWriter(LocalSupplierInvoicesDependencies dependencies)
    {
        _context = dependencies.Context;
    }

    public async Task PersistAsync(List<StoreLocalSupplierInvoiceDetails> updates)
    {
        if (updates.Count == 0)
            return;

        var db = _context.Db;
        await db.Ado.BeginTranAsync();
        try
        {
            // 审核字段必须一次性覆盖写入，避免旧商品编码、条码状态或建议操作残留。
            await db.Updateable(updates).UpdateColumns(new[]
            {
                "ProductCode",
                "StoreProductCode",
                "LastPurchasePrice",
                "AutoPricing",
                "IsSpecialProduct",
                "DiscountRate",
                "ExistingProductCount",
                "BarcodeStatus",
                "BarcodeMatchCount",
                "PricingFloatRate",
                "NewAutoRetailPrice",
                "ActivityType",
                "UpdatedAt",
            }).ExecuteCommandAsync();
            await db.Ado.CommitTranAsync();
        }
        catch (Exception)
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }
}
