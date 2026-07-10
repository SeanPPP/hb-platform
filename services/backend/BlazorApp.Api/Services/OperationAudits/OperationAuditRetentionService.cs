using BlazorApp.Shared.Models.POSM;
using SqlSugar;

namespace BlazorApp.Api.Services.OperationAudits;

public sealed class OperationAuditRetentionService
{
    private const int RetentionDays = 730;
    private const int DeleteBatchSize = 1000;
    private readonly ISqlSugarClient _db;

    public OperationAuditRetentionService(ISqlSugarClient db)
    {
        _db = db;
    }

    public async Task<int> CleanupExpiredAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc).AddDays(-RetentionDays);
        var totalDeleted = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var eventIds = await _db.Queryable<PosOperationAudit>()
                .Where(item => item.ReceivedAtUtc < cutoff)
                .OrderBy(item => item.ReceivedAtUtc)
                .Select(item => item.EventId)
                .Take(DeleteBatchSize)
                .ToListAsync();
            if (eventIds.Count == 0)
            {
                break;
            }

            // 父子表必须在同一事务删除，避免留下无法归属的商品明细。
            _db.Ado.BeginTran();
            try
            {
                await _db.Deleteable<PosOperationAuditItem>()
                    .Where(item => eventIds.Contains(item.EventId))
                    .ExecuteCommandAsync();
                var deleted = await _db.Deleteable<PosOperationAudit>()
                    .Where(item => eventIds.Contains(item.EventId))
                    .ExecuteCommandAsync();
                _db.Ado.CommitTran();
                totalDeleted += deleted;
                if (deleted == 0)
                {
                    break;
                }
            }
            catch
            {
                _db.Ado.RollbackTran();
                throw;
            }
        }

        return totalDeleted;
    }
}
