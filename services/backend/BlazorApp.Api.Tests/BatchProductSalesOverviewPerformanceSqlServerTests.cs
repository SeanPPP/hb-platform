using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed partial class BatchProductSalesAnalysisSqlServerIntegrationTests
{
    // 显式开启的本机基准；使用已有 fixture 的随机隔离数据库，绝不写生产数据。
    [BatchSalesOverviewBenchmarkFact]
    public async Task Overview_Performance_SQLServer_10_89_500商品_58店_22天()
    {
        await PrepareAnalysisSchemaAsync();
        _catalog!.CodeFirst.InitTables<BatchProductSalesDiscountRefreshState, BatchProductSalesDiscountSnapshot>();
        var start = new DateTime(2026, 8, 17);
        var dates = Enumerable.Range(0, 22).Select(i => start.AddDays(i)).ToArray();
        var products = Enumerable.Range(1, 500).Select(i => $"P{i:D3}").ToArray();
        var stores = Enumerable.Range(1, 58).Select(i => $"S{i:D2}").ToList();
        await SeedProductsAsync(products.Select(code => (code, code, (string?)null)).ToArray());
        await _catalog.Insertable(stores.Select(code => new Store { StoreCode = code, StoreName = code, IsDeleted = false }).ToList()).ExecuteCommandAsync();
        await _catalog.Insertable(dates.Select(day => State(day, "Fresh", day.ToString("yyyyMMdd"))).ToList()).ExecuteCommandAsync();
        await _catalog.Insertable(dates.Select(day => new BatchProductSalesDiscountRefreshState
        {
            Date = day, Status = "Fresh", RuleVersion = 1, StatisticsVersion = "fixture-statistics",
            SourceVersion = "fixture-source", RequestedAtUtc = day, NextAttemptAtUtc = day,
            CompletedAtUtc = day.AddHours(1), SnapshotCount = products.Length,
        }).ToList()).ExecuteCommandAsync();
        foreach (var batch in products.Chunk(20))
        {
            var facts = new List<ProductStoreDailySalesStatistic>();
            var snapshots = new List<BatchProductSalesDiscountSnapshot>();
            foreach (var code in batch)
            foreach (var day in dates)
            {
                var rows = stores.Select(store => new BatchProductSalesAggregateRow
                {
                    ProductCode = code, BranchCode = store, Date = day,
                    Quantity = 2, RegularQuantity = 1, DiscountQuantity = 1, SalesAmount = 9,
                    OriginalPriceMin = 5, OriginalPriceMax = 5, DiscountPriceMin = 4, DiscountPriceMax = 4,
                }).ToList();
                facts.AddRange(stores.Select(store => new ProductStoreDailySalesStatistic
                {
                    ProductCode = code, BranchCode = store, SupplierCode = "FIXTURE", Date = day,
                    TotalQuantity = 2, TotalAmount = 9, UpdateTime = day,
                }));
                snapshots.Add(new BatchProductSalesDiscountSnapshot
                {
                    Id = $"{code}-{day:yyyyMMdd}", ProductCode = code, StartDate = day, EndDate = day,
                    SnapshotFormat = 2, SourceVersion = "fixture-source", Status = "Fresh",
                    RequestedAtUtc = day, NextAttemptAtUtc = day, CompletedAtUtc = day.AddHours(1),
                    PayloadJson = JsonSerializer.Serialize(rows), StoreCodesJson = JsonSerializer.Serialize(stores),
                });
            }
            await _catalog.Fastest<ProductStoreDailySalesStatistic>().BulkCopyAsync(facts);
            await _catalog.Fastest<BatchProductSalesDiscountSnapshot>().BulkCopyAsync(snapshots);
        }

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var output = Environment.GetEnvironmentVariable("BATCH_SALES_BENCHMARK_OUTPUT_DIR");
        if (string.IsNullOrWhiteSpace(output)) output = Path.Combine(Path.GetTempPath(), $"hb-batch-overview-benchmark-{_id}");
        Directory.CreateDirectory(output);
        var measurements = new List<object>();
        var service = CreateAnalysisService();
        foreach (var count in new[] { 10, 89, 500 })
        {
            var chosen = products.Take(count).ToList();
            var request = new BatchProductSalesQueryRequestDto { ItemNumbers = chosen, StartDate = start, EndDate = dates[^1], StoreCodes = stores };
            var summary = (await service.QueryAsync(request, stores)).Data!;
            Assert.Equal(count * 58 * 22 * 2m, summary.Overview.Metrics!.Quantity);
            Assert.Equal(count * 58 * 22 * 9m, summary.Overview.Metrics.SalesAmount);
            Assert.Equal(58, summary.Overview.Branches.Count);
            var locked = new BatchProductSalesBranchOverviewRequestDto
            {
                ProductCodes = chosen, StoreCodes = stores, StartDate = start, EndDate = dates[^1],
                ReadyDates = summary.Coverage.ReadyDates, CoverageVersion = summary.Coverage.Version,
            };
            var queryMeasure = await MeasureOverviewAsync("query", count, 20, () => service.QueryAsync(request, stores), options);
            Assert.Equal(2, queryMeasure.FactSqlCount);
            Assert.Equal(0, queryMeasure.SnapshotSqlCount);
            measurements.Add(queryMeasure);
            locked.BranchCode = stores[0];
            measurements.Add(await MeasureOverviewAsync("branch", count, 20, () => service.GetBranchOverviewAsync(locked, stores), options));
            var branch = await service.GetBranchOverviewAsync(locked, stores);
            Assert.Equal(count * 22 * 2m, branch.Data!.Branch.Metrics.Quantity);
            locked.BranchCode = null;
            var discounts = await service.GetDiscountOverviewAsync(locked, stores);
            Assert.Equal("Fresh", discounts.Data!.DiscountStatisticStatus);
            Assert.Equal(summary.Overview.Metrics.Quantity, discounts.Data.Overview.Metrics!.Quantity);
            Assert.Equal(count * 58 * 22m, discounts.Data.Overview.Metrics.RegularQuantity);
            var discountMeasure = await MeasureOverviewAsync("discounts", count, 3, () => service.GetDiscountOverviewAsync(locked, stores), options);
            Assert.Equal(1, discountMeasure.SnapshotSqlCount);
            Assert.Equal(1, discountMeasure.DiscountStateSqlCount);
            measurements.Add(discountMeasure);

            var payloads = await _catalog.Queryable<BatchProductSalesDiscountSnapshot>().Where(row => chosen.Contains(row.ProductCode)).Select(row => row.PayloadJson!).ToListAsync();
            var parsing = Stopwatch.StartNew();
            var parsedRows = 0;
            foreach (var payload in payloads) parsedRows += JsonSerializer.Deserialize<List<BatchProductSalesAggregateRow>>(payload)!.Count;
            parsing.Stop();
            Assert.Equal(count * 58 * 22, parsedRows);
            measurements.Add(new { endpoint = "snapshot-json-parse-only", products = count, samples = 1, elapsedMs = parsing.Elapsed.TotalMilliseconds, parsedRows, payloadBytes = payloads.Sum(text => System.Text.Encoding.UTF8.GetByteCount(text)) });
            if (count == 10)
            {
                var detailRequest = new BatchProductSalesDetailRequestDto { ProductCode = chosen[0], StartDate = start, EndDate = dates[^1], StoreCodes = stores, ReadyDates = locked.ReadyDates, CoverageVersion = locked.CoverageVersion };
                measurements.Add(await MeasureOverviewAsync("detail", 1, 20, () => service.GetDetailAsync(detailRequest, stores), options));
                await WriteContract("query", request, await service.QueryAsync(request, stores));
                await WriteContract("detail", detailRequest, await service.GetDetailAsync(detailRequest, stores));
                locked.BranchCode = stores[0];
                await WriteContract("branch", locked, await service.GetBranchOverviewAsync(locked, stores));
                await WriteContract("branch-discounts", locked, await service.GetDiscountOverviewAsync(locked, stores));
                locked.BranchCode = null;
                await WriteContract("discounts", locked, discounts);
                var csv = await service.ExportDetailCsvAsync(locked, stores);
                // 服务端仅在临时文件的最终覆盖核验通过后返回 CSV 正文。
                await File.WriteAllTextAsync(Path.Combine(output, "export.csv"), csv);
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "measurements.json"), JsonSerializer.Serialize(new
        {
            environment = "local isolated SQL Server; service calls, excluding HTTP/browser latency; warm database caches, no application result cache",
            range = new[] { "2026-08-17", "2026-09-07" }, stores = 58, statisticRows = 500 * 58 * 22,
            note = "query/branch/detail P95 use 20 samples; discounts has 3 samples; parse-only has 1 sample. No production P95 or old-release comparison is claimed.",
            measurements,
        }, new JsonSerializerOptions(options) { WriteIndented = true }));

        async Task WriteContract(string name, object request, object response) =>
            await File.WriteAllTextAsync(Path.Combine(output, $"{name}.json"), JsonSerializer.Serialize(new { request, response }, options));
    }

    private sealed record OverviewMeasurement(string Endpoint, int Products, int Samples, double MedianMs, double P95Ms, double MedianSqlMs, int SqlCount, int FactSqlCount, int SnapshotSqlCount, int DiscountStateSqlCount, int ResponseBytes);
    private async Task<OverviewMeasurement> MeasureOverviewAsync<T>(string endpoint, int products, int samples, Func<Task<T>> read, JsonSerializerOptions options)
    {
        var elapsed = new List<double>(); var sqlElapsed = new List<double>();
        var sqlCount = 0; var factCount = 0; var snapshotCount = 0; var stateCount = 0; var bytes = 0;
        await read(); // 预热不计入样本，避免把 JIT 开销误称为稳定查询耗时。
        for (var sample = 0; sample < samples; sample++)
        {
            sqlCount = factCount = snapshotCount = stateCount = 0;
            double sqlMs = 0; var sqlWatch = new Stopwatch();
            _catalog!.Aop.OnLogExecuting = (_, _) => sqlWatch.Restart();
            _catalog.Aop.OnLogExecuted = (sql, _) =>
            {
                sqlMs += sqlWatch.Elapsed.TotalMilliseconds; sqlCount++;
                if (sql.Contains("FROM [ProductStoreDailySalesStatistic]", StringComparison.OrdinalIgnoreCase)) factCount++;
                if (sql.Contains("FROM [BatchProductSalesDiscountSnapshot]", StringComparison.OrdinalIgnoreCase)) snapshotCount++;
                if (sql.Contains("FROM [BatchProductSalesDiscountRefreshState]", StringComparison.OrdinalIgnoreCase)) stateCount++;
            };
            try
            {
                var watch = Stopwatch.StartNew(); var value = await read(); watch.Stop();
                elapsed.Add(watch.Elapsed.TotalMilliseconds); sqlElapsed.Add(sqlMs);
                bytes = JsonSerializer.SerializeToUtf8Bytes(value, options).Length;
            }
            finally { _catalog.Aop.OnLogExecuting = null; _catalog.Aop.OnLogExecuted = null; }
        }
        elapsed.Sort(); sqlElapsed.Sort();
        return new(endpoint, products, samples, elapsed[samples / 2], elapsed[(int)Math.Ceiling(samples * .95) - 1], sqlElapsed[samples / 2], sqlCount, factCount, snapshotCount, stateCount, bytes);
    }
}

internal sealed class BatchSalesOverviewBenchmarkFactAttribute : FactAttribute
{
    public BatchSalesOverviewBenchmarkFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BATCH_SALES_RUN_BENCHMARK") != "1" || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BATCH_SALES_SQLSERVER_TEST_CONNECTION")))
            Skip = "需显式设置 BATCH_SALES_RUN_BENCHMARK=1 及本机隔离 SQL Server 连接。";
    }
}
