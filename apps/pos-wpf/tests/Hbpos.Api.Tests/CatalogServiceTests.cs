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
            DbContext = CreateDbContext(client);
        }

        public HbposSqlSugarContext DbContext { get; }

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
            string uuid)
        {
            await client.Insertable(new StoreRetailPrice
            {
                UUID = uuid,
                StoreCode = storeCode,
                ProductCode = productCode,
                StoreProductCode = $"{storeCode}-{productCode}",
                StoreRetailPriceValue = retailPrice,
                IsActive = true,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }).ExecuteCommandAsync();
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
}
