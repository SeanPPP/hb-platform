using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
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
                typeof(ProductGrade),
                typeof(WarehouseCategory)
            );
        }

        [Fact]
        public async Task GetAntdTableDataAsync_FiltersByCategoryGuidsIncludingChildren()
        {
            await SeedWarehouseCategoryAsync("cat-root", null, "根分类");
            await SeedWarehouseCategoryAsync("cat-child", "cat-root", "子分类");
            await SeedWarehouseTableProductAsync("P-CAT-ROOT", "ITEM-CAT-ROOT", "根分类商品", "cat-root");
            await SeedWarehouseTableProductAsync("P-CAT-CHILD", "ITEM-CAT-CHILD", "子分类商品", "cat-child");
            await SeedWarehouseTableProductAsync("P-CAT-OTHER", "ITEM-CAT-OTHER", "其他分类商品", null);

            var service = CreateService();

            var result = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                CategoryGuids = new List<string> { "cat-root" },
                IncludeSubCategories = true,
            });

            Assert.Equal(2, result.Total);
            Assert.Contains(result.Items, item => item.ProductCode == "P-CAT-ROOT");
            Assert.Contains(result.Items, item => item.ProductCode == "P-CAT-CHILD");
            Assert.DoesNotContain(result.Items, item => item.ProductCode == "P-CAT-OTHER");
        }

        [Fact]
        public async Task GetAntdTableDataAsync_FiltersUncategorizedOnly()
        {
            await SeedWarehouseCategoryAsync("cat-assigned", null, "已分类");
            await SeedWarehouseTableProductAsync("P-ASSIGNED", "ITEM-ASSIGNED", "已分类商品", "cat-assigned");
            await SeedWarehouseTableProductAsync("P-UNCATEGORIZED-NULL", "ITEM-UNCAT-NULL", "未分类空值商品", null);
            await SeedWarehouseTableProductAsync("P-UNCATEGORIZED-EMPTY", "ITEM-UNCAT-EMPTY", "未分类空字符串商品", string.Empty);

            var service = CreateService();

            var result = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                UncategorizedOnly = true,
            });

            Assert.Equal(2, result.Total);
            Assert.Contains(result.Items, item => item.ProductCode == "P-UNCATEGORIZED-NULL");
            Assert.Contains(result.Items, item => item.ProductCode == "P-UNCATEGORIZED-EMPTY");
            Assert.DoesNotContain(result.Items, item => item.ProductCode == "P-ASSIGNED");
        }

        [Fact]
        public async Task GetAntdTableDataAsync_CategoryGuidsTakePriorityOverUncategorizedOnly()
        {
            await SeedWarehouseCategoryAsync("cat-priority", null, "优先分类");
            await SeedWarehouseTableProductAsync("P-PRIORITY-CAT", "ITEM-PRIORITY-CAT", "分类优先商品", "cat-priority");
            await SeedWarehouseTableProductAsync("P-PRIORITY-UNCAT", "ITEM-PRIORITY-UNCAT", "未分类商品", null);

            var service = CreateService();

            var result = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                CategoryGuids = new List<string> { "cat-priority" },
                UncategorizedOnly = true,
            });

            Assert.Single(result.Items);
            Assert.Equal("P-PRIORITY-CAT", result.Items[0].ProductCode);
        }

        [Fact]
        public async Task GetAntdTableDataAsync_BlankCategoryGuidsDoNotOverrideUncategorizedOnly()
        {
            await SeedWarehouseCategoryAsync("cat-blank-priority", null, "已分类");
            await SeedWarehouseTableProductAsync("P-BLANK-CAT", "ITEM-BLANK-CAT", "已分类商品", "cat-blank-priority");
            await SeedWarehouseTableProductAsync("P-BLANK-UNCAT", "ITEM-BLANK-UNCAT", "未分类商品", null);

            var service = CreateService();

            var result = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                CategoryGuids = new List<string> { "", "   " },
                UncategorizedOnly = true,
            });

            var item = Assert.Single(result.Items);
            Assert.Equal("P-BLANK-UNCAT", item.ProductCode);
        }

        [Fact]
        public async Task DetectAsync_ReturnsWarehousePricesNamesVolumeAndPackingQuantity()
        {
            await _db.Insertable(new Product
            {
                UUID = "product-uuid-detect-1",
                ProductCode = "P-DETECT-001",
                ProductName = "检测商品一",
                EnglishName = "Detect Product One",
                ItemNumber = "ITEM-DETECT-001",
                Barcode = "BAR-DETECT-001",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new Product
            {
                UUID = "product-uuid-detect-2",
                ProductCode = "P-DETECT-002",
                ProductName = "检测商品二",
                EnglishName = "Detect Product Two",
                ItemNumber = "ITEM-DETECT-002",
                Barcode = "BAR-DETECT-002",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new WarehouseProduct
            {
                ProductCode = "P-DETECT-001",
                DomesticPrice = 10.25m,
                OEMPrice = 20.50m,
                Volume = 0.125m,
                PackingQuantity = 24,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new WarehouseProduct
            {
                ProductCode = "P-DETECT-002",
                DomesticPrice = 11.25m,
                OEMPrice = 21.50m,
                Volume = 0.225m,
                PackingQuantity = null,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new DomesticProduct
            {
                ProductCode = "P-DETECT-001",
                ProductName = "检测商品一",
                PackingQuantity = 48,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new DomesticProduct
            {
                ProductCode = "P-DETECT-002",
                ProductName = "检测商品二",
                PackingQuantity = 36,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var service = CreateService();

            var result = await service.DetectAsync(new List<DetectionItemDto>
            {
                new() { ProductCode = "P-DETECT-001", ItemNumber = "ITEM-DETECT-001" },
                new() { ProductCode = "P-DETECT-002", ItemNumber = "ITEM-DETECT-002" },
            });

            Assert.Collection(
                result,
                first =>
                {
                    Assert.True(first.Exists);
                    Assert.Equal("检测商品一", first.ProductName);
                    Assert.Equal("Detect Product One", first.EnglishName);
                    Assert.Equal(10.25m, first.WarehouseDomesticPrice);
                    Assert.Equal(20.50m, first.WarehouseOEMPrice);
                    Assert.Equal(0.125m, first.WarehouseVolume);
                    Assert.Equal(48, first.PackingQuantity);
                },
                second =>
                {
                    Assert.True(second.Exists);
                    Assert.Equal("检测商品二", second.ProductName);
                    Assert.Equal("Detect Product Two", second.EnglishName);
                    Assert.Equal(11.25m, second.WarehouseDomesticPrice);
                    Assert.Equal(21.50m, second.WarehouseOEMPrice);
                    Assert.Equal(0.225m, second.WarehouseVolume);
                    Assert.Equal(36, second.PackingQuantity);
                }
            );
        }

        [Fact]
        public async Task DetectAsync_UsesDomesticProductFallbackForNewProductAndWarehouseOemPriceForExisting()
        {
            await _db.Insertable(new Product
            {
                UUID = "product-uuid-detect-existing",
                ProductCode = "P-DETECT-EXISTING",
                ProductName = "仓库已有商品",
                EnglishName = "Existing Warehouse Product",
                ItemNumber = "ITEM-DETECT-EXISTING",
                Barcode = "BAR-DETECT-EXISTING",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new WarehouseProduct
            {
                ProductCode = "P-DETECT-EXISTING",
                ImportPrice = 8.8m,
                OEMPrice = 19.9m,
                DomesticPrice = 6.6m,
                Volume = 0.2m,
                PackingQuantity = 12,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new DomesticProduct
            {
                ProductCode = "P-DETECT-EXISTING",
                HBProductNo = "ITEM-DETECT-EXISTING",
                Barcode = "BAR-DETECT-EXISTING",
                ProductName = "国内已有商品名",
                EnglishProductName = "Domestic Existing Name",
                OEMPrice = 25.5m,
                PackingQuantity = 48,
                UnitVolume = 0.33m,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new DomesticProduct
            {
                ProductCode = "P-DOMESTIC-CODE",
                HBProductNo = "HB138-066",
                Barcode = "9527913800028",
                ProductName = "金/黑框混30X40",
                EnglishProductName = "Frame Mixed 30X40",
                OEMPrice = 15.5m,
                PackingQuantity = 24,
                UnitVolume = 0.4m,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var service = CreateService();

            var result = await service.DetectAsync(new List<DetectionItemDto>
            {
                new() { ProductCode = "P-DETECT-EXISTING", ItemNumber = "ITEM-DETECT-EXISTING", Barcode = "BAR-DETECT-EXISTING" },
                new() { ItemNumber = "HB138-066", Barcode = "9527913800028" },
            });

            Assert.Collection(
                result,
                existing =>
                {
                    Assert.True(existing.Exists);
                    Assert.Equal("国内已有商品名", existing.ProductName);
                    Assert.Equal("Domestic Existing Name", existing.EnglishName);
                    Assert.Equal(8.8m, existing.WarehouseImportPrice);
                    Assert.Equal(19.9m, existing.WarehouseOEMPrice);
                    Assert.Equal(48, existing.PackingQuantity);
                    Assert.Equal(0.33m, existing.WarehouseVolume);
                },
                newProduct =>
                {
                    Assert.False(newProduct.Exists);
                    Assert.Equal("none", newProduct.MatchType);
                    Assert.Equal("P-DOMESTIC-CODE", newProduct.ProductCode);
                    Assert.Equal("HB138-066", newProduct.ItemNumber);
                    Assert.Equal("金/黑框混30X40", newProduct.ProductName);
                    Assert.Equal(15.5m, newProduct.WarehouseOEMPrice);
                    Assert.Equal(24, newProduct.PackingQuantity);
                    Assert.Equal(0.4m, newProduct.WarehouseVolume);
                }
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
        public async Task BatchToggleActiveAsync_UpdatesLinkedProductStatusTables()
        {
            await SeedPriceSyncProductAsync(
                "P-TOGGLE-LINKED",
                purchasePrice: 4.28m,
                retailPrice: 11.99m,
                importPrice: 4.28m,
                oemPrice: 11.99m
            );
            await _db.Insertable(new DomesticProduct
            {
                ProductCode = "P-TOGGLE-LINKED",
                ProductName = "Toggle Linked",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await SeedStoreRetailPriceAsync("S01", "P-TOGGLE-LINKED", purchasePrice: 4.28m, retailPrice: 11.99m);
            await _db.Insertable(new StoreMultiCodeProduct
            {
                UUID = "multi-code-toggle-linked",
                StoreCode = "S01",
                ProductCode = "P-TOGGLE-LINKED",
                MultiBarcode = "BAR-MULTI-TOGGLE",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            var service = CreateService();

            var result = await service.BatchToggleActiveAsync(
                new BatchToggleWarehouseProductsActiveRequestDto
                {
                    ProductCodes = new List<string> { "P-TOGGLE-LINKED" },
                    IsActive = false,
                }
            );

            var warehouseProduct = await _db.Queryable<WarehouseProduct>().SingleAsync(x => x.ProductCode == "P-TOGGLE-LINKED");
            var product = await _db.Queryable<Product>().SingleAsync(x => x.ProductCode == "P-TOGGLE-LINKED");
            var domesticProduct = await _db.Queryable<DomesticProduct>().SingleAsync(x => x.ProductCode == "P-TOGGLE-LINKED");
            var storeRetailPrice = await _db.Queryable<StoreRetailPrice>().SingleAsync(x => x.ProductCode == "P-TOGGLE-LINKED");
            var storeMultiCodeProduct = await _db.Queryable<StoreMultiCodeProduct>().SingleAsync(x => x.ProductCode == "P-TOGGLE-LINKED");

            Assert.True(result.Success);
            Assert.Equal(1, result.SuccessCount);
            Assert.Equal(0, result.FailedCount);
            Assert.False(warehouseProduct.IsActive);
            Assert.False(product.IsActive);
            Assert.False(domesticProduct.IsActive);
            Assert.False(storeRetailPrice.IsActive);
            Assert.False(storeMultiCodeProduct.IsActive);
            Assert.Equal("System", domesticProduct.UpdatedBy);
        }

        [Fact]
        public async Task BatchToggleActiveAsync_WhenWarehouseProductMissing_ReturnsPartialFailure()
        {
            await SeedPriceSyncProductAsync(
                "P-TOGGLE-EXISTS",
                purchasePrice: 4.28m,
                retailPrice: 11.99m,
                importPrice: 4.28m,
                oemPrice: 11.99m
            );
            var service = CreateService();

            var result = await service.BatchToggleActiveAsync(
                new BatchToggleWarehouseProductsActiveRequestDto
                {
                    ProductCodes = new List<string> { "P-TOGGLE-EXISTS", "P-TOGGLE-MISSING" },
                    IsActive = false,
                }
            );

            var warehouseProduct = await _db.Queryable<WarehouseProduct>().SingleAsync(x => x.ProductCode == "P-TOGGLE-EXISTS");

            Assert.False(result.Success);
            Assert.Equal(1, result.SuccessCount);
            Assert.Equal(1, result.FailedCount);
            Assert.Contains("仓库商品不存在: P-TOGGLE-MISSING", result.Errors);
            Assert.False(warehouseProduct.IsActive);
        }

        [Fact]
        public async Task BatchToggleActiveAsync_WhenOnlyLinkedProductExists_DoesNotUpdateLinkedTables()
        {
            await _db.Insertable(new Product
            {
                UUID = "product-toggle-linked-only",
                ProductCode = "P-TOGGLE-LINKED-ONLY",
                ProductName = "Linked Only",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new DomesticProduct
            {
                ProductCode = "P-TOGGLE-LINKED-ONLY",
                ProductName = "Linked Only Domestic",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await SeedStoreRetailPriceAsync("S01", "P-TOGGLE-LINKED-ONLY", purchasePrice: 4.28m, retailPrice: 11.99m);
            var service = CreateService();

            var result = await service.BatchToggleActiveAsync(
                new BatchToggleWarehouseProductsActiveRequestDto
                {
                    ProductCodes = new List<string> { "P-TOGGLE-LINKED-ONLY" },
                    IsActive = false,
                }
            );

            var product = await _db.Queryable<Product>().SingleAsync(x => x.ProductCode == "P-TOGGLE-LINKED-ONLY");
            var domesticProduct = await _db.Queryable<DomesticProduct>().SingleAsync(x => x.ProductCode == "P-TOGGLE-LINKED-ONLY");
            var storeRetailPrice = await _db.Queryable<StoreRetailPrice>().SingleAsync(x => x.ProductCode == "P-TOGGLE-LINKED-ONLY");

            Assert.False(result.Success);
            Assert.Equal(0, result.SuccessCount);
            Assert.Equal(1, result.FailedCount);
            Assert.Contains("仓库商品不存在: P-TOGGLE-LINKED-ONLY", result.Errors);
            Assert.True(product.IsActive);
            Assert.True(domesticProduct.IsActive);
            Assert.True(storeRetailPrice.IsActive);
        }

        [Fact]
        public async Task BatchToggleActiveAsync_WhenWarehouseProductDeleted_DoesNotUpdateLinkedTables()
        {
            await SeedPriceSyncProductAsync(
                "P-TOGGLE-DELETED",
                purchasePrice: 4.28m,
                retailPrice: 11.99m,
                importPrice: 4.28m,
                oemPrice: 11.99m
            );
            await _db.Updateable<WarehouseProduct>()
                .SetColumns(w => w.IsDeleted == true)
                .Where(w => w.ProductCode == "P-TOGGLE-DELETED")
                .ExecuteCommandAsync();
            await _db.Insertable(new DomesticProduct
            {
                ProductCode = "P-TOGGLE-DELETED",
                ProductName = "Deleted Warehouse Domestic",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await SeedStoreRetailPriceAsync("S01", "P-TOGGLE-DELETED", purchasePrice: 4.28m, retailPrice: 11.99m);
            var service = CreateService();

            var result = await service.BatchToggleActiveAsync(
                new BatchToggleWarehouseProductsActiveRequestDto
                {
                    ProductCodes = new List<string> { "P-TOGGLE-DELETED" },
                    IsActive = false,
                }
            );

            var warehouseProduct = await _db.Queryable<WarehouseProduct>().SingleAsync(x => x.ProductCode == "P-TOGGLE-DELETED");
            var product = await _db.Queryable<Product>().SingleAsync(x => x.ProductCode == "P-TOGGLE-DELETED");
            var domesticProduct = await _db.Queryable<DomesticProduct>().SingleAsync(x => x.ProductCode == "P-TOGGLE-DELETED");
            var storeRetailPrice = await _db.Queryable<StoreRetailPrice>().SingleAsync(x => x.ProductCode == "P-TOGGLE-DELETED");

            Assert.False(result.Success);
            Assert.Equal(0, result.SuccessCount);
            Assert.Equal(1, result.FailedCount);
            Assert.Contains("仓库商品不存在: P-TOGGLE-DELETED", result.Errors);
            Assert.True(warehouseProduct.IsActive);
            Assert.True(product.IsActive);
            Assert.True(domesticProduct.IsActive);
            Assert.True(storeRetailPrice.IsActive);
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

        [Fact]
        public async Task ImportFromDomesticAsync_已有英文名_商品主档使用英文名称()
        {
            await SeedDomesticImportProductAsync(
                productCode: "P-IMPORT-EN",
                productName: "夜光麦芽糖",
                englishName: "Glow-in-the-Dark Malts"
            );
            var service = CreateService();

            var result = await service.ImportFromDomesticAsync(new ImportFromDomesticRequestDto
            {
                ProductCodes = new List<string> { "P-IMPORT-EN" },
            });

            Assert.True(result.Success);
            Assert.Equal(1, result.SuccessCount);
            var product = await _db.Queryable<Product>()
                .Where(p => p.ProductCode == "P-IMPORT-EN")
                .SingleAsync();
            Assert.Equal("Glow-in-the-Dark Malts", product.ProductName);
            Assert.Equal("Glow-in-the-Dark Malts", product.EnglishName);
        }

        [Fact]
        public async Task ImportFromDomesticAsync_缺英文名_自动翻译并写回国内商品()
        {
            await SeedDomesticImportProductAsync(
                productCode: "P-IMPORT-TRANSLATE",
                productName: "光变爆珠5.5",
                englishName: null
            );
            var translationService = new Mock<ITranslationService>();
            translationService
                .Setup(x => x.BatchTranslateToEnglishAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new Dictionary<string, string>
                {
                    ["光变爆珠5.5"] = "Light-Changing Bursting Beads 5.5",
                });
            translationService
                .Setup(x => x.ContainsChinese(It.IsAny<string>()))
                .Returns<string>(ContainsChineseForTest);
            var service = CreateService(translationService.Object);

            var result = await service.ImportFromDomesticAsync(new ImportFromDomesticRequestDto
            {
                ProductCodes = new List<string> { "P-IMPORT-TRANSLATE" },
            });

            Assert.True(result.Success);
            var product = await _db.Queryable<Product>()
                .Where(p => p.ProductCode == "P-IMPORT-TRANSLATE")
                .SingleAsync();
            Assert.Equal("Light-Changing Bursting Beads 5.5", product.ProductName);
            Assert.Equal("Light-Changing Bursting Beads 5.5", product.EnglishName);
            var domestic = await _db.Queryable<DomesticProduct>()
                .Where(p => p.ProductCode == "P-IMPORT-TRANSLATE")
                .SingleAsync();
            Assert.Equal("Light-Changing Bursting Beads 5.5", domestic.EnglishProductName);
            translationService.Verify(x => x.BatchTranslateToEnglishAsync(
                It.Is<List<string>>(texts => texts.Contains("光变爆珠5.5"))),
                Times.Once);
        }

        [Fact]
        public async Task ImportFromDomesticAsync_翻译仍含中文_不污染英文字段()
        {
            await SeedDomesticImportProductAsync(
                productCode: "P-IMPORT-CHINESE-TRANSLATION",
                productName: "大黄油",
                englishName: null
            );
            var translationService = new Mock<ITranslationService>();
            translationService
                .Setup(x => x.BatchTranslateToEnglishAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new Dictionary<string, string> { ["大黄油"] = "大黄油" });
            translationService
                .Setup(x => x.ContainsChinese(It.IsAny<string>()))
                .Returns<string>(ContainsChineseForTest);
            var service = CreateService(translationService.Object);

            var result = await service.ImportFromDomesticAsync(new ImportFromDomesticRequestDto
            {
                ProductCodes = new List<string> { "P-IMPORT-CHINESE-TRANSLATION" },
            });

            Assert.True(result.Success);
            var product = await _db.Queryable<Product>()
                .Where(p => p.ProductCode == "P-IMPORT-CHINESE-TRANSLATION")
                .SingleAsync();
            Assert.Equal("大黄油", product.ProductName);
            Assert.Null(product.EnglishName);
            var domestic = await _db.Queryable<DomesticProduct>()
                .Where(p => p.ProductCode == "P-IMPORT-CHINESE-TRANSLATION")
                .SingleAsync();
            Assert.Null(domestic.EnglishProductName);
        }

        [Fact]
        public async Task ImportFromDomesticAsync_已有商品仍是国内中文名_智能补英文()
        {
            await SeedDomesticImportProductAsync(
                productCode: "P-IMPORT-EXISTING-CHINESE",
                productName: "5.5果胶",
                englishName: "5.5 Fruit Gel"
            );
            await _db.Insertable(new Product
            {
                UUID = "product-existing-chinese",
                ProductCode = "P-IMPORT-EXISTING-CHINESE",
                ProductName = "5.5果胶",
                EnglishName = null,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            var service = CreateService();

            var result = await service.ImportFromDomesticAsync(new ImportFromDomesticRequestDto
            {
                ProductCodes = new List<string> { "P-IMPORT-EXISTING-CHINESE" },
            });

            Assert.True(result.Success);
            var product = await _db.Queryable<Product>()
                .Where(p => p.ProductCode == "P-IMPORT-EXISTING-CHINESE")
                .SingleAsync();
            Assert.Equal("5.5 Fruit Gel", product.ProductName);
            Assert.Equal("5.5 Fruit Gel", product.EnglishName);
        }

        [Fact]
        public async Task ImportFromDomesticAsync_已有商品人工名称_不覆盖商品名称()
        {
            await SeedDomesticImportProductAsync(
                productCode: "P-IMPORT-CUSTOM",
                productName: "小熊",
                englishName: "Bear"
            );
            await _db.Insertable(new Product
            {
                UUID = "product-existing-custom",
                ProductCode = "P-IMPORT-CUSTOM",
                ProductName = "Custom Display Name",
                EnglishName = "Custom English",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            var service = CreateService();

            var result = await service.ImportFromDomesticAsync(new ImportFromDomesticRequestDto
            {
                ProductCodes = new List<string> { "P-IMPORT-CUSTOM" },
            });

            Assert.True(result.Success);
            var product = await _db.Queryable<Product>()
                .Where(p => p.ProductCode == "P-IMPORT-CUSTOM")
                .SingleAsync();
            Assert.Equal("Custom Display Name", product.ProductName);
            Assert.Equal("Custom English", product.EnglishName);
        }

        [Fact]
        public async Task GetDomesticProductsNotInWarehouseAsync_弹窗商品名称优先显示英文()
        {
            await SeedDomesticImportProductAsync(
                productCode: "P-IMPORT-LIST",
                productName: "夜光5.5",
                englishName: "Glow-in-the-Dark 5.5"
            );
            var service = CreateService();

            var result = await service.GetDomesticProductsNotInWarehouseAsync(
                new GetDomesticProductsNotInWarehouseRequestDto
                {
                    Page = 1,
                    PageSize = 20,
                    GlobalSearch = "P-IMPORT-LIST",
                }
            );

            var item = Assert.Single(result.Items);
            Assert.Equal("Glow-in-the-Dark 5.5", item.ProductName);
            Assert.Equal("Glow-in-the-Dark 5.5", item.EnglishName);
        }

        [Fact]
        public async Task GetMobileLocationPrintPayloadAsync_ReturnsProductDescriptionAndMiddlePackage()
        {
            await _db.Insertable(new Product
            {
                UUID = "product-mobile-location-label",
                ProductCode = "P-LABEL-001",
                ProductName = "3D TOYS",
                ItemNumber = "HB313-129",
                Barcode = "9525813130129",
                MiddlePackageQuantity = 24,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new WarehouseProduct
            {
                ProductCode = "P-LABEL-001",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new Location
            {
                LocationGuid = "loc-label-001",
                LocationCode = "A-00-00-01",
                LocationBarcode = "5544492778828",
                LocationType = 1,
                Status = 1,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new ProductLocation
            {
                Guid = "pl-label-001",
                ProductCode = "P-LABEL-001",
                LocationGuid = "loc-label-001",
                IsDeleted = false,
            }).ExecuteCommandAsync();
            var service = CreateService();

            var payload = await service.GetMobileLocationPrintPayloadAsync("P-LABEL-001");

            Assert.NotNull(payload);
            Assert.Equal("loc-label-001", payload!.LocationGuid);
            Assert.Equal("A-00-00-01", payload.LocationCode);
            Assert.Equal("5544492778828", payload.LocationBarcode);
            Assert.Equal("HB313-129", payload.ItemNumber);
            Assert.Equal("3D TOYS", payload.ProductName);
            Assert.Equal(24, payload.MiddlePackageQuantity);
            Assert.Equal(1, payload.ProductCount);
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

        private async Task SeedWarehouseCategoryAsync(
            string categoryGuid,
            string? parentGuid,
            string categoryName
        )
        {
            await _db.Insertable(new WarehouseCategory
            {
                CategoryGUID = categoryGuid,
                ParentGUID = parentGuid,
                CategoryName = categoryName,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
        }

        private async Task SeedWarehouseTableProductAsync(
            string productCode,
            string itemNumber,
            string productName,
            string? warehouseCategoryGuid
        )
        {
            await _db.Insertable(new Product
            {
                UUID = $"{productCode}-uuid",
                ProductCode = productCode,
                ItemNumber = itemNumber,
                ProductName = productName,
                WarehouseCategoryGUID = warehouseCategoryGuid,
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

        private async Task SeedDomesticImportProductAsync(
            string productCode,
            string productName,
            string? englishName
        )
        {
            await _db.Insertable(new DomesticProduct
            {
                ProductCode = productCode,
                HBProductNo = productCode,
                Barcode = $"BAR-{productCode}",
                ProductName = productName,
                EnglishProductName = englishName,
                ProductType = 0,
                DomesticPrice = 2.1m,
                OEMPrice = 4.99m,
                ImportPrice = 1.2m,
                UnitVolume = 0.069m,
                ProductImage = $"/{productCode}.jpg",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
        }

        private ProductWarehouseReactService CreateService(ITranslationService? translationService = null)
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
                Mock.Of<IDataSyncFullService>(),
                translationService ?? CreateDefaultTranslationService()
            );
        }

        private static ITranslationService CreateDefaultTranslationService()
        {
            var translationService = new Mock<ITranslationService>();
            translationService
                .Setup(x => x.BatchTranslateToEnglishAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new Dictionary<string, string>());
            translationService
                .Setup(x => x.ContainsChinese(It.IsAny<string>()))
                .Returns<string>(ContainsChineseForTest);
            return translationService.Object;
        }

        private static bool ContainsChineseForTest(string text)
        {
            return !string.IsNullOrWhiteSpace(text)
                && text.Any(c => c >= '\u4e00' && c <= '\u9fff');
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
