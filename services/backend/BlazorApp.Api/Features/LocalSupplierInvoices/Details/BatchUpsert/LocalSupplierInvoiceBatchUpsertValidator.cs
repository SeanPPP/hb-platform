using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Features.LocalSupplierInvoices.Details.BatchUpsert;

internal sealed record LocalSupplierInvoiceBatchUpsertFailure(
    string Message,
    string ErrorCode
);

internal static class LocalSupplierInvoiceBatchUpsertValidator
{
    internal static LocalSupplierInvoiceBatchUpsertFailure? ValidateFreshHeader(
        StoreLocalSupplierInvoice initialHeader,
        StoreLocalSupplierInvoice? freshHeader
    )
    {
        if (freshHeader == null)
            return new LocalSupplierInvoiceBatchUpsertFailure("单据不存在", "NOT_FOUND");

        if (
            !ScopeEquals(initialHeader.StoreCode, freshHeader.StoreCode)
            || !ScopeEquals(initialHeader.SupplierCode, freshHeader.SupplierCode)
        )
        {
            return new LocalSupplierInvoiceBatchUpsertFailure(
                "单据归属已变化，请刷新后重试",
                "VALIDATION_ERROR"
            );
        }

        return null;
    }

    internal static LocalSupplierInvoiceBatchUpsertFailure? ValidateDetailOwnership(
        IReadOnlyCollection<StoreLocalSupplierInvoiceDetails> existingDetails,
        string invoiceGuid,
        StoreLocalSupplierInvoice freshHeader
    )
    {
        var hasForeignDetail = existingDetails.Any(detail =>
            !ScopeEquals(detail.InvoiceGUID, invoiceGuid)
            || !ScopeEquals(detail.StoreCode, freshHeader.StoreCode)
            || !ScopeEquals(detail.SupplierCode, freshHeader.SupplierCode)
        );
        return hasForeignDetail
            ? new LocalSupplierInvoiceBatchUpsertFailure(
                "部分明细不存在或不属于当前进货单",
                "VALIDATION_ERROR"
            )
            : null;
    }

    private static bool ScopeEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
