using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SalesDetailReportSqlServerFactAttribute : FactAttribute
{
    private const string ConnectionEnvironmentVariable = "HB_TEST_SQLSERVER_CONNECTION";

    public SalesDetailReportSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)))
            Skip = $"未配置 {ConnectionEnvironmentVariable}，跳过真实 SQL Server 销售明细验证。";
    }
}

[Trait("Category", "SQL")]
public sealed class SalesDetailReportSqlServerIntegrationTests
{
    private static readonly DateTime SeedDate = new(2026, 9, 9);
    private static readonly DateTime CompareDate = new(2026, 9, 8);
    private const string SqlServerTestConnectionEnvVar = "HB_TEST_SQLSERVER_CONNECTION";

    [SalesDetailReportSqlServerFact]
    public async Task 分店栏单商品精确筛选按授权门店范围且统计商品码去空格()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedStoreAsync("B1", "授权店");
        await fixture.SeedStoreAsync("B2", "范围外店");
        await fixture.SeedChinaSupplierAsync("C1", "国内供应商");
        await fixture.SeedProductAsync("P-ONE", "商品一");
        await fixture.SeedProductAsync("P-TWO", "商品二");
        await fixture.SeedFactAsync(SeedDate, "B1", "C1", " P-ONE ", 1, 10m, "统计一");
        await fixture.SeedFactAsync(SeedDate, "B1", "C1", "P-TWO", 3, 30m, "统计二");
        await fixture.SeedFactAsync(SeedDate, "B2", "C1", "P-ONE", 9, 90m, "统计一");

        var result = await fixture.CreateService().GetSalesDetailReportAsync(
            Range(), SalesDetailKind.China, branchCodes: new() { "B1" },
            selectedProductCode: "P-ONE", sections: new[] { SalesDetailSection.Branches });

        Assert.Equal(SalesStatisticRefreshStatus.Fresh, result.StatisticStatus);
        var branch = Assert.Single(result.Data!.Branches!.Rows);
        Assert.Equal("B1", branch.Code);
        Assert.Equal(10m, branch.Revenue);
        Assert.Equal(1, branch.Quantity);
        Assert.DoesNotContain(result.Data.Branches.Rows, row => row.Code == "B2");
    }

    [SalesDetailReportSqlServerFact]
    public async Task 不同销售种类和所选供应商保持供应商归属口径()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedStoreAsync("B1", "测试店");
        await fixture.SeedChinaSupplierAsync("C1", "国内一");
        await fixture.SeedLocalSupplierAsync("200", "HB仓库");
        await fixture.SeedProductAsync("P-CN", "国内商品");
        await fixture.SeedProductAsync("P-AU", "澳洲商品");
        await fixture.SeedMappingAsync("P-CN", "C1");
        await fixture.SeedFactAsync(SeedDate, "B1", "200", "P-CN", 2, 20m, "国内统计商品");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS1", "P-AU", 5, 50m, "澳洲统计商品");

        var china = await fixture.CreateService().GetSalesDetailReportAsync(
            Range(), SalesDetailKind.China, branchCodes: new() { "B1" },
            selectedSupplierCode: "C1", selectedProductCode: "P-CN",
            sections: new[] { SalesDetailSection.Branches });
        var australia = await fixture.CreateService().GetSalesDetailReportAsync(
            Range(), SalesDetailKind.Australia, branchCodes: new() { "B1" },
            selectedSupplierCode: "200", selectedProductCode: "P-CN",
            sections: new[] { SalesDetailSection.Branches });

        var chinaBranch = Assert.Single(china.Data!.Branches!.Rows);
        Assert.Equal("B1", chinaBranch.Code);
        Assert.Equal(20m, chinaBranch.Revenue);
        var australiaBranch = Assert.Single(australia.Data!.Branches!.Rows);
        Assert.Equal("B1", australiaBranch.Code);
        Assert.Equal(20m, australiaBranch.Revenue);
    }

    [SalesDetailReportSqlServerFact]
    public async Task 关键词多个token可分别命中商品和国内供应商字段()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedStoreAsync("B1", "测试店");
        await fixture.SeedChinaSupplierAsync("C2", "Beta 国内供应商");
        await fixture.SeedChinaSupplierAsync("C2", "Beta 国内供应商重复记录");
        await fixture.SeedProductAsync("P-MULTI", "普通商品", englishName: "Alpha 英文名", itemNumber: "货号");
        await fixture.SeedFactAsync(SeedDate, "B1", "C2", "P-MULTI", 4, 40m, "统计名称", "统计条码");

        var result = await fixture.CreateService().GetSalesDetailReportAsync(
            Range(), SalesDetailKind.China, branchCodes: new() { "B1" }, search: "Alpha Beta",
            sections: new[] { SalesDetailSection.Products });

        var product = Assert.Single(result.Data!.Products!.Rows);
        Assert.Equal("P-MULTI", product.Code);
        Assert.Equal(40m, product.Revenue);
        Assert.Equal(1, result.Data.Products.Total);
    }

    [SalesDetailReportSqlServerFact]
    public async Task 重复ProductCode元信息同时命中搜索也不会放大汇总和商品销售事实()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedStoreAsync("B1", "测试店");
        await fixture.SeedLocalSupplierAsync("AUS1", "澳洲供应商");
        await fixture.SeedProductAsync("P-DUP", "商品主记录 Alpha", uuid: "product-dup-a");
        await fixture.SeedProductAsync("P-DUP", "商品重复记录 Alpha", uuid: "product-dup-b");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS1", "P-DUP", 2, 100m, "统计商品");

        var result = await fixture.CreateService().GetSalesDetailReportAsync(
            Range(), SalesDetailKind.Australia, branchCodes: new() { "B1" },
            search: "Alpha", sections: new[] { SalesDetailSection.Summary, SalesDetailSection.Products });

        var product = Assert.Single(result.Data!.Products!.Rows);
        Assert.Equal("P-DUP", product.Code);
        Assert.Equal(100m, product.Revenue);
        Assert.Equal(2, product.Quantity);
        Assert.Equal(1, result.Data.Products.Total);
        Assert.Equal(100m, result.Data.Summary!.Summary!.Revenue);
        Assert.Equal(2, result.Data.Summary.Summary.Quantity);
    }

    [SalesDetailReportSqlServerFact]
    public async Task 全量请求带selectedProduct时产品候选仍保留而分店栏按商品收窄()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedStoreAsync("B1", "测试店");
        await fixture.SeedLocalSupplierAsync("AUS1", "澳洲供应商");
        await fixture.SeedProductAsync("P-SELECT", "已选商品");
        await fixture.SeedProductAsync("P-OTHER", "其他商品");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS1", "P-SELECT", 1, 10m, "已选统计商品");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS1", "P-OTHER", 3, 30m, "其他统计商品");

        var result = await fixture.CreateService().GetSalesDetailReportAsync(
            Range(), SalesDetailKind.Australia, branchCodes: new() { "B1" },
            selectedProductCode: " P-SELECT ");

        var branch = Assert.Single(result.Data!.Branches!.Rows);
        Assert.Equal(10m, branch.Revenue);
        Assert.Equal(1, branch.Quantity);
        Assert.Equal(new[] { "P-OTHER", "P-SELECT" }, result.Data.Products!.Rows.Select(row => row.Code).OrderBy(code => code));
        Assert.Equal(2, result.Data.Products.Total);
    }

    [SalesDetailReportSqlServerFact]
    public async Task 分店栏本期同期单商品指标和缺成本语义与全量请求一致()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedFreshStateAsync(CompareDate);
        await fixture.SeedStoreAsync("B1", "测试店");
        await fixture.SeedLocalSupplierAsync("AUS1", "澳洲供应商");
        await fixture.SeedProductAsync("P-ROLL", "跨期商品");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS1", "P-ROLL", 3, 30m, "本期统计商品", totalCost: 12m, grossProfit: 18m);
        await fixture.SeedFactAsync(CompareDate, "B1", "AUS1", "P-ROLL", 2, 20m, "同期统计商品", totalCost: null, grossProfit: null, useAmountAsDefaultGrossProfit: false);

        var branchesOnly = await fixture.CreateService().GetSalesDetailReportAsync(
            Range(SeedDate, CompareDate), SalesDetailKind.Australia, branchCodes: new() { "B1" },
            selectedProductCode: "P-ROLL", sections: new[] { SalesDetailSection.Branches });
        var full = await fixture.CreateService().GetSalesDetailReportAsync(
            Range(SeedDate, CompareDate), SalesDetailKind.Australia, branchCodes: new() { "B1" },
            selectedProductCode: "P-ROLL");

        var branch = Assert.Single(branchesOnly.Data!.Branches!.Rows);
        Assert.Equal(30m, branch.Revenue);
        Assert.Equal(20m, branch.CompareRevenue);
        Assert.Equal(3, branch.Quantity);
        Assert.Equal(2, branch.CompareQuantity);
        Assert.Equal(30m, branch.AverageTransaction);
        Assert.Equal(20m, branch.CompareAverageTransaction);
        Assert.Equal(10m, branch.AverageUnitPrice);
        Assert.Equal(10m, branch.CompareAverageUnitPrice);
        Assert.Equal(18m, branch.GrossProfit);
        Assert.Null(branch.CompareGrossProfit); // 同期缺成本时不能伪造毛利和毛利率。
        Assert.Equal(0.6m, branch.GrossMarginRate);
        Assert.Null(branch.CompareGrossMarginRate);

        var fullBranch = Assert.Single(full.Data!.Branches!.Rows);
        Assert.Equal(branch.Revenue, fullBranch.Revenue);
        Assert.Equal(branch.CompareRevenue, fullBranch.CompareRevenue);
        Assert.Equal(branch.Quantity, fullBranch.Quantity);
        Assert.Equal(branch.CompareQuantity, fullBranch.CompareQuantity);
        Assert.Equal(branch.AverageTransaction, fullBranch.AverageTransaction);
        Assert.Equal(branch.CompareAverageTransaction, fullBranch.CompareAverageTransaction);
        Assert.Equal(branch.AverageUnitPrice, fullBranch.AverageUnitPrice);
        Assert.Equal(branch.CompareAverageUnitPrice, fullBranch.CompareAverageUnitPrice);
        Assert.Equal(branch.GrossProfit, fullBranch.GrossProfit);
        Assert.Null(fullBranch.CompareGrossProfit);
        Assert.Equal(branch.GrossMarginRate, fullBranch.GrossMarginRate);
        Assert.Null(fullBranch.CompareGrossMarginRate);
    }

    [SalesDetailReportSqlServerFact]
    public async Task 日投影搜索与原查询七结果集一致并保留空白权限与跨日最大名称语义()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        var earlier = SeedDate.AddDays(-2);
        foreach (var date in new[] { earlier, CompareDate, SeedDate }) await fixture.SeedFreshStateAsync(date);
        await fixture.SeedStoreAsync("B1", "授权店");
        await fixture.SeedStoreAsync("B2", "范围外店");
        await fixture.SeedLocalSupplierAsync("AUS1", "Beta 澳洲供应商");
        await fixture.SeedLocalSupplierAsync("200", "HB仓库");
        await fixture.SeedChinaSupplierAsync("C1", "Beta 国内供应商");
        await fixture.SeedProductAsync("P-ONE", "普通资料");
        await fixture.SeedProductAsync("P-TWO", "Alpha 商品", uuid: "a");
        await fixture.SeedProductAsync("P-TWO", "Alpha 重复商品", uuid: "b");
        await fixture.SeedMappingAsync("P-ONE", "C1");
        await fixture.SeedFactAsync(CompareDate, "B1", "200", "P-ONE", 2, 20m, "Alpha 旧名称", "Z-OLD");
        await fixture.SeedFactAsync(SeedDate, "B1", "200", "P-ONE", -1, -10m, "Zulu 当前名称", "A-NEW");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS1", "P-TWO", 3, 30m, "普通名称");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS1", " P-TWO", 1, 5m, "普通名称");
        await fixture.SeedFactAsync(SeedDate, " B1", "AUS1", "P-TWO", 100, 999m, "权限外原始门店码");
        await fixture.SeedFactAsync(SeedDate, "B2", "AUS1", "P-TWO", 10, 100m, "范围外");
        await fixture.SeedFactAsync(earlier, "B1", "C1", "P-ONE", 4, 40m, "同期名称", totalCost: null,
            grossProfit: null, useAmountAsDefaultGrossProfit: false);
        var range = new DateRangeDto { StartDate = CompareDate, EndDate = SeedDate, CompareStartDate = earlier, CompareEndDate = earlier };
        await fixture.EnableProjectionAsync(earlier, CompareDate, SeedDate);

        foreach (var kind in new[] { SalesDetailKind.Australia, SalesDetailKind.China })
        // 较长候选词可能位于末尾，也可能只命中历史条码；其余词仍可分别命中供应商和商品资料。
        foreach (var query in new[] { ("Alpha", (string?)null), ("Alpha Beta", (string?)null),
            ("Beta Alpha", (string?)null), ("Beta Z-OLD", (string?)null), ("Beta Zulu", (string?)null), ("商品 Beta", (string?)null),
            ("P-ONE", (string?)null), ("Beta", "P-ONE") })
        {
            var original = await fixture.ReadRawReportAsync(range, kind, query.Item1, query.Item2, projected: false);
            var projected = await fixture.ReadRawReportAsync(range, kind, query.Item1, query.Item2, projected: true);
            Assert.Equal(original, projected);
        }
        var compressed = await fixture.CreateService().GetSalesDetailReportAsync(range, SalesDetailKind.Australia,
            branchCodes: new() { "B1" }, search: "Alpha");
        Assert.Equal(35m, compressed.Data!.Summary!.Summary!.Revenue);
        Assert.Equal("P-TWO", Assert.Single(compressed.Data.Products!.Rows).Code);
        Assert.Equal(1, compressed.Data.Products.Total);
        Assert.Equal(45m, compressed.Data.Suppliers!.Rows.Sum(row => row.Revenue));
        Assert.Equal(DateTimeKind.Utc, compressed.StatisticUpdatedAt!.Value.Kind);
        Assert.EndsWith("Z\"", JsonSerializer.Serialize(compressed.StatisticUpdatedAt));
    }

    [SalesDetailReportSqlServerFact]
    public async Task 最长供应商候选经仓库映射且晚查重复商品资料仍与原查询七集一致()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedStoreAsync("B1", "授权店");
        await fixture.SeedChinaSupplierAsync("C1", "WholesaleSupplierAnchor");
        await fixture.SeedProductAsync("P-SELECT", "选中商品");
        await fixture.SeedProductAsync("P-LATE", "普通目录", uuid: "a");
        await fixture.SeedProductAsync("P-LATE", "重复目录", itemNumber: "X7", uuid: "b");
        await fixture.SeedMappingAsync("P-SELECT", "C1");
        await fixture.SeedMappingAsync("P-LATE", "C1");
        await fixture.SeedFactAsync(SeedDate, "B1", "200", "P-SELECT", 1, 10m, "普通事实");
        await fixture.SeedFactAsync(SeedDate, "B1", "200", " P-LATE ", 3, 30m, "空白码事实");
        await fixture.EnableProjectionAsync(SeedDate);

        const string search = "X7 WholesaleSupplierAnchor";
        var original = await fixture.ReadRawReportAsync(
            Range(), SalesDetailKind.China, search, "P-SELECT", projected: false);
        var projected = await fixture.ReadRawReportAsync(
            Range(), SalesDetailKind.China, search, "P-SELECT", projected: true);
        Assert.Equal(original, projected); // fixture 同时断言固定返回七个结果集。

        using var document = JsonDocument.Parse(projected);
        var sets = document.RootElement;
        Assert.Equal(7, sets.GetArrayLength());
        Assert.Equal(0m, sets[1][0][4].GetDecimal()); // 选中商品不匹配短词，关键词筛选后的汇总为空。
        Assert.Equal(10m, sets[2][0][4].GetDecimal()); // 左两栏忽略关键词，仍只按选中商品统计。
        Assert.Equal(10m, sets[3][0][4].GetDecimal());
        Assert.Equal("P-LATE", sets[4][0][0].GetString()); // selectedProduct 不缩小商品候选。
        Assert.Equal(30m, sets[4][0][4].GetDecimal()); // 重复 Product 元数据不能放大销售金额。
        Assert.Equal(1, sets[5][0][0].GetInt32());
    }

    [SalesDetailReportSqlServerFact]
    public async Task 规范商品码候选扩回全部原始码并保持跨原始码最大名称金额与选中语义()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedStoreAsync("B1", "授权店");
        await fixture.SeedLocalSupplierAsync("AUS1", "Aus");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS1", " P-CROSS", 1, 10m, "Alpha");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS1", "P-CROSS", 2, 20m, "Zulu");
        await fixture.EnableProjectionAsync(SeedDate);

        foreach (var query in new[] { (Search: "Alpha", Selected: (string?)null),
            (Search: "Alpha Aus", Selected: (string?)null), (Search: "Alpha Aus", Selected: (string?)"P-CROSS") })
        {
            var original = await fixture.ReadRawReportAsync(
                Range(), SalesDetailKind.Australia, query.Search, query.Selected, projected: false);
            var projected = await fixture.ReadRawReportAsync(
                Range(), SalesDetailKind.Australia, query.Search, query.Selected, projected: true);
            Assert.Equal(original, projected);
        }

        using var selected = JsonDocument.Parse(await fixture.ReadRawReportAsync(
            Range(), SalesDetailKind.Australia, "Alpha Aus", "P-CROSS", projected: true));
        var sets = selected.RootElement;
        Assert.Equal(0m, sets[1][0][4].GetDecimal()); // 规范码聚合后的 MAX 名称是 Zulu，不能用局部 raw 的 Alpha 命中。
        Assert.Equal(30m, sets[2][0][4].GetDecimal()); // 左两栏仍保留选中规范商品码下的全部 raw 金额。
        Assert.Equal(30m, sets[3][0][4].GetDecimal());
        Assert.Equal(0, sets[4].GetArrayLength());
        Assert.Equal(0, sets[5][0][0].GetInt32());
    }

    [SalesDetailReportSqlServerFact]
    public async Task 多词预筛保留当前重复商品资料供应商命中与选中商品()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedStoreAsync("B1", "授权店");
        await fixture.SeedLocalSupplierAsync("AUS-META", "普通供应商");
        await fixture.SeedLocalSupplierAsync("AUS-SUP", "SupplierNeedle");
        await fixture.SeedLocalSupplierAsync("AUS-SELECT", "选中供应商");
        await fixture.SeedProductAsync("P-META", "普通目录", uuid: "meta-a");
        await fixture.SeedProductAsync("P-META", "重复目录", englishName: "MetaNeedle", uuid: "meta-b");
        await fixture.SeedProductAsync("P-SUP", "供应商商品", itemNumber: "LateCode");
        await fixture.SeedProductAsync("P-SELECT", "选中商品");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS-META", "P-META", 1, 10m, "VeryLongAnchor");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS-SUP", "P-SUP", 2, 20m, "VeryLongAnchor");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS-SELECT", "P-SELECT", 4, 40m, "普通事实");
        await fixture.EnableProjectionAsync(SeedDate);

        async Task<JsonDocument> ReadEquivalentAsync(string search, string? selectedProduct = null)
        {
            var original = await fixture.ReadRawReportAsync(
                Range(), SalesDetailKind.Australia, search, selectedProduct, projected: false);
            var projected = await fixture.ReadRawReportAsync(
                Range(), SalesDetailKind.Australia, search, selectedProduct, projected: true);
            Assert.Equal(original, projected);
            return JsonDocument.Parse(projected);
        }

        // 候选供应商为空且其余词也不命中供应商时，重复 Product 中任一当前资料命中即可保留规范码。
        using (var metadata = await ReadEquivalentAsync("VeryLongAnchor MetaNeedle"))
        {
            Assert.Equal("P-META", metadata.RootElement[4][0][0].GetString());
            Assert.Equal(10m, metadata.RootElement[4][0][4].GetDecimal());
            Assert.Equal(1, metadata.RootElement[5][0][0].GetInt32());
        }
        // 其余词命中当前供应商时必须跳过商品预筛，留给最终跨字段 AND 判断。
        using (var supplier = await ReadEquivalentAsync("VeryLongAnchor SupplierNeedle"))
        {
            Assert.Equal("P-SUP", supplier.RootElement[4][0][0].GetString());
            Assert.Equal(20m, supplier.RootElement[4][0][4].GetDecimal());
        }
        // anchor 已命中候选供应商时，仍在读取事实后查剩余商品资料。
        using (var lateMetadata = await ReadEquivalentAsync("SupplierNeedle LateCode"))
        {
            Assert.Equal("P-SUP", lateMetadata.RootElement[4][0][0].GetString());
            Assert.Equal(20m, lateMetadata.RootElement[4][0][4].GetDecimal());
        }
        // selectedProduct 即使不命中任一关键词，也必须留在事实中供供应商和分店两栏使用。
        using (var selected = await ReadEquivalentAsync("VeryLongAnchor MetaNeedle", "P-SELECT"))
        {
            Assert.Equal(0m, selected.RootElement[1][0][4].GetDecimal());
            Assert.Equal(40m, selected.RootElement[2][0][4].GetDecimal());
            Assert.Equal(40m, selected.RootElement[3][0][4].GetDecimal());
            Assert.Equal("P-META", selected.RootElement[4][0][0].GetString());
        }
    }

    [SalesDetailReportSqlServerFact]
    public async Task 名称发布标识或映射变化时投影拒绝旧覆盖并由接口回退原查询()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedStoreAsync("B1", "授权店");
        await fixture.SeedChinaSupplierAsync("C1", "国内供应商");
        await fixture.SeedProductAsync("P-ONE", "普通资料");
        await fixture.SeedMappingAsync("P-ONE", "C1");
        await fixture.SeedFactAsync(SeedDate, "B1", "200", "P-ONE", 2, 20m, "Alpha 原名称");
        await fixture.EnableProjectionAsync(SeedDate);
        await fixture.ChangeStatisticNameAsync(SeedDate, "Zulu 新名称");
        var stale = await Assert.ThrowsAsync<SqlException>(() => fixture.ReadRawReportAsync(Range(), SalesDetailKind.China, "Zulu", null, projected: true));
        Assert.Equal(51012, stale.Number);
        var result = await fixture.CreateService().GetSalesDetailReportAsync(Range(), SalesDetailKind.China,
            branchCodes: new() { "B1" }, search: "Zulu");
        Assert.Equal(20m, Assert.Single(result.Data!.Products!.Rows).Revenue);

        await fixture.RefreshProjectionAsync(SeedDate);
        await fixture.SeedMappingAsync("P-ONE", "C1"); // 旧 SQL 会放大重复映射，签名必须也能识别重复项。
        var remapped = await Assert.ThrowsAsync<SqlException>(() => fixture.ReadRawReportAsync(Range(), SalesDetailKind.China, "Zulu", null, projected: true));
        Assert.Equal(51012, remapped.Number);
    }

    [SalesDetailReportSqlServerFact]
    public async Task 澳洲普通商品搜索允许无放大的映射变化但中国搜索与分类变化仍拒绝旧投影()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedStoreAsync("B1", "授权店");
        await fixture.SeedLocalSupplierAsync("AUS1", "澳洲供应商");
        await fixture.SeedChinaSupplierAsync("C1", "国内一");
        await fixture.SeedChinaSupplierAsync("C2", "国内二");
        await fixture.SeedProductAsync("P-ONE", "普通资料");
        await fixture.SeedMappingAsync("P-ONE", "C1");
        await fixture.SeedFactAsync(SeedDate, "B1", "200", "P-ONE", 2, 20m, "Alpha 商品");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS1", "P-TWO", 1, 5m, "其他商品");
        await fixture.EnableProjectionAsync(SeedDate);
        await fixture.ChangeMappingAsync("P-ONE", "C2");
        // 放行仅承诺对外澳洲数据一致；不比较澳洲 DTO 从未消费的两个中国分母。
        using var original = JsonDocument.Parse(await fixture.ReadRawReportAsync(Range(), SalesDetailKind.Australia, "Alpha", null, projected: false));
        using var projected = JsonDocument.Parse(await fixture.ReadRawReportAsync(Range(), SalesDetailKind.Australia, "Alpha", null, projected: true));
        for (var i = 0; i < 6; i++) Assert.Equal(original.RootElement[i].GetRawText(), projected.RootElement[i].GetRawText());
        foreach (var column in new[] { 0, 2 }) Assert.Equal(original.RootElement[6][0][column].GetDecimal(), projected.RootElement[6][0][column].GetDecimal());
        var china = await Assert.ThrowsAsync<SqlException>(() => fixture.ReadRawReportAsync(Range(), SalesDetailKind.China, "Alpha", null, projected: true));
        Assert.Equal(51012, china.Number);
        var supplierWord = await Assert.ThrowsAsync<SqlException>(() => fixture.ReadRawReportAsync(Range(), SalesDetailKind.Australia, "国内二", null, projected: true));
        Assert.Equal(51012, supplierWord.Number);
        await fixture.SeedChinaSupplierAsync("AUS1", "改变分类");
        var reclassified = await Assert.ThrowsAsync<SqlException>(() => fixture.ReadRawReportAsync(Range(), SalesDetailKind.Australia, "Alpha", null, projected: true));
        Assert.Equal(51012, reclassified.Number);
    }

    [SalesDetailReportSqlServerFact]
    public async Task 批末完成时间变化不使相同商品事实投影失效()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedStoreAsync("B1", "授权店");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS1", "P-ONE", 2, 20m, "Alpha 商品");
        await fixture.SetPublishStatusAsync(SeedDate, "ProvisionalFresh");
        await fixture.EnableProjectionAsync(SeedDate);
        await fixture.SetPublishStatusAsync(SeedDate, "Fresh");
        Assert.Equal(await fixture.ReadRawReportAsync(Range(), SalesDetailKind.Australia, "Alpha", null, projected: false),
            await fixture.ReadRawReportAsync(Range(), SalesDetailKind.Australia, "Alpha", null, projected: true));
    }

    [SalesDetailReportSqlServerFact]
    public async Task 无关商品映射或供应商新增不淘汰历史中国查询但实际映射变化仍拒绝()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedFreshStateAsync(CompareDate);
        await fixture.SeedStoreAsync("B1", "授权店");
        await fixture.SeedChinaSupplierAsync("C1", "Alpha 国内供应商");
        await fixture.SeedChinaSupplierAsync("C2", "另一供应商");
        await fixture.SeedMappingAsync("P-ONE", "C1");
        await fixture.SeedFactAsync(SeedDate, "B1", "200", "P-ONE", 2, 20m, "Alpha 商品");
        await fixture.SeedFactAsync(CompareDate, "B1", "200", "P-ONE", 1, 5m, "Alpha 商品");
        await fixture.EnableProjectionAsync(SeedDate, CompareDate);
        await fixture.SeedMappingAsync("UNRELATED", "C2");
        await fixture.SeedChinaSupplierAsync("UNRELATED", "新增未销售供应商");
        foreach (var kind in new[] { SalesDetailKind.Australia, SalesDetailKind.China })
            Assert.Equal(await fixture.ReadRawReportAsync(Range(SeedDate, CompareDate), kind, "Alpha", null, projected: false),
                await fixture.ReadRawReportAsync(Range(SeedDate, CompareDate), kind, "Alpha", null, projected: true));
        await fixture.ChangeMappingAsync("P-ONE", "C2");
        var changed = await Assert.ThrowsAsync<SqlException>(() => fixture.ReadRawReportAsync(Range(SeedDate, CompareDate), SalesDetailKind.China, "Alpha", null, projected: true));
        Assert.Equal(51012, changed.Number);
    }

    [SalesDetailReportSqlServerFact]
    public async Task 下次日任务排队和运行时仍读取相同的已发布投影()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedStoreAsync("B1", "授权店");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS1", "P-ONE", 2, 20m, "Alpha 商品");
        await fixture.EnableProjectionAsync(SeedDate);
        foreach (var status in new[] { "Queued", "Running" })
        {
            await fixture.BeginNextPublishAsync(SeedDate, status);
            Assert.Equal(await fixture.ReadRawReportAsync(Range(), SalesDetailKind.Australia, "Alpha", null, projected: false),
                await fixture.ReadRawReportAsync(Range(), SalesDetailKind.Australia, "Alpha", null, projected: true));
        }
        await fixture.ChangeStatisticNameAsync(SeedDate, "Zulu 新名称");
        var changed = await Assert.ThrowsAsync<SqlException>(() => fixture.ReadRawReportAsync(Range(), SalesDetailKind.Australia, "Zulu", null, projected: true));
        Assert.Equal(51012, changed.Number);
    }

    [SalesDetailReportSqlServerFact]
    public async Task 投影范围超过366天拒绝执行且缺少发布时间不能发布但无任务标识仍可使用()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedStoreAsync("B1", "授权店");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS1", "P-ONE", 2, 20m, "Alpha 商品");
        await fixture.SetPublishIdentityAsync(SeedDate, missingAggregation: false);
        await fixture.EnableProjectionAsync(SeedDate);
        Assert.Equal(await fixture.ReadRawReportAsync(Range(), SalesDetailKind.Australia, "Alpha", null, projected: false),
            await fixture.ReadRawReportAsync(Range(), SalesDetailKind.Australia, "Alpha", null, projected: true));
        foreach (var days in new[] { 367, 1001 })
        foreach (var compare in new[] { false, true })
        {
            var range = Range();
            if (compare) { range.CompareStartDate = SeedDate.AddDays(1 - days); range.CompareEndDate = SeedDate; }
            else range.StartDate = SeedDate.AddDays(1 - days);
            var error = await Assert.ThrowsAsync<SqlException>(() => fixture.ReadRawReportAsync(range, SalesDetailKind.Australia, "Alpha", null, projected: true));
            Assert.Equal(51012, error.Number);
            Assert.Contains("日期范围超限", error.Message);
        }
        await fixture.SetPublishIdentityAsync(SeedDate, missingAggregation: true);
        await fixture.RefreshProjectionAsync(SeedDate);
        var unpublished = await Assert.ThrowsAsync<SqlException>(() => fixture.ReadRawReportAsync(Range(), SalesDetailKind.Australia, "Alpha", null, projected: true));
        Assert.Equal(51012, unpublished.Number);
    }

    private static DateRangeDto Range() => Range(SeedDate, null);

    private static DateRangeDto Range(DateTime currentDate, DateTime? compareDate)
        => new()
        {
            StartDate = currentDate, EndDate = currentDate,
            CompareStartDate = compareDate, CompareEndDate = compareDate,
        };

    private sealed class SalesDetailSqlServerFixture : IAsyncDisposable
    {
        private readonly string _masterConnectionString;
        private readonly string _databaseName;
        private readonly string _databaseConnectionString;
        private readonly SqlSugarClient _db;
        private readonly SqlSugarClient _posmDb;
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());

        private SalesDetailSqlServerFixture(string masterConnectionString, string databaseName, string databaseConnectionString)
        {
            _masterConnectionString = masterConnectionString;
            _databaseName = databaseName;
            _databaseConnectionString = databaseConnectionString;
            _db = new SqlSugarClient(CreateConnectionConfig(databaseConnectionString));
            _posmDb = new SqlSugarClient(CreateConnectionConfig(databaseConnectionString));
        }

        public static async Task<SalesDetailSqlServerFixture> CreateAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerTestConnectionEnvVar);
            if (string.IsNullOrWhiteSpace(baseConnectionString))
                throw new InvalidOperationException($"未配置 {SqlServerTestConnectionEnvVar}。");
            EnsureLoopbackSqlServer(baseConnectionString);

            var databaseName = $"HbSalesDetail_{Guid.NewGuid():N}";
            var masterConnectionString = BuildConnectionString(baseConnectionString, "master");
            var databaseConnectionString = BuildConnectionString(baseConnectionString, databaseName);
            await ExecuteNonQueryAsync(masterConnectionString, $"CREATE DATABASE {QuoteSqlServerName(databaseName)};");
            try
            {
                await ExecuteNonQueryAsync(databaseConnectionString, "ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;");
                await ExecuteNonQueryAsync(databaseConnectionString, SchemaSql);
                return new SalesDetailSqlServerFixture(masterConnectionString, databaseName, databaseConnectionString);
            }
            catch
            {
                await DropDatabaseAsync(masterConnectionString, databaseName);
                throw;
            }
        }

        public SalesDashboardReactService CreateService()
        {
            return new SalesDashboardReactService(
                CreateSqlSugarContext(_db), CreatePosmSqlSugarContext(_posmDb), Mock.Of<IMapper>(),
                NullLogger<SalesDashboardReactService>.Instance, _cache);
        }

        // 所有测试数据都使用参数写入，避免把测试输入拼接进 SQL。
        public Task SeedFreshStateAsync(DateTime date) => ExecuteNonQueryAsync(_databaseConnectionString, """
            INSERT INTO [dbo].[SalesStatisticRefreshState]
                ([StatisticType], [Date], [Status], [LastAggregatedAtUtc], [CompletedAtUtc], [SourceProductVersion], [JobId])
            VALUES (N'ProductStoreDaily', @date, N'Fresh', SYSUTCDATETIME(), SYSUTCDATETIME(), N'test-source-v1', NEWID());
            """, ("@date", date));

        public async Task EnableProjectionAsync(params DateTime[] dates)
        {
            await ExecuteNonQueryAsync(_databaseConnectionString, SalesDetailQueryProjection.CreateSchemaSql);
            foreach (var date in dates) await RefreshProjectionAsync(date);
        }

        public async Task RefreshProjectionAsync(DateTime date)
        {
            await using var connection = new SqlConnection(_databaseConnectionString);
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            await using var command = new SqlCommand(SalesDetailQueryProjection.BuildRefreshDaySql(_databaseName), connection, transaction);
            command.Parameters.AddWithValue("@sdpDate", date);
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        public Task ChangeStatisticNameAsync(DateTime date, string name) => ExecuteNonQueryAsync(_databaseConnectionString, """
            BEGIN TRANSACTION;
            UPDATE dbo.ProductStoreDailySalesStatistic SET ProductName=@name WHERE [Date]=@date;
            UPDATE dbo.SalesStatisticRefreshState SET JobId=NEWID(),LastAggregatedAtUtc=SYSUTCDATETIME(),CompletedAtUtc=SYSUTCDATETIME()
            WHERE [Date]=@date AND StatisticType='ProductStoreDaily';
            COMMIT TRANSACTION;
            """, ("@date", date), ("@name", name));

        public Task SetPublishStatusAsync(DateTime date, string status) => ExecuteNonQueryAsync(_databaseConnectionString,
            "UPDATE dbo.SalesStatisticRefreshState SET Status=@status,CompletedAtUtc=DATEADD(second,1,CompletedAtUtc) WHERE [Date]=@date AND StatisticType='ProductStoreDaily';",
            ("@date", date), ("@status", status));

        public Task SetPublishIdentityAsync(DateTime date, bool missingAggregation) => ExecuteNonQueryAsync(_databaseConnectionString,
            "UPDATE dbo.SalesStatisticRefreshState SET JobId=NULL,LastAggregatedAtUtc=CASE WHEN @missing=1 THEN NULL ELSE LastAggregatedAtUtc END WHERE [Date]=@date AND StatisticType='ProductStoreDaily';",
            ("@date", date), ("@missing", missingAggregation));

        public Task BeginNextPublishAsync(DateTime date, string status) => ExecuteNonQueryAsync(_databaseConnectionString,
            "UPDATE dbo.SalesStatisticRefreshState SET JobId=NEWID(),Status=@status WHERE [Date]=@date AND StatisticType='ProductStoreDaily';",
            ("@date", date), ("@status", status));

        public Task ChangeMappingAsync(string product, string supplier) => ExecuteNonQueryAsync(_databaseConnectionString,
            "UPDATE dbo.posm_product_supplier_mapping SET ChinaSupplierCode=@supplier WHERE ProductCode=@product AND LocalSupplierCode='200';",
            ("@product", product), ("@supplier", supplier));

        public async Task<string> ReadRawReportAsync(DateRangeDto range, SalesDetailKind kind, string search, string? selectedProduct, bool projected)
        {
            var builder = typeof(SalesDashboardReactService).GetMethod("BuildSalesDetailReportSqlServerCore", BindingFlags.Static | BindingFlags.NonPublic)!;
            var sql = (string)builder.Invoke(null, new object?[] { _databaseName, range, kind, new[] { "B1" }, null, null,
                selectedProduct, search, 1, 20, Enum.GetValues<SalesDetailSection>().ToHashSet(), null, projected, false })!;
            await using var connection = new SqlConnection(_databaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("SET NOCOUNT ON;SET TRANSACTION ISOLATION LEVEL SNAPSHOT;BEGIN TRANSACTION;" + sql + ";COMMIT TRANSACTION;", connection);
            command.Parameters.AddWithValue("@sdrCurrentStart", range.StartDate);
            command.Parameters.AddWithValue("@sdrCurrentEnd", range.EndDate.AddDays(1));
            command.Parameters.AddWithValue("@sdrCompareStart", (object?)range.CompareStartDate ?? DBNull.Value);
            command.Parameters.AddWithValue("@sdrCompareEnd", (object?)range.CompareEndDate?.AddDays(1) ?? DBNull.Value);
            command.Parameters.AddWithValue("@sdrHasCompare", range.CompareStartDate.HasValue ? 1 : 0);
            command.Parameters.AddWithValue("@sdrKind", kind == SalesDetailKind.China ? 1 : 0);
            command.Parameters.AddWithValue("@sdrBranch0", "B1");
            command.Parameters.AddWithValue("@sdrSelectedBranch0", "B1");
            if (selectedProduct != null) command.Parameters.AddWithValue("@sdrSelectedProduct", selectedProduct);
            var tokens = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < tokens.Length; i++) command.Parameters.AddWithValue($"@sdrSearch{i}", $"%{tokens[i]}%");
            var sets = new List<List<object?[]>>();
            await using var reader = await command.ExecuteReaderAsync();
            do
            {
                if (reader.FieldCount == 0) continue;
                var rows = new List<object?[]>();
                while (await reader.ReadAsync())
                {
                    var values = new object[reader.FieldCount];
                    reader.GetValues(values);
                    // 投影使用更宽的 decimal 精度；比较数值本身，不把无意义的小数尾零视为差异。
                    rows.Add(values.Select(value => value switch
                    {
                        DBNull => null,
                        decimal number => (object)decimal.Parse(number.ToString("G29", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture),
                        _ => value,
                    }).ToArray());
                }
                sets.Add(rows);
            } while (await reader.NextResultAsync());
            Assert.Equal(7, sets.Count);
            return JsonSerializer.Serialize(sets);
        }

        public Task SeedStoreAsync(string code, string name) => ExecuteNonQueryAsync(_databaseConnectionString,
            "INSERT INTO [dbo].[Store] ([StoreCode], [StoreName], [IsActive], [IsDeleted]) VALUES (@code, @name, 1, 0);",
            ("@code", code), ("@name", name));

        public Task SeedChinaSupplierAsync(string code, string name) => ExecuteNonQueryAsync(_databaseConnectionString,
            "INSERT INTO [dbo].[ChinaSupplier] ([SupplierCode], [SupplierName]) VALUES (@code, @name);",
            ("@code", code), ("@name", name));

        public Task SeedLocalSupplierAsync(string code, string name) => ExecuteNonQueryAsync(_databaseConnectionString,
            "INSERT INTO [dbo].[LocalSupplier] ([LocalSupplierCode], [Name], [IsDeleted]) VALUES (@code, @name, 0);",
            ("@code", code), ("@name", name));

        public Task SeedProductAsync(string code, string name, string? englishName = null, string? itemNumber = null, string? uuid = null)
            => ExecuteNonQueryAsync(_databaseConnectionString, """
                INSERT INTO [dbo].[Product]
                    ([UUID], [ProductCode], [ProductName], [EnglishName], [ItemNumber], [Barcode], [LocalSupplierCode], [ProductImage])
                VALUES (@uuid, @code, @name, @englishName, @itemNumber, NULL, NULL, NULL);
                """, ("@uuid", uuid ?? $"product-{Guid.NewGuid():N}"), ("@code", code), ("@name", name),
                ("@englishName", englishName), ("@itemNumber", itemNumber));

        public Task SeedMappingAsync(string productCode, string chinaSupplierCode) => ExecuteNonQueryAsync(_databaseConnectionString,
            "INSERT INTO [dbo].[posm_product_supplier_mapping] ([ProductCode], [LocalSupplierCode], [ChinaSupplierCode], [IsDeleted]) VALUES (@product, N'200', @china, 0);",
            ("@product", productCode), ("@china", chinaSupplierCode));

        public Task SeedFactAsync(DateTime date, string branch, string supplier, string product, int quantity, decimal amount,
            string? productName = null, string? barcode = null, decimal? totalCost = null, decimal? grossProfit = null,
            bool useAmountAsDefaultGrossProfit = true)
            => ExecuteNonQueryAsync(_databaseConnectionString, """
                INSERT INTO [dbo].[ProductStoreDailySalesStatistic]
                    ([Date], [BranchCode], [SupplierCode], [ProductCode], [ProductName], [Barcode], [TotalQuantity], [TotalAmount], [OrderCount], [TotalCost], [GrossProfit], [CostSource], [UpdateTime])
                VALUES (@date, @branch, @supplier, @product, @productName, @barcode, @quantity, @amount, 1, @totalCost, @grossProfit, N'Test', SYSUTCDATETIME());
                """, ("@date", date), ("@branch", branch), ("@supplier", supplier), ("@product", product),
                ("@productName", productName), ("@barcode", barcode), ("@quantity", quantity), ("@amount", amount),
                ("@totalCost", totalCost), ("@grossProfit", useAmountAsDefaultGrossProfit ? grossProfit ?? amount : grossProfit));

        public async ValueTask DisposeAsync()
        {
            _cache.Dispose();
            _db.Dispose();
            _posmDb.Dispose();
            await DropDatabaseAsync(_masterConnectionString, _databaseName);
        }

        private static readonly string SchemaSql = """
            SET NOCOUNT ON;
            CREATE TABLE [dbo].[Store] (
                [StoreCode] nvarchar(50) NOT NULL PRIMARY KEY,
                [StoreName] nvarchar(100) NOT NULL,
                [IsActive] bit NOT NULL,
                [IsDeleted] bit NOT NULL
            );
            CREATE TABLE [dbo].[LocalSupplier] (
                [LocalSupplierCode] nvarchar(64) NOT NULL PRIMARY KEY,
                [Name] nvarchar(128) NOT NULL,
                [IsDeleted] bit NOT NULL
            );
            CREATE TABLE [dbo].[ChinaSupplier] (
                [SupplierCode] nvarchar(50) NULL,
                [SupplierName] nvarchar(200) NULL
            );
            CREATE TABLE [dbo].[Product] (
                [UUID] nvarchar(50) NOT NULL PRIMARY KEY,
                [ProductCode] nvarchar(50) NULL,
                [ProductName] nvarchar(200) NULL,
                [EnglishName] nvarchar(200) NULL,
                [ItemNumber] nvarchar(50) NULL,
                [Barcode] nvarchar(50) NULL,
                [LocalSupplierCode] nvarchar(50) NULL,
                [ProductImage] nvarchar(200) NULL
            );
            CREATE TABLE [dbo].[posm_product_supplier_mapping] (
                [ProductCode] nvarchar(50) NOT NULL,
                [LocalSupplierCode] nvarchar(50) NOT NULL,
                [ChinaSupplierCode] nvarchar(50) NULL,
                [IsDeleted] bit NOT NULL
            );
            CREATE TABLE [dbo].[ProductStoreDailySalesStatistic] (
                [Date] datetime2(7) NOT NULL,
                [BranchCode] nvarchar(50) NOT NULL,
                [SupplierCode] nvarchar(50) NOT NULL,
                [ProductCode] nvarchar(50) NOT NULL,
                [ProductName] nvarchar(255) NULL,
                [Barcode] nvarchar(100) NULL,
                [TotalQuantity] int NOT NULL,
                [TotalAmount] decimal(18,2) NOT NULL,
                [OrderCount] int NOT NULL,
                [TotalCost] decimal(18,2) NULL,
                [GrossProfit] decimal(18,2) NULL,
                [CostSource] nvarchar(50) NOT NULL,
                [UpdateTime] datetime2(7) NOT NULL,
                CONSTRAINT [PK_ProductStoreDailySalesStatistic] PRIMARY KEY ([Date], [BranchCode], [SupplierCode], [ProductCode])
            );
            CREATE TABLE [dbo].[SalesStatisticRefreshState] (
                [StatisticType] nvarchar(80) NOT NULL,
                [Date] datetime2(7) NOT NULL,
                [Status] nvarchar(20) NOT NULL,
                [LastAggregatedAtUtc] datetime2(7) NULL,
                [CompletedAtUtc] datetime2(7) NULL,
                [SourceProductVersion] nvarchar(64) NULL,
                [JobId] uniqueidentifier NULL,
                CONSTRAINT [PK_SalesStatisticRefreshState] PRIMARY KEY ([StatisticType], [Date])
            );
            """;

        private static ConnectionConfig CreateConnectionConfig(string connectionString) => new()
        {
            ConnectionString = connectionString, DbType = DbType.SqlServer,
            IsAutoCloseConnection = true, InitKeyType = InitKeyType.Attribute,
        };

        private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
        {
            var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
            typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
            return context;
        }

        private static POSMSqlSugarContext CreatePosmSqlSugarContext(ISqlSugarClient db)
        {
            var context = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(POSMSqlSugarContext));
            typeof(POSMSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
            return context;
        }

        private static async Task ExecuteNonQueryAsync(string connectionString, string sql, params (string Name, object? Value)[] parameters)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }

        private static string BuildConnectionString(string connectionString, string databaseName)
        {
            var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = databaseName };
            return builder.ConnectionString;
        }

        private static void EnsureLoopbackSqlServer(string connectionString)
        {
            var dataSource = new SqlConnectionStringBuilder(connectionString).DataSource.Trim();
            if (dataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) dataSource = dataSource[4..];
            var parts = dataSource.Split(',', 2, StringSplitOptions.TrimEntries);
            var host = parts[0].Trim().Trim('[', ']');
            if (!host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                && !host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{SqlServerTestConnectionEnvVar} 必须指向 localhost 或 127.0.0.1。");
            if (parts.Length == 2 && (!int.TryParse(parts[1], out var port) || port is < 1 or > 65535))
                throw new InvalidOperationException($"{SqlServerTestConnectionEnvVar} 的 SQL Server 端口无效。");
        }

        private static async Task DropDatabaseAsync(string masterConnectionString, string databaseName)
        {
            var quotedName = QuoteSqlServerName(databaseName);
            await ExecuteNonQueryAsync(masterConnectionString, $"""
                IF DB_ID(N'{databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE {quotedName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE {quotedName};
                END;
                """);
        }

        private static string QuoteSqlServerName(string name) => $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";
    }
}
