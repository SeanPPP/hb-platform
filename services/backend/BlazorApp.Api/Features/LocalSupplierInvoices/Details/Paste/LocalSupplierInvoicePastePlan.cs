using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Features.LocalSupplierInvoices.Details.Paste;

internal sealed class LocalSupplierInvoicePastePlan
{
    private readonly IReadOnlyList<PastedDetailItemDto> _items;
    private readonly Func<string?, IEnumerable<string>?, string?> _serializeAdditionalBarcodes;

    private LocalSupplierInvoicePastePlan(
        string invoiceGuid,
        string mode,
        IReadOnlyList<PastedDetailItemDto> items,
        string updatedBy,
        DateTime now,
        Func<string?, IEnumerable<string>?, string?> serializeAdditionalBarcodes
    )
    {
        InvoiceGuid = invoiceGuid;
        Mode = mode;
        _items = items;
        UpdatedBy = updatedBy;
        Now = now;
        _serializeAdditionalBarcodes = serializeAdditionalBarcodes;
    }

    internal string InvoiceGuid { get; }
    internal string Mode { get; }
    internal string UpdatedBy { get; }
    internal DateTime Now { get; }

    internal static LocalSupplierInvoicePastePlan Create(
        PasteDetailsRequest request,
        string updatedBy,
        DateTime now,
        Func<PastedDetailItemDto, bool> isLikelyHeaderItem,
        Func<PastedDetailItemDto, PastedDetailItemDto> normalizeItem,
        Func<string?, IEnumerable<string>?, string?> serializeAdditionalBarcodes
    )
    {
        var items = (request.Items ?? new List<PastedDetailItemDto>())
            .Where(item => item != null && !isLikelyHeaderItem(item))
            .Select(item => normalizeItem(item!))
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.ItemNumber)
                || !string.IsNullOrWhiteSpace(item.Barcode)
            )
            .ToList();
        return new LocalSupplierInvoicePastePlan(
            request.InvoiceGuid,
            request.Mode,
            items,
            updatedBy,
            now,
            serializeAdditionalBarcodes
        );
    }

    internal List<StoreLocalSupplierInvoiceDetails> BuildRows(
        StoreLocalSupplierInvoice freshHeader
    ) =>
        _items
            .Select(item => new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = UuidHelper.GenerateUuid7(),
                InvoiceGUID = InvoiceGuid,
                StoreCode = freshHeader.StoreCode,
                SupplierCode = freshHeader.SupplierCode,
                ItemNumber = item.ItemNumber,
                Barcode = item.Barcode,
                AdditionalBarcodesJson = _serializeAdditionalBarcodes(
                    item.Barcode,
                    item.AdditionalBarcodes
                ),
                ProductName = item.ProductName,
                Quantity = item.Quantity ?? 1,
                PurchasePrice = item.PurchasePrice,
                NewAutoRetailPrice = item.NewAutoRetailPrice,
                RetailPrice = item.RetailPrice,
                AutoPricing = true,
                Amount = (item.Quantity ?? 1) * (item.PurchasePrice ?? 0),
                CreatedAt = Now,
                UpdatedAt = Now,
                CreatedBy = UpdatedBy,
                UpdatedBy = UpdatedBy,
                IsDeleted = false,
            })
            .ToList();
}
