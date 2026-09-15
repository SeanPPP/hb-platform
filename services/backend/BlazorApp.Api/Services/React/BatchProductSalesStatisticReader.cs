using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

internal sealed record BatchProductSalesStatisticStatus(
    bool IsFresh, string Status, DateTime? UpdatedAt, IReadOnlyList<DateTime> PendingDates, string Version);

internal sealed record BatchProductSalesDateCoverage(
    IReadOnlyList<DateTime> ReadyDates,
    IReadOnlyDictionary<DateTime, string> PendingReasons,
    IReadOnlyDictionary<DateTime, string> DateVersions,
    DateTime? UpdatedAt);

/// <summary>页面和折扣任务共享同一个统计版本边界，禁止从未完成统计推断零销量。</summary>
internal sealed class BatchProductSalesStatisticReader(ISqlSugarClient db)
{
    internal async Task<BatchProductSalesStatisticStatus> StatusAsync(DateTime start, DateTime end, CancellationToken token)
    {
        var previousToken = db.Ado.CancellationToken;
        db.Ado.CancellationToken = token;
        try
        {
        var endExclusive = end.Date.AddDays(1);
        var states = await db.Queryable<SalesStatisticRefreshState>().With(SqlWith.Null)
            .Where(s => s.StatisticType == SalesStatisticType.ProductStoreDaily && s.Date >= start && s.Date < endExclusive)
            .OrderBy(s => s.Date).ToListAsync(token);
        token.ThrowIfCancellationRequested();
        var byDate = states.GroupBy(s => s.Date.Date).ToDictionary(g => g.Key, g => g.ToList());
        var pending = new List<DateTime>();
        for (var date = start.Date; date <= end.Date; date = date.AddDays(1))
            if (!byDate.TryGetValue(date, out var rows) || rows.Count != 1 || !string.Equals(rows[0].Status, SalesStatisticRefreshStatus.Fresh, StringComparison.OrdinalIgnoreCase))
                pending.Add(date);
        // 单纯检查水位不会使结果失效；实际重算、上传水位及商品版本变化会产生新键。
        var version = Hash(JsonSerializer.Serialize(states.Select(s => new
        {
            s.Date, s.Status, s.LastSourceUploadTime, s.LastAggregatedAtUtc, s.CompletedAtUtc, s.SourceProductVersion,
        })));
        var updated = states.Select(s => s.CompletedAtUtc ?? s.LastAggregatedAtUtc ?? s.LastCheckedAtUtc).Max();
        return new(pending.Count == 0, pending.Count == 0 ? "Fresh" : "Pending", updated, pending, version);
        }
        catch (Exception exception) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException("统计查询已取消。", exception, token);
        }
        finally
        {
            RestoreAdoCancellationToken(previousToken);
        }
    }

    /// <summary>仅此入口允许前台按日发现可读事实；StatusAsync 的全范围严格 Fresh 契约保持不变。</summary>
    internal async Task<BatchProductSalesDateCoverage> CoverageAsync(DateTime start, DateTime end, CancellationToken token)
    {
        var previousToken = db.Ado.CancellationToken;
        db.Ado.CancellationToken = token;
        try
        {
        var endExclusive = end.Date.AddDays(1);
        var states = await db.Queryable<SalesStatisticRefreshState>().With(SqlWith.Null)
            .Where(s => s.StatisticType == SalesStatisticType.ProductStoreDaily && s.Date >= start.Date && s.Date < endExclusive)
            .OrderBy(s => s.Date).ToListAsync(token);
        token.ThrowIfCancellationRequested();
        var byDate = states.GroupBy(s => s.Date.Date).ToDictionary(g => g.Key, g => g.ToList());
        var ready = new List<DateTime>();
        var pending = new Dictionary<DateTime, string>();
        var versions = new Dictionary<DateTime, string>();
        for (var date = start.Date; date <= end.Date; date = date.AddDays(1))
        {
            var rows = byDate.GetValueOrDefault(date) ?? [];
            if (rows.Count == 1 && string.Equals(rows[0].Status, SalesStatisticRefreshStatus.Fresh, StringComparison.OrdinalIgnoreCase))
            {
                ready.Add(date);
                versions[date] = Hash(JsonSerializer.Serialize(new
                {
                    rows[0].Date, rows[0].Status, rows[0].LastSourceUploadTime, rows[0].LastAggregatedAtUtc,
                    rows[0].CompletedAtUtc, rows[0].SourceProductVersion,
                }));
            }
            else
            {
                pending[date] = rows.Count switch
                {
                    0 => "missing",
                    > 1 => "duplicate",
                    _ => string.IsNullOrWhiteSpace(rows[0].Status) ? "missing" : rows[0].Status.Trim().ToLowerInvariant(),
                };
            }
        }
        var updated = states.Select(s => s.CompletedAtUtc ?? s.LastAggregatedAtUtc ?? s.LastCheckedAtUtc).Max();
        return new(ready, pending, versions, updated);
        }
        catch (Exception exception) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException("统计查询已取消。", exception, token);
        }
        finally
        {
            RestoreAdoCancellationToken(previousToken);
        }
    }

    internal async Task<List<BatchProductSalesAggregateRow>> ReadAsync(string product, DateTime start, DateTime end,
        IReadOnlyList<string> stores, CancellationToken token)
    {
        if (stores.Count == 0) return [];
        var previousToken = db.Ado.CancellationToken;
        db.Ado.CancellationToken = token;
        try
        {
        var endExclusive = end.Date.AddDays(1);
        var rows = await db.Queryable<ProductStoreDailySalesStatistic>().With(SqlWith.Null)
            .Where(s => s.ProductCode == product && s.Date >= start && s.Date < endExclusive && stores.Contains(s.BranchCode))
            .GroupBy(s => new { s.Date, s.BranchCode, s.ProductCode })
            .Select(s => new BatchProductSalesAggregateRow
            {
                Date = s.Date, BranchCode = s.BranchCode, ProductCode = s.ProductCode,
                Quantity = SqlFunc.AggregateSum(s.TotalQuantity),
                UnknownQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                // 即使销售与退货净额相抵，日统计仍没有折扣分类证据，不能显示为 complete。
                UnknownRowCount = 1,
                SalesAmount = SqlFunc.AggregateSum(s.TotalAmount),
            }).ToListAsync(token);
        token.ThrowIfCancellationRequested();
        return rows;
        }
        catch (Exception exception) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException("统计查询已取消。", exception, token);
        }
        finally
        {
            RestoreAdoCancellationToken(previousToken);
        }
    }

    internal async Task<List<BatchProductSalesAggregateRow>> ReadAsync(string product, IReadOnlyList<DateTime> dates,
        IReadOnlyList<string> stores, CancellationToken token)
    {
        if (stores.Count == 0 || dates.Count == 0) return [];
        var previousToken = db.Ado.CancellationToken;
        db.Ado.CancellationToken = token;
        try
        {
        var days = dates.Select(date => date.Date).Distinct().ToList();
        var datePredicate = BuildDatePredicate(days);
        var rows = await db.Queryable<ProductStoreDailySalesStatistic>().With(SqlWith.Null)
            .Where(s => s.ProductCode == product && stores.Contains(s.BranchCode))
            .Where(datePredicate.ToExpression())
            .GroupBy(s => new { s.Date, s.BranchCode, s.ProductCode })
            .Select(s => new BatchProductSalesAggregateRow
            {
                Date = s.Date, BranchCode = s.BranchCode, ProductCode = s.ProductCode,
                Quantity = SqlFunc.AggregateSum(s.TotalQuantity), UnknownQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                UnknownRowCount = 1, SalesAmount = SqlFunc.AggregateSum(s.TotalAmount),
            }).ToListAsync(token);
        token.ThrowIfCancellationRequested();
        return rows;
        }
        catch (Exception exception) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException("统计查询已取消。", exception, token);
        }
        finally
        {
            RestoreAdoCancellationToken(previousToken);
        }
    }

    internal async Task<List<BatchProductSalesAggregateRow>> ReadAsync(IReadOnlyList<string> products, IReadOnlyList<DateTime> dates,
        IReadOnlyList<string> stores, CancellationToken token)
    {
        if (products.Count == 0 || stores.Count == 0 || dates.Count == 0) return [];
        var previousToken = db.Ado.CancellationToken;
        db.Ado.CancellationToken = token;
        try
        {
        var days = dates.Select(date => date.Date).Distinct().ToList();
        var codes = products.Where(code => !string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var datePredicate = BuildDatePredicate(days);
        var rows = await db.Queryable<ProductStoreDailySalesStatistic>().With(SqlWith.Null)
            .Where(s => codes.Contains(s.ProductCode) && stores.Contains(s.BranchCode))
            .Where(datePredicate.ToExpression())
            .GroupBy(s => new { s.Date, s.BranchCode, s.ProductCode })
            .Select(s => new BatchProductSalesAggregateRow
            {
                Date = s.Date, BranchCode = s.BranchCode, ProductCode = s.ProductCode,
                Quantity = SqlFunc.AggregateSum(s.TotalQuantity), UnknownQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                UnknownRowCount = 1, SalesAmount = SqlFunc.AggregateSum(s.TotalAmount),
            }).ToListAsync(token);
        token.ThrowIfCancellationRequested();
        return rows;
        }
        catch (Exception exception) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException("统计查询已取消。", exception, token);
        }
        finally
        {
            RestoreAdoCancellationToken(previousToken);
        }
    }

    /// <summary>SqlSugar 的 token 查询会写入 client ADO；请求结束前必须还原嵌套调用的原令牌。</summary>
    private void RestoreAdoCancellationToken(CancellationToken? token)
    {
        if (token.HasValue) db.Ado.CancellationToken = token.Value;
        else db.Ado.RemoveCancellationToken();
    }

    private static Expressionable<ProductStoreDailySalesStatistic> BuildDatePredicate(IEnumerable<DateTime> days)
    {
        var predicate = Expressionable.Create<ProductStoreDailySalesStatistic>();
        foreach (var date in days)
        {
            var start = date.Date; var end = start.AddDays(1);
            predicate = predicate.Or(row => row.Date >= start && row.Date < end);
        }
        return predicate;
    }

    internal static bool TotalsMatch(IEnumerable<BatchProductSalesAggregateRow> statistics, IEnumerable<BatchProductSalesAggregateRow> facts)
    {
        static Dictionary<(DateTime, string), (decimal Quantity, decimal Amount)> Totals(IEnumerable<BatchProductSalesAggregateRow> rows) =>
            rows.GroupBy(r => (r.Date.Date, r.BranchCode.ToUpperInvariant())).ToDictionary(g => g.Key,
                g => (g.Sum(r => r.Quantity), g.Sum(r => r.SalesAmount)));
        var expected = Totals(statistics);
        var actual = Totals(facts);
        // 包括净量为零但仍有退货或金额的日期；不能只对商品总和做校验。
        return expected.Keys.Union(actual.Keys).All(key =>
        {
            var left = expected.GetValueOrDefault(key);
            var right = actual.GetValueOrDefault(key);
            // 生产日统计 TotalAmount 为 decimal(18,4)，成交分摊保留 6 位；按存储精度核对，数量仍严格相等。
            return left.Quantity == right.Quantity && left.Amount == Math.Round(right.Amount, 4, MidpointRounding.AwayFromZero);
        });
    }

    internal static List<BatchProductSalesAggregateRow> PrepareSnapshot(
        IEnumerable<BatchProductSalesAggregateRow> statistics, IEnumerable<BatchProductSalesAggregateRow> facts)
    {
        var expected = statistics.ToDictionary(r => (r.Date.Date, r.BranchCode.ToUpperInvariant()));
        var actual = facts.GroupBy(r => (r.Date.Date, r.BranchCode.ToUpperInvariant())).ToDictionary(g => g.Key, g => g.ToList());
        return expected.Keys.Union(actual.Keys).Select(key =>
        {
            var source = expected.GetValueOrDefault(key);
            var rows = actual.GetValueOrDefault(key) ?? [];
            var metrics = BatchProductSalesAnalysisService.BuildAggregateMetrics(rows);
            return new BatchProductSalesAggregateRow
            {
                Date = key.Item1, BranchCode = key.Item2, ProductCode = source?.ProductCode ?? rows[0].ProductCode,
                Quantity = source?.Quantity ?? 0m, SalesAmount = source?.SalesAmount ?? 0m,
                // 发布金额沿用已校验日统计；折扣与退货证据保留真实成交聚合，不改变价格来凑金额。
                RegularQuantity = metrics.RegularQuantity, DiscountQuantity = metrics.DiscountQuantity,
                UnknownQuantity = metrics.UnknownQuantity, ReturnQuantity = metrics.ReturnQuantity,
                UnknownRowCount = rows.Sum(r => r.UnknownRowCount),
                OriginalPriceMin = metrics.OriginalPriceMin, OriginalPriceMax = metrics.OriginalPriceMax,
                DiscountPriceMin = metrics.DiscountPriceMin, DiscountPriceMax = metrics.DiscountPriceMax,
            };
        }).OrderBy(r => r.Date).ThenBy(r => r.BranchCode, StringComparer.Ordinal).ToList();
    }

    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
