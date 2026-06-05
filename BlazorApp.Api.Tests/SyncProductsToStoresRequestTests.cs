using BlazorApp.Shared.DTOs;
using Xunit;

namespace BlazorApp.Api.Tests;

public class SyncProductsToStoresRequestTests
{
    [Fact]
    public void NormalizeFieldSelection_前端只传Fields时只同步勾选字段()
    {
        var request = new SyncProductsToStoresRequest
        {
            ProductCodes = ["P001"],
            StoreCodes = ["S01"],
            Fields = ["retailPrice"],
        };

        request.NormalizeFieldSelection();

        Assert.False(request.SyncPurchasePrice);
        Assert.True(request.SyncRetailPrice);
        Assert.False(request.SyncIsAutoPricing);
        Assert.False(request.SyncIsSpecialProduct);
        Assert.False(request.SyncDiscountRate);
    }

    [Fact]
    public void NormalizeFieldSelection_Fields不启用未暴露的折扣率字段()
    {
        var request = new SyncProductsToStoresRequest
        {
            ProductCodes = ["P001"],
            StoreCodes = ["S01"],
            Fields = ["discountRate"],
        };

        request.NormalizeFieldSelection();

        Assert.False(request.SyncPurchasePrice);
        Assert.False(request.SyncRetailPrice);
        Assert.False(request.SyncIsAutoPricing);
        Assert.False(request.SyncIsSpecialProduct);
        Assert.False(request.SyncDiscountRate);
    }
}
