using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Features.LocalSupplierInvoices.Details.Infrastructure;

/// <summary>集中维护明细写入的 SQL Server 行锁及兼容 provider 的事务内重读。</summary>
internal sealed class LocalSupplierInvoiceDetailsLockStore
{
    internal const string HeaderLockSql = """
        SELECT TOP (1) *
        FROM [dbo].[StoreLocalSupplierInvoice] WITH (UPDLOCK, HOLDLOCK)
        WHERE [InvoiceGUID] = @InvoiceGuid AND [IsDeleted] = 0;
        """;

    internal const string AllDetailsLockSql = """
        SELECT *
        FROM [dbo].[StoreLocalSupplierInvoiceDetails] WITH (UPDLOCK, HOLDLOCK)
        WHERE [InvoiceGUID] = @InvoiceGuid;
        """;

    private readonly ISqlSugarClient _db;

    internal LocalSupplierInvoiceDetailsLockStore(ISqlSugarClient db)
    {
        _db = db;
    }

    internal async Task<StoreLocalSupplierInvoice?> LockHeaderAsync(string invoiceGuid)
    {
        EnsureTransaction();
        if (_db.CurrentConnectionConfig.DbType == DbType.SqlServer)
        {
            var rows = await _db.Ado.SqlQueryAsync<StoreLocalSupplierInvoice>(
                HeaderLockSql,
                new SugarParameter("@InvoiceGuid", invoiceGuid)
            );
            return rows.SingleOrDefault();
        }

        // SQLite 等兼容 provider 不发送 SQL Server hint，但读取必须发生在已开启的事务内。
        var header = await _db.Queryable<StoreLocalSupplierInvoice>()
            .FirstAsync(item => item.InvoiceGUID == invoiceGuid && item.IsDeleted == false);
        return header;
    }

    internal async Task<List<StoreLocalSupplierInvoiceDetails>> LockDetailsByGuidsAsync(
        IReadOnlyCollection<string> detailGuids
    )
    {
        EnsureTransaction();
        var guids = detailGuids
            .Where(detailGuid => !string.IsNullOrWhiteSpace(detailGuid))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (guids.Length == 0)
            return new List<StoreLocalSupplierInvoiceDetails>();

        if (_db.CurrentConnectionConfig.DbType == DbType.SqlServer)
        {
            var parameterNames = guids
                .Select((_, index) => $"@DetailGuid{index}")
                .ToArray();
            var parameters = guids
                .Select((detailGuid, index) =>
                    new SugarParameter(parameterNames[index], detailGuid)
                )
                .ToArray();
            var sql = $"""
                SELECT *
                FROM [dbo].[StoreLocalSupplierInvoiceDetails] WITH (UPDLOCK, HOLDLOCK)
                WHERE [DetailGUID] IN ({string.Join(", ", parameterNames)});
                """;
            return await _db.Ado.SqlQueryAsync<StoreLocalSupplierInvoiceDetails>(
                sql,
                parameters
            );
        }

        return await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .Where(detail => guids.Contains(detail.DetailGUID))
            .ToListAsync();
    }

    internal async Task<List<StoreLocalSupplierInvoiceDetails>> LockAllDetailsAsync(
        string invoiceGuid
    )
    {
        EnsureTransaction();
        if (_db.CurrentConnectionConfig.DbType == DbType.SqlServer)
        {
            return await _db.Ado.SqlQueryAsync<StoreLocalSupplierInvoiceDetails>(
                AllDetailsLockSql,
                new SugarParameter("@InvoiceGuid", invoiceGuid)
            );
        }

        return await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .Where(detail => detail.InvoiceGUID == invoiceGuid)
            .ToListAsync();
    }

    internal async Task UpdateHeaderTotalAsync(
        string invoiceGuid,
        StoreLocalSupplierInvoice lockedHeader,
        DateTime now
    )
    {
        EnsureTransaction();
        var total = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .Where(detail => detail.InvoiceGUID == invoiceGuid && detail.IsDeleted == false)
            .SumAsync(detail => detail.Amount ?? 0);
        var headerUpdated = await _db.Updateable<StoreLocalSupplierInvoice>()
            .SetColumns(header => header.TotalAmount == total)
            .SetColumns(header => header.UpdatedAt == now)
            .Where(header =>
                header.InvoiceGUID == invoiceGuid
                && header.StoreCode == lockedHeader.StoreCode
                && header.SupplierCode == lockedHeader.SupplierCode
                && header.IsDeleted == false
            )
            .ExecuteCommandAsync();
        if (headerUpdated != 1)
            throw new InvalidOperationException("进货单金额更新失败");
    }

    private void EnsureTransaction()
    {
        if (_db.Ado.Transaction == null)
            throw new InvalidOperationException("进货单明细锁必须在事务内获取");
    }
}
