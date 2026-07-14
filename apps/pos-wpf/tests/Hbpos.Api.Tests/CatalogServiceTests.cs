using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Shared.Models;
using Hbpos.Api.Data;
using Hbpos.Api.Services;
using Hbpos.Contracts.Catalog;
using SqlSugar;

namespace Hbpos.Api.Tests;

public sealed class CatalogServiceTests
{
    [Fact]
    public async Task LookupSellableItemAsync_creates_store_retail_price_from_product_base_price()
    {
        await using var fixture = await CatalogSqliteFixture.CreateAsync();
        await fixture.SeedStoreAsync("S01");
        await fixture.SeedProductAsync(new Product
        {
            UUID = "PRODUCT-UUID-01",
            ProductCode = "P01",
            ProductName = "Lookup Apple",
            ItemNumber = "ITEM01",
            Barcode = "BAR01",
            LocalSupplierCode = "SUP01",
            PurchasePrice = 4.25m,
            RetailPrice = 10.5m,
            IsAutoPricing = true,
            IsSpecialProduct = true,
            IsActive = true,
            IsDeleted = false
        });
        var service = new CatalogService(fixture.DbContext, new PriceIndexBuilder(), new CatalogIndexCache());

        var response = await service.LookupSellableItemAsync("S01", "BAR01", null, CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response!.Found);
        Assert.NotNull(response.Item);
        Assert.Equal(PriceSourceKind.StoreRetailPrice, response.Item!.PriceSource);
        Assert.Equal(10.5m, response.Item.RetailPrice);
        Assert.NotEqual("PRODUCT-UUID-01", response.Item.ReferenceCode);
        var storePrice = Assert.Single(await fixture.LoadStoreRetailPricesAsync());
        Assert.Equal("S01", storePrice.StoreCode);
        Assert.Equal("P01", storePrice.ProductCode);
        Assert.Equal("SUP01", storePrice.SupplierCode);
        Assert.Equal(4.25m, storePrice.PurchasePrice);
        Assert.Equal(10.5m, storePrice.StoreRetailPriceValue);
        Assert.True(storePrice.IsAutoPricing);
        Assert.True(storePrice.IsSpecialProduct);
        Assert.Equal("pos-device", storePrice.CreatedBy);
        Assert.Equal("pos-device", storePrice.UpdatedBy);
    }

    [Fact]
    public async Task LookupSellableItemAsync_creates_zero_store_retail_price_when_product_base_price_is_null()
    {
        await using var fixture = await CatalogSqliteFixture.CreateAsync();
        await fixture.SeedStoreAsync("S01");
        await fixture.SeedProductAsync(new Product
        {
            UUID = "PRODUCT-UUID-05",
            ProductCode = "P05",
            ProductName = "Lookup Zero Price",
            Barcode = "BAR05",
            RetailPrice = null,
            IsActive = true,
            IsDeleted = false
        });
        var service = new CatalogService(fixture.DbContext, new PriceIndexBuilder(), new CatalogIndexCache());

        var response = await service.LookupSellableItemAsync("S01", "BAR05", null, CancellationToken.None);

        Assert.True(response?.Found);
        Assert.Equal(PriceSourceKind.StoreRetailPrice, response!.Item!.PriceSource);
        Assert.Equal(0m, response.Item.RetailPrice);
        var storePrice = Assert.Single(await fixture.LoadStoreRetailPricesAsync());
        Assert.Equal(0m, storePrice.StoreRetailPriceValue);
        Assert.Equal("pos-device", storePrice.CreatedBy);
    }

    [Fact]
    public async Task LookupSellableItemAsync_reuses_existing_store_retail_price_created_by_previous_lookup()
    {
        await using var fixture = await CatalogSqliteFixture.CreateAsync();
        await fixture.SeedStoreAsync("S01");
        await fixture.SeedProductAsync(new Product
        {
            UUID = "PRODUCT-UUID-02",
            ProductCode = "P02",
            ProductName = "Lookup Banana",
            Barcode = "BAR02",
            RetailPrice = 7.25m,
            IsActive = true,
            IsDeleted = false
        });
        var service = new CatalogService(fixture.DbContext, new PriceIndexBuilder(), new CatalogIndexCache());

        var first = await service.LookupSellableItemAsync("S01", "BAR02", null, CancellationToken.None);
        var second = await service.LookupSellableItemAsync("S01", "BAR02", null, CancellationToken.None);

        Assert.True(first?.Found);
        Assert.True(second?.Found);
        Assert.Equal(PriceSourceKind.StoreRetailPrice, second!.Item!.PriceSource);
        Assert.Single(await fixture.LoadStoreRetailPricesAsync());
    }

    [Fact]
    public async Task LookupSellableItemAsync_concurrent_product_base_lookup_creates_single_store_retail_price()
    {
        await using var fixture = await CatalogSqliteFixture.CreateAsync();
        await fixture.SeedStoreAsync("S01");
        await fixture.SeedProductAsync(new Product
        {
            UUID = "PRODUCT-UUID-04",
            ProductCode = "P04",
            ProductName = "Lookup Mango",
            Barcode = "BAR04",
            RetailPrice = 12.75m,
            IsActive = true,
            IsDeleted = false
        });
        var service = new CatalogService(fixture.DbContext, new PriceIndexBuilder(), new CatalogIndexCache());

        var responses = await Task.WhenAll(
            service.LookupSellableItemAsync("S01", "BAR04", null, CancellationToken.None),
            service.LookupSellableItemAsync("S01", "BAR04", null, CancellationToken.None));

        Assert.All(responses, response =>
        {
            Assert.True(response?.Found);
            Assert.Equal(PriceSourceKind.StoreRetailPrice, response!.Item!.PriceSource);
            Assert.Equal(12.75m, response.Item.RetailPrice);
        });
        Assert.Single(await fixture.LoadStoreRetailPricesAsync());
    }

    [Fact]
    public async Task LookupSellableItemAsync_refreshes_stale_product_base_cache_when_store_price_already_exists()
    {
        await using var fixture = await CatalogSqliteFixture.CreateAsync();
        await fixture.SeedStoreAsync("S01");
        await fixture.SeedProductAsync(new Product
        {
            UUID = "PRODUCT-UUID-03",
            ProductCode = "P03",
            ProductName = "Lookup Pear",
            Barcode = "BAR03",
            RetailPrice = 8.25m,
            IsActive = true,
            IsDeleted = false
        });
        var service = new CatalogService(fixture.DbContext, new PriceIndexBuilder(), new CatalogIndexCache());
        var cachedBase = await service.GetSellableItemsAsync("S01", since: null, CancellationToken.None);
        Assert.Equal(PriceSourceKind.ProductBase, Assert.Single(cachedBase!.Items).PriceSource);
        await fixture.SeedStoreRetailPriceAsync("S01", "P03", 6.75m, "STORE-PRICE-03");

        var response = await service.LookupSellableItemAsync("S01", "BAR03", null, CancellationToken.None);

        Assert.True(response?.Found);
        Assert.Equal(PriceSourceKind.StoreRetailPrice, response!.Item!.PriceSource);
        Assert.Equal(6.75m, response.Item.RetailPrice);
        Assert.Equal("STORE-PRICE-03", response.Item.ReferenceCode);
        Assert.Single(await fixture.LoadStoreRetailPricesAsync());
    }

    [Fact]
    public async Task LookupSellableItemAsync_product_barcode_uses_narrow_index_input_and_creates_store_retail_price()
    {
        await using var fixture = await CatalogSqliteFixture.CreateAsync();
        await fixture.SeedStoreAsync("S01");
        await fixture.SeedProductAsync(new Product
        {
            UUID = "PRODUCT-UUID-NARROW",
            ProductCode = "P-NARROW",
            ProductName = "Lookup Narrow",
            Barcode = "NARROW-BAR",
            RetailPrice = 10.25m,
            IsActive = true,
            IsDeleted = false
        });
        await fixture.SeedProductAsync(new Product
        {
            UUID = "PRODUCT-UUID-NOISE",
            ProductCode = "P-NOISE",
            ProductName = "Lookup Noise",
            Barcode = "NOISE-BAR",
            RetailPrice = 99.99m,
            IsActive = true,
            IsDeleted = false
        });
        fixture.ExecutedCommands.Clear();
        var builder = new RecordingPriceIndexBuilder();
        var service = new CatalogService(fixture.DbContext, builder, new CatalogIndexCache());

        var response = await service.LookupSellableItemAsync("S01", "NARROW-BAR", null, CancellationToken.None);

        Assert.True(response?.Found);
        Assert.Equal("P-NARROW", response!.Item!.ProductCode);
        Assert.Equal(PriceSourceKind.StoreRetailPrice, response.Item.PriceSource);
        Assert.All(builder.Inputs, input =>
            Assert.DoesNotContain(input.Products, product => product.ProductCode == "P-NOISE"));
        var storePrice = Assert.Single(await fixture.LoadStoreRetailPricesAsync());
        Assert.Equal("P-NARROW", storePrice.ProductCode);
        AssertSingleCandidateUnion(fixture, "NARROW-BAR");
    }

    [Fact]
    public async Task LookupSellableItemAsync_matches_item_number_with_same_product_semantics()
    {
        await using var fixture = await CatalogSqliteFixture.CreateAsync();
        await fixture.SeedStoreAsync("S01");
        await fixture.SeedProductAsync(new Product
        {
            UUID = "PRODUCT-UUID-ITEM",
            ProductCode = "P-ITEM",
            ProductName = "Lookup Item",
            ItemNumber = "ITEM-NARROW",
            Barcode = "BAR-NARROW",
            RetailPrice = 4.5m,
            IsActive = true,
            IsDeleted = false
        });
        await fixture.SeedStoreRetailPriceAsync("S01", "P-ITEM", 3.75m, "PRICE-ITEM");
        var service = new CatalogService(fixture.DbContext, new PriceIndexBuilder(), new CatalogIndexCache());

        var response = await service.LookupSellableItemAsync("S01", "ITEM-NARROW", null, CancellationToken.None);

        Assert.True(response?.Found);
        Assert.Equal("P-ITEM", response!.Item!.ProductCode);
        Assert.Equal("ITEM-NARROW", response.Item.LookupCode);
        Assert.Equal(3.75m, response.Item.RetailPrice);
        Assert.Equal(PriceSourceKind.StoreRetailPrice, response.Item.PriceSource);
    }

    [Fact]
    public async Task LookupSellableItemAsync_multi_barcode_returns_store_multi_code_price()
    {
        await using var fixture = await CatalogSqliteFixture.CreateAsync();
        await fixture.SeedStoreAsync("S01");
        await fixture.SeedProductAsync(new Product
        {
            UUID = "PRODUCT-UUID-MULTI",
            ProductCode = "P-MULTI",
            ProductName = "Lookup Multi",
            Barcode = "BASE-MULTI",
            RetailPrice = 8m,
            IsActive = true,
            IsDeleted = false
        });
        await fixture.SeedStoreMultiCodeProductAsync("S01", "P-MULTI", "MC-MULTI", "MULTI-BAR", 6.25m, "MULTI-UUID");
        var service = new CatalogService(fixture.DbContext, new PriceIndexBuilder(), new CatalogIndexCache());

        var response = await service.LookupSellableItemAsync("S01", "MULTI-BAR", null, CancellationToken.None);

        Assert.True(response?.Found);
        Assert.Equal("P-MULTI", response!.Item!.ProductCode);
        Assert.Equal(6.25m, response.Item.RetailPrice);
        Assert.Equal(PriceSourceKind.StoreMultiCodeProduct, response.Item.PriceSource);
    }

    [Fact]
    public async Task LookupSellableItemAsync_clearance_barcode_wins_over_product_barcode()
    {
        await using var fixture = await CatalogSqliteFixture.CreateAsync();
        await fixture.SeedStoreAsync("S01");
        await fixture.SeedProductAsync(new Product
        {
            UUID = "PRODUCT-UUID-CLEARANCE",
            ProductCode = "P-CLEARANCE",
            ProductName = "Lookup Clearance",
            Barcode = "DUP-BAR",
            RetailPrice = 12m,
            IsActive = true,
            IsDeleted = false
        });
        await fixture.SeedStoreClearancePriceAsync("S01", "P-CLEARANCE", "DUP-BAR", 2.5m, "CLEARANCE-UUID");
        fixture.ExecutedCommands.Clear();
        var service = new CatalogService(fixture.DbContext, new PriceIndexBuilder(), new CatalogIndexCache());

        var response = await service.LookupSellableItemAsync("S01", "DUP-BAR", null, CancellationToken.None);

        Assert.True(response?.Found);
        Assert.Equal(2.5m, response!.Item!.RetailPrice);
        Assert.Equal(PriceSourceKind.StoreClearancePrice, response.Item.PriceSource);
        AssertSingleCandidateUnion(fixture, "DUP-BAR");
    }

    [Fact]
    public async Task LookupSellableItemAsync_multi_hit_uses_single_candidate_union()
    {
        await using var fixture = await CatalogSqliteFixture.CreateAsync();
        await fixture.SeedStoreAsync("S01");
        await fixture.SeedProductAsync(new Product
        {
            UUID = "PRODUCT-UUID-MULTI-SHORT-CIRCUIT",
            ProductCode = "P-MULTI-SHORT-CIRCUIT",
            ProductName = "Lookup Multi Short Circuit",
            Barcode = "BASE-MULTI-SHORT-CIRCUIT",
            RetailPrice = 8m,
            IsActive = true,
            IsDeleted = false
        });
        await fixture.SeedStoreMultiCodeProductAsync(
            "S01",
            "P-MULTI-SHORT-CIRCUIT",
            "MC-MULTI-SHORT-CIRCUIT",
            "MULTI-SHORT-CIRCUIT",
            6.25m,
            "MULTI-UUID-SHORT-CIRCUIT");
        fixture.ExecutedCommands.Clear();
        var service = new CatalogService(fixture.DbContext, new PriceIndexBuilder(), new CatalogIndexCache());

        var response = await service.LookupSellableItemAsync(
            "S01",
            "MULTI-SHORT-CIRCUIT",
            null,
            CancellationToken.None);

        Assert.True(response?.Found);
        Assert.Equal(PriceSourceKind.StoreMultiCodeProduct, response!.Item!.PriceSource);
        AssertSingleCandidateUnion(fixture, "MULTI-SHORT-CIRCUIT");
    }

    [Fact]
    public async Task LookupSellableItemAsync_set_barcode_uses_related_store_multi_code_price()
    {
        await using var fixture = await CatalogSqliteFixture.CreateAsync();
        await fixture.SeedStoreAsync("S01");
        await fixture.SeedProductAsync(new Product
        {
            UUID = "PRODUCT-UUID-SET",
            ProductCode = "P-SET",
            ProductName = "Lookup Set",
            Barcode = "BASE-SET",
            RetailPrice = 18m,
            IsActive = true,
            IsDeleted = false
        });
        await fixture.SeedStoreMultiCodeProductAsync("S01", "P-SET", "MC-SET", null, 11.75m, "MULTI-SET-UUID");
        await fixture.SeedProductSetCodeAsync("P-SET", "MC-SET", "SET-BAR", 14m, "SET-UUID");
        var service = new CatalogService(fixture.DbContext, new PriceIndexBuilder(), new CatalogIndexCache());

        var response = await service.LookupSellableItemAsync("S01", "SET-BAR", null, CancellationToken.None);

        Assert.True(response?.Found);
        Assert.Equal("P-SET", response!.Item!.ProductCode);
        Assert.Equal(11.75m, response.Item.RetailPrice);
        Assert.Equal(PriceSourceKind.StoreMultiCodeProduct, response.Item.PriceSource);
    }

    [Fact]
    public async Task LookupSellableItemAsync_miss_returns_found_false_without_full_index_input()
    {
        await using var fixture = await CatalogSqliteFixture.CreateAsync();
        await fixture.SeedStoreAsync("S01");
        await fixture.SeedProductAsync(new Product
        {
            UUID = "PRODUCT-UUID-MISS-NOISE",
            ProductCode = "P-MISS-NOISE",
            ProductName = "Lookup Miss Noise",
            Barcode = "MISS-NOISE-BAR",
            RetailPrice = 21m,
            IsActive = true,
            IsDeleted = false
        });
        fixture.ExecutedCommands.Clear();
        var builder = new RecordingPriceIndexBuilder();
        var service = new CatalogService(fixture.DbContext, builder, new CatalogIndexCache());

        var response = await service.LookupSellableItemAsync("S01", "NO-SUCH-BAR", null, CancellationToken.None);

        Assert.NotNull(response);
        Assert.False(response!.Found);
        Assert.All(builder.Inputs, input => Assert.Empty(input.Products));
        AssertSingleCandidateUnion(fixture, "NO-SUCH-BAR");
        var lookupSourceSelects = fixture.ExecutedCommands
            .Where(command =>
                command.Sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                (ContainsTableSource(command.Sql, "Product") ||
                 ContainsTableSource(command.Sql, "StoreClearancePrice") ||
                 ContainsTableSource(command.Sql, "StoreMultiCodeProduct") ||
                 ContainsTableSource(command.Sql, "ProductSetCode")))
            .ToList();
        Assert.Single(lookupSourceSelects);
        Assert.DoesNotContain(fixture.ExecutedCommands, command =>
            command.Sql.Contains("Barcode` IS NOT NULL OR", StringComparison.OrdinalIgnoreCase) ||
            command.Sql.Contains("Barcode\" IS NOT NULL OR", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LookupSellableItemAsync_missing_store_returns_null()
    {
        await using var fixture = await CatalogSqliteFixture.CreateAsync();
        var service = new CatalogService(fixture.DbContext, new PriceIndexBuilder(), new CatalogIndexCache());

        var response = await service.LookupSellableItemAsync("NO-STORE", "ANY-BAR", null, CancellationToken.None);

        Assert.Null(response);
    }

    [Fact]
    public async Task GetSellableItemsAsync_reads_store_retail_prices_in_projected_keyset_batches()
    {
        const int storeRetailPriceCount = 20_001;
        await using var fixture = await CatalogSqliteFixture.CreateAsync();
        await fixture.SeedStoreAsync("S01");
        await fixture.SeedProductAsync(new Product
        {
            UUID = "PRODUCT-UUID-END",
            ProductCode = "P20000",
            ProductName = "Batch End Product",
            Barcode = "BATCH-END",
            RetailPrice = 1m,
            IsActive = true,
            IsDeleted = false
        });
        await fixture.SeedStoreRetailPricesAsync("S01", storeRetailPriceCount);
        fixture.ExecutedCommands.Clear();
        var service = new CatalogService(fixture.DbContext, new PriceIndexBuilder(), new CatalogIndexCache());

        var response = await service.GetSellableItemsAsync("S01", since: null, CancellationToken.None);

        var item = Assert.Single(response!.Items);
        Assert.Equal(987.65m, item.RetailPrice);
        Assert.Equal("PRICE-UUID-20000", item.ReferenceCode);

        var priceSelects = fixture.ExecutedCommands
            .Where(command => command.Sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && command.Sql.Contains("StoreRetailPrice", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(priceSelects.Count >= 2, $"预期至少两条门店价分批查询，实际为 {priceSelects.Count} 条。\n{string.Join("\n---\n", priceSelects.Select(command => command.Sql))}");

        Assert.All(priceSelects, command =>
        {
            var sql = command.Sql;
            Assert.Matches(@"(?is)\bORDER\s+BY\b.*\bProductCode\b.*\bASC\b.*\bUUID\b.*\bASC\b", sql);
            Assert.Contains("20000", sql, StringComparison.Ordinal);

            var fromIndex = sql.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);
            Assert.True(fromIndex > 0, $"门店价查询缺少 FROM：{sql}");
            var selectList = sql[..fromIndex];
            Assert.DoesNotContain("*", selectList, StringComparison.Ordinal);
            Assert.DoesNotContain("StoreProductCode", selectList, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SupplierCode", selectList, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PurchasePrice", selectList, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("IsAutoPricing", selectList, StringComparison.OrdinalIgnoreCase);
        });

        var secondCommand = priceSelects[1];
        var secondQuery = secondCommand.Sql;
        var whereIndex = secondQuery.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
        var orderByIndex = secondQuery.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);
        Assert.True(whereIndex >= 0 && orderByIndex > whereIndex, $"第二批查询缺少游标条件：{secondQuery}");
        var cursorPredicate = secondQuery[whereIndex..orderByIndex];
        Assert.Matches(
            @"(?is)\bAND\s*\({2}\s*\[ProductCode\]\s*>\s*@lastProductCode\s*\)\s+OR\s+\(\s*\[ProductCode\]\s*=\s*@lastProductCode\s+AND\s+\[UUID\]\s*>\s*@lastUuid\s*\)\s*\)",
            cursorPredicate);
        Assert.DoesNotContain("CASE WHEN", cursorPredicate, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OFFSET", secondQuery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIMIT 20000,", secondQuery, StringComparison.OrdinalIgnoreCase);

        var productCodeCursor = Assert.Single(secondCommand.Parameters, parameter =>
            string.Equals(parameter.ParameterName, "@lastProductCode", StringComparison.Ordinal));
        var uuidCursor = Assert.Single(secondCommand.Parameters, parameter =>
            string.Equals(parameter.ParameterName, "@lastUuid", StringComparison.Ordinal));
        Assert.Equal("P20000", productCodeCursor.Value);
        Assert.Equal("PRICE-UUID-19999", uuidCursor.Value);
    }

    [Fact]
    public async Task GetSellableItemsAsync_reads_products_in_projected_keyset_batches()
    {
        const int productCount = 20_001;
        await using var fixture = await CatalogSqliteFixture.CreateAsync();
        await fixture.SeedStoreAsync("S01");
        await fixture.SeedProductsAsync(productCount);
        fixture.ExecutedCommands.Clear();
        var service = new CatalogService(fixture.DbContext, new PriceIndexBuilder(), new CatalogIndexCache());

        var response = await service.GetSellableItemsAsync("S01", since: null, CancellationToken.None);

        Assert.Equal(productCount, response!.Items.Count);
        var productSelects = fixture.ExecutedCommands
            .Where(command =>
                command.Sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                ContainsTableSource(command.Sql, "Product"))
            .ToList();
        Assert.True(productSelects.Count >= 2, $"预期至少两条商品分批查询，实际为 {productSelects.Count} 条。");

        Assert.All(productSelects, command =>
        {
            Assert.Matches(@"(?is)\bORDER\s+BY\b.*\bProductCode\b.*\bASC\b.*\bUUID\b.*\bASC\b", command.Sql);
            Assert.DoesNotContain("OFFSET", command.Sql, StringComparison.OrdinalIgnoreCase);
        });

        var secondCommand = productSelects[1];
        var productCodeCursor = Assert.Single(secondCommand.Parameters, parameter =>
            string.Equals(parameter.ParameterName, "@lastProductCode", StringComparison.Ordinal));
        var uuidCursor = Assert.Single(secondCommand.Parameters, parameter =>
            string.Equals(parameter.ParameterName, "@lastUuid", StringComparison.Ordinal));
        Assert.Equal("P19999", productCodeCursor.Value);
        Assert.Equal("PRODUCT-UUID-19999", uuidCursor.Value);
    }

    [Fact]
    public async Task GetSellableItemsAsync_projects_only_price_index_fields_for_all_sources()
    {
        await using var fixture = await CatalogSqliteFixture.CreateAsync();
        await fixture.SeedStoreAsync("S01");
        await fixture.SeedProductAsync(new Product
        {
            UUID = "PRODUCT-PROJECTION",
            ProductCode = "P-PROJECTION",
            ProductName = "Projection Product",
            Barcode = "PROJECTION-BAR",
            RetailPrice = 10m,
            IsActive = true,
            IsDeleted = false
        });
        await fixture.SeedStoreRetailPriceAsync(
            "S01",
            "P-PROJECTION",
            9m,
            "PRICE-PROJECTION",
            discountRate: 0.9m,
            isSpecialProduct: true);
        await fixture.SeedStoreMultiCodeProductAsync(
            "S01",
            "P-PROJECTION",
            "MC-PROJECTION",
            "MULTI-PROJECTION",
            8m,
            "MULTI-PROJECTION",
            discountRate: 0.8m,
            isSpecialProduct: true);
        await fixture.SeedStoreClearancePriceAsync("S01", "P-PROJECTION", "CLEARANCE-PROJECTION", 7m, "CLEARANCE-PROJECTION");
        await fixture.SeedProductSetCodeAsync("P-PROJECTION", "MC-PROJECTION", "SET-PROJECTION", 6m, "SET-PROJECTION");
        fixture.ExecutedCommands.Clear();
        var service = new CatalogService(fixture.DbContext, new PriceIndexBuilder(), new CatalogIndexCache());

        var response = await service.GetSellableItemsPageAsync(
            "S01",
            since: null,
            cursor: null,
            pageSize: 100,
            CancellationToken.None);

        Assert.NotNull(response);
        AssertLookupItem(
            response!.Items,
            "PROJECTION-BAR",
            PriceSourceKind.StoreRetailPrice,
            9m,
            "PRICE-PROJECTION",
            0.9m,
            isSpecialProduct: true);
        AssertLookupItem(
            response.Items,
            "MULTI-PROJECTION",
            PriceSourceKind.StoreMultiCodeProduct,
            8m,
            "MULTI-PROJECTION",
            0.8m,
            isSpecialProduct: false);
        AssertLookupItem(
            response.Items,
            "CLEARANCE-PROJECTION",
            PriceSourceKind.StoreClearancePrice,
            7m,
            "CLEARANCE-PROJECTION",
            discountRate: null,
            isSpecialProduct: false);
        AssertLookupItem(
            response.Items,
            "SET-PROJECTION",
            PriceSourceKind.StoreMultiCodeProduct,
            8m,
            "MULTI-PROJECTION",
            0.8m,
            isSpecialProduct: false);

        AssertSourceProjection(
            fixture,
            "Product",
            ["ProductCode", "ProductName", "ItemNumber", "Barcode", "RetailPrice", "UpdatedAt", "CreatedAt", "ProductImage", "UUID"],
            ["ProductCategoryGUID", "LocalSupplierCode", "EnglishName", "ProductType", "PurchasePrice", "IsAutoPricing", "WarehouseCategoryGUID"]);
        AssertSourceProjection(
            fixture,
            "StoreRetailPrice",
            ["ProductCode", "StoreRetailPriceValue", "UpdatedAt", "CreatedAt", "UUID", "DiscountRate", "IsSpecialProduct"],
            ["StoreCode", "StoreProductCode", "SupplierCode", "PurchasePrice", "IsAutoPricing"]);
        AssertSourceProjection(
            fixture,
            "StoreMultiCodeProduct",
            ["ProductCode", "MultiCodeProductCode", "MultiBarcode", "MultiCodeRetailPrice", "UpdatedAt", "CreatedAt", "UUID", "DiscountRate"],
            ["StoreCode", "StoreMultiCodeProductCode", "PurchasePrice", "IsAutoPricing", "IsSpecialProduct"]);
        AssertSourceProjection(
            fixture,
            "StoreClearancePrice",
            ["ProductCode", "ClearanceBarcode", "ClearancePrice", "UpdatedAt", "CreatedAt", "UUID"],
            ["StoreCode", "CreatedBy", "UpdatedBy"]);
        AssertSourceProjection(
            fixture,
            "ProductSetCode",
            ["ProductCode", "SetProductCode", "SetBarcode", "SetRetailPrice", "UpdatedAt", "CreatedAt", "SetCodeId"],
            ["SetItemNumber", "SetPurchasePrice", "SetQuantity", "SetType", "CreatedBy", "UpdatedBy"]);
    }

    private static void AssertLookupItem(
        IReadOnlyList<CatalogLookupItemDto> items,
        string lookupCode,
        PriceSourceKind priceSource,
        decimal retailPrice,
        string referenceCode,
        decimal? discountRate,
        bool isSpecialProduct)
    {
        var item = Assert.Single(items, candidate => candidate.LookupCode == lookupCode);
        Assert.Equal(priceSource, item.PriceSource);
        Assert.Equal(retailPrice, item.RetailPrice);
        Assert.Equal(referenceCode, item.ReferenceCode);
        Assert.Equal(discountRate, item.DiscountRate);
        Assert.Equal(isSpecialProduct, item.IsSpecialProduct);
        Assert.False(string.IsNullOrWhiteSpace(item.RowVersion));
    }

    private static void AssertSourceProjection(
        CatalogSqliteFixture fixture,
        string tableName,
        IReadOnlyList<string> requiredColumns,
        IReadOnlyList<string> unrelatedColumns)
    {
        var commands = fixture.ExecutedCommands
            .Where(command =>
                command.Sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                ContainsTableSource(command.Sql, tableName))
            .ToList();
        Assert.NotEmpty(commands);

        Assert.All(commands, command =>
        {
            var fromIndex = command.Sql.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);
            Assert.True(fromIndex > 0, $"{tableName} 查询缺少 FROM：{command.Sql}");
            var selectList = command.Sql[..fromIndex];
            Assert.DoesNotContain("*", selectList, StringComparison.Ordinal);
            Assert.All(requiredColumns, column => Assert.Contains(column, selectList, StringComparison.OrdinalIgnoreCase));
            Assert.All(unrelatedColumns, column => Assert.DoesNotContain(column, selectList, StringComparison.OrdinalIgnoreCase));
        });
    }

    private static void AssertSingleCandidateUnion(CatalogSqliteFixture fixture, string lookupCandidate)
    {
        var candidateCommands = fixture.ExecutedCommands
            .Where(command =>
                command.Sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                CommandContainsLookupCandidate(command, lookupCandidate))
            .ToList();
        var command = Assert.Single(candidateCommands);
        Assert.True(
            command.Sql.Contains("UNION ALL", StringComparison.OrdinalIgnoreCase),
            $"四类 lookup 来源必须合并为单条 UNION ALL。\n{command.Sql}");
        Assert.Contains("StoreClearancePrice", command.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("StoreMultiCodeProduct", command.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProductSetCode", command.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Product", command.Sql, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsTableSource(string sql, string tableName)
    {
        return sql.Contains($"FROM `{tableName}`", StringComparison.OrdinalIgnoreCase) ||
               sql.Contains($"FROM \"{tableName}\"", StringComparison.OrdinalIgnoreCase) ||
               sql.Contains($"FROM [{tableName}]", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CommandContainsLookupCandidate(
        (string Sql, SugarParameter[] Parameters) command,
        string lookupCandidate)
    {
        var quotedCandidate = $"'{lookupCandidate.Replace("'", "''", StringComparison.Ordinal)}'";
        return command.Sql.Contains(quotedCandidate, StringComparison.Ordinal) ||
               command.Parameters.Any(parameter =>
                   string.Equals(parameter.Value?.ToString(), lookupCandidate, StringComparison.Ordinal));
    }

    private sealed class CatalogSqliteFixture : IAsyncDisposable
    {
        private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-catalog-{Guid.NewGuid():N}.db");
        private readonly SqlSugarClient client;

        private CatalogSqliteFixture()
        {
            client = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = $"Data Source={databasePath}",
                DbType = DbType.Sqlite,
                InitKeyType = InitKeyType.Attribute,
                IsAutoCloseConnection = true
            });

            client.CodeFirst.InitTables<Store, Product, StoreRetailPrice>();
            client.CodeFirst.InitTables<StoreMultiCodeProduct, StoreClearancePrice, ProductSetCode>();
            client.Aop.OnLogExecuting = (sql, parameters) =>
                ExecutedCommands.Add((sql, parameters.ToArray()));
            DbContext = CreateDbContext(client);
        }

        public HbposSqlSugarContext DbContext { get; }

        public List<(string Sql, SugarParameter[] Parameters)> ExecutedCommands { get; } = [];

        public static Task<CatalogSqliteFixture> CreateAsync()
        {
            return Task.FromResult(new CatalogSqliteFixture());
        }

        public async Task SeedStoreAsync(string storeCode)
        {
            await client.Insertable(new Store
            {
                StoreGUID = $"STORE-{storeCode}",
                StoreCode = storeCode,
                StoreName = $"Store {storeCode}",
                IsActive = true,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }).ExecuteCommandAsync();
        }

        public async Task SeedProductAsync(Product product)
        {
            product.CreatedAt = DateTime.UtcNow;
            product.UpdatedAt = DateTime.UtcNow;
            await client.Insertable(product).ExecuteCommandAsync();
        }

        public async Task SeedStoreRetailPriceAsync(
            string storeCode,
            string productCode,
            decimal retailPrice,
            string uuid,
            decimal? discountRate = null,
            bool isSpecialProduct = false)
        {
            await client.Insertable(new StoreRetailPrice
            {
                UUID = uuid,
                StoreCode = storeCode,
                ProductCode = productCode,
                StoreProductCode = $"{storeCode}-{productCode}",
                StoreRetailPriceValue = retailPrice,
                DiscountRate = discountRate,
                IsSpecialProduct = isSpecialProduct,
                IsActive = true,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }).ExecuteCommandAsync();
        }

        public async Task SeedStoreMultiCodeProductAsync(
            string storeCode,
            string productCode,
            string multiCodeProductCode,
            string? multiBarcode,
            decimal retailPrice,
            string uuid,
            decimal? discountRate = null,
            bool isSpecialProduct = false)
        {
            await client.Insertable(new StoreMultiCodeProduct
            {
                UUID = uuid,
                StoreCode = storeCode,
                ProductCode = productCode,
                MultiCodeProductCode = multiCodeProductCode,
                MultiBarcode = multiBarcode,
                MultiCodeRetailPrice = retailPrice,
                DiscountRate = discountRate,
                IsSpecialProduct = isSpecialProduct,
                IsActive = true,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }).ExecuteCommandAsync();
        }

        public async Task SeedStoreClearancePriceAsync(
            string storeCode,
            string productCode,
            string clearanceBarcode,
            decimal clearancePrice,
            string uuid)
        {
            await client.Insertable(new StoreClearancePrice
            {
                UUID = uuid,
                StoreCode = storeCode,
                ProductCode = productCode,
                ClearanceBarcode = clearanceBarcode,
                ClearancePrice = clearancePrice,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }).ExecuteCommandAsync();
        }

        public async Task SeedProductSetCodeAsync(
            string productCode,
            string setProductCode,
            string setBarcode,
            decimal setRetailPrice,
            string uuid)
        {
            await client.Insertable(new ProductSetCode
            {
                SetCodeId = uuid,
                ProductCode = productCode,
                SetProductCode = setProductCode,
                SetItemNumber = $"{setBarcode}-ITEM",
                SetBarcode = setBarcode,
                SetRetailPrice = setRetailPrice,
                SetQuantity = 1,
                SetType = 1,
                IsActive = true,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }).ExecuteCommandAsync();
        }

        public async Task SeedStoreRetailPricesAsync(string storeCode, int count)
        {
            const int insertBatchSize = 100;
            var batch = new List<StoreRetailPrice>(insertBatchSize);
            var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            for (var index = 0; index < count; index++)
            {
                batch.Add(new StoreRetailPrice
                {
                    UUID = $"PRICE-UUID-{index:D5}",
                    StoreCode = storeCode,
                    ProductCode = "P20000",
                    StoreProductCode = $"{storeCode}-P20000-{index:D5}",
                    SupplierCode = "UNUSED-SUPPLIER",
                    PurchasePrice = 2m,
                    StoreRetailPriceValue = index == count - 1 ? 987.65m : index,
                    IsActive = true,
                    IsAutoPricing = true,
                    IsSpecialProduct = index == count - 1,
                    IsDeleted = false,
                    CreatedAt = createdAt,
                    UpdatedAt = createdAt.AddSeconds(index)
                });

                if (batch.Count == insertBatchSize)
                {
                    await client.Insertable(batch).ExecuteCommandAsync();
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await client.Insertable(batch).ExecuteCommandAsync();
            }
        }

        public async Task SeedProductsAsync(int count)
        {
            const int insertBatchSize = 100;
            var batch = new List<Product>(insertBatchSize);
            var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            for (var index = 0; index < count; index++)
            {
                batch.Add(new Product
                {
                    UUID = $"PRODUCT-UUID-{index:D5}",
                    ProductCode = $"P{index:D5}",
                    ProductName = $"Product {index:D5}",
                    Barcode = $"PRODUCT-BAR-{index:D5}",
                    RetailPrice = index,
                    IsActive = true,
                    IsDeleted = false,
                    CreatedAt = createdAt,
                    UpdatedAt = createdAt.AddSeconds(index)
                });

                if (batch.Count == insertBatchSize)
                {
                    await client.Insertable(batch).ExecuteCommandAsync();
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await client.Insertable(batch).ExecuteCommandAsync();
            }
        }

        public async Task<List<StoreRetailPrice>> LoadStoreRetailPricesAsync()
        {
            return await client.Queryable<StoreRetailPrice>()
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();
        }

        public ValueTask DisposeAsync()
        {
            client.Dispose();
            if (File.Exists(databasePath))
            {
                try
                {
                    File.Delete(databasePath);
                }
                catch (IOException)
                {
                }
            }

            return ValueTask.CompletedTask;
        }

        private static HbposSqlSugarContext CreateDbContext(ISqlSugarClient mainDb)
        {
            var context = (HbposSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HbposSqlSugarContext));
            SetAutoProperty(context, nameof(HbposSqlSugarContext.MainDb), mainDb);
            SetAutoProperty(context, nameof(HbposSqlSugarContext.PosmDb), mainDb);
            return context;
        }

        private static void SetAutoProperty(HbposSqlSugarContext context, string propertyName, ISqlSugarClient value)
        {
            var backingField = typeof(HbposSqlSugarContext).GetField(
                $"<{propertyName}>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(backingField);
            backingField!.SetValue(context, value);
        }
    }

    private sealed class RecordingPriceIndexBuilder : IPriceIndexBuilder
    {
        private readonly PriceIndexBuilder inner = new();

        public List<PriceIndexInput> Inputs { get; } = [];

        public IReadOnlyList<SellableItemDto> Build(string storeCode, PriceIndexInput input)
        {
            Inputs.Add(input);
            return inner.Build(storeCode, input);
        }
    }
}
