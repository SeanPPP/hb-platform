using BlazorApp.Api.Features.LocalSupplierInvoices.Details.Infrastructure;
using BlazorApp.Api.Features.LocalSupplierInvoices.Details.Paste;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Features.LocalSupplierInvoices.Details.Paste.Infrastructure;

internal sealed record LocalSupplierInvoicePasteTransactionResult(
    int Inserted,
    LocalSupplierInvoicePasteFailure? Failure
)
{
    internal static LocalSupplierInvoicePasteTransactionResult Success(int inserted) =>
        new(inserted, null);

    internal static LocalSupplierInvoicePasteTransactionResult Rejected(
        LocalSupplierInvoicePasteFailure failure
    ) => new(0, failure);
}

internal sealed class LocalSupplierInvoicePasteTransactionStore
{
    private readonly ISqlSugarClient _db;
    private readonly LocalSupplierInvoiceDetailsLockStore _locks;

    internal LocalSupplierInvoicePasteTransactionStore(ISqlSugarClient db)
    {
        _db = db;
        _locks = new LocalSupplierInvoiceDetailsLockStore(db);
    }

    internal async Task<StoreLocalSupplierInvoice?> LoadInitialHeaderAsync(string invoiceGuid) =>
        await _db.Queryable<StoreLocalSupplierInvoice>()
            .FirstAsync(header =>
                header.InvoiceGUID == invoiceGuid && header.IsDeleted == false
            );

    internal async Task<LocalSupplierInvoicePasteTransactionResult> ExecuteAsync(
        StoreLocalSupplierInvoice initialHeader,
        LocalSupplierInvoicePastePlan plan
    )
    {
        await using var processLock =
            await LocalSupplierInvoiceDetailsMutationLock.AcquireProcessAsync(plan.InvoiceGuid);
        await _db.Ado.BeginTranAsync();
        try
        {
            var freshHeader = await _locks.LockHeaderAsync(plan.InvoiceGuid);
            var headerFailure = LocalSupplierInvoicePasteValidator.ValidateFreshHeader(
                initialHeader,
                freshHeader
            );
            if (headerFailure != null)
                return await RollbackFailureAsync(headerFailure);

            var lockedHeader = freshHeader!;
            _ = await _locks.LockAllDetailsAsync(plan.InvoiceGuid);
            var detailRows = plan.BuildRows(lockedHeader);
            if (plan.Mode == "replace")
            {
                await _db.Deleteable<StoreLocalSupplierInvoiceDetails>()
                    .Where(detail =>
                        detail.InvoiceGUID == plan.InvoiceGuid && detail.IsDeleted == false
                    )
                    .ExecuteCommandAsync();
            }

            var inserted = detailRows.Count > 0
                ? await _db.Insertable(detailRows).ExecuteCommandAsync()
                : 0;
            await _locks.UpdateHeaderTotalAsync(plan.InvoiceGuid, lockedHeader, plan.Now);

            await _db.Ado.CommitTranAsync();
            return LocalSupplierInvoicePasteTransactionResult.Success(inserted);
        }
        catch
        {
            await _db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private async Task<LocalSupplierInvoicePasteTransactionResult> RollbackFailureAsync(
        LocalSupplierInvoicePasteFailure failure
    )
    {
        await _db.Ado.RollbackTranAsync();
        return LocalSupplierInvoicePasteTransactionResult.Rejected(failure);
    }
}
