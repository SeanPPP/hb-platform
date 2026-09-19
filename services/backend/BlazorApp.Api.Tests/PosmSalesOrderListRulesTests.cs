using BlazorApp.Api.Features.PosmSalesOrders;
using BlazorApp.Shared.DTOs;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class PosmSalesOrderListRulesTests
{
    private static PosmSalesOrderQueryParams Range(int days, string? branchCode = null) =>
        new()
        {
            StartDate = new DateTime(2026, 6, 1),
            EndDate = new DateTime(2026, 6, 1).AddDays(days - 1),
            BranchCode = branchCode,
        };

    [Fact]
    public void Web查询必须提供有序的日期区间()
    {
        Assert.False(PosmSalesOrderListRules.TryValidateWebQuery(new PosmSalesOrderQueryParams(), out _, out var missing));
        Assert.Equal(PosmSalesOrderListRules.ErrorDateRangeRequired, missing);

        var reversed = new PosmSalesOrderQueryParams { StartDate = new DateTime(2026, 6, 2), EndDate = new DateTime(2026, 6, 1) };
        Assert.False(PosmSalesOrderListRules.TryValidateWebQuery(reversed, out _, out var reversedCode));
        Assert.Equal(PosmSalesOrderListRules.ErrorDateRangeRequired, reversedCode);
    }

    [Fact]
    public void 日期区间最长九十二天含首尾()
    {
        Assert.True(PosmSalesOrderListRules.TryValidateWebQuery(Range(PosmSalesOrderListRules.MaxRangeDays), out _, out _));

        Assert.False(PosmSalesOrderListRules.TryValidateWebQuery(Range(PosmSalesOrderListRules.MaxRangeDays + 1), out var error, out var code));
        Assert.Equal(PosmSalesOrderListRules.ErrorDateRangeTooLong, code);
        Assert.Contains("92", error);
    }

    [Theory]
    [InlineData("skuCountMin")]
    [InlineData("quantityMax")]
    [InlineData("sortSku")]
    [InlineData("sortQuantity")]
    public void 件数种数条件在全部分店时最长七天(string condition)
    {
        PosmSalesOrderQueryParams Build(int days, string? branch = null, List<string>? branches = null)
        {
            var query = Range(days, branch);
            query.BranchCodes = branches;
            switch (condition)
            {
                case "skuCountMin": query.SkuCountMin = 2; break;
                case "quantityMax": query.QuantityMax = 3; break;
                case "sortSku": query.SortField = "skuCount"; query.SortDirection = "desc"; break;
                case "sortQuantity": query.SortField = " Quantity "; query.SortDirection = "asc"; break;
            }
            return query;
        }

        Assert.True(PosmSalesOrderListRules.TryValidateWebQuery(Build(PosmSalesOrderListRules.DetailAggregateAllStoresMaxDays), out _, out _));
        Assert.False(PosmSalesOrderListRules.TryValidateWebQuery(Build(PosmSalesOrderListRules.DetailAggregateAllStoresMaxDays + 1), out _, out var code));
        Assert.Equal(PosmSalesOrderListRules.ErrorDetailFilterRangeTooLong, code);
        // 指定分店、或授权范围只有一家分店时沿用 92 天上限。
        Assert.True(PosmSalesOrderListRules.TryValidateWebQuery(Build(PosmSalesOrderListRules.MaxRangeDays, branch: "1005"), out _, out _));
        Assert.True(PosmSalesOrderListRules.TryValidateWebQuery(Build(PosmSalesOrderListRules.MaxRangeDays, branches: new() { "1005", " 1005 " }), out _, out _));
        Assert.False(PosmSalesOrderListRules.TryValidateWebQuery(Build(30, branches: new() { "1005", "1033" }), out _, out _));
    }

    [Fact]
    public void 普通排序与金额条件不触发明细汇总约束()
    {
        var query = Range(PosmSalesOrderListRules.MaxRangeDays);
        query.SortField = "actualPay";
        query.SortDirection = "desc";
        query.ActualPayMin = 20m;
        query.ItemCountMin = 1;

        Assert.False(PosmSalesOrderListRules.NeedsDetailAggregates(query));
        Assert.True(PosmSalesOrderListRules.TryValidateWebQuery(query, out _, out _));
    }

    [Theory]
    [InlineData("31C1BA", true)]
    [InlineData(" 1b4d21 ", true)]
    [InlineData("01A0B860-80A2", true)]
    [InlineData("9527902201201", true)]
    [InlineData("C1B", false)]
    [InlineData("HB022-249", false)]
    [InlineData("squishy", false)]
    [InlineData("POS_1005_1231", false)]
    public void 只有像订单号片段的关键词才按订单号匹配(string keyword, bool expected)
    {
        Assert.Equal(expected, PosmSalesOrderListRules.IsOrderNumberFragment(keyword));
    }
}
