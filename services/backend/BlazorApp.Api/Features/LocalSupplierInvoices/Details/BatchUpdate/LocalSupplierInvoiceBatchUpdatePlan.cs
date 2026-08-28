using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Features.LocalSupplierInvoices.Details.BatchUpdate;

internal sealed class LocalSupplierInvoiceBatchUpdatePlan
{
    private readonly bool _updatePurchasePrice;
    private readonly decimal? _purchasePrice;
    private readonly bool _updateRetailPrice;
    private readonly decimal? _retailPrice;
    private readonly bool _updateIsAutoPricing;
    private readonly bool? _isAutoPricing;
    private readonly bool _updateIsSpecialProduct;
    private readonly bool? _isSpecialProduct;
    private readonly bool _updateDiscountRate;
    private readonly decimal? _discountRate;

    private LocalSupplierInvoiceBatchUpdatePlan(
        string invoiceGuid,
        IReadOnlyList<string> detailGuids,
        UpdateToStorePricesFields editFields,
        string updatedBy,
        DateTime now
    )
    {
        InvoiceGuid = invoiceGuid;
        DetailGuids = detailGuids.ToArray();
        RequestedDetailCount = DetailGuids.Count;
        UpdatedBy = updatedBy;
        Now = now;
        _updatePurchasePrice = editFields.UpdatePurchasePrice;
        _purchasePrice = editFields.PurchasePrice;
        _updateRetailPrice = editFields.UpdateRetailPrice;
        _retailPrice = editFields.RetailPrice;
        _updateIsAutoPricing = editFields.UpdateIsAutoPricing;
        _isAutoPricing = editFields.IsAutoPricing;
        _updateIsSpecialProduct = editFields.UpdateIsSpecialProduct;
        _isSpecialProduct = editFields.IsSpecialProduct;
        _updateDiscountRate = editFields.UpdateDiscountRate;
        _discountRate = editFields.DiscountRate;
        PersistenceColumns = BuildPersistenceColumns();
    }

    internal string InvoiceGuid { get; }
    internal IReadOnlyList<string> DetailGuids { get; }
    internal int RequestedDetailCount { get; }
    internal string UpdatedBy { get; }
    internal DateTime Now { get; }
    internal string[] PersistenceColumns { get; }

    internal static LocalSupplierInvoiceBatchUpdatePlan Create(
        string invoiceGuid,
        IReadOnlyList<string> detailGuids,
        UpdateToStorePricesFields editFields,
        string updatedBy,
        DateTime now
    ) => new(invoiceGuid, detailGuids, editFields, updatedBy, now);

    internal async Task ApplyAllowedFieldsAsync(
        StoreLocalSupplierInvoice freshHeader,
        IReadOnlyCollection<StoreLocalSupplierInvoiceDetails> freshDetails,
        Func<
            StoreLocalSupplierInvoiceDetails,
            string?,
            string?,
            Task
        > applyAutoPricingPreviewAsync
    )
    {
        foreach (var detail in freshDetails)
        {
            if (_updatePurchasePrice && _purchasePrice.HasValue)
            {
                detail.PurchasePrice = _purchasePrice.Value;
                detail.Amount = (detail.Quantity ?? 0m) * _purchasePrice.Value;
            }
            if (_updateRetailPrice && _retailPrice.HasValue)
                detail.RetailPrice = _retailPrice.Value;
            if (_updateIsAutoPricing && _isAutoPricing.HasValue)
                detail.AutoPricing = _isAutoPricing.Value;
            if (_updateIsSpecialProduct && _isSpecialProduct.HasValue)
                detail.IsSpecialProduct = _isSpecialProduct.Value;
            if (_updateDiscountRate && _discountRate.HasValue)
                detail.DiscountRate = _discountRate.Value;

            await applyAutoPricingPreviewAsync(
                detail,
                freshHeader.SupplierCode,
                freshHeader.StoreCode
            );
            detail.UpdatedAt = Now;
            detail.UpdatedBy = UpdatedBy;
        }
    }

    private string[] BuildPersistenceColumns()
    {
        var columns = new List<string>();
        if (_updatePurchasePrice)
        {
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.PurchasePrice));
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.Amount));
        }
        if (_updateRetailPrice)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.RetailPrice));
        if (_updateIsAutoPricing)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.AutoPricing));
        if (_updateIsSpecialProduct)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.IsSpecialProduct));
        if (_updateDiscountRate)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.DiscountRate));

        // 自动定价预览沿用旧行为，可由任意批量编辑触发派生字段刷新。
        columns.Add(nameof(StoreLocalSupplierInvoiceDetails.PricingFloatRate));
        columns.Add(nameof(StoreLocalSupplierInvoiceDetails.NewAutoRetailPrice));
        columns.Add(nameof(StoreLocalSupplierInvoiceDetails.UpdatedAt));
        columns.Add(nameof(StoreLocalSupplierInvoiceDetails.UpdatedBy));
        return columns.ToArray();
    }
}
