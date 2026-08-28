using BlazorApp.Api.Features.LocalSupplierInvoices.Details.BatchUpdate;
using BlazorApp.Api.Features.LocalSupplierInvoices.Details.Infrastructure;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Features.LocalSupplierInvoices.Details.BatchUpdate.Infrastructure;

internal sealed record LocalSupplierInvoiceBatchUpdateInitialState(
    StoreLocalSupplierInvoice? Header,
    List<StoreLocalSupplierInvoiceDetails> Details
);

internal sealed record LocalSupplierInvoiceBatchUpdateTransactionResult(
    int Updated,
    int Failed,
    LocalSupplierInvoiceBatchUpdateFailure? Failure
)
{
    internal static LocalSupplierInvoiceBatchUpdateTransactionResult Success(
        int updated,
        int failed
    ) => new(updated, failed, null);

    internal static LocalSupplierInvoiceBatchUpdateTransactionResult Rejected(
        LocalSupplierInvoiceBatchUpdateFailure failure
    ) => new(0, 0, failure);
}

internal sealed class LocalSupplierInvoiceBatchUpdateTransactionStore
{
    private readonly ISqlSugarClient _db;
    private readonly LocalSupplierInvoiceDetailsLockStore _locks;

    internal LocalSupplierInvoiceBatchUpdateTransactionStore(ISqlSugarClient db)
    {
        _db = db;
        _locks = new LocalSupplierInvoiceDetailsLockStore(db);
    }

    internal async Task<LocalSupplierInvoiceBatchUpdateInitialState> LoadInitialStateAsync(
        string invoiceGuid,
        IReadOnlyList<string> detailGuids
    )
    {
        var details = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .Where(detail =>
                detail.InvoiceGUID == invoiceGuid
                && detailGuids.Contains(detail.DetailGUID)
                && detail.IsDeleted == false
            )
            .ToListAsync();
        var header = await _db.Queryable<StoreLocalSupplierInvoice>()
            .FirstAsync(item => item.InvoiceGUID == invoiceGuid && item.IsDeleted == false);
        return new LocalSupplierInvoiceBatchUpdateInitialState(header, details);
    }

    internal async Task<LocalSupplierInvoiceBatchUpdateTransactionResult> ExecuteAsync(
        LocalSupplierInvoiceBatchUpdateInitialState initialState,
        LocalSupplierInvoiceBatchUpdatePlan plan,
        Func<
            StoreLocalSupplierInvoiceDetails,
            string?,
            string?,
            Task
        > applyAutoPricingPreviewAsync
    )
    {
        await using var processLock =
            await LocalSupplierInvoiceDetailsMutationLock.AcquireProcessAsync(plan.InvoiceGuid);
        await _db.Ado.BeginTranAsync();
        try
        {
            var freshHeader = await _locks.LockHeaderAsync(plan.InvoiceGuid);
            var initialDetailGuids = initialState.Details
                .Select(detail => detail.DetailGUID)
                .Distinct()
                .ToArray();
            var freshDetails = await _locks.LockDetailsByGuidsAsync(initialDetailGuids);
            var scopeFailure = LocalSupplierInvoiceBatchUpdateValidator.ValidateFreshScope(
                plan.InvoiceGuid,
                initialState.Header,
                freshHeader,
                initialState.Details,
                freshDetails
            );
            if (scopeFailure != null)
                return await RollbackFailureAsync(scopeFailure);

            var lockedHeader = freshHeader!;
            await plan.ApplyAllowedFieldsAsync(
                lockedHeader,
                freshDetails,
                applyAutoPricingPreviewAsync
            );
            var updated = await _db.Updateable(freshDetails)
                .UpdateColumns(plan.PersistenceColumns)
                .WhereColumns(detail => detail.DetailGUID)
                .ExecuteCommandAsync();
            if (updated != freshDetails.Count)
                throw new InvalidOperationException("批量明细更新失败");

            await _locks.UpdateHeaderTotalAsync(plan.InvoiceGuid, lockedHeader, plan.Now);

            await _db.Ado.CommitTranAsync();
            return LocalSupplierInvoiceBatchUpdateTransactionResult.Success(
                updated,
                plan.RequestedDetailCount - updated
            );
        }
        catch
        {
            await _db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private async Task<LocalSupplierInvoiceBatchUpdateTransactionResult> RollbackFailureAsync(
        LocalSupplierInvoiceBatchUpdateFailure failure
    )
    {
        await _db.Ado.RollbackTranAsync();
        return LocalSupplierInvoiceBatchUpdateTransactionResult.Rejected(failure);
    }
}
