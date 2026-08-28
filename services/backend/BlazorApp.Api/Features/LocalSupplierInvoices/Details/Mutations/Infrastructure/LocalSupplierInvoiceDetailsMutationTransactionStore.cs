using BlazorApp.Api.Features.LocalSupplierInvoices.Details.Infrastructure;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Features.LocalSupplierInvoices.Details.Mutations.Infrastructure;

internal sealed record LocalSupplierInvoiceDetailsMutationFailure(
    string Message,
    string ErrorCode
);

internal sealed record LocalSupplierInvoiceDetailsMutationResult(
    int Affected,
    int Failed,
    LocalSupplierInvoiceDetailsMutationFailure? Failure
)
{
    internal static LocalSupplierInvoiceDetailsMutationResult Success(
        int affected,
        int failed = 0
    ) => new(affected, failed, null);

    internal static LocalSupplierInvoiceDetailsMutationResult Rejected(
        string message,
        string errorCode
    ) => new(0, 0, new LocalSupplierInvoiceDetailsMutationFailure(message, errorCode));
}

internal sealed class LocalSupplierInvoiceDetailsMutationTransactionStore
{
    private readonly ISqlSugarClient _db;
    private readonly LocalSupplierInvoiceDetailsLockStore _locks;

    internal LocalSupplierInvoiceDetailsMutationTransactionStore(ISqlSugarClient db)
    {
        _db = db;
        _locks = new LocalSupplierInvoiceDetailsLockStore(db);
    }

    internal async Task<LocalSupplierInvoiceDetailsMutationResult> ExecuteUpdateActionAsync(
        string invoiceGuid,
        string detailGuid,
        int action,
        DateTime now
    )
    {
        await using var processLock =
            await LocalSupplierInvoiceDetailsMutationLock.AcquireProcessAsync(invoiceGuid);
        await _db.Ado.BeginTranAsync();
        try
        {
            var lockedHeader = await _locks.LockHeaderAsync(invoiceGuid);
            var lockedDetails = await _locks.LockDetailsByGuidsAsync(new[] { detailGuid });
            var detail = lockedDetails.SingleOrDefault(item =>
                ScopeEquals(item.DetailGUID, detailGuid)
                && BelongsToHeader(item, invoiceGuid, lockedHeader)
                && item.IsDeleted == false
            );
            if (detail == null)
                return await RollbackRejectedAsync("明细不存在", "NOT_FOUND");
            if (detail.ActivityType == 99)
            {
                return await RollbackRejectedAsync(
                    "已执行完成的明细不能修改操作类型",
                    "VALIDATION_ERROR"
                );
            }

            var updated = await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
                .SetColumns(item => item.ActivityType == action)
                .SetColumns(item => item.UpdatedAt == now)
                .Where(item =>
                    item.DetailGUID == detailGuid
                    && item.InvoiceGUID == invoiceGuid
                    && item.StoreCode == lockedHeader!.StoreCode
                    && item.SupplierCode == lockedHeader.SupplierCode
                    && item.IsDeleted == false
                )
                .ExecuteCommandAsync();
            if (updated != 1)
                throw new InvalidOperationException("明细操作类型更新失败");

            await _db.Ado.CommitTranAsync();
            return LocalSupplierInvoiceDetailsMutationResult.Success(updated);
        }
        catch
        {
            await _db.Ado.RollbackTranAsync();
            throw;
        }
    }

    internal async Task<LocalSupplierInvoiceDetailsMutationResult> ExecuteBatchUpdateActionAsync(
        string invoiceGuid,
        IReadOnlyCollection<string> detailGuids,
        int action,
        DateTime now
    )
    {
        await using var processLock =
            await LocalSupplierInvoiceDetailsMutationLock.AcquireProcessAsync(invoiceGuid);
        await _db.Ado.BeginTranAsync();
        try
        {
            var lockedHeader = await _locks.LockHeaderAsync(invoiceGuid);
            var lockedDetails = await _locks.LockDetailsByGuidsAsync(detailGuids);
            var details = lockedDetails
                .Where(item =>
                    ScopeEquals(item.InvoiceGUID, invoiceGuid) && item.IsDeleted == false
                )
                .ToList();
            if (lockedHeader == null || details.Count == 0)
                return await RollbackRejectedAsync("没有找到要更新的明细", "NOT_FOUND");
            if (details.Any(item => !BelongsToHeader(item, invoiceGuid, lockedHeader)))
                return await RollbackRejectedAsync("批量更新失败", "BATCH_UPDATE_ERROR");
            if (details.Any(item => item.ActivityType == 99))
            {
                return await RollbackRejectedAsync(
                    "已执行完成的明细不能修改操作类型",
                    "VALIDATION_ERROR"
                );
            }

            var guids = details.Select(item => item.DetailGUID).ToArray();
            var updated = await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
                .SetColumns(item => item.ActivityType == action)
                .SetColumns(item => item.UpdatedAt == now)
                .Where(item =>
                    guids.Contains(item.DetailGUID)
                    && item.InvoiceGUID == invoiceGuid
                    && item.StoreCode == lockedHeader.StoreCode
                    && item.SupplierCode == lockedHeader.SupplierCode
                    && item.IsDeleted == false
                )
                .ExecuteCommandAsync();
            if (updated != details.Count)
                throw new InvalidOperationException("批量明细操作类型更新失败");

            await _db.Ado.CommitTranAsync();
            return LocalSupplierInvoiceDetailsMutationResult.Success(
                updated,
                details.Count - updated
            );
        }
        catch
        {
            await _db.Ado.RollbackTranAsync();
            throw;
        }
    }

    internal async Task<LocalSupplierInvoiceDetailsMutationResult> ExecuteDeleteAsync(
        string invoiceGuid,
        IReadOnlyCollection<string> detailGuids,
        string updatedBy,
        DateTime now
    )
    {
        await using var processLock =
            await LocalSupplierInvoiceDetailsMutationLock.AcquireProcessAsync(invoiceGuid);
        await _db.Ado.BeginTranAsync();
        try
        {
            var lockedHeader = await _locks.LockHeaderAsync(invoiceGuid);
            var lockedDetails = await _locks.LockDetailsByGuidsAsync(detailGuids);
            if (lockedHeader == null)
                return await RollbackRejectedAsync("删除失败", "DELETE_ERROR");

            var details = lockedDetails
                .Where(item =>
                    ScopeEquals(item.InvoiceGUID, invoiceGuid) && item.IsDeleted == false
                )
                .ToList();
            if (details.Any(item => !BelongsToHeader(item, invoiceGuid, lockedHeader)))
                return await RollbackRejectedAsync("删除失败", "DELETE_ERROR");

            var guids = details.Select(item => item.DetailGUID).ToArray();
            var affected = guids.Length == 0
                ? 0
                : await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
                    .SetColumns(item => item.IsDeleted == true)
                    .SetColumns(item => item.UpdatedAt == now)
                    .SetColumns(item => item.UpdatedBy == updatedBy)
                    .Where(item =>
                        guids.Contains(item.DetailGUID)
                        && item.InvoiceGUID == invoiceGuid
                        && item.StoreCode == lockedHeader.StoreCode
                        && item.SupplierCode == lockedHeader.SupplierCode
                        && item.IsDeleted == false
                    )
                    .ExecuteCommandAsync();
            if (affected != details.Count)
                throw new InvalidOperationException("明细删除失败");

            await _locks.UpdateHeaderTotalAsync(invoiceGuid, lockedHeader, now);
            await _db.Ado.CommitTranAsync();
            return LocalSupplierInvoiceDetailsMutationResult.Success(affected);
        }
        catch
        {
            await _db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private async Task<LocalSupplierInvoiceDetailsMutationResult> RollbackRejectedAsync(
        string message,
        string errorCode
    )
    {
        await _db.Ado.RollbackTranAsync();
        return LocalSupplierInvoiceDetailsMutationResult.Rejected(message, errorCode);
    }

    private static bool BelongsToHeader(
        StoreLocalSupplierInvoiceDetails detail,
        string invoiceGuid,
        StoreLocalSupplierInvoice? header
    ) =>
        header != null
        && ScopeEquals(detail.InvoiceGUID, invoiceGuid)
        && ScopeEquals(detail.StoreCode, header.StoreCode)
        && ScopeEquals(detail.SupplierCode, header.SupplierCode);

    private static bool ScopeEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
