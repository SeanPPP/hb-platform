using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public sealed class ProductWarehouseReactServiceTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _sqliteConnection;
        private readonly SqlSugarClient _db;

        public ProductWarehouseReactServiceTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
            _sqliteConnection = new SqliteConnection($"Data Source={_dbPath}");
            _sqliteConnection.Open();

            _db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = _sqliteConnection.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            });

            _db.CodeFirst.InitTables(
                typeof(Product),
                typeof(WarehouseProduct),
                typeof(DomesticProduct),
                typeof(DomesticSetProduct),
                typeof(ChinaSupplier),
                typeof(StoreMultiCodeProduct),
                typeof(Store),
                typeof(StoreRetailPrice),
                typeof(ProductLocation),
                typeof(Location),
                typeof(ProductGrade)
            );
        }

        [Fact]
        public async Task LookupMobileProductsAsync_ReturnsWarehouseFieldsUsedByMobileUi()
        {
            await _db.Insertable(new ChinaSupplier
            {
                Guid = "supplier-guid",
                SupplierCode = "SUP-001",
                SupplierName = "Supplier One",
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new Product
            {
                UUID = "product-uuid-1",
                ProductCode = "P001",
                ProductName = "Widget",
                ItemNumber = "ITEM-001",
                Barcode = "BAR-001",
                ProductImage = null,
                LocalSupplierCode = "LOCAL-01",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new WarehouseProduct
            {
                ProductCode = "P001",
                OEMPrice = 12.5m,
                ImportPrice = 8.8m,
                StockQuantity = 33,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new DomesticProduct
            {
                ProductCode = "P001",
                SupplierCode = "SUP-001",
                ProductName = "Widget",
                ProductImage = "https://cdn.example.com/fallback.png",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new Location
            {
                LocationGuid = "loc-001",
                LocationCode = "A-01-01-01",
                LocationBarcode = "LOCBAR001",
                LocationType = 1,
                Status = 1,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new ProductLocation
            {
                Guid = "product-location-001",
                ProductCode = "P001",
                LocationGuid = "loc-001",
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new ProductGrade
            {
                Id = "grade-001",
                ProductCode = "P001",
                Grade = "A",
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var service = CreateService();

            var result = await service.LookupMobileProductsAsync("ITEM-001");

            var item = Assert.Single(result);
            Assert.Equal("P001", item.ProductCode);
            Assert.Equal("ITEM-001", item.ItemNumber);
            Assert.Equal("BAR-001", item.Barcode);
            Assert.Equal("Supplier One", item.SupplierName);
            Assert.Equal("A", item.Grade);
            Assert.Equal(33, item.StockQuantity);
            Assert.Equal("A-01-01-01", item.LocationCode);
            Assert.Equal("https://cdn.example.com/fallback.png", item.ProductImage);
        }

        [Fact]
        public async Task LookupAndGetMobileProductAsync_ReturnWarehouseIsActiveAndLegacyIsActiveWithSameValue()
        {
            await _db.Insertable(new Product
            {
                UUID = "product-uuid-active-1",
                ProductCode = "P-ACTIVE-001",
                ProductName = "Active Widget",
                ItemNumber = "ITEM-ACTIVE-001",
                Barcode = "BAR-ACTIVE-001",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new WarehouseProduct
            {
                ProductCode = "P-ACTIVE-001",
                IsActive = false,
                StockQuantity = 5,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new DomesticProduct
            {
                ProductCode = "P-ACTIVE-001",
                ProductName = "Active Widget",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var service = CreateService();

            var lookupItems = await service.LookupMobileProductsAsync("ITEM-ACTIVE-001");
            var lookupItem = Assert.Single(lookupItems);
            Assert.False(lookupItem.WarehouseIsActive);
            Assert.False(lookupItem.IsActive);

            var detailItem = await service.GetMobileProductAsync("P-ACTIVE-001");
            Assert.NotNull(detailItem);
            Assert.False(detailItem!.WarehouseIsActive);
            Assert.False(detailItem.IsActive);
        }

        [Fact]
        public async Task GetDomesticProductsNotInWarehouseAsync_ReturnsProductImageForImportModal()
        {
            await _db.Insertable(new DomesticProduct
            {
                ProductCode = "DP-IMG-001",
                HBProductNo = "HB022-109",
                Barcode = "9525810220074",
                ProductName = "圆球",
                ProductImage = null,
                ProductType = 0,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var service = CreateService();

            var result = await service.GetDomesticProductsNotInWarehouseAsync(
                new GetDomesticProductsNotInWarehouseRequestDto
                {
                    Page = 1,
                    PageSize = 20,
                    GlobalSearch = "HB022-109",
                }
            );

            var item = Assert.Single(result.Items);
            Assert.Equal("HB022-109", item.ItemNumber);
            Assert.Equal(
                "https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200/HB022-109.jpg",
                item.ProductImage
            );
        }

        [Fact]
        public async Task SetMobileProductLocationAsync_BindsEmptyPickingLocation()
        {
            await SeedWarehouseProductAsync("P-PICK-EMPTY", "ITEM-PICK-EMPTY", "BAR-PICK-EMPTY");
            await SeedLocationAsync("loc-pick-empty", "A-00-00-01", 1);
            var service = CreateService();

            var result = await service.SetMobileProductLocationAsync("P-PICK-EMPTY", "loc-pick-empty");

            Assert.NotNull(result);
            Assert.Equal("loc-pick-empty", result.LocationGuid);
            Assert.Equal("A-00-00-01", result.LocationCode);
        }

        [Fact]
        public async Task SetMobileProductLocationAsync_BlocksOccupiedPickingLocation()
        {
            await SeedWarehouseProductAsync("P-PICK-TARGET", "ITEM-PICK-TARGET", "BAR-PICK-TARGET");
            await SeedWarehouseProductAsync("P-PICK-OTHER", "ITEM-PICK-OTHER", "BAR-PICK-OTHER");
            await SeedLocationAsync("loc-pick-used", "A-00-00-02", 1);
            await _db.Insertable(new ProductLocation
            {
                Guid = "product-location-pick-used",
                ProductCode = "P-PICK-OTHER",
                LocationGuid = "loc-pick-used",
                IsDeleted = false,
            }).ExecuteCommandAsync();
            var service = CreateService();

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SetMobileProductLocationAsync("P-PICK-TARGET", "loc-pick-used")
            );

            Assert.Equal("该配货位已有商品，不能继续绑定", error.Message);
        }

        [Fact]
        public async Task PatchMobileProductAsync_WhenWarehouseIsActiveIsFalse_OnlyUpdatesWarehouseProductIsActive()
        {
            var productUpdatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var domesticUpdatedAt = new DateTime(2026, 1, 3, 3, 4, 5, DateTimeKind.Utc);
            var gradeUpdatedAt = new DateTime(2026, 1, 4, 3, 4, 5, DateTimeKind.Utc);
            await _db.Insertable(new Product
            {
                UUID = "product-uuid-patch-1",
                ProductCode = "P-PATCH-001",
                ProductName = "Patch Widget",
                ItemNumber = "ITEM-PATCH-001",
                Barcode = "BAR-PATCH-001",
                PurchasePrice = 4.28m,
                RetailPrice = 11.99m,
                IsActive = true,
                IsDeleted = false,
                UpdatedAt = productUpdatedAt,
            }).ExecuteCommandAsync();

            await _db.Insertable(new WarehouseProduct
            {
                ProductCode = "P-PATCH-001",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new DomesticProduct
            {
                ProductCode = "P-PATCH-001",
                ProductName = "Patch Widget",
                DomesticPrice = 6.66m,
                OEMPrice = 7.77m,
                ImportPrice = 8.88m,
                IsActive = true,
                IsDeleted = false,
                UpdatedAt = domesticUpdatedAt,
            }).ExecuteCommandAsync();

            await _db.Insertable(new ProductGrade
            {
                Id = "grade-patch-1",
                ProductCode = "P-PATCH-001",
                Grade = "D",
                IsDeleted = false,
                UpdatedAt = gradeUpdatedAt,
            }).ExecuteCommandAsync();

            var service = CreateService();

            var result = await service.PatchMobileProductAsync(
                "P-PATCH-001",
                new WarehouseMobileProductPatchDto { WarehouseIsActive = false }
            );

            Assert.NotNull(result);
            Assert.False(result!.WarehouseIsActive);
            Assert.False(result.IsActive);

            var warehouseProduct = await _db.Queryable<WarehouseProduct>()
                .Where(w => w.ProductCode == "P-PATCH-001")
                .FirstAsync();
            var product = await _db.Queryable<Product>()
                .Where(p => p.ProductCode == "P-PATCH-001")
                .FirstAsync();
            var domesticProduct = await _db.Queryable<DomesticProduct>()
                .Where(dp => dp.ProductCode == "P-PATCH-001")
                .FirstAsync();
            var productGrade = await _db.Queryable<ProductGrade>()
                .Where(pg => pg.ProductCode == "P-PATCH-001")
                .FirstAsync();

            Assert.NotNull(warehouseProduct);
            Assert.NotNull(product);
            Assert.NotNull(domesticProduct);
            Assert.NotNull(productGrade);
            Assert.False(warehouseProduct!.IsActive);
            Assert.True(product!.IsActive);
            Assert.True(domesticProduct!.IsActive);
            Assert.Equal("D", productGrade!.Grade);
            Assert.Equal(gradeUpdatedAt, productGrade.UpdatedAt);
            Assert.Equal(4.28m, product.PurchasePrice);
            Assert.Equal(11.99m, product.RetailPrice);
            Assert.Equal(productUpdatedAt, product.UpdatedAt);
            Assert.Equal(6.66m, domesticProduct.DomesticPrice);
            Assert.Equal(7.77m, domesticProduct.OEMPrice);
            Assert.Equal(8.88m, domesticProduct.ImportPrice);
            Assert.Equal(domesticUpdatedAt, domesticProduct.UpdatedAt);
        }

        [Fact]
        public async Task PatchMobileProductAsync_WhenLegacyIsActiveProvided_UpdatesWarehouseProductForCompatibility()
        {
            await SeedWarehouseProductAsync("P-LEGACY-ACTIVE", "ITEM-LEGACY-ACTIVE", "BAR-LEGACY-ACTIVE");
            var service = CreateService();

            var result = await service.PatchMobileProductAsync(
                "P-LEGACY-ACTIVE",
                new WarehouseMobileProductPatchDto { IsActive = false }
            );

            Assert.NotNull(result);
            Assert.False(result!.WarehouseIsActive);
            Assert.False(result.IsActive);
        }

        [Fact]
        public async Task PatchMobileProductAsync_WhenNewAndLegacyStatusConflict_UsesWarehouseIsActive()
        {
            await SeedWarehouseProductAsync("P-STATUS-CONFLICT", "ITEM-STATUS-CONFLICT", "BAR-STATUS-CONFLICT");
            var service = CreateService();

            var result = await service.PatchMobileProductAsync(
                "P-STATUS-CONFLICT",
                new WarehouseMobileProductPatchDto { WarehouseIsActive = false, IsActive = true }
            );

            Assert.NotNull(result);
            Assert.False(result!.WarehouseIsActive);
            Assert.False(result.IsActive);
        }

        [Fact]
        public async Task PatchMobileProductAsync_WhenImportPriceChanges_UpdatesProductAndAllActiveStorePurchasePrices()
        {
            await SeedPriceSyncProductAsync(
                "P-IMPORT-SYNC",
                purchasePrice: 4.28m,
                retailPrice: 11.99m,
                importPrice: 4.28m,
                oemPrice: 11.99m
            );
            await SeedStoreAsync("S01", isActive: true, isDeleted: false);
            await SeedStoreAsync("S02", isActive: true, isDeleted: false);
            await SeedStoreAsync("S03", isActive: false, isDeleted: false);
            await SeedStoreAsync("S04", isActive: true, isDeleted: true);
            await SeedStoreRetailPriceAsync("S01", "P-IMPORT-SYNC", purchasePrice: 4.28m, retailPrice: 11.99m);
            var service = CreateService();

            await service.PatchMobileProductAsync(
                "P-IMPORT-SYNC",
                new WarehouseMobileProductPatchDto
                {
                    PurchasePrice = 5.55m,
                }
            );

            var product = await _db.Queryable<Product>().SingleAsync(x => x.ProductCode == "P-IMPORT-SYNC");
            var warehouseProduct = await _db.Queryable<WarehouseProduct>().SingleAsync(x => x.ProductCode == "P-IMPORT-SYNC");
            var activeStorePrices = await _db.Queryable<StoreRetailPrice>()
                .Where(x => x.ProductCode == "P-IMPORT-SYNC" && !x.IsDeleted)
                .OrderBy(x => x.StoreCode)
                .ToListAsync();

            Assert.Equal(5.55m, product.PurchasePrice);
            Assert.Equal(5.55m, warehouseProduct.ImportPrice);
            Assert.Collection(
                activeStorePrices,
                s01 =>
                {
                    Assert.Equal("S01", s01.StoreCode);
                    Assert.Equal(5.55m, s01.PurchasePrice);
                    Assert.Equal(11.99m, s01.StoreRetailPriceValue);
                    Assert.Equal("MobileWarehousePricePatch", s01.UpdatedBy);
                },
                s02 =>
                {
                    Assert.Equal("S02", s02.StoreCode);
                    Assert.Equal("S02P-IMPORT-SYNC", s02.StoreProductCode);
                    Assert.Equal(5.55m, s02.PurchasePrice);
                    Assert.Equal(11.99m, s02.StoreRetailPriceValue);
                    Assert.Equal("MobileWarehousePricePatch", s02.CreatedBy);
                    Assert.Equal("MobileWarehousePricePatch", s02.UpdatedBy);
                }
            );
        }

        [Fact]
        public async Task BatchUpdateAsync_WhenImportPriceChanges_UpdatesWarehouseProductProductAndStoreRetailPrices()
        {
            await SeedPriceSyncProductAsync(
                "P-BATCH-IMPORT-SYNC",
                purchasePrice: 4.28m,
                retailPrice: 11.99m,
                importPrice: 4.28m,
                oemPrice: 11.99m
            );
            await SeedStoreRetailPriceAsync("S01", "P-BATCH-IMPORT-SYNC", purchasePrice: 4.28m, retailPrice: 11.99m);
            var service = CreateService();

            var result = await service.BatchUpdateAsync(
                new List<UpdateItemDto>
                {
                    new()
                    {
                        ProductCode = "P-BATCH-IMPORT-SYNC",
                        ImportPrice = 6.66m,
                        IsActive = true,
                    },
                }
            );

            var product = await _db.Queryable<Product>().SingleAsync(x => x.ProductCode == "P-BATCH-IMPORT-SYNC");
            var warehouseProduct = await _db.Queryable<WarehouseProduct>().SingleAsync(x => x.ProductCode == "P-BATCH-IMPORT-SYNC");
            var storePrice = await _db.Queryable<StoreRetailPrice>().SingleAsync(x => x.ProductCode == "P-BATCH-IMPORT-SYNC");

            Assert.True(result.Success);
            Assert.Equal(1, result.SuccessCount);
            Assert.Equal(6.66m, warehouseProduct.ImportPrice);
            Assert.Equal(6.66m, product.PurchasePrice);
            Assert.Equal(6.66m, storePrice.PurchasePrice);
            Assert.Equal(11.99m, storePrice.StoreRetailPriceValue);
        }

        [Fact]
        public async Task PatchMobileProductAsync_WhenRetailPriceChangesWithoutStoreSync_DoesNotTouchStoreRetailPrices()
        {
            await SeedPriceSyncProductAsync(
                "P-RETAIL-NO-SYNC",
                purchasePrice: 4.28m,
                retailPrice: 11.99m,
                importPrice: 4.28m,
                oemPrice: 11.99m
            );
            await SeedStoreAsync("S01", isActive: true, isDeleted: false);
            await SeedStoreAsync("S02", isActive: true, isDeleted: false);
            await SeedStoreRetailPriceAsync("S01", "P-RETAIL-NO-SYNC", purchasePrice: 4.28m, retailPrice: 11.99m);
            var service = CreateService();

            await service.PatchMobileProductAsync(
                "P-RETAIL-NO-SYNC",
                new WarehouseMobileProductPatchDto
                {
                    RetailPrice = 12.99m,
                    SyncStoreRetailPrices = false,
                }
            );

            var product = await _db.Queryable<Product>().SingleAsync(x => x.ProductCode == "P-RETAIL-NO-SYNC");
            var warehouseProduct = await _db.Queryable<WarehouseProduct>().SingleAsync(x => x.ProductCode == "P-RETAIL-NO-SYNC");
            var storePrices = await _db.Queryable<StoreRetailPrice>()
                .Where(x => x.ProductCode == "P-RETAIL-NO-SYNC" && !x.IsDeleted)
                .ToListAsync();

            Assert.Equal(12.99m, product.RetailPrice);
            Assert.Equal(12.99m, warehouseProduct.OEMPrice);
            var storePrice = Assert.Single(storePrices);
            Assert.Equal("S01", storePrice.StoreCode);
            Assert.Equal(11.99m, storePrice.StoreRetailPriceValue);
        }

        [Fact]
        public async Task PatchMobileProductAsync_WhenRetailPriceChangesWithStoreSync_UpdatesAllActiveStoreRetailPrices()
        {
            await SeedPriceSyncProductAsync(
                "P-RETAIL-SYNC",
                purchasePrice: 4.28m,
                retailPrice: 11.99m,
                importPrice: 4.28m,
                oemPrice: 11.99m
            );
            await SeedStoreAsync("S01", isActive: true, isDeleted: false);
            await SeedStoreAsync("S02", isActive: true, isDeleted: false);
            await SeedStoreAsync("S03", isActive: false, isDeleted: false);
            await SeedStoreRetailPriceAsync("S01", "P-RETAIL-SYNC", purchasePrice: 4.28m, retailPrice: 11.99m);
            var service = CreateService();

            await service.PatchMobileProductAsync(
                "P-RETAIL-SYNC",
                new WarehouseMobileProductPatchDto
                {
                    OEMPrice = 12.99m,
                    SyncStoreRetailPrices = true,
                }
            );

            var product = await _db.Queryable<Product>().SingleAsync(x => x.ProductCode == "P-RETAIL-SYNC");
            var warehouseProduct = await _db.Queryable<WarehouseProduct>().SingleAsync(x => x.ProductCode == "P-RETAIL-SYNC");
            var activeStorePrices = await _db.Queryable<StoreRetailPrice>()
                .Where(x => x.ProductCode == "P-RETAIL-SYNC" && !x.IsDeleted)
                .OrderBy(x => x.StoreCode)
                .ToListAsync();

            Assert.Equal(12.99m, product.RetailPrice);
            Assert.Equal(12.99m, warehouseProduct.OEMPrice);
            Assert.Collection(
                activeStorePrices,
                s01 =>
                {
                    Assert.Equal("S01", s01.StoreCode);
                    Assert.Equal(12.99m, s01.StoreRetailPriceValue);
                    Assert.Equal(4.28m, s01.PurchasePrice);
                    Assert.Equal("MobileWarehousePricePatch", s01.UpdatedBy);
                },
                s02 =>
                {
                    Assert.Equal("S02", s02.StoreCode);
                    Assert.Equal("S02P-RETAIL-SYNC", s02.StoreProductCode);
                    Assert.Equal(12.99m, s02.StoreRetailPriceValue);
                    Assert.Equal(4.28m, s02.PurchasePrice);
                    Assert.Equal("MobileWarehousePricePatch", s02.CreatedBy);
                    Assert.Equal("MobileWarehousePricePatch", s02.UpdatedBy);
                }
            );
        }

        [Fact]
        public async Task PatchMobileProductAsync_WhenLinkedPurchasePricesConflict_Throws()
        {
            await SeedPriceSyncProductAsync(
                "P-PURCHASE-CONFLICT",
                purchasePrice: 4.28m,
                retailPrice: 11.99m,
                importPrice: 4.28m,
                oemPrice: 11.99m
            );
            var service = CreateService();

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.PatchMobileProductAsync(
                    "P-PURCHASE-CONFLICT",
                    new WarehouseMobileProductPatchDto
                    {
                        PurchasePrice = 5.55m,
                        ImportPrice = 6.66m,
                    }
                )
            );

            Assert.Equal("进货价和进口价不一致", error.Message);
        }

        [Fact]
        public async Task PatchMobileProductAsync_WhenLinkedRetailPricesConflict_Throws()
        {
            await SeedPriceSyncProductAsync(
                "P-RETAIL-CONFLICT",
                purchasePrice: 4.28m,
                retailPrice: 11.99m,
                importPrice: 4.28m,
                oemPrice: 11.99m
            );
            var service = CreateService();

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.PatchMobileProductAsync(
                    "P-RETAIL-CONFLICT",
                    new WarehouseMobileProductPatchDto
                    {
                        RetailPrice = 12.99m,
                        OEMPrice = 13.99m,
                    }
                )
            );

            Assert.Equal("零售价和OEM价不一致", error.Message);
        }

        [Fact]
        public async Task SetMobileProductLocationAsync_AllowsOccupiedStorageLocation()
        {
            await SeedWarehouseProductAsync("P-STORAGE-TARGET", "ITEM-STORAGE-TARGET", "BAR-STORAGE-TARGET");
            await SeedWarehouseProductAsync("P-STORAGE-OTHER", "ITEM-STORAGE-OTHER", "BAR-STORAGE-OTHER");
            await SeedLocationAsync("loc-storage-used", "A-00-00-03", 2);
            await _db.Insertable(new ProductLocation
            {
                Guid = "product-location-storage-used",
                ProductCode = "P-STORAGE-OTHER",
                LocationGuid = "loc-storage-used",
                IsDeleted = false,
            }).ExecuteCommandAsync();
            var service = CreateService();

            var result = await service.SetMobileProductLocationAsync("P-STORAGE-TARGET", "loc-storage-used");

            Assert.NotNull(result);
            Assert.Equal("loc-storage-used", result.LocationGuid);
            var boundCount = await _db.Queryable<ProductLocation>()
                .Where(pl => !pl.IsDeleted && pl.LocationGuid == "loc-storage-used")
                .CountAsync();
            Assert.Equal(2, boundCount);
        }

        [Fact]
        public async Task SetMobileProductLocationAsync_ThrowsWhenLocationMissing()
        {
            await SeedWarehouseProductAsync("P-MISSING-LOC", "ITEM-MISSING-LOC", "BAR-MISSING-LOC");
            var service = CreateService();

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SetMobileProductLocationAsync("P-MISSING-LOC", "loc-missing")
            );

            Assert.Equal("货位不存在", error.Message);
        }

        public void Dispose()
        {
            _db.Dispose();
            _sqliteConnection.Dispose();

            if (File.Exists(_dbPath))
            {
                SqliteTempFileCleanup.DeleteIfExists(_dbPath);
            }
        }

        private async Task SeedWarehouseProductAsync(
            string productCode,
            string itemNumber,
            string barcode
        )
        {
            await _db.Insertable(new Product
            {
                UUID = $"product-uuid-{productCode}",
                ProductCode = productCode,
                ProductName = productCode,
                ItemNumber = itemNumber,
                Barcode = barcode,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new WarehouseProduct
            {
                ProductCode = productCode,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
        }

        private async Task SeedLocationAsync(string locationGuid, string locationCode, int locationType)
        {
            await _db.Insertable(new Location
            {
                LocationGuid = locationGuid,
                LocationCode = locationCode,
                LocationBarcode = $"{locationCode}-BAR",
                LocationType = locationType,
                Status = 1,
                IsDeleted = false,
            }).ExecuteCommandAsync();
        }

        private async Task SeedPriceSyncProductAsync(
            string productCode,
            decimal purchasePrice,
            decimal retailPrice,
            decimal importPrice,
            decimal oemPrice
        )
        {
            await _db.Insertable(new Product
            {
                UUID = $"product-uuid-{productCode}",
                ProductCode = productCode,
                ProductName = productCode,
                ItemNumber = $"ITEM-{productCode}",
                Barcode = $"BAR-{productCode}",
                LocalSupplierCode = "LOCAL-01",
                PurchasePrice = purchasePrice,
                RetailPrice = retailPrice,
                IsActive = true,
                IsAutoPricing = false,
                IsSpecialProduct = false,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new WarehouseProduct
            {
                ProductCode = productCode,
                ImportPrice = importPrice,
                OEMPrice = oemPrice,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
        }

        private async Task SeedStoreAsync(string storeCode, bool isActive, bool isDeleted)
        {
            await _db.Insertable(new Store
            {
                StoreGUID = $"store-guid-{storeCode}",
                StoreCode = storeCode,
                StoreName = $"Store {storeCode}",
                IsActive = isActive,
                IsDeleted = isDeleted,
            }).ExecuteCommandAsync();
        }

        private async Task SeedStoreRetailPriceAsync(
            string storeCode,
            string productCode,
            decimal purchasePrice,
            decimal retailPrice
        )
        {
            await _db.Insertable(new StoreRetailPrice
            {
                UUID = $"store-price-{storeCode}-{productCode}",
                StoreCode = storeCode,
                ProductCode = productCode,
                StoreProductCode = $"sp-{storeCode}-{productCode}",
                SupplierCode = "LOCAL-01",
                PurchasePrice = purchasePrice,
                StoreRetailPriceValue = retailPrice,
                DiscountRate = null,
                IsActive = true,
                IsAutoPricing = false,
                IsSpecialProduct = false,
                IsDeleted = false,
            }).ExecuteCommandAsync();
        }

        private ProductWarehouseReactService CreateService()
        {
            var configuration = new ConfigurationBuilder().Build();
            var context = CreateSqlSugarContext(_db);
            var itemBarcodeService = new ItemBarcodeService(
                context,
                NullLogger<ItemBarcodeService>.Instance,
                configuration
            );

            return new ProductWarehouseReactService(
                context,
                CreateHqSqlSugarContext(),
                NullLogger<ProductWarehouseReactService>.Instance,
                configuration,
                itemBarcodeService,
                Mock.Of<IMapper>(),
                Mock.Of<IDataSyncFullService>()
            );
        }

        private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
        {
            var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(
                typeof(SqlSugarContext)
            );

            var dbField = typeof(SqlSugarContext).GetField(
                "_db",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            dbField!.SetValue(context, db);

            return context;
        }

        private static HqSqlSugarContext CreateHqSqlSugarContext()
        {
            var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
                typeof(HqSqlSugarContext)
            );

            var dbField = typeof(HqSqlSugarContext).GetField(
                "_db",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            dbField!.SetValue(context, new Mock<ISqlSugarClient>().Object);

            return context;
        }
    }
}
