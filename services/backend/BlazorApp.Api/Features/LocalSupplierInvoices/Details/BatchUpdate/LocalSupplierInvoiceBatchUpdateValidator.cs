using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Features.LocalSupplierInvoices.Details.BatchUpdate;

internal sealed record LocalSupplierInvoiceBatchUpdateFailure(
    string Message,
    string ErrorCode
);

internal sealed record LocalSupplierInvoiceBatchUpdateRequestValidation(
    IReadOnlyList<string> DetailGuids,
    UpdateToStorePricesFields EditFields,
    LocalSupplierInvoiceBatchUpdateFailure? Failure
);

internal static class LocalSupplierInvoiceBatchUpdateValidator
{
    internal static LocalSupplierInvoiceBatchUpdateRequestValidation ValidateRequest(
        BatchUpdateInvoiceDetailsRequest request
    )
    {
        var editFields = request.EditFields ?? new UpdateToStorePricesFields();
        var hasAnyField =
            editFields.UpdatePurchasePrice
            || editFields.UpdateRetailPrice
            || editFields.UpdateIsAutoPricing
            || editFields.UpdateIsSpecialProduct
            || editFields.UpdateDiscountRate;
        if (!hasAnyField)
        {
            return FailedRequest(
                editFields,
                "请至少选择一个要更新的字段",
                "VALIDATION_ERROR"
            );
        }

        var valueErrors = new List<string>();
        if (editFields.UpdatePurchasePrice && !editFields.PurchasePrice.HasValue)
            valueErrors.Add("进货价不能为空");
        if (editFields.UpdateRetailPrice && !editFields.RetailPrice.HasValue)
            valueErrors.Add("零售价不能为空");
        if (editFields.UpdateIsAutoPricing && !editFields.IsAutoPricing.HasValue)
            valueErrors.Add("自动定价不能为空");
        if (editFields.UpdateIsSpecialProduct && !editFields.IsSpecialProduct.HasValue)
            valueErrors.Add("特殊商品不能为空");
        if (editFields.UpdateDiscountRate && !editFields.DiscountRate.HasValue)
            valueErrors.Add("折扣率不能为空");
        if (valueErrors.Count > 0)
        {
            return FailedRequest(
                editFields,
                string.Join("，", valueErrors),
                "VALIDATION_ERROR"
            );
        }

        var detailGuids = (request.Items ?? new List<InvoiceDetailUpsertItemDto>())
            .Where(item => !string.IsNullOrWhiteSpace(item.DetailGUID))
            .Select(item => item.DetailGUID!)
            .Distinct()
            .ToList();
        if (detailGuids.Count == 0)
        {
            return FailedRequest(editFields, "未选择任何明细", "VALIDATION_ERROR");
        }

        return new LocalSupplierInvoiceBatchUpdateRequestValidation(
            detailGuids,
            editFields,
            null
        );
    }

    internal static LocalSupplierInvoiceBatchUpdateFailure? ValidateFreshScope(
        string invoiceGuid,
        StoreLocalSupplierInvoice? initialHeader,
        StoreLocalSupplierInvoice? freshHeader,
        IReadOnlyCollection<StoreLocalSupplierInvoiceDetails> initialDetails,
        IReadOnlyCollection<StoreLocalSupplierInvoiceDetails> freshDetails
    )
    {
        if (initialHeader == null || freshHeader == null)
            return ScopeChanged();

        if (
            !ScopeEquals(initialHeader.InvoiceGUID, freshHeader.InvoiceGUID)
            || !ScopeEquals(initialHeader.StoreCode, freshHeader.StoreCode)
            || !ScopeEquals(initialHeader.SupplierCode, freshHeader.SupplierCode)
        )
        {
            return ScopeChanged();
        }

        if (initialDetails.Count != freshDetails.Count)
            return ScopeChanged();

        var freshByGuid = freshDetails.ToDictionary(detail => detail.DetailGUID);
        foreach (var initialDetail in initialDetails)
        {
            if (
                !freshByGuid.TryGetValue(initialDetail.DetailGUID, out var freshDetail)
                || initialDetail.IsDeleted
                || freshDetail.IsDeleted
                || !BelongsToHeader(initialDetail, invoiceGuid, initialHeader)
                || !BelongsToHeader(freshDetail, invoiceGuid, freshHeader)
                || !ScopeEquals(initialDetail.InvoiceGUID, freshDetail.InvoiceGUID)
                || !ScopeEquals(initialDetail.StoreCode, freshDetail.StoreCode)
                || !ScopeEquals(initialDetail.SupplierCode, freshDetail.SupplierCode)
            )
            {
                return ScopeChanged();
            }
        }

        return null;
    }

    private static bool BelongsToHeader(
        StoreLocalSupplierInvoiceDetails detail,
        string invoiceGuid,
        StoreLocalSupplierInvoice header
    ) =>
        ScopeEquals(detail.InvoiceGUID, invoiceGuid)
        && ScopeEquals(detail.StoreCode, header.StoreCode)
        && ScopeEquals(detail.SupplierCode, header.SupplierCode);

    private static LocalSupplierInvoiceBatchUpdateRequestValidation FailedRequest(
        UpdateToStorePricesFields editFields,
        string message,
        string errorCode
    ) =>
        new(
            Array.Empty<string>(),
            editFields,
            new LocalSupplierInvoiceBatchUpdateFailure(message, errorCode)
        );

    private static LocalSupplierInvoiceBatchUpdateFailure ScopeChanged() =>
        new("批量更新失败", "BATCH_UPDATE_ERROR");

    private static bool ScopeEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
