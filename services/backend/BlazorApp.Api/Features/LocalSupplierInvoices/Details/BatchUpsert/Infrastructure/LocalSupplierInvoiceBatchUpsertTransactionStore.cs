using BlazorApp.Api.Features.LocalSupplierInvoices.Details.BatchUpsert;
using BlazorApp.Api.Features.LocalSupplierInvoices.Details.Infrastructure;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Features.LocalSupplierInvoices.Details.BatchUpsert.Infrastructure;

internal sealed class LocalSupplierInvoiceBatchUpsertTransactionStore
{
    private readonly ISqlSugarClient _db;
    private readonly LocalSupplierInvoiceDetailsLockStore _locks;

    internal LocalSupplierInvoiceBatchUpsertTransactionStore(ISqlSugarClient db)
    {
        _db = db;
        _locks = new LocalSupplierInvoiceDetailsLockStore(db);
    }

    internal async Task<StoreLocalSupplierInvoice?> LoadInitialHeaderAsync(string invoiceGuid) =>
        await _db.Queryable<StoreLocalSupplierInvoice>()
            .FirstAsync(header => header.InvoiceGUID == invoiceGuid && header.IsDeleted == false);

    internal async Task<LocalSupplierInvoiceBatchUpsertTransactionResult> ExecuteAsync(
        StoreLocalSupplierInvoice initialHeader,
        LocalSupplierInvoiceBatchUpsertPlan plan
    )
    {
        await using var processLock =
            await LocalSupplierInvoiceDetailsMutationLock.AcquireProcessAsync(plan.InvoiceGuid);
        await _db.Ado.BeginTranAsync();
        try
        {
            var freshHeader = await _locks.LockHeaderAsync(plan.InvoiceGuid);
            var headerFailure = LocalSupplierInvoiceBatchUpsertValidator.ValidateFreshHeader(
                initialHeader,
                freshHeader
            );
            if (headerFailure != null)
                return await RollbackFailureAsync(headerFailure);

            var lockedHeader = freshHeader!;
            var lockedRecords = await _locks.LockDetailsByGuidsAsync(
                plan.RequestedUpdateDetailGuids
            );
            var existingRecords = lockedRecords
                .Where(detail => detail.IsDeleted == false)
                .ToList();

            var ownershipFailure =
                LocalSupplierInvoiceBatchUpsertValidator.ValidateDetailOwnership(
                    existingRecords,
                    plan.InvoiceGuid,
                    lockedHeader
                );
            if (ownershipFailure != null)
                return await RollbackFailureAsync(ownershipFailure);

            var writeSet = plan.BuildWriteSet(existingRecords, lockedHeader);
            var inserted = writeSet.Inserts.Count > 0
                ? await _db.Insertable(writeSet.Inserts).ExecuteCommandAsync()
                : 0;
            var updated = 0;
            foreach (
                var updateGroup in writeSet.Updates.GroupBy(
                    update => string.Join("\u001f", update.UpdateColumns),
                    StringComparer.Ordinal
                )
            )
            {
                var updateColumns = updateGroup.First().UpdateColumns;
                var details = updateGroup.Select(update => update.Detail).ToList();
                updated += await _db.Updateable(details)
                    .UpdateColumns(updateColumns)
                    .WhereColumns(detail => detail.DetailGUID)
                    .ExecuteCommandAsync();
            }
            if (updated != writeSet.Updates.Count)
                throw new InvalidOperationException("批量明细更新失败");

            await _locks.UpdateHeaderTotalAsync(plan.InvoiceGuid, lockedHeader, plan.Now);

            await _db.Ado.CommitTranAsync();
            return LocalSupplierInvoiceBatchUpsertTransactionResult.Success(inserted, updated);
        }
        catch
        {
            await _db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private async Task<LocalSupplierInvoiceBatchUpsertTransactionResult> RollbackFailureAsync(
        LocalSupplierInvoiceBatchUpsertFailure failure
    )
    {
        await _db.Ado.RollbackTranAsync();
        return LocalSupplierInvoiceBatchUpsertTransactionResult.Failed(failure);
    }
}

internal sealed record LocalSupplierInvoiceBatchUpsertTransactionResult(
    int Inserted,
    int Updated,
    LocalSupplierInvoiceBatchUpsertFailure? Failure
)
{
    internal static LocalSupplierInvoiceBatchUpsertTransactionResult Success(
        int inserted,
        int updated
    ) => new(inserted, updated, null);

    internal static LocalSupplierInvoiceBatchUpsertTransactionResult Failed(
        LocalSupplierInvoiceBatchUpsertFailure failure
    ) => new(0, 0, failure);
}
