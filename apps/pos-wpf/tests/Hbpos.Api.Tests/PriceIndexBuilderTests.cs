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
            [new StoreRetailPriceRecord("P01", 8.5m, null, ReferenceCode: "SRP-UUID-01")],
            [],
            [],
            []));

        var barcodeItem = Assert.Single(items, x => x.LookupCode == "BAR01");
        Assert.Equal(8.5m, barcodeItem.RetailPrice);
        Assert.Equal(PriceSourceKind.StoreRetailPrice, barcodeItem.PriceSource);
        Assert.Equal("SRP-UUID-01", barcodeItem.ReferenceCode);
    }

    [Theory]
    [InlineData(20, 0.2)]
    [InlineData(0.2, 0.2)]
    [InlineData(100, 1)]
    public void Build_NormalizesStoreRetailDiscountRate(decimal sourceDiscountRate, decimal expectedDiscountRate)
    {
        var items = _builder.Build("S01", new PriceIndexInput(
            null,
            [new ProductPriceRecord("P01", "Apple", "ITEM01", "BAR01", 10m, null)],
            [new StoreRetailPriceRecord("P01", 8.5m, null, ReferenceCode: "SRP-UUID-01", DiscountRate: sourceDiscountRate)],
            [],
            [],
            []));

        var barcodeItem = Assert.Single(items, x => x.LookupCode == "BAR01");
        Assert.Equal(expectedDiscountRate, barcodeItem.DiscountRate);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Build_IgnoresInvalidStoreRetailDiscountRate(decimal sourceDiscountRate)
    {
        var items = _builder.Build("S01", new PriceIndexInput(
            null,
            [new ProductPriceRecord("P01", "Apple", "ITEM01", "BAR01", 10m, null)],
            [new StoreRetailPriceRecord("P01", 8.5m, null, ReferenceCode: "SRP-UUID-01", DiscountRate: sourceDiscountRate)],
            [],
            [],
            []));

        var barcodeItem = Assert.Single(items, x => x.LookupCode == "BAR01");
        Assert.Null(barcodeItem.DiscountRate);
    }

    [Theory]
    [InlineData(20, 0.2)]
    [InlineData(0.2, 0.2)]
    public void Build_NormalizesStoreMultiCodeDiscountRate(decimal sourceDiscountRate, decimal expectedDiscountRate)
    {
        var items = _builder.Build("S01", new PriceIndexInput(
            null,
            [new ProductPriceRecord("P01", "Apple", null, null, 10m, null)],
            [],
            [new StoreMultiCodeProductRecord("P01", "M01", "MULTI01", 6m, null, ReferenceCode: "SMCP-UUID-01", DiscountRate: sourceDiscountRate)],
            [],
            []));

        var item = Assert.Single(items);
        Assert.Equal(expectedDiscountRate, item.DiscountRate);
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
    public void Build_CarriesSpecialProductFlagFromStoreRetailPrice()
    {
        var items = _builder.Build("S01", new PriceIndexInput(
            null,
            [new ProductPriceRecord("P01", "Apple", "ITEM01", "BAR01", 10m, null)],
            [new StoreRetailPriceRecord("P01", 8.5m, null, IsSpecialProduct: true)],
            [],
            [],
            []));

        Assert.All(items, item => Assert.True(item.IsSpecialProduct));
    }

    [Fact]
    public void Build_FallsBackToProductBasePriceWhenStoreRetailPriceMissing()
    {
        var items = _builder.Build("S01", new PriceIndexInput(
            null,
            [new ProductPriceRecord("P01", "Apple", null, "BAR01", 10m, null, ReferenceCode: "PRODUCT-UUID-01")],
            [],
            [],
            [],
            []));

        var item = Assert.Single(items);
        Assert.Equal(10m, item.RetailPrice);
        Assert.Equal(PriceSourceKind.ProductBase, item.PriceSource);
        Assert.Equal("PRODUCT-UUID-01", item.ReferenceCode);
    }

    [Fact]
    public void Build_UsesClearanceBarcodePrice()
    {
        var items = _builder.Build("S01", new PriceIndexInput(
            null,
            [new ProductPriceRecord("P01", "Apple", null, "BAR01", 10m, null)],
            [],
            [],
            [new StoreClearancePriceRecord("P01", "CLR01", 3m, null, ReferenceCode: "CLR-UUID-01")],
            []));

        var clearanceItem = Assert.Single(items, x => x.LookupCode == "CLR01");
        Assert.Equal(3m, clearanceItem.RetailPrice);
        Assert.Equal(PriceSourceKind.StoreClearancePrice, clearanceItem.PriceSource);
        Assert.Equal("CLR-UUID-01", clearanceItem.ReferenceCode);
    }

    [Fact]
    public void Build_UsesStoreMultiCodePriceBeforeSetRetailPrice()
    {
        var items = _builder.Build("S01", new PriceIndexInput(
            null,
            [new ProductPriceRecord("P01", "Apple Set", null, null, 10m, null)],
            [],
            [new StoreMultiCodeProductRecord("P01", "SET-P01", null, 7m, null, ReferenceCode: "SMCP-UUID-01")],
            [],
            [new ProductSetCodeRecord("P01", "SET-P01", "SETBAR01", 12m, null, ReferenceCode: "SET-UUID-01")]));

        var setItem = Assert.Single(items);
        Assert.Equal(7m, setItem.RetailPrice);
        Assert.Equal(PriceSourceKind.StoreMultiCodeProduct, setItem.PriceSource);
        Assert.Equal("SMCP-UUID-01", setItem.ReferenceCode);
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
            [new ProductSetCodeRecord("P01", "SET-P01", "SETBAR01", 12m, null, ReferenceCode: "SET-UUID-01")]));

        var setItem = Assert.Single(items);
        Assert.Equal(12m, setItem.RetailPrice);
        Assert.Equal(PriceSourceKind.ProductSetCode, setItem.PriceSource);
        Assert.Equal("SET-UUID-01", setItem.ReferenceCode);
    }

    [Fact]
    public void Build_UsesMultiBarcodePrice()
    {
        var items = _builder.Build("S01", new PriceIndexInput(
            null,
            [new ProductPriceRecord("P01", "Apple", null, null, 10m, null)],
            [],
            [new StoreMultiCodeProductRecord("P01", "M01", "MULTI01", 6m, null, ReferenceCode: "SMCP-UUID-01")],
            [],
            []));

        var item = Assert.Single(items);
        Assert.Equal(6m, item.RetailPrice);
        Assert.Equal(PriceSourceKind.StoreMultiCodeProduct, item.PriceSource);
        Assert.Equal("SMCP-UUID-01", item.ReferenceCode);
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
