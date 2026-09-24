using BlazorApp.Api.Features.PosmSalesOrders;
using BlazorApp.Shared.DTOs;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class PosmSalesOrderMobileRulesTests
{
    [Fact]
    public void TryResolveRange_区间含首尾不超过30天才放行()
    {
        Assert.True(
            PosmSalesOrderMobileRules.TryResolveRange("2026-09-01", "2026-09-30", out var range, out _, out _)
        );
        Assert.Equal(30, PosmSalesOrderMobileRules.CountDays(range.StartDate, range.EndDate));

        Assert.False(
            PosmSalesOrderMobileRules.TryResolveRange("2026-09-01", "2026-10-01", out _, out var error, out var code)
        );
        Assert.Equal("DATE_RANGE_TOO_LONG", code);
        Assert.Contains("30", error);
    }

    [Theory]
    [InlineData(null, "2026-09-01")]
    [InlineData("2026-09-01", "")]
    [InlineData("2026/09/01", "2026-09-02")]
    [InlineData("2026-02-30", "2026-03-01")]
    [InlineData("2026-09-05", "2026-09-01")]
    public void TryResolveRange_缺失格式错误或倒序一律拒绝(string? start, string? end)
    {
        Assert.False(PosmSalesOrderMobileRules.TryResolveRange(start, end, out _, out _, out var code));
        Assert.Equal("INVALID_DATE_RANGE", code);
    }

    [Fact]
    public void TryResolveRange_同一天算一天()
    {
        Assert.True(
            PosmSalesOrderMobileRules.TryResolveRange("2026-09-17", "2026-09-17", out var range, out _, out _)
        );
        Assert.Equal(1, PosmSalesOrderMobileRules.CountDays(range.StartDate, range.EndDate));
    }

    [Theory]
    [InlineData(null, "desc")]
    [InlineData("ASC", "asc")]
    [InlineData(" desc ", "desc")]
    [InlineData("orderGuid", "desc")]
    public void NormalizeSortDirection_只认升降序其余回退最新在前(string? raw, string expected)
    {
        Assert.Equal(expected, PosmSalesOrderMobileRules.NormalizeSortDirection(raw));
    }

    [Fact]
    public void NormalizeOrderType_All等同不过滤()
    {
        Assert.Null(PosmSalesOrderMobileRules.NormalizeOrderType(null));
        Assert.Null(PosmSalesOrderMobileRules.NormalizeOrderType(OrderType.All));
        Assert.Equal(OrderType.Paid, PosmSalesOrderMobileRules.NormalizeOrderType(OrderType.Paid));
    }

    [Fact]
    public void NormalizePageSize_默认20且封顶100()
    {
        Assert.Equal(20, PosmSalesOrderMobileRules.NormalizePageSize(0));
        Assert.Equal(20, PosmSalesOrderMobileRules.NormalizePageSize(-5));
        Assert.Equal(50, PosmSalesOrderMobileRules.NormalizePageSize(50));
        Assert.Equal(100, PosmSalesOrderMobileRules.NormalizePageSize(500));
    }

    [Fact]
    public void ResolveEffectiveBranchCodes_全分店账号直接采用请求值()
    {
        Assert.Null(PosmSalesOrderMobileRules.ResolveEffectiveBranchCodes(null, null));
        Assert.Null(PosmSalesOrderMobileRules.ResolveEffectiveBranchCodes(new[] { " ", "" }, null));
        Assert.Equal(
            new[] { "S1", "S2" },
            PosmSalesOrderMobileRules.ResolveEffectiveBranchCodes(new[] { " S1", "S2", "s1" }, null)
        );
    }

    [Fact]
    public void ResolveEffectiveBranchCodes_授权账号只能收窄不能放大()
    {
        var authorized = new List<string> { "S1", "S2" };
        Assert.Same(
            authorized,
            PosmSalesOrderMobileRules.ResolveEffectiveBranchCodes(null, authorized)
        );
        Assert.Equal(
            new[] { "S2" },
            PosmSalesOrderMobileRules.ResolveEffectiveBranchCodes(new[] { "s2", "S9" }, authorized)
        );
        // 请求的分店全部越权时返回空列表，调用方据此返回空结果而不是回退到授权范围。
        Assert.Empty(PosmSalesOrderMobileRules.ResolveEffectiveBranchCodes(new[] { "S9" }, authorized)!);
    }
}
