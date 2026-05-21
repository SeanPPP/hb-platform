using Hbpos.Api.Services;
using Hbpos.Contracts.Catalog;

namespace Hbpos.Api.Tests;

public sealed class PriceIndexBuilderTests
{
    private readonly PriceIndexBuilder _builder = new();

    [Fact]
    public void Build_UsesStoreRetailPriceBeforeProductBasePrice()
    {
        var items = _builder.Build("S01", new PriceIndexInput(
            null,
            [new ProductPriceRecord("P01", "Apple", "ITEM01", "BAR01", 10m, null)],
            [new StoreRetailPriceRecord("P01", 8.5m, null)],
            [],
            [],
            []));

        var barcodeItem = Assert.Single(items, x => x.LookupCode == "BAR01");
        Assert.Equal(8.5m, barcodeItem.RetailPrice);
        Assert.Equal(PriceSourceKind.StoreRetailPrice, barcodeItem.PriceSource);
    }

    [Fact]
    public void Build_CarriesProductImageFromProductRecord()
    {
        var items = _builder.Build("S01", new PriceIndexInput(
            null,
            [new ProductPriceRecord("P01", "Apple", "ITEM01", "BAR01", 10m, null, "https://images.example/P01.jpg")],
            [],
            [],
            [],
            []));

        var item = Assert.Single(items, x => x.LookupCode == "BAR01");
        Assert.Equal("https://images.example/P01.jpg", item.ProductImage);
    }

    [Fact]
    public void Build_FallsBackToProductBasePriceWhenStoreRetailPriceMissing()
    {
        var items = _builder.Build("S01", new PriceIndexInput(
            null,
            [new ProductPriceRecord("P01", "Apple", null, "BAR01", 10m, null)],
            [],
            [],
            [],
            []));

        var item = Assert.Single(items);
        Assert.Equal(10m, item.RetailPrice);
        Assert.Equal(PriceSourceKind.ProductBase, item.PriceSource);
    }

    [Fact]
    public void Build_UsesClearanceBarcodePrice()
    {
        var items = _builder.Build("S01", new PriceIndexInput(
            null,
            [new ProductPriceRecord("P01", "Apple", null, "BAR01", 10m, null)],
            [],
            [],
            [new StoreClearancePriceRecord("P01", "CLR01", 3m, null)],
            []));

        var clearanceItem = Assert.Single(items, x => x.LookupCode == "CLR01");
        Assert.Equal(3m, clearanceItem.RetailPrice);
        Assert.Equal(PriceSourceKind.StoreClearancePrice, clearanceItem.PriceSource);
    }

    [Fact]
    public void Build_UsesStoreMultiCodePriceBeforeSetRetailPrice()
    {
        var items = _builder.Build("S01", new PriceIndexInput(
            null,
            [new ProductPriceRecord("P01", "Apple Set", null, null, 10m, null)],
            [],
            [new StoreMultiCodeProductRecord("P01", "SET-P01", null, 7m, null)],
            [],
            [new ProductSetCodeRecord("P01", "SET-P01", "SETBAR01", 12m, null)]));

        var setItem = Assert.Single(items);
        Assert.Equal(7m, setItem.RetailPrice);
        Assert.Equal(PriceSourceKind.StoreMultiCodeProduct, setItem.PriceSource);
    }

    [Fact]
    public void Build_FallsBackToSetRetailPriceWhenStoreMultiCodePriceMissing()
    {
        var items = _builder.Build("S01", new PriceIndexInput(
            null,
            [new ProductPriceRecord("P01", "Apple Set", null, null, 10m, null)],
            [],
            [],
            [],
            [new ProductSetCodeRecord("P01", "SET-P01", "SETBAR01", 12m, null)]));

        var setItem = Assert.Single(items);
        Assert.Equal(12m, setItem.RetailPrice);
        Assert.Equal(PriceSourceKind.ProductSetCode, setItem.PriceSource);
    }

    [Fact]
    public void Build_UsesMultiBarcodePrice()
    {
        var items = _builder.Build("S01", new PriceIndexInput(
            null,
            [new ProductPriceRecord("P01", "Apple", null, null, 10m, null)],
            [],
            [new StoreMultiCodeProductRecord("P01", "M01", "MULTI01", 6m, null)],
            [],
            []));

        var item = Assert.Single(items);
        Assert.Equal(6m, item.RetailPrice);
        Assert.Equal(PriceSourceKind.StoreMultiCodeProduct, item.PriceSource);
    }

    [Fact]
    public void Build_NormalizesLookupCodesBeforeDeduplicating()
    {
        var items = _builder.Build("S01", new PriceIndexInput(
            null,
            [new ProductPriceRecord("P01", "Apple", "ABC01", " abc01 ", 10m, null)],
            [],
            [],
            [],
            []));

        var item = Assert.Single(items);
        Assert.Equal("abc01", item.LookupCode);
    }
}
