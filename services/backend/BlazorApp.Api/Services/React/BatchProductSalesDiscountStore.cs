using System.Text.Json;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

/// <summary>只保存聚合，不保存成交明细。请求线程只查快照或排队，事实读取由 worker 执行。</summary>
internal sealed class BatchProductSalesDiscountStore(ISqlSugarClient db)
{
    internal bool SchemaReady => db.DbMaintenance.IsAnyTable("BatchProductSalesDiscountSnapshot", false);

    internal static BatchProductSalesDiscountSnapshot Create(string product, DateTime start, DateTime end,
        IEnumerable<string> stores, string version)
    {
        var scope = JsonSerializer.Serialize(stores.Select(s => s.Trim().ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal));
        return new()
        {
            Id = BatchProductSalesStatisticReader.Hash(JsonSerializer.Serialize(new { product = product.ToUpperInvariant(), start, end, scope, version, format = 1 })),
            SourceVersion = version, ProductCode = product, StartDate = start, EndDate = end, StoreCodesJson = scope,
            RequestedAtUtc = DateTime.UtcNow, NextAttemptAtUtc = DateTime.UtcNow,
        };
    }

    internal async Task<BatchProductSalesDiscountSnapshot?> FindOrQueueAsync(BatchProductSalesDiscountSnapshot request, CancellationToken token)
    {
        if (!SchemaReady) return null;
        var existing = await FindAsync(request.Id);
        if (existing != null) return existing;
        token.ThrowIfCancellationRequested();
        try { await db.Insertable(request).ExecuteCommandAsync(); }
        catch
        {
            // 主键唯一约束负责跨实例去重；只有确认同键已存在才能视为并发成功。
            existing = await FindAsync(request.Id);
            if (existing == null) throw;
            return existing;
        }
        return request;
    }

    internal Task<BatchProductSalesDiscountSnapshot?> FindAsync(string id) =>
        db.Queryable<BatchProductSalesDiscountSnapshot>().With(SqlWith.Null).Where(s => s.Id == id).FirstAsync()!;

    internal async Task<BatchProductSalesDiscountSnapshot?> ClaimAsync(DateTime now)
    {
        // 连续进程崩溃也计入上限，避免过期 Running 永久重试。
        await db.Updateable<BatchProductSalesDiscountSnapshot>()
            .SetColumns(s => s.Status == "Failed").SetColumns(s => s.LeaseUntilUtc == null)
            .Where(s => s.Status == "Running" && s.Attempts >= 3 && s.LeaseUntilUtc <= now)
            .ExecuteCommandAsync();
        var candidate = await db.Queryable<BatchProductSalesDiscountSnapshot>().With(SqlWith.Null)
            .Where(s => ((s.Status == "Queued" || s.Status == "Failed") && s.Attempts < 3 && s.NextAttemptAtUtc <= now)
                || (s.Status == "Running" && s.LeaseUntilUtc <= now))
            .OrderBy(s => s.RequestedAtUtc).FirstAsync();
        if (candidate == null) return null;
        var lease = Guid.NewGuid().ToString("N");
        var until = now.AddMinutes(3);
        var changed = await db.Updateable<BatchProductSalesDiscountSnapshot>()
            .SetColumns(s => s.Status == "Running").SetColumns(s => s.LeaseToken == lease)
            .SetColumns(s => s.LeaseUntilUtc == until).SetColumns(s => s.Attempts == s.Attempts + 1)
            .Where(s => s.Id == candidate.Id && s.Attempts == candidate.Attempts &&
                (((s.Status == "Queued" || s.Status == "Failed") && s.NextAttemptAtUtc <= now)
                 || (s.Status == "Running" && s.LeaseUntilUtc <= now)))
            .ExecuteCommandAsync();
        if (changed != 1) return null;
        candidate.Status = "Running"; candidate.LeaseToken = lease; candidate.LeaseUntilUtc = until; candidate.Attempts++;
        return candidate;
    }

    internal async Task<bool> FinishAsync(BatchProductSalesDiscountSnapshot job, string status,
        List<BatchProductSalesAggregateRow>? rows, DateTime now)
    {
        var payload = rows == null ? null : JsonSerializer.Serialize(rows, new JsonSerializerOptions { IgnoreReadOnlyProperties = true });
        var next = now.AddMinutes(Math.Min(30, job.Attempts * 2));
        // 一个 UPDATE 发布状态和全部日聚合；租约失效的旧 worker 无权覆盖结果。
        return await db.Updateable<BatchProductSalesDiscountSnapshot>()
            .SetColumns(s => s.Status == status).SetColumns(s => s.PayloadJson == payload)
            .SetColumns(s => s.CompletedAtUtc == now).SetColumns(s => s.NextAttemptAtUtc == next)
            .SetColumns(s => s.LeaseUntilUtc == null)
            .Where(s => s.Id == job.Id && s.Status == "Running" && s.LeaseToken == job.LeaseToken && s.LeaseUntilUtc > now)
            .ExecuteCommandAsync() == 1;
    }

    internal async Task<string> ComputeAsync(BatchProductSalesDiscountSnapshot job,
        Func<IReadOnlyList<string>, DateTime, DateTime, IReadOnlyList<string>, CancellationToken, Task<List<BatchProductSalesAggregateRow>>> readFacts,
        CancellationToken token)
    {
        var reader = new BatchProductSalesStatisticReader(db);
        var before = await reader.StatusAsync(job.StartDate, job.EndDate, token);
        if (!before.IsFresh || before.Version != job.SourceVersion)
        {
            await FinishAsync(job, "Superseded", null, DateTime.UtcNow);
            return "Superseded";
        }
        var stores = JsonSerializer.Deserialize<List<string>>(job.StoreCodesJson)!;
        var facts = await readFacts([job.ProductCode], job.StartDate, job.EndDate, stores, token);
        var quantities = await reader.ReadAsync(job.ProductCode, job.StartDate, job.EndDate, stores, token);
        var after = await reader.StatusAsync(job.StartDate, job.EndDate, token);
        var status = !after.IsFresh || after.Version != before.Version ? "Superseded"
            : BatchProductSalesStatisticReader.TotalsMatch(quantities, facts) ? "Fresh" : "OutOfSync";
        token.ThrowIfCancellationRequested();
        await FinishAsync(job, status, status == "Fresh" ? BatchProductSalesStatisticReader.PrepareSnapshot(quantities, facts) : null, DateTime.UtcNow);
        return status;
    }
}
