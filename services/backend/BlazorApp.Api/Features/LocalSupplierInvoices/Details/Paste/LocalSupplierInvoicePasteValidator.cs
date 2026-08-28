using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Features.LocalSupplierInvoices.Details.Paste;

internal sealed record LocalSupplierInvoicePasteFailure(
    string Message,
    string ErrorCode
);

internal static class LocalSupplierInvoicePasteValidator
{
    internal static LocalSupplierInvoicePasteFailure? ValidateFreshHeader(
        StoreLocalSupplierInvoice initialHeader,
        StoreLocalSupplierInvoice? freshHeader
    )
    {
        if (freshHeader == null)
            return new LocalSupplierInvoicePasteFailure("粘贴失败：订单不存在", "PASTE_ERROR");

        if (
            !ScopeEquals(initialHeader.InvoiceGUID, freshHeader.InvoiceGUID)
            || !ScopeEquals(initialHeader.StoreCode, freshHeader.StoreCode)
            || !ScopeEquals(initialHeader.SupplierCode, freshHeader.SupplierCode)
        )
        {
            return new LocalSupplierInvoicePasteFailure(
                "粘贴失败：订单归属已变化，请刷新后重试",
                "PASTE_ERROR"
            );
        }

        return null;
    }

    private static bool ScopeEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
