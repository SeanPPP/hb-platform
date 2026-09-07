using BlazorApp.Api.Services.React;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class HqPurchasePriceSyncGuardTests
{
    [Fact]
    public void PreservePositive_已有正数遇到HQ零或空时保留原值()
    {
        Assert.Equal(9m, HqPurchasePriceSyncGuard.PreservePositive(9m, 0m));
        Assert.Equal(9m, HqPurchasePriceSyncGuard.PreservePositive(9m, null));
    }

    [Fact]
    public void PreservePositive_HQ正数正常覆盖且无原值时保留HQ空值()
    {
        Assert.Equal(3m, HqPurchasePriceSyncGuard.PreservePositive(9m, 3m));
        Assert.Equal(0m, HqPurchasePriceSyncGuard.PreservePositive(null, 0m));
        Assert.Null(HqPurchasePriceSyncGuard.PreservePositive(null, null));
    }

    [Fact]
    public void PreservePositiveForOrdinaryProduct_只保护普通商品并放行套装类型变化()
    {
        Assert.Equal(
            9m,
            HqPurchasePriceSyncGuard.PreservePositiveForOrdinaryProduct(9m, 0m, 0, 0)
        );
        Assert.Equal(
            9m,
            HqPurchasePriceSyncGuard.PreservePositiveForOrdinaryProduct(9m, 0m, null, 0)
        );
        Assert.Equal(
            0m,
            HqPurchasePriceSyncGuard.PreservePositiveForOrdinaryProduct(9m, 0m, 1, 1)
        );
        Assert.Equal(
            0m,
            HqPurchasePriceSyncGuard.PreservePositiveForOrdinaryProduct(9m, 0m, 0, 1)
        );
    }
}
