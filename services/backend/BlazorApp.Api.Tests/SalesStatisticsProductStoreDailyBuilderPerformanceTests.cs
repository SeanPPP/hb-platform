using System.Collections;
using System.Diagnostics;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Models;
using Xunit;
using Xunit.Abstractions;

namespace BlazorApp.Api.Tests;

/// <summary>
/// Builder 的成本索引回归与量级验证。测试保留 DomainRules 的全量列表调用作为慢参考，
/// 不改动成本优先级或跨门店一致性规则。
/// </summary>
public sealed class SalesStatisticsProductStoreDailyBuilderPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public SalesStatisticsProductStoreDailyBuilderPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Build_大量商品分店成本只遍历一次并保留所有统计行()
    {
        const int productCount = 15_000;
        const int storeCount = 28;
        var date = new DateTime(2026, 9, 7);
        var rawRows = Enumerable.Range(0, productCount)
            .Select(index => new ProductStoreDailySourceRow
            {
                Date = date,
                OrderGuid = $"ORDER-{index:D5}",
                DetailGuid = $"DETAIL-{index:D5}",
                BranchCode = "S01",
                SupplierCode = "SUP",
                ProductCode = $"P-{index:D5}",
                ProductName = $"Product {index}",
                Quantity = 1,
                ActualAmount = index + 1,
                IsHBSalesSource = true,
            })
            .ToList();
        var storeCosts = new CountingReadOnlyList<StoreCostRow>(
            Enumerable.Range(0, productCount)
                .SelectMany(index => Enumerable.Range(0, storeCount).Select(store => new StoreCostRow
                {
                    StoreCode = $"S{store + 1:D2}",
                    SupplierCode = "SUP",
                    ProductCode = $"P-{index:D5}",
                    PurchasePrice = 2m,
                }))
                .ToList());
        var input = CreateInput(rawRows, storeCosts);

        var stopwatch = Stopwatch.StartNew();
        var result = new BlazorApp.Api.Services.SalesStatisticsProductStoreDailyBuilder().Build(input);
        stopwatch.Stop();

        _output.WriteLine(
            $"Builder: {productCount:N0} groups x {storeCount} stores, {stopwatch.Elapsed.TotalMilliseconds:N0} ms, " +
            $"StoreCosts enumerations={storeCosts.EnumerationCount}."
        );

        Assert.Equal(productCount, result.Statistics.Count);
        Assert.Equal(1, storeCosts.EnumerationCount);
        Assert.All(result.Statistics, statistic =>
        {
            Assert.Equal(2m, statistic.UnitCostSnapshot);
            Assert.Equal(2m, statistic.TotalCost);
            Assert.Equal("StoreRetailPrice", statistic.CostSource);
        });
    }

    [Fact]
    public void Build_成本字段与旧全量列表解析参考完全一致()
    {
        var date = new DateTime(2026, 9, 7);
        var rawRows = new[]
        {
            Source(date, "P-CURRENT", "S01", quantity: 2.5m),
            Source(date, "P-PRODUCT", "S01", quantity: 2m),
            Source(date, "P-WAREHOUSE", "S01", quantity: 3m),
            Source(date, "P-FALLBACK", "S01", quantity: 4m, pricingUnit: "each"),
            Source(date, "P-CONFLICT", "S01", quantity: 1m, pricingUnit: "each"),
            Source(date, "P-UNIT-CONFLICT", "S01", quantity: 1m, pricingUnit: "each"),
        };
        var storeCosts = new List<StoreCostRow>();
        foreach (var store in Enumerable.Range(1, 28))
        {
            storeCosts.Add(new StoreCostRow
            {
                StoreCode = " s01 ",
                SupplierCode = " sup ",
                ProductCode = " p-current ",
                PurchasePrice = 3m,
            });
            storeCosts.Add(new StoreCostRow
            {
                StoreCode = $"S{store + 1:D2}",
                SupplierCode = "SUP",
                ProductCode = "P-PRODUCT",
                PurchasePrice = 4m,
            });
            storeCosts.Add(new StoreCostRow
            {
                StoreCode = $"S{store + 1:D2}",
                SupplierCode = "SUP",
                ProductCode = "P-FALLBACK",
                PricingUnit = "each",
                PricingUnitKnown = true,
                PurchasePrice = 7m,
            });
            storeCosts.Add(new StoreCostRow
            {
                StoreCode = $"S{store + 1:D2}",
                SupplierCode = "SUP",
                ProductCode = "P-CONFLICT",
                PricingUnit = "each",
                PricingUnitKnown = true,
                PurchasePrice = store == 1 ? 8m : 7m,
            });
            storeCosts.Add(new StoreCostRow
            {
                StoreCode = $"S{store + 1:D2}",
                SupplierCode = "SUP",
                ProductCode = "P-UNIT-CONFLICT",
                PricingUnit = store == 1 ? "kg" : "each",
                PricingUnitKnown = true,
                PurchasePrice = 7m,
            });
        }

        // 大小写、首尾空白和其它供应商的同商品成本不能改变原解析结果。
        storeCosts.Add(new StoreCostRow
        {
            StoreCode = "S01", SupplierCode = "OTHER", ProductCode = "P-CURRENT", PurchasePrice = 999m,
        });
        storeCosts.Add(new StoreCostRow
        {
            StoreCode = "S01", SupplierCode = "SUP", ProductCode = " ", PurchasePrice = 999m,
        });
        var productCosts = new[]
        {
            new ProductCostRow { ProductCode = "P-PRODUCT", PurchasePrice = 5m },
        };
        var warehouseCosts = new[]
        {
            new WarehouseCostRow { ProductCode = "P-WAREHOUSE", ImportPrice = 6m },
        };
        var input = CreateInput(rawRows, storeCosts, productCosts, warehouseCosts);
        var actual = new BlazorApp.Api.Services.SalesStatisticsProductStoreDailyBuilder().Build(input);

        foreach (var statistic in actual.Statistics)
        {
            var source = rawRows.Single(row =>
                string.Equals(row.ProductCode, statistic.ProductCode, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(source.Quantity * 10m, statistic.TotalAmount);
            var expectedCost = SalesStatisticsProductStoreDailyDomainRules.ResolveCost(
                new[] { source },
                statistic.BranchCode,
                statistic.SupplierCode,
                statistic.ProductCode,
                storeCosts,
                productCosts
                    .GroupBy(row => row.ProductCode!.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Select(row => row.PurchasePrice).FirstOrDefault(price => price is > 0)),
                warehouseCosts
                    .GroupBy(row => row.ProductCode.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Select(row => row.ImportPrice).FirstOrDefault(price => price is > 0)),
                source.PricingUnit
            );

            Assert.Equal(expectedCost.UnitCost, statistic.UnitCostSnapshot);
            Assert.Equal(expectedCost.TotalCost, statistic.TotalCost);
            Assert.Equal(expectedCost.CostSource, statistic.CostSource);
            Assert.Equal(
                statistic.TotalCost.HasValue ? statistic.TotalAmount - statistic.TotalCost.Value : null,
                statistic.GrossProfit
            );
            Assert.Equal(
                statistic.TotalAmount > 0m && statistic.GrossProfit.HasValue
                    ? statistic.GrossProfit.Value / statistic.TotalAmount
                    : null,
                statistic.GrossMarginRate
            );
        }
    }

    private static ProductStoreDailyRefreshInput CreateInput(
        IReadOnlyList<ProductStoreDailySourceRow> rawRows,
        IReadOnlyList<StoreCostRow> storeCosts,
        IReadOnlyList<ProductCostRow>? productCosts = null,
        IReadOnlyList<WarehouseCostRow>? warehouseCosts = null
    ) => new(
        rawRows[0].Date,
        rawRows,
        new HashSet<ProductStoreDailySourceRow>(),
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        storeCosts,
        productCosts ?? Array.Empty<ProductCostRow>(),
        warehouseCosts ?? Array.Empty<WarehouseCostRow>(),
        null
    );

    private static ProductStoreDailySourceRow Source(
        DateTime date,
        string productCode,
        string branchCode,
        decimal quantity,
        string? pricingUnit = null
    ) => new()
    {
        Date = date,
        OrderGuid = $"ORDER-{productCode}",
        DetailGuid = $"DETAIL-{productCode}",
        BranchCode = branchCode,
        SupplierCode = "SUP",
        ProductCode = productCode,
        ProductName = productCode,
        Quantity = quantity,
        ActualAmount = quantity * 10m,
        IsHBSalesSource = true,
        PricingUnit = pricingUnit,
    };

    private sealed class CountingReadOnlyList<T> : IReadOnlyList<T>
    {
        private readonly IReadOnlyList<T> _items;

        internal CountingReadOnlyList(IReadOnlyList<T> items) => _items = items;

        internal int EnumerationCount { get; private set; }
        public int Count => _items.Count;
        public T this[int index] => _items[index];

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
