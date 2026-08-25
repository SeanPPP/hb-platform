using BlazorApp.Api.Services.React;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SetChildPurchasePriceAllocatorTests
{
    private sealed record Item(string Code, decimal RetailPrice, int Quantity = 1);

    [Fact]
    public void AllocateByRetailRatio_按业务键稳定分摊并由最后一项吸收尾差()
    {
        var items = new[]
        {
            new Item("C", 1m),
            new Item("A", 1m),
            new Item("B", 1m),
        };

        var result = SetChildPurchasePriceAllocator.AllocateByRetailRatio(
            items,
            10m,
            item => item.Code,
            item => item.RetailPrice
        );

        Assert.Equal(3.33m, result["A"]);
        Assert.Equal(3.33m, result["B"]);
        Assert.Equal(3.34m, result["C"]);
        Assert.Equal(10m, result.Values.Sum());
    }

    [Fact]
    public void AllocateByRetailRatio_只按零售价比例且忽略套装数量()
    {
        var items = new[]
        {
            new Item("A", 20m, Quantity: 9),
            new Item("B", 30m, Quantity: 1),
        };

        var result = SetChildPurchasePriceAllocator.AllocateByRetailRatio(
            items,
            10m,
            item => item.Code,
            item => item.RetailPrice
        );

        Assert.Equal(4m, result["A"]);
        Assert.Equal(6m, result["B"]);
    }

    [Fact]
    public void AllocateByRetailRatio_极小总成本和多个子项不会产生负数尾差()
    {
        var items = new[]
        {
            new Item("D", 1m),
            new Item("C", 1m),
            new Item("B", 1m),
            new Item("A", 1m),
        };

        var result = SetChildPurchasePriceAllocator.AllocateByRetailRatio(
            items,
            0.02m,
            item => item.Code,
            item => item.RetailPrice
        );

        Assert.Equal(0.02m, result.Values.Sum());
        Assert.All(result.Values, value => Assert.True(value >= 0m));
        Assert.Equal(0m, result["D"]);
    }
}
