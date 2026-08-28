using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Features.LocalSupplierInvoices.Details.BatchUpsert;

internal sealed class LocalSupplierInvoiceBatchUpsertPlan
{
    private readonly List<StoreLocalSupplierInvoiceDetails> _newDetails;
    private readonly List<LocalSupplierInvoiceBatchUpsertUpdateDraft> _requestedUpdates;

    private LocalSupplierInvoiceBatchUpsertPlan(
        string invoiceGuid,
        DateTime now,
        string updatedBy,
        List<StoreLocalSupplierInvoiceDetails> newDetails,
        List<LocalSupplierInvoiceBatchUpsertUpdateDraft> requestedUpdates
    )
    {
        InvoiceGuid = invoiceGuid;
        Now = now;
        UpdatedBy = updatedBy;
        _newDetails = newDetails;
        _requestedUpdates = requestedUpdates;
    }

    internal string InvoiceGuid { get; }
    internal DateTime Now { get; }
    internal string UpdatedBy { get; }
    internal IReadOnlyList<string> RequestedUpdateDetailGuids =>
        _requestedUpdates.Select(update => update.Detail.DetailGUID).ToList();

    internal static LocalSupplierInvoiceBatchUpsertPlan Create(
        string invoiceGuid,
        IEnumerable<InvoiceDetailUpsertItemDto?>? items,
        string updatedBy,
        DateTime now,
        Func<string?, IEnumerable<string>?, string?> serializeAdditionalBarcodes
    )
    {
        var newDetails = new List<StoreLocalSupplierInvoiceDetails>();
        var requestedUpdates = new List<LocalSupplierInvoiceBatchUpsertUpdateDraft>();
        foreach (var item in items ?? Enumerable.Empty<InvoiceDetailUpsertItemDto?>())
        {
            if (item == null)
                continue;

            var mapped = MapRequestedFields(item, now, updatedBy, serializeAdditionalBarcodes);
            if (string.IsNullOrWhiteSpace(item.DetailGUID))
            {
                mapped.DetailGUID = UuidHelper.GenerateUuid7();
                mapped.InvoiceGUID = invoiceGuid;
                mapped.CreatedAt = now;
                mapped.CreatedBy = updatedBy;
                mapped.IsDeleted = false;
                newDetails.Add(mapped);
            }
            else
            {
                mapped.DetailGUID = item.DetailGUID;
                requestedUpdates.Add(
                    new LocalSupplierInvoiceBatchUpsertUpdateDraft(
                        mapped,
                        BuildUpdateColumns(mapped)
                    )
                );
            }
        }

        return new LocalSupplierInvoiceBatchUpsertPlan(
            invoiceGuid,
            now,
            updatedBy,
            newDetails,
            requestedUpdates
        );
    }

    internal LocalSupplierInvoiceBatchUpsertWriteSet BuildWriteSet(
        IReadOnlyCollection<StoreLocalSupplierInvoiceDetails> existingRecords,
        StoreLocalSupplierInvoice freshHeader
    )
    {
        var existingByGuid = existingRecords.ToDictionary(detail => detail.DetailGUID);
        var inserts = _newDetails
            .Select(detail => CreateScopedInsert(detail, freshHeader, preserveDetailGuid: true))
            .ToList();
        var updates = new List<LocalSupplierInvoiceBatchUpsertUpdateCommand>();

        foreach (var requestedUpdate in _requestedUpdates)
        {
            if (existingByGuid.TryGetValue(requestedUpdate.Detail.DetailGUID, out var existing))
            {
                MergeRequestedFields(existing, requestedUpdate.Detail);
                updates.Add(
                    new LocalSupplierInvoiceBatchUpsertUpdateCommand(
                        existing,
                        requestedUpdate.UpdateColumns
                    )
                );
            }
            else
            {
                inserts.Add(
                    CreateScopedInsert(
                        requestedUpdate.Detail,
                        freshHeader,
                        preserveDetailGuid: false
                    )
                );
            }
        }

        return new LocalSupplierInvoiceBatchUpsertWriteSet(inserts, updates);
    }

    private static StoreLocalSupplierInvoiceDetails MapRequestedFields(
        InvoiceDetailUpsertItemDto item,
        DateTime now,
        string updatedBy,
        Func<string?, IEnumerable<string>?, string?> serializeAdditionalBarcodes
    ) =>
        new()
        {
            StoreProductCode = item.StoreProductCode,
            ProductCode = item.ProductCode,
            ItemNumber = item.ItemNumber,
            Barcode = item.Barcode,
            AdditionalBarcodesJson = serializeAdditionalBarcodes(
                item.Barcode,
                item.AdditionalBarcodes
            ),
            ProductName = item.ProductName,
            ProductCategoryGUID = item.ProductCategoryGUID,
            Quantity = item.Quantity,
            LastPurchasePrice = item.LastPurchasePrice,
            PurchasePrice = item.PurchasePrice,
            RetailPrice = item.RetailPrice,
            Amount = item.Amount,
            ActivityType = item.ActivityType,
            DiscountRate = item.DiscountRate,
            AutoPricing = item.AutoPricing,
            PricingFloatRate = item.PricingFloatRate,
            NewAutoRetailPrice = item.NewAutoRetailPrice,
            IsSpecialProduct = item.IsSpecialProduct,
            UpdatedAt = now,
            UpdatedBy = updatedBy,
        };

    private static string[] BuildUpdateColumns(
        StoreLocalSupplierInvoiceDetails requested
    )
    {
        var columns = new List<string>();
        if (requested.StoreProductCode != null)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.StoreProductCode));
        if (requested.ProductCode != null)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.ProductCode));
        if (requested.ItemNumber != null)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.ItemNumber));
        if (requested.Barcode != null)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.Barcode));
        if (requested.AdditionalBarcodesJson != null)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.AdditionalBarcodesJson));
        if (requested.ProductName != null)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.ProductName));
        if (requested.ProductCategoryGUID != null)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.ProductCategoryGUID));
        if (requested.Quantity != null)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.Quantity));
        if (requested.LastPurchasePrice != null)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.LastPurchasePrice));
        if (requested.PurchasePrice != null)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.PurchasePrice));
        if (requested.RetailPrice != null)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.RetailPrice));
        if (requested.Amount != null)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.Amount));
        if (requested.ActivityType != null)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.ActivityType));
        if (requested.DiscountRate != null)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.DiscountRate));
        if (requested.AutoPricing != null)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.AutoPricing));
        if (requested.PricingFloatRate != null)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.PricingFloatRate));
        if (requested.NewAutoRetailPrice != null)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.NewAutoRetailPrice));
        if (requested.IsSpecialProduct != null)
            columns.Add(nameof(StoreLocalSupplierInvoiceDetails.IsSpecialProduct));

        columns.Add(nameof(StoreLocalSupplierInvoiceDetails.UpdatedAt));
        columns.Add(nameof(StoreLocalSupplierInvoiceDetails.UpdatedBy));
        return columns.ToArray();
    }

    private StoreLocalSupplierInvoiceDetails CreateScopedInsert(
        StoreLocalSupplierInvoiceDetails source,
        StoreLocalSupplierInvoice freshHeader,
        bool preserveDetailGuid
    ) =>
        new()
        {
            DetailGUID = preserveDetailGuid ? source.DetailGUID : UuidHelper.GenerateUuid7(),
            InvoiceGUID = InvoiceGuid,
            StoreCode = freshHeader.StoreCode,
            SupplierCode = freshHeader.SupplierCode,
            StoreProductCode = source.StoreProductCode,
            ProductCode = source.ProductCode,
            ItemNumber = source.ItemNumber,
            Barcode = source.Barcode,
            AdditionalBarcodesJson = source.AdditionalBarcodesJson,
            ProductName = source.ProductName,
            ProductCategoryGUID = source.ProductCategoryGUID,
            Quantity = source.Quantity,
            LastPurchasePrice = source.LastPurchasePrice,
            PurchasePrice = source.PurchasePrice,
            RetailPrice = source.RetailPrice,
            Amount = source.Amount,
            ActivityType = source.ActivityType,
            DiscountRate = source.DiscountRate,
            AutoPricing = source.AutoPricing,
            PricingFloatRate = source.PricingFloatRate,
            NewAutoRetailPrice = source.NewAutoRetailPrice,
            IsSpecialProduct = source.IsSpecialProduct,
            CreatedAt = Now,
            UpdatedAt = Now,
            CreatedBy = UpdatedBy,
            UpdatedBy = UpdatedBy,
            IsDeleted = false,
        };

    private static void MergeRequestedFields(
        StoreLocalSupplierInvoiceDetails existing,
        StoreLocalSupplierInvoiceDetails requested
    )
    {
        if (requested.StoreProductCode != null)
            existing.StoreProductCode = requested.StoreProductCode;
        if (requested.ProductCode != null)
            existing.ProductCode = requested.ProductCode;
        if (requested.ItemNumber != null)
            existing.ItemNumber = requested.ItemNumber;
        if (requested.Barcode != null)
            existing.Barcode = requested.Barcode;
        if (requested.AdditionalBarcodesJson != null)
            existing.AdditionalBarcodesJson = requested.AdditionalBarcodesJson;
        if (requested.ProductName != null)
            existing.ProductName = requested.ProductName;
        if (requested.ProductCategoryGUID != null)
            existing.ProductCategoryGUID = requested.ProductCategoryGUID;
        if (requested.Quantity != null)
            existing.Quantity = requested.Quantity;
        if (requested.LastPurchasePrice != null)
            existing.LastPurchasePrice = requested.LastPurchasePrice;
        if (requested.PurchasePrice != null)
            existing.PurchasePrice = requested.PurchasePrice;
        if (requested.RetailPrice != null)
            existing.RetailPrice = requested.RetailPrice;
        if (requested.Amount != null)
            existing.Amount = requested.Amount;
        if (requested.ActivityType != null)
            existing.ActivityType = requested.ActivityType;
        if (requested.DiscountRate != null)
            existing.DiscountRate = requested.DiscountRate;
        if (requested.AutoPricing != null)
            existing.AutoPricing = requested.AutoPricing;
        if (requested.PricingFloatRate != null)
            existing.PricingFloatRate = requested.PricingFloatRate;
        if (requested.NewAutoRetailPrice != null)
            existing.NewAutoRetailPrice = requested.NewAutoRetailPrice;
        if (requested.IsSpecialProduct != null)
            existing.IsSpecialProduct = requested.IsSpecialProduct;
        existing.UpdatedAt = requested.UpdatedAt;
        existing.UpdatedBy = requested.UpdatedBy;
    }
}

internal sealed record LocalSupplierInvoiceBatchUpsertWriteSet(
    List<StoreLocalSupplierInvoiceDetails> Inserts,
    List<LocalSupplierInvoiceBatchUpsertUpdateCommand> Updates
);

internal sealed record LocalSupplierInvoiceBatchUpsertUpdateDraft(
    StoreLocalSupplierInvoiceDetails Detail,
    string[] UpdateColumns
);

internal sealed record LocalSupplierInvoiceBatchUpsertUpdateCommand(
    StoreLocalSupplierInvoiceDetails Detail,
    string[] UpdateColumns
);
