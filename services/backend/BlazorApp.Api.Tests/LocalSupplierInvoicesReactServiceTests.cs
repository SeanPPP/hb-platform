using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public sealed class LocalSupplierInvoicesReactServiceTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _sqliteConnection;
        private readonly SqlSugarClient _db;

        public LocalSupplierInvoicesReactServiceTests()
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
                MoreSettings = new ConnMoreSettings(),
            });

            _db.CodeFirst.InitTables(
                typeof(Store),
                typeof(UserStore),
                typeof(HBLocalSupplier),
                typeof(Product),
                typeof(StoreRetailPrice),
                typeof(StoreMultiCodeProduct),
                typeof(ProductSetCode),
                typeof(StoreLocalSupplierInvoice),
                typeof(StoreLocalSupplierInvoiceDetails)
            );
        }

        [Fact]
        public async Task GetGridDataAsync_DefaultsToOrderDateDescendingAndClampsPageSizeTo20()
        {
            await SeedStoreAndSupplierAsync();
            for (var index = 1; index <= 25; index++)
            {
                await _db.Insertable(new StoreLocalSupplierInvoice
                {
                    InvoiceGUID = $"invoice-{index:00}",
                    StoreCode = "S01",
                    SupplierCode = "SUP01",
                    InvoiceNo = $"INV-{index:00}",
                    OrderDate = new DateTime(2026, 1, index),
                    CreatedAt = new DateTime(2026, 2, 26 - index),
                    IsDeleted = false,
                }).ExecuteCommandAsync();
            }

            var result = await CreateService().GetGridDataAsync(new GridRequestDto
            {
                StartRow = 0,
                PageSize = 999,
            });

            Assert.True(result.Success, result.Message);
            Assert.Equal(25, result.Total);
            Assert.Equal(20, result.Items?.Count);
            Assert.Equal("INV-25", result.Items?.First().InvoiceNo);
        }

        [Fact]
        public async Task GetGridDataAsync_OrderDateInRangeIncludesTheWholeEndDate()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("before", "INV-1", new DateTime(2026, 1, 1, 23, 0, 0));
            await InsertInvoiceAsync("inside-start", "INV-2", new DateTime(2026, 1, 2, 0, 0, 0));
            await InsertInvoiceAsync("inside-end", "INV-3", new DateTime(2026, 1, 5, 23, 59, 0));
            await InsertInvoiceAsync("after", "INV-4", new DateTime(2026, 1, 6, 0, 0, 0));

            var result = await CreateService().GetGridDataAsync(new GridRequestDto
            {
                StartRow = 0,
                PageSize = 20,
                FilterModel = new Dictionary<string, FilterModelDto>
                {
                    ["orderDate"] = new()
                    {
                        FilterType = "date",
                        Type = "inRange",
                        Filter = "2026-01-02",
                        FilterTo = "2026-01-05",
                    },
                },
            });

            Assert.True(result.Success, result.Message);
            Assert.Equal(2, result.Total);
            Assert.Equal(new[] { "INV-3", "INV-2" }, result.Items?.Select(item => item.InvoiceNo).ToArray());
        }

        [Fact]
        public async Task GetGridDataAsync_统计当前页涨跌价商品数量()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-price-up", "INV-UP", new DateTime(2026, 1, 7));
            await InsertInvoiceAsync("invoice-price-flat", "INV-FLAT", new DateTime(2026, 1, 6));

            await _db.Insertable(new[]
            {
                new StoreLocalSupplierInvoiceDetails
                {
                    DetailGUID = "detail-up-1",
                    InvoiceGUID = "invoice-price-up",
                    LastPurchasePrice = 2.00m,
                    PurchasePrice = 2.50m,
                    IsDeleted = false,
                },
                new StoreLocalSupplierInvoiceDetails
                {
                    DetailGUID = "detail-up-2",
                    InvoiceGUID = "invoice-price-up",
                    LastPurchasePrice = 1.00m,
                    PurchasePrice = 1.20m,
                    IsDeleted = false,
                },
                new StoreLocalSupplierInvoiceDetails
                {
                    DetailGUID = "detail-flat",
                    InvoiceGUID = "invoice-price-up",
                    LastPurchasePrice = 3.00m,
                    PurchasePrice = 3.00m,
                    IsDeleted = false,
                },
                new StoreLocalSupplierInvoiceDetails
                {
                    DetailGUID = "detail-down",
                    InvoiceGUID = "invoice-price-up",
                    LastPurchasePrice = 5.00m,
                    PurchasePrice = 4.80m,
                    IsDeleted = false,
                },
                new StoreLocalSupplierInvoiceDetails
                {
                    DetailGUID = "detail-zero-last-price",
                    InvoiceGUID = "invoice-price-up",
                    LastPurchasePrice = 0m,
                    PurchasePrice = 1.00m,
                    IsDeleted = false,
                },
                new StoreLocalSupplierInvoiceDetails
                {
                    DetailGUID = "detail-null-last-price",
                    InvoiceGUID = "invoice-price-up",
                    LastPurchasePrice = null,
                    PurchasePrice = 1.00m,
                    IsDeleted = false,
                },
                new StoreLocalSupplierInvoiceDetails
                {
                    DetailGUID = "detail-null-purchase-price",
                    InvoiceGUID = "invoice-price-up",
                    LastPurchasePrice = 1.00m,
                    PurchasePrice = null,
                    IsDeleted = false,
                },
                new StoreLocalSupplierInvoiceDetails
                {
                    DetailGUID = "detail-deleted",
                    InvoiceGUID = "invoice-price-up",
                    LastPurchasePrice = 1.00m,
                    PurchasePrice = 9.00m,
                    IsDeleted = true,
                },
                new StoreLocalSupplierInvoiceDetails
                {
                    DetailGUID = "detail-other-invoice-down",
                    InvoiceGUID = "invoice-price-flat",
                    LastPurchasePrice = 4.00m,
                    PurchasePrice = 3.50m,
                    IsDeleted = false,
                },
            }).ExecuteCommandAsync();

            var result = await CreateService().GetGridDataAsync(new GridRequestDto
            {
                StartRow = 0,
                PageSize = 20,
            });

            Assert.True(result.Success, result.Message);
            var priceUpInvoice = Assert.Single(result.Items!, item => item.InvoiceGUID == "invoice-price-up");
            var flatInvoice = Assert.Single(result.Items!, item => item.InvoiceGUID == "invoice-price-flat");
            Assert.Equal(2, priceUpInvoice.PriceIncreaseItemCount);
            Assert.Equal(1, priceUpInvoice.PriceDecreaseItemCount);
            Assert.Equal(0, flatInvoice.PriceIncreaseItemCount);
            Assert.Equal(1, flatInvoice.PriceDecreaseItemCount);
        }

        [Fact]
        public async Task GetDetailsGridAsync_ReturnsPagedDetailsAndClampsPageSizeTo50()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-1", "INV-1", new DateTime(2026, 1, 7));
            for (var index = 1; index <= 55; index++)
            {
                await _db.Insertable(new StoreLocalSupplierInvoiceDetails
                {
                    DetailGUID = $"detail-{index:00}",
                    InvoiceGUID = "invoice-1",
                    StoreCode = "S01",
                    SupplierCode = "SUP01",
                    ProductCode = $"P{index:00}",
                    ItemNumber = $"ITEM-{index:00}",
                    Barcode = $"BAR-{index:00}",
                    ProductName = $"Product {index:00}",
                    PurchasePrice = index,
                    Quantity = index,
                    CreatedAt = new DateTime(2026, 1, 1).AddMinutes(index),
                    IsDeleted = false,
                }).ExecuteCommandAsync();
            }

            var result = await CreateService().GetDetailsGridAsync("invoice-1", new GridRequestDto
            {
                StartRow = 0,
                PageSize = 999,
            });

            Assert.True(result.Success, result.Message);
            Assert.Equal(55, result.Total);
            Assert.Equal(50, result.Items?.Count);
            Assert.Equal("detail-55", result.Items?.First().DetailGUID);
        }

        [Fact]
        public async Task GetDetailsGridAsync_按有效上次进货价筛选涨价和减价明细()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-price-change", "INV-PRICE", new DateTime(2026, 1, 8));

            await _db.Insertable(new[]
            {
                new StoreLocalSupplierInvoiceDetails
                {
                    DetailGUID = "detail-up",
                    InvoiceGUID = "invoice-price-change",
                    LastPurchasePrice = 2.00m,
                    PurchasePrice = 2.50m,
                    CreatedAt = new DateTime(2026, 1, 1, 10, 0, 0),
                    IsDeleted = false,
                },
                new StoreLocalSupplierInvoiceDetails
                {
                    DetailGUID = "detail-down",
                    InvoiceGUID = "invoice-price-change",
                    LastPurchasePrice = 3.00m,
                    PurchasePrice = 2.80m,
                    CreatedAt = new DateTime(2026, 1, 1, 11, 0, 0),
                    IsDeleted = false,
                },
                new StoreLocalSupplierInvoiceDetails
                {
                    DetailGUID = "detail-flat",
                    InvoiceGUID = "invoice-price-change",
                    LastPurchasePrice = 4.00m,
                    PurchasePrice = 4.00m,
                    CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0),
                    IsDeleted = false,
                },
                new StoreLocalSupplierInvoiceDetails
                {
                    DetailGUID = "detail-zero-last-price",
                    InvoiceGUID = "invoice-price-change",
                    LastPurchasePrice = 0m,
                    PurchasePrice = 9.00m,
                    CreatedAt = new DateTime(2026, 1, 1, 13, 0, 0),
                    IsDeleted = false,
                },
                new StoreLocalSupplierInvoiceDetails
                {
                    DetailGUID = "detail-null-last-price",
                    InvoiceGUID = "invoice-price-change",
                    LastPurchasePrice = null,
                    PurchasePrice = 1.00m,
                    CreatedAt = new DateTime(2026, 1, 1, 14, 0, 0),
                    IsDeleted = false,
                },
                new StoreLocalSupplierInvoiceDetails
                {
                    DetailGUID = "detail-null-purchase-price",
                    InvoiceGUID = "invoice-price-change",
                    LastPurchasePrice = 1.00m,
                    PurchasePrice = null,
                    CreatedAt = new DateTime(2026, 1, 1, 15, 0, 0),
                    IsDeleted = false,
                },
            }).ExecuteCommandAsync();

            var upResult = await CreateService().GetDetailsGridAsync("invoice-price-change", new GridRequestDto
            {
                StartRow = 0,
                PageSize = 50,
                FilterModel = new Dictionary<string, FilterModelDto>
                {
                    ["priceChange"] = new()
                    {
                        FilterType = "text",
                        Type = "equals",
                        Filter = "up",
                    },
                },
            });
            var downResult = await CreateService().GetDetailsGridAsync("invoice-price-change", new GridRequestDto
            {
                StartRow = 0,
                PageSize = 50,
                FilterModel = new Dictionary<string, FilterModelDto>
                {
                    ["priceChange"] = new()
                    {
                        FilterType = "text",
                        Type = "equals",
                        Filter = "down",
                    },
                },
            });

            Assert.True(upResult.Success, upResult.Message);
            Assert.Equal(1, upResult.Total);
            Assert.Equal("detail-up", Assert.Single(upResult.Items!).DetailGUID);
            Assert.True(downResult.Success, downResult.Message);
            Assert.Equal(1, downResult.Total);
            Assert.Equal("detail-down", Assert.Single(downResult.Items!).DetailGUID);
        }

        [Fact]
        public async Task CheckProductsAsync_WhenProductNoLongerMatches_ClearsProductFieldsAndRecalculatesAutoPricingPreview()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-check", "INV-CHECK", new DateTime(2026, 1, 8));
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-stale",
                InvoiceGUID = "invoice-check",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "MISSING",
                Barcode = "NEW-BARCODE",
                ProductName = "Missing Product",
                Quantity = 1,
                PurchasePrice = 2.00m,
                ProductCode = "OLD-PRODUCT",
                StoreProductCode = "OLD-STORE-PRODUCT",
                LastPurchasePrice = 9.99m,
                AutoPricing = true,
                IsSpecialProduct = true,
                DiscountRate = 0.20m,
                PricingFloatRate = 300m,
                NewAutoRetailPrice = 8.88m,
                ExistingProductCount = 1,
                BarcodeStatus = 2,
                BarcodeMatchCount = 4,
                ActivityType = (int)DetailAction.UpdatePurchasePrice,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().CheckProductsAsync(new CheckProductsRequest
            {
                InvoiceGuid = "invoice-check",
                DetailGuids = new List<string> { "detail-stale" },
            });

            Assert.True(result.Success);

            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .FirstAsync(x => x.DetailGUID == "detail-stale");
            Assert.Null(detail.ProductCode);
            Assert.Null(detail.StoreProductCode);
            Assert.Equal(9.99m, detail.LastPurchasePrice);
            Assert.True(detail.AutoPricing);
            Assert.Null(detail.IsSpecialProduct);
            Assert.Null(detail.DiscountRate);
            Assert.Equal(250m, detail.PricingFloatRate);
            Assert.Equal(5.00m, detail.NewAutoRetailPrice);
            Assert.Equal(0, detail.ExistingProductCount);
            Assert.Equal(1, detail.BarcodeStatus);
            Assert.Equal(0, detail.BarcodeMatchCount);
            Assert.Equal((int)DetailAction.CreateProduct, detail.ActivityType);
        }

        [Fact]
        public async Task CheckProductsAsync_LastPurchasePrice为空时_补充分店上次进货价()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-check-last-price-empty", "INV-CHECK-LAST-EMPTY", new DateTime(2026, 1, 8));
            await _db.Insertable(new Product
            {
                UUID = "product-last-empty",
                ProductCode = "P-LAST-EMPTY",
                ItemNumber = "LAST-EMPTY",
                Barcode = "BAR-LAST-EMPTY",
                ProductName = "Last Price Empty Product",
                LocalSupplierCode = "SUP01",
                PurchasePrice = 4.44m,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreRetailPrice
            {
                UUID = "store-price-last-empty",
                StoreCode = "S01",
                ProductCode = "P-LAST-EMPTY",
                StoreProductCode = "S01-LAST-EMPTY",
                SupplierCode = "SUP01",
                PurchasePrice = 3.33m,
                StoreRetailPriceValue = 6.66m,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-last-empty",
                InvoiceGUID = "invoice-check-last-price-empty",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "LAST-EMPTY",
                Barcode = "BAR-LAST-EMPTY",
                ProductName = "Last Price Empty Product",
                PurchasePrice = 5.55m,
                LastPurchasePrice = null,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().CheckProductsAsync(new CheckProductsRequest
            {
                InvoiceGuid = "invoice-check-last-price-empty",
                DetailGuids = new List<string> { "detail-last-empty" },
            });

            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .SingleAsync(x => x.DetailGUID == "detail-last-empty");

            Assert.True(result.Success, result.Message);
            Assert.Equal(3.33m, detail.LastPurchasePrice);
        }

        [Fact]
        public async Task CheckProductsAsync_LastPurchasePrice已有值时_不覆盖()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-check-last-price-existing", "INV-CHECK-LAST-EXISTING", new DateTime(2026, 1, 8));
            await _db.Insertable(new Product
            {
                UUID = "product-last-existing",
                ProductCode = "P-LAST-EXISTING",
                ItemNumber = "LAST-EXISTING",
                Barcode = "BAR-LAST-EXISTING",
                ProductName = "Last Price Existing Product",
                LocalSupplierCode = "SUP01",
                PurchasePrice = 4.44m,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreRetailPrice
            {
                UUID = "store-price-last-existing",
                StoreCode = "S01",
                ProductCode = "P-LAST-EXISTING",
                StoreProductCode = "S01-LAST-EXISTING",
                SupplierCode = "SUP01",
                PurchasePrice = 3.33m,
                StoreRetailPriceValue = 6.66m,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-last-existing",
                InvoiceGUID = "invoice-check-last-price-existing",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "LAST-EXISTING",
                Barcode = "BAR-LAST-EXISTING",
                ProductName = "Last Price Existing Product",
                PurchasePrice = 5.55m,
                LastPurchasePrice = 9.99m,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().CheckProductsAsync(new CheckProductsRequest
            {
                InvoiceGuid = "invoice-check-last-price-existing",
                DetailGuids = new List<string> { "detail-last-existing" },
            });

            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .SingleAsync(x => x.DetailGUID == "detail-last-existing");

            Assert.True(result.Success, result.Message);
            Assert.Equal(9.99m, detail.LastPurchasePrice);
        }

        [Fact]
        public async Task CheckProductsAsync_分店价缺失时_用商品主档价补空上次进货价()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-check-last-price-product", "INV-CHECK-LAST-PRODUCT", new DateTime(2026, 1, 8));
            await _db.Insertable(new Product
            {
                UUID = "product-last-product",
                ProductCode = "P-LAST-PRODUCT",
                ItemNumber = "LAST-PRODUCT",
                Barcode = "BAR-LAST-PRODUCT",
                ProductName = "Last Price Product Fallback",
                LocalSupplierCode = "SUP01",
                PurchasePrice = 4.44m,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-last-product",
                InvoiceGUID = "invoice-check-last-price-product",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "LAST-PRODUCT",
                Barcode = "BAR-LAST-PRODUCT",
                ProductName = "Last Price Product Fallback",
                PurchasePrice = 5.55m,
                LastPurchasePrice = null,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().CheckProductsAsync(new CheckProductsRequest
            {
                InvoiceGuid = "invoice-check-last-price-product",
                DetailGuids = new List<string> { "detail-last-product" },
            });

            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .SingleAsync(x => x.DetailGUID == "detail-last-product");

            Assert.True(result.Success, result.Message);
            Assert.Equal(4.44m, detail.LastPurchasePrice);
        }

        [Fact]
        public async Task CheckProductsAsync_商品不存在且有进货价_应计算定价浮率和新自动零售价()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-check-missing-auto", "INV-CHECK-MISSING-AUTO", new DateTime(2026, 1, 16));
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-check-missing-auto",
                InvoiceGUID = "invoice-check-missing-auto",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "NEW-AUTO-001",
                Barcode = "9300000000012",
                ProductName = "Missing Auto Product",
                Quantity = 1,
                PurchasePrice = 4.00m,
                Amount = 4.00m,
                AutoPricing = true,
                ExistingProductCount = 0,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().CheckProductsAsync(new CheckProductsRequest
            {
                InvoiceGuid = "invoice-check-missing-auto",
                DetailGuids = new List<string> { "detail-check-missing-auto" },
            });

            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .SingleAsync(x => x.DetailGUID == "detail-check-missing-auto");

            Assert.True(result.Success, result.Message);
            Assert.Equal(0, detail.ExistingProductCount);
            Assert.Equal(250m, detail.PricingFloatRate);
            Assert.Equal(10.00m, detail.NewAutoRetailPrice);
        }

        [Fact]
        public async Task CheckProductsAsync_新商品明细自动定价为空_检测后默认自动()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-check-new-auto-default", "INV-CHECK-NEW-AUTO-DEFAULT", new DateTime(2026, 1, 18));
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-new-auto-default",
                InvoiceGUID = "invoice-check-new-auto-default",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "NEW-AUTO-DEFAULT",
                Barcode = "9300000000014",
                ProductName = "New Auto Default Product",
                Quantity = 1,
                PurchasePrice = 3.00m,
                Amount = 3.00m,
                AutoPricing = null,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().CheckProductsAsync(new CheckProductsRequest
            {
                InvoiceGuid = "invoice-check-new-auto-default",
                DetailGuids = new List<string> { "detail-new-auto-default" },
            });

            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .SingleAsync(x => x.DetailGUID == "detail-new-auto-default");

            Assert.True(result.Success, result.Message);
            Assert.Equal(0, detail.ExistingProductCount);
            Assert.True(detail.AutoPricing);
            Assert.Equal(250m, detail.PricingFloatRate);
            Assert.Equal(7.50m, detail.NewAutoRetailPrice);
        }

        [Fact]
        public async Task CheckProductsAsync_明细已改自动定价_不应被分店价格覆盖为否()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-check-existing-auto", "INV-CHECK-EXISTING-AUTO", new DateTime(2026, 1, 17));
            await _db.Insertable(new Product
            {
                UUID = "product-existing-auto",
                ProductCode = "P-AUTO-001",
                ItemNumber = "AUTO-001",
                Barcode = "9300000000013",
                ProductName = "Existing Auto Product",
                LocalSupplierCode = "SUP01",
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreRetailPrice
            {
                UUID = "store-price-existing-auto",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ProductCode = "P-AUTO-001",
                StoreProductCode = "S01-AUTO-001",
                PurchasePrice = 1.00m,
                StoreRetailPriceValue = 2.00m,
                IsAutoPricing = false,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-existing-auto",
                InvoiceGUID = "invoice-check-existing-auto",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "AUTO-001",
                Barcode = "9300000000013",
                ProductName = "Existing Auto Product",
                Quantity = 1,
                PurchasePrice = 4.00m,
                Amount = 4.00m,
                AutoPricing = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().CheckProductsAsync(new CheckProductsRequest
            {
                InvoiceGuid = "invoice-check-existing-auto",
                DetailGuids = new List<string> { "detail-existing-auto" },
            });

            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .SingleAsync(x => x.DetailGUID == "detail-existing-auto");

            Assert.True(result.Success, result.Message);
            Assert.Equal(1, detail.ExistingProductCount);
            Assert.True(detail.AutoPricing);
            Assert.Equal(250m, detail.PricingFloatRate);
            Assert.Equal(10.00m, detail.NewAutoRetailPrice);
        }

        [Fact]
        public async Task CheckProductsAsync_UsesIndexedItemNumberLookupForUppercaseItems()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-indexed", "INV-INDEXED", new DateTime(2026, 1, 9));
            await _db.Insertable(new Product
            {
                UUID = "product-indexed",
                ProductCode = "P-INDEXED",
                ItemNumber = "ITEM-INDEXED",
                Barcode = "BAR-INDEXED",
                ProductName = "Indexed Product",
                LocalSupplierCode = "SUP01",
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-indexed",
                InvoiceGUID = "invoice-indexed",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "ITEM-INDEXED",
                Barcode = "BAR-INDEXED",
                ProductName = "Indexed Product",
                Quantity = 1,
                PurchasePrice = 2.00m,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var executedSql = new List<string>();
            _db.Aop.OnLogExecuting = (sql, _) => executedSql.Add(sql);

            var result = await CreateService().CheckProductsAsync(new CheckProductsRequest
            {
                InvoiceGuid = "invoice-indexed",
            });

            Assert.True(result.Success);
            Assert.DoesNotContain(
                executedSql,
                sql => sql.Contains("UPPER", StringComparison.OrdinalIgnoreCase)
                    && sql.Contains("ItemNumber", StringComparison.OrdinalIgnoreCase)
            );
        }

        [Fact]
        public async Task CheckProductsAsync_FallsBackToCaseInsensitiveItemNumberLookup()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-case", "INV-CASE", new DateTime(2026, 1, 9));
            await _db.Insertable(new Product
            {
                UUID = "product-case",
                ProductCode = "P-CASE",
                ItemNumber = "item-case",
                Barcode = "BAR-CASE",
                ProductName = "Case Product",
                LocalSupplierCode = "SUP01",
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-case",
                InvoiceGUID = "invoice-case",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "ITEM-CASE",
                Barcode = "BAR-CASE",
                ProductName = "Case Product",
                Quantity = 1,
                PurchasePrice = 2.00m,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var executedSql = new List<string>();
            _db.Aop.OnLogExecuting = (sql, _) => executedSql.Add(sql);

            var result = await CreateService().CheckProductsAsync(new CheckProductsRequest
            {
                InvoiceGuid = "invoice-case",
            });

            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .FirstAsync(x => x.DetailGUID == "detail-case");

            Assert.True(result.Success);
            Assert.Equal("P-CASE", detail.ProductCode);
            Assert.Equal(1, detail.ExistingProductCount);
            Assert.Equal((int)DetailAction.UpdatePurchasePrice, detail.ActivityType);
            Assert.Contains(
                executedSql,
                sql => sql.Contains("UPPER", StringComparison.OrdinalIgnoreCase)
                    && sql.Contains("ItemNumber", StringComparison.OrdinalIgnoreCase)
            );
        }

        [Fact]
        public async Task CheckProductsAsync_主商品匹配且有副条码时_默认添加多码()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-add-multi-check", "INV-ADD-MULTI-CHECK", new DateTime(2026, 1, 9));
            await _db.Insertable(new Product
            {
                UUID = "product-add-multi-check",
                ProductCode = "P-ADD-MULTI-CHECK",
                ItemNumber = "88842",
                Barcode = "191554882676",
                ProductName = "Men Travel Perfume Assorted 35mL",
                LocalSupplierCode = "SUP01",
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-add-multi-check",
                InvoiceGUID = "invoice-add-multi-check",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "88842",
                Barcode = "191554882676",
                AdditionalBarcodesJson = JsonSerializer.Serialize(new[]
                {
                    "191554882690",
                    "191554882669",
                }),
                ProductName = "Men Travel Perfume Assorted 35mL",
                Quantity = 48,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().CheckProductsAsync(new CheckProductsRequest
            {
                InvoiceGuid = "invoice-add-multi-check",
                DetailGuids = new List<string> { "detail-add-multi-check" },
            });

            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .SingleAsync(x => x.DetailGUID == "detail-add-multi-check");

            Assert.True(result.Success, result.Message);
            Assert.Equal("P-ADD-MULTI-CHECK", detail.ProductCode);
            Assert.Equal((int)DetailAction.AddMultiCode, detail.ActivityType);
        }

        [Fact]
        public async Task QueryInChunksParallelAsync_多Chunk查询使用独立连接()
        {
            var service = CreateService();
            var method = typeof(LocalSupplierInvoicesReactService)
                .GetMethod("QueryInChunksParallelAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(typeof(string), typeof(string));
            var keys = Enumerable.Range(1, 6).Select(index => $"K{index:000}").ToList();
            var clientsByFirstKey = new ConcurrentDictionary<string, ISqlSugarClient>();

            Func<ISqlSugarClient, List<string>, Task<List<string>>> fetch = (db, chunk) =>
            {
                clientsByFirstKey[chunk[0]] = db;
                return Task.FromResult(chunk.ToList());
            };

            var task = (Task<List<string>>)method.Invoke(
                service,
                new object[] { keys, 2, fetch, 3 }
            )!;
            var result = await task;

            Assert.Equal(keys, result);
            Assert.Equal(3, clientsByFirstKey.Count);
            Assert.NotSame(clientsByFirstKey["K001"], clientsByFirstKey["K003"]);
            Assert.NotSame(clientsByFirstKey["K003"], clientsByFirstKey["K005"]);
        }

        [Fact]
        public void BatchExecuteActionsRequestDto_包含确认契约字段()
        {
            Assert.NotNull(typeof(BatchExecuteActionsRequestDto).GetProperty("ExpectedActions"));
            Assert.NotNull(typeof(BatchExecuteActionsRequestDto).GetProperty("ConfirmedCreateProductCount"));
            Assert.NotNull(typeof(BatchExecuteActionsRequestDto).GetProperty("ConfirmedAt"));

            var actionDtoType = typeof(BatchExecuteActionsRequestDto).Assembly.GetType(
                "BlazorApp.Shared.DTOs.BatchExecuteExpectedActionDto"
            );

            Assert.NotNull(actionDtoType);
            Assert.NotNull(actionDtoType!.GetProperty("DetailGuid"));
            Assert.NotNull(actionDtoType.GetProperty("ActivityType"));
            Assert.NotNull(actionDtoType.GetProperty("Action"));
        }

        [Fact]
        public async Task BatchExecuteActionsAsync_RejectsEmptyDetailGuids()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-empty", "INV-EMPTY", new DateTime(2026, 1, 9));

            var result = await CreateService().BatchExecuteActionsAsync(
                "invoice-empty",
                new List<string>(),
                "tester"
            );

            Assert.False(result.Success);
            Assert.Equal("VALIDATION_ERROR", result.Code);
        }

        [Fact]
        public async Task BatchExecuteActions_确认动作与当前数据库不一致时_拒绝执行()
        {
            await SeedExecutablePriceUpdateAsync();

            var controller = CreateController();
            var actionResult = await controller.BatchExecuteActions(
                "invoice-execute",
                new BatchExecuteActionsRequestDto
                {
                    DetailGuids = new List<string> { "detail-price" },
                    ExpectedActions = new List<BatchExecuteExpectedActionDto>
                    {
                        new()
                        {
                            DetailGuid = "detail-price",
                            ActivityType = (int)DetailAction.AddMultiCode,
                        },
                    },
                    ConfirmedCreateProductCount = 0,
                    ConfirmedAt = DateTime.UtcNow,
                }
            );

            var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult);
            Assert.False(ReadAnonymousProperty<bool>(badRequest.Value!, "success"));
            Assert.Equal("VALIDATION_ERROR", ReadAnonymousProperty<string>(badRequest.Value!, "code"));
            Assert.Contains("确认", ReadAnonymousProperty<string>(badRequest.Value!, "message"));
            Assert.NotNull(ReadAnonymousProperty<object>(badRequest.Value!, "details"));
        }

        [Fact]
        public async Task BatchExecuteActions_确认创建商品数量与当前数据库不一致时_拒绝执行()
        {
            await SeedExecutablePriceUpdateAsync();

            var controller = CreateController();
            var actionResult = await controller.BatchExecuteActions(
                "invoice-execute",
                new BatchExecuteActionsRequestDto
                {
                    DetailGuids = new List<string> { "detail-price" },
                    ExpectedActions = new List<BatchExecuteExpectedActionDto>
                    {
                        new()
                        {
                            DetailGuid = "detail-price",
                            ActivityType = (int)DetailAction.UpdatePurchasePrice,
                        },
                    },
                    ConfirmedCreateProductCount = 1,
                    ConfirmedAt = DateTime.UtcNow,
                }
            );

            var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult);
            Assert.False(ReadAnonymousProperty<bool>(badRequest.Value!, "success"));
            Assert.Equal("VALIDATION_ERROR", ReadAnonymousProperty<string>(badRequest.Value!, "code"));
            Assert.Contains("创建商品", ReadAnonymousProperty<string>(badRequest.Value!, "message"));
            Assert.NotNull(ReadAnonymousProperty<object>(badRequest.Value!, "details"));
        }

        [Fact]
        public async Task BatchExecuteActionsAsync_UsesSavedActivityTypeAndUpdatesByProductCode()
        {
            await SeedExecutablePriceUpdateAsync();
            await SeedSecondExecutablePriceUpdateDetailAsync();

            var result = await CreateService().BatchExecuteActionsAsync(
                "invoice-execute",
                new List<string> { "detail-price", "detail-price-2" },
                "tester"
            );

            Assert.True(result.Success);
            Assert.Equal(2, result.Data?.UpdatedPurchasePrices);
            Assert.Equal(0, result.Data?.AddedMultiCodes);

            var product = await _db.Queryable<Product>().FirstAsync(x => x.ProductCode == "P001");
            var secondProduct = await _db.Queryable<Product>().FirstAsync(x => x.ProductCode == "P002");
            var price = await _db.Queryable<StoreRetailPrice>().FirstAsync(x => x.ProductCode == "P001");
            var secondPrice = await _db.Queryable<StoreRetailPrice>().FirstAsync(x => x.ProductCode == "P002");
            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .FirstAsync(x => x.DetailGUID == "detail-price");
            var secondDetail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .FirstAsync(x => x.DetailGUID == "detail-price-2");
            var multiCodeCount = await _db.Queryable<StoreMultiCodeProduct>().CountAsync();

            Assert.Equal(5.55m, product.PurchasePrice);
            Assert.Equal(6.66m, secondProduct.PurchasePrice);
            Assert.Equal(5.55m, price.PurchasePrice);
            Assert.Equal(6.66m, secondPrice.PurchasePrice);
            Assert.Equal(99, detail.ActivityType);
            Assert.Equal(99, secondDetail.ActivityType);
            Assert.Equal(0, multiCodeCount);
        }

        [Fact]
        public async Task BatchExecuteActionsAsync_更新进货价为0时跳过()
        {
            await SeedExecutablePriceUpdateAsync();
            await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
                .SetColumns(x => x.PurchasePrice == 0m)
                .Where(x => x.DetailGUID == "detail-price")
                .ExecuteCommandAsync();

            var result = await CreateService().BatchExecuteActionsAsync(
                "invoice-execute",
                new List<string> { "detail-price" },
                "tester"
            );

            var product = await _db.Queryable<Product>().FirstAsync(x => x.ProductCode == "P001");
            var price = await _db.Queryable<StoreRetailPrice>().FirstAsync(x => x.ProductCode == "P001");
            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .FirstAsync(x => x.DetailGUID == "detail-price");

            Assert.True(result.Success, $"{result.ErrorCode} {result.Message}");
            Assert.Equal(0, result.Data?.UpdatedPurchasePrices);
            Assert.Equal(1, result.Data?.Skipped);
            Assert.Equal(0, result.Data?.Failed);
            Assert.Equal(1.11m, product.PurchasePrice);
            Assert.Equal(1.11m, price.PurchasePrice);
            Assert.Equal((int)DetailAction.UpdatePurchasePrice, detail.ActivityType);
        }

        [Fact]
        public async Task BatchExecuteActionsAsync_UpdateItemNumber_BatchUpdatesProducts()
        {
            await SeedExecutableItemNumberUpdatesAsync();

            var result = await CreateService().BatchExecuteActionsAsync(
                "invoice-item-update",
                new List<string> { "detail-item-1", "detail-item-2" },
                "tester"
            );

            Assert.True(result.Success);
            Assert.Equal(2, result.Data?.UpdatedItemNumbers);

            var firstProduct = await _db.Queryable<Product>().FirstAsync(x => x.ProductCode == "PITEM1");
            var secondProduct = await _db.Queryable<Product>().FirstAsync(x => x.ProductCode == "PITEM2");
            var completedDetails = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .Where(x => new[] { "detail-item-1", "detail-item-2" }.Contains(x.DetailGUID))
                .ToListAsync();

            Assert.Equal("ITEM-NEW-1", firstProduct.ItemNumber);
            Assert.Equal("ITEM-NEW-2", secondProduct.ItemNumber);
            Assert.All(completedDetails, detail => Assert.Equal(99, detail.ActivityType));
        }

        [Fact]
        public async Task BatchExecuteActionsAsync_WhenAnySelectedDetailIsInvalid_RollsBackAllChanges()
        {
            await SeedExecutablePriceUpdateAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-invalid",
                InvoiceGUID = "invoice-execute",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "ITEM-MISSING",
                Barcode = "BAR-MISSING",
                ProductName = "Invalid Detail",
                PurchasePrice = 3.33m,
                ExistingProductCount = 1,
                BarcodeStatus = 1,
                ActivityType = (int)DetailAction.UpdatePurchasePrice,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().BatchExecuteActionsAsync(
                "invoice-execute",
                new List<string> { "detail-price", "detail-invalid" },
                "tester"
            );

            Assert.False(result.Success);
            Assert.Equal("VALIDATION_ERROR", result.Code);

            var product = await _db.Queryable<Product>().FirstAsync(x => x.ProductCode == "P001");
            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .FirstAsync(x => x.DetailGUID == "detail-price");
            Assert.Equal(1.11m, product.PurchasePrice);
            Assert.Equal((int)DetailAction.UpdatePurchasePrice, detail.ActivityType);
        }

        [Fact]
        public async Task BatchExecuteActionsAsync_WhenCreateProductItemNumberDiffersOnlyByCaseFromExisting_ReturnsValidationError()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-create-item-case", "INV-CREATE-ITEM-CASE", new DateTime(2026, 1, 11));
            await _db.Insertable(new Product
            {
                UUID = "product-existing-item-case",
                ProductCode = "P-DUP-ITEM",
                ItemNumber = "ITEM-DUP",
                Barcode = "BAR-KEEP",
                ProductName = "Existing Product",
                LocalSupplierCode = "SUP01",
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-create-item-case",
                InvoiceGUID = "invoice-create-item-case",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "item-dup",
                Barcode = "BAR-NEW",
                ProductName = "Duplicate Item Product",
                PurchasePrice = 2.22m,
                ActivityType = (int)DetailAction.CreateProduct,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().BatchExecuteActionsAsync(
                "invoice-create-item-case",
                new List<string> { "detail-create-item-case" },
                "tester"
            );

            Assert.False(result.Success);
            Assert.Equal("VALIDATION_ERROR", result.Code);
            Assert.Equal(1, await _db.Queryable<Product>().CountAsync());
        }

        [Fact]
        public async Task BatchExecuteActionsAsync_WhenCreateProductBarcodeDiffersOnlyByCaseFromExisting_ReturnsValidationError()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-create-barcode-case", "INV-CREATE-BARCODE-CASE", new DateTime(2026, 1, 11));
            await _db.Insertable(new Product
            {
                UUID = "product-existing-barcode-case",
                ProductCode = "P-DUP-BARCODE",
                ItemNumber = "ITEM-KEEP",
                Barcode = "BAR-DUP",
                ProductName = "Existing Product",
                LocalSupplierCode = "SUP01",
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-create-barcode-case",
                InvoiceGUID = "invoice-create-barcode-case",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "ITEM-NEW",
                Barcode = "bar-dup",
                ProductName = "Duplicate Barcode Product",
                PurchasePrice = 2.22m,
                ActivityType = (int)DetailAction.CreateProduct,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().BatchExecuteActionsAsync(
                "invoice-create-barcode-case",
                new List<string> { "detail-create-barcode-case" },
                "tester"
            );

            Assert.False(result.Success);
            Assert.Equal("VALIDATION_ERROR", result.Code);
            Assert.Equal(1, await _db.Queryable<Product>().CountAsync());
        }

        [Fact]
        public async Task BatchExecuteActionsAsync_WhenCreateProductValuesDifferOnlyByCaseWithinBatch_ReturnsValidationError()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-create-batch-case", "INV-CREATE-BATCH-CASE", new DateTime(2026, 1, 11));
            await _db.Insertable(new List<StoreLocalSupplierInvoiceDetails>
            {
                new()
                {
                    DetailGUID = "detail-create-batch-case-1",
                    InvoiceGUID = "invoice-create-batch-case",
                    StoreCode = "S01",
                    SupplierCode = "SUP01",
                    ItemNumber = "ITEM-CASE",
                    Barcode = "BAR-CASE",
                    ProductName = "Batch Duplicate 1",
                    PurchasePrice = 2.22m,
                    ActivityType = (int)DetailAction.CreateProduct,
                    IsDeleted = false,
                },
                new()
                {
                    DetailGUID = "detail-create-batch-case-2",
                    InvoiceGUID = "invoice-create-batch-case",
                    StoreCode = "S01",
                    SupplierCode = "SUP01",
                    ItemNumber = "item-case",
                    Barcode = "bar-case",
                    ProductName = "Batch Duplicate 2",
                    PurchasePrice = 3.33m,
                    ActivityType = (int)DetailAction.CreateProduct,
                    IsDeleted = false,
                },
            }).ExecuteCommandAsync();

            var result = await CreateService().BatchExecuteActionsAsync(
                "invoice-create-batch-case",
                new List<string>
                {
                    "detail-create-batch-case-1",
                    "detail-create-batch-case-2",
                },
                "tester"
            );

            Assert.False(result.Success);
            Assert.Equal("VALIDATION_ERROR", result.Code);
            Assert.Equal(0, await _db.Queryable<Product>().CountAsync());
        }

        [Fact]
        public async Task BatchExecuteActionsAsync_WhenCreateProductDuplicateExists_ReturnsValidationError()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-create-dup", "INV-CREATE-DUP", new DateTime(2026, 1, 11));
            await _db.Insertable(new Product
            {
                UUID = "product-existing",
                ProductCode = "P-DUP",
                ItemNumber = "ITEM-DUP",
                Barcode = "BAR-DUP",
                ProductName = "Existing Product",
                LocalSupplierCode = "SUP01",
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-create-dup",
                InvoiceGUID = "invoice-create-dup",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "ITEM-DUP",
                Barcode = "BAR-DUP-NEW",
                ProductName = "Duplicate Product",
                PurchasePrice = 2.22m,
                ActivityType = (int)DetailAction.CreateProduct,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().BatchExecuteActionsAsync(
                "invoice-create-dup",
                new List<string> { "detail-create-dup" },
                "tester"
            );

            var productCount = await _db.Queryable<Product>().CountAsync();

            Assert.False(result.Success);
            Assert.Equal("VALIDATION_ERROR", result.Code);
            Assert.Equal(1, productCount);
        }

        [Fact]
        public async Task BatchExecuteActionsAsync_WhenMultiCodeDuplicateDiffersOnlyByCase_ReturnsValidationError()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-multi-case", "INV-MULTI-CASE", new DateTime(2026, 1, 12));
            await _db.Insertable(new Product
            {
                UUID = "product-multi-case",
                ProductCode = "P-MULTI-CASE",
                ItemNumber = "ITEM-MULTI",
                Barcode = "BAR-MAIN",
                ProductName = "Multi Product",
                LocalSupplierCode = "SUP01",
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreMultiCodeProduct
            {
                UUID = "multi-existing-case",
                StoreCode = "S01",
                ProductCode = "P-MULTI-CASE",
                MultiCodeProductCode = "MULTI-CASE-001",
                StoreMultiCodeProductCode = "S01MULTI-CASE-001",
                MultiBarcode = "BAR-MULTI",
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-multi-case",
                InvoiceGUID = "invoice-multi-case",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ProductCode = "P-MULTI-CASE",
                ItemNumber = "ITEM-MULTI-ALT",
                Barcode = "bar-multi",
                ProductName = "Multi Product",
                PurchasePrice = 1.23m,
                ActivityType = (int)DetailAction.AddMultiCode,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().BatchExecuteActionsAsync(
                "invoice-multi-case",
                new List<string> { "detail-multi-case" },
                "tester"
            );

            Assert.False(result.Success);
            Assert.Equal("VALIDATION_ERROR", result.Code);
            Assert.Equal(1, await _db.Queryable<StoreMultiCodeProduct>().CountAsync());
        }

        [Fact]
        public async Task BatchExecuteActionsAsync_WhenMultiCodeDuplicateExists_ReturnsValidationError()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-multi-dup", "INV-MULTI-DUP", new DateTime(2026, 1, 12));
            await _db.Insertable(new Product
            {
                UUID = "product-multi",
                ProductCode = "P-MULTI",
                ItemNumber = "ITEM-MULTI",
                Barcode = "BAR-MAIN",
                ProductName = "Multi Product",
                LocalSupplierCode = "SUP01",
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreMultiCodeProduct
            {
                UUID = "multi-existing",
                StoreCode = "S01",
                ProductCode = "P-MULTI",
                MultiCodeProductCode = "MULTI-001",
                StoreMultiCodeProductCode = "S01MULTI-001",
                MultiBarcode = "BAR-MULTI",
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-multi-dup",
                InvoiceGUID = "invoice-multi-dup",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ProductCode = "P-MULTI",
                ItemNumber = "ITEM-MULTI-ALT",
                Barcode = "BAR-MULTI",
                ProductName = "Multi Product",
                PurchasePrice = 1.23m,
                ActivityType = (int)DetailAction.AddMultiCode,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().BatchExecuteActionsAsync(
                "invoice-multi-dup",
                new List<string> { "detail-multi-dup" },
                "tester"
            );

            var multiCodeCount = await _db.Queryable<StoreMultiCodeProduct>().CountAsync();
            var productSetCodeCount = await _db.Queryable<ProductSetCode>().CountAsync();

            Assert.False(result.Success);
            Assert.Equal("VALIDATION_ERROR", result.Code);
            Assert.Equal(1, multiCodeCount);
            Assert.Equal(0, productSetCodeCount);
        }

        [Fact]
        public async Task BatchExecuteActionsAsync_AddMultiCode_副条码分别写入多码表()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-multi-secondary", "INV-MULTI-SECONDARY", new DateTime(2026, 1, 12));
            await _db.Insertable(new Product
            {
                UUID = "product-multi-secondary",
                ProductCode = "P-MULTI-SECONDARY",
                ItemNumber = "88842",
                Barcode = "191554882676",
                ProductName = "Men Travel Perfume Assorted 35mL",
                LocalSupplierCode = "SUP01",
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-multi-secondary",
                InvoiceGUID = "invoice-multi-secondary",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ProductCode = "P-MULTI-SECONDARY",
                ItemNumber = "88842",
                Barcode = "191554882676",
                AdditionalBarcodesJson = JsonSerializer.Serialize(new[]
                {
                    "191554882690",
                    "191554882669",
                }),
                ProductName = "Men Travel Perfume Assorted 35mL",
                PurchasePrice = 1.6546m,
                ActivityType = (int)DetailAction.AddMultiCode,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().BatchExecuteActionsAsync(
                "invoice-multi-secondary",
                new List<string> { "detail-multi-secondary" },
                "tester"
            );

            var productSetCodes = await _db.Queryable<ProductSetCode>()
                .Where(x => x.ProductCode == "P-MULTI-SECONDARY")
                .OrderBy(x => x.SetBarcode)
                .ToListAsync();
            var storeMultiCodes = await _db.Queryable<StoreMultiCodeProduct>()
                .Where(x => x.ProductCode == "P-MULTI-SECONDARY")
                .OrderBy(x => x.MultiBarcode)
                .ToListAsync();
            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .SingleAsync(x => x.DetailGUID == "detail-multi-secondary");

            Assert.True(result.Success, $"{result.ErrorCode} {result.Message}");
            Assert.Equal(2, result.Data?.AddedMultiCodes);
            Assert.Equal(new[] { "191554882669", "191554882690" }, productSetCodes.Select(x => x.SetBarcode).ToArray());
            Assert.Equal(new[] { "191554882669", "191554882690" }, storeMultiCodes.Select(x => x.MultiBarcode).ToArray());
            Assert.Equal(99, detail.ActivityType);
        }

        [Fact]
        public async Task BatchExecuteActionsAsync_AddMultiCode_副条码已存在时回滚()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-multi-secondary-dup", "INV-MULTI-SECONDARY-DUP", new DateTime(2026, 1, 12));
            await _db.Insertable(new Product
            {
                UUID = "product-multi-secondary-dup",
                ProductCode = "P-MULTI-SECONDARY-DUP",
                ItemNumber = "88842",
                Barcode = "191554882676",
                ProductName = "Men Travel Perfume Assorted 35mL",
                LocalSupplierCode = "SUP01",
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreMultiCodeProduct
            {
                UUID = "multi-existing-secondary",
                StoreCode = "S01",
                ProductCode = "P-MULTI-SECONDARY-DUP",
                MultiCodeProductCode = "MULTI-SECONDARY-001",
                StoreMultiCodeProductCode = "S01MULTI-SECONDARY-001",
                MultiBarcode = "191554882669",
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-multi-secondary-dup",
                InvoiceGUID = "invoice-multi-secondary-dup",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ProductCode = "P-MULTI-SECONDARY-DUP",
                ItemNumber = "88842",
                Barcode = "191554882676",
                AdditionalBarcodesJson = JsonSerializer.Serialize(new[]
                {
                    "191554882690",
                    "191554882669",
                }),
                ProductName = "Men Travel Perfume Assorted 35mL",
                PurchasePrice = 1.6546m,
                ActivityType = (int)DetailAction.AddMultiCode,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().BatchExecuteActionsAsync(
                "invoice-multi-secondary-dup",
                new List<string> { "detail-multi-secondary-dup" },
                "tester"
            );

            var multiCodeCount = await _db.Queryable<StoreMultiCodeProduct>().CountAsync();
            var productSetCodeCount = await _db.Queryable<ProductSetCode>().CountAsync();

            Assert.False(result.Success);
            Assert.Equal("VALIDATION_ERROR", result.Code);
            var validationDetails = Assert.IsType<BatchExecuteActionsResultDto>(result.Details);
            Assert.Contains(validationDetails.Errors, error => error.Contains("191554882669", StringComparison.Ordinal));
            Assert.Equal(1, multiCodeCount);
            Assert.Equal(0, productSetCodeCount);
        }

        [Fact]
        public async Task BatchUpdateDetailsAsync_只更新勾选字段并允许False和0持久化()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-batch-edit", "INV-BATCH-EDIT", new DateTime(2026, 1, 13));
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-batch-edit",
                InvoiceGUID = "invoice-batch-edit",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ProductName = "Batch Edit Product",
                Quantity = 3,
                PurchasePrice = 1.50m,
                RetailPrice = 9.99m,
                Amount = 4.50m,
                AutoPricing = true,
                IsSpecialProduct = true,
                DiscountRate = 0.25m,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().BatchUpdateDetailsAsync(
                "invoice-batch-edit",
                new BatchUpdateInvoiceDetailsRequest
                {
                    Items = new List<InvoiceDetailUpsertItemDto>
                    {
                        new() { DetailGUID = "detail-batch-edit" },
                    },
                    EditFields = new UpdateToStorePricesFields
                    {
                        UpdatePurchasePrice = true,
                        PurchasePrice = 2.00m,
                        UpdateIsAutoPricing = true,
                        IsAutoPricing = false,
                        UpdateIsSpecialProduct = true,
                        IsSpecialProduct = false,
                        UpdateDiscountRate = true,
                        DiscountRate = 0m,
                    },
                },
                "tester"
            );

            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .SingleAsync(x => x.DetailGUID == "detail-batch-edit");
            var invoice = await _db.Queryable<StoreLocalSupplierInvoice>()
                .SingleAsync(x => x.InvoiceGUID == "invoice-batch-edit");

            Assert.True(result.Success, result.Message);
            Assert.Equal(1, result.Data?.Updated);
            Assert.Equal(2.00m, detail.PurchasePrice);
            Assert.Equal(9.99m, detail.RetailPrice);
            Assert.Equal(6.00m, detail.Amount);
            Assert.False(detail.AutoPricing);
            Assert.False(detail.IsSpecialProduct);
            Assert.Equal(0m, detail.DiscountRate);
            Assert.Equal(6.00m, invoice.TotalAmount);
        }

        [Fact]
        public async Task BatchUpdateDetailsAsync_商品不存在明细_自动定价False改True后刷新保持True()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-batch-edit-missing-product", "INV-BATCH-EDIT-MISSING-PRODUCT", new DateTime(2026, 1, 15));
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-missing-product-auto",
                InvoiceGUID = "invoice-batch-edit-missing-product",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "NEW-001",
                Barcode = "9300000000011",
                ProductName = "Missing Product Auto Pricing",
                Quantity = 1,
                PurchasePrice = 2.00m,
                Amount = 2.00m,
                ExistingProductCount = 0,
                BarcodeStatus = 1,
                BarcodeMatchCount = 1,
                AutoPricing = false,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().BatchUpdateDetailsAsync(
                "invoice-batch-edit-missing-product",
                new BatchUpdateInvoiceDetailsRequest
                {
                    Items = new List<InvoiceDetailUpsertItemDto>
                    {
                        new() { DetailGUID = "detail-missing-product-auto" },
                    },
                    EditFields = new UpdateToStorePricesFields
                    {
                        UpdateIsAutoPricing = true,
                        IsAutoPricing = true,
                    },
                },
                "tester"
            );

            var details = await CreateService().GetDetailsAsync("invoice-batch-edit-missing-product");
            var detail = Assert.Single(details.Data!);

            Assert.True(result.Success, result.Message);
            Assert.Equal(1, result.Data?.Updated);
            Assert.Equal(0, detail.ExistingProductCount);
            Assert.True(detail.AutoPricing);
            Assert.Equal(250m, detail.PricingFloatRate);
            Assert.Equal(5.00m, detail.NewAutoRetailPrice);
        }

        [Fact]
        public async Task BatchUpdateDetailsAsync_勾选字段缺少值_返回校验错误且不更新时间戳()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-batch-edit-missing", "INV-BATCH-EDIT-MISSING", new DateTime(2026, 1, 14));
            var originalUpdatedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-batch-edit-missing",
                InvoiceGUID = "invoice-batch-edit-missing",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ProductName = "Batch Edit Missing Value Product",
                Quantity = 3,
                PurchasePrice = 1.50m,
                Amount = 4.50m,
                AutoPricing = true,
                UpdatedAt = originalUpdatedAt,
                UpdatedBy = "seed",
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().BatchUpdateDetailsAsync(
                "invoice-batch-edit-missing",
                new BatchUpdateInvoiceDetailsRequest
                {
                    Items = new List<InvoiceDetailUpsertItemDto>
                    {
                        new() { DetailGUID = "detail-batch-edit-missing" },
                    },
                    EditFields = new UpdateToStorePricesFields
                    {
                        UpdatePurchasePrice = true,
                    },
                },
                "tester"
            );

            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .SingleAsync(x => x.DetailGUID == "detail-batch-edit-missing");

            Assert.False(result.Success);
            Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
            Assert.Equal(1.50m, detail.PurchasePrice);
            Assert.Equal(4.50m, detail.Amount);
            Assert.Equal(originalUpdatedAt, detail.UpdatedAt);
            Assert.Equal("seed", detail.UpdatedBy);
        }

        [Fact]
        public async Task PasteDetailsAsync_新商品默认自动定价为True()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-paste-auto-default", "INV-PASTE-AUTO-DEFAULT", new DateTime(2026, 1, 19));

            var result = await CreateService().PasteDetailsAsync(
                new PasteDetailsRequest
                {
                    InvoiceGuid = "invoice-paste-auto-default",
                    Items = new List<PastedDetailItemDto>
                    {
                        new()
                        {
                            ItemNumber = "PASTE-AUTO-001",
                            Barcode = "9300000000015",
                            ProductName = "Paste Auto Default Product",
                            Quantity = 1,
                            PurchasePrice = 2.00m,
                            RetailPrice = 9.99m,
                        },
                    },
                },
                "tester"
            );

            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .SingleAsync(x => x.InvoiceGUID == "invoice-paste-auto-default");

            Assert.True(result.Success, result.Message);
            Assert.True(detail.AutoPricing);
            Assert.Equal(9.99m, detail.RetailPrice);
        }

        [Fact]
        public async Task PasteDetailsAsync_多条码单元格保存主条码和副条码()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-paste-multi-barcode", "INV-PASTE-MULTI-BARCODE", new DateTime(2026, 1, 20));

            var result = await CreateService().PasteDetailsAsync(
                new PasteDetailsRequest
                {
                    InvoiceGuid = "invoice-paste-multi-barcode",
                    Items = new List<PastedDetailItemDto>
                    {
                        new()
                        {
                            ItemNumber = "88841",
                            Barcode = "191554890459,191554890480,191554890497,191554890473,191554888418",
                            ProductName = "Women Travel Perfume Assorted 35mL",
                            Quantity = 48,
                            PurchasePrice = 1.6546m,
                        },
                    },
                },
                "tester"
            );

            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .SingleAsync(x => x.InvoiceGUID == "invoice-paste-multi-barcode");

            Assert.True(result.Success, result.Message);
            Assert.Equal("191554890459", detail.Barcode);
            Assert.Equal(
                new[]
                {
                    "191554890480",
                    "191554890497",
                    "191554890473",
                    "191554888418",
                },
                JsonSerializer.Deserialize<List<string>>(detail.AdditionalBarcodesJson!)!
            );
            Assert.Equal(79.4208m, detail.Amount);
        }

        [Fact]
        public async Task PasteDetailsAsync_忽略供应商表头行()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-paste-header", "INV-PASTE-HEADER", new DateTime(2026, 1, 21));

            var result = await CreateService().PasteDetailsAsync(
                new PasteDetailsRequest
                {
                    InvoiceGuid = "invoice-paste-header",
                    Items = new List<PastedDetailItemDto>
                    {
                        new()
                        {
                            ItemNumber = "Item No.",
                            Barcode = "Barcode",
                            ProductName = "Description",
                        },
                        new()
                        {
                            ItemNumber = "15085-1xLV5085",
                            Barcode = "840417950853",
                            ProductName = "Women Perfumen New Crystal Absolute",
                            Quantity = 6,
                            PurchasePrice = 2.5000m,
                        },
                    },
                },
                "tester"
            );

            var details = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .Where(x => x.InvoiceGUID == "invoice-paste-header")
                .OrderBy(x => x.ItemNumber)
                .ToListAsync();

            Assert.True(result.Success, result.Message);
            Assert.Single(details);
            Assert.Equal("15085-1xLV5085", details[0].ItemNumber);
            Assert.Equal(15.0000m, details[0].Amount);
        }

        [Fact]
        public async Task UpdateDetailsToStorePricesAsync_更新分店进货价并同步商品主档_不调用Hq商品同步服务()
        {
            await SeedExecutablePriceUpdateAsync();
            var hqSync = new Mock<ILocalSupplierInvoiceHqProductSyncService>(MockBehavior.Strict);

            var result = await CreateService(hqSync.Object).UpdateDetailsToStorePricesAsync(
                new UpdateToStorePricesRequest
                {
                    InvoiceGuid = "invoice-execute",
                    DetailGuids = new List<string> { "detail-price" },
                    TargetStoreCodes = new List<string> { "S01" },
                    UpdateFields = new UpdateToStorePricesFields
                    {
                        UpdatePurchasePrice = true,
                    },
                },
                "tester"
            );

            var product = await _db.Queryable<Product>()
                .FirstAsync(x => x.ProductCode == "P001");

            Assert.True(result.Success);
            Assert.Equal(1, result.Data?.Updated);
            Assert.Equal(1, result.Data?.UpdatedPurchasePrices);
            Assert.Equal(5.55m, product.PurchasePrice);
            Assert.Equal("tester", product.UpdatedBy);
            Assert.Null(typeof(UpdateToStorePricesResultDto).GetProperty("HqSynced"));
            hqSync.Verify(
                x => x.EnsureHqProductsAsync(
                    It.IsAny<string>(),
                    It.IsAny<EnsureHqProductsRequest>(),
                    It.IsAny<string>()
                ),
                Times.Never
            );
        }

        [Fact]
        public async Task UpdateDetailsToStorePricesAsync_零售价为空时_使用新自动零售价更新分店零售价()
        {
            await SeedExecutablePriceUpdateAsync();
            await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
                .SetColumns(x => x.RetailPrice == null)
                .SetColumns(x => x.NewAutoRetailPrice == 6.99m)
                .Where(x => x.DetailGUID == "detail-price")
                .ExecuteCommandAsync();

            var result = await CreateService().UpdateDetailsToStorePricesAsync(
                new UpdateToStorePricesRequest
                {
                    InvoiceGuid = "invoice-execute",
                    DetailGuids = new List<string> { "detail-price" },
                    TargetStoreCodes = new List<string> { "S01" },
                    UpdateFields = new UpdateToStorePricesFields
                    {
                        UpdateRetailPrice = true,
                    },
                },
                "tester"
            );

            var storePrice = await _db.Queryable<StoreRetailPrice>()
                .FirstAsync(x => x.UUID == "SRP-001");
            var product = await _db.Queryable<Product>()
                .FirstAsync(x => x.ProductCode == "P001");

            Assert.True(result.Success);
            Assert.Equal(1, result.Data?.Updated);
            Assert.Equal(0, result.Data?.UpdatedPurchasePrices);
            Assert.Equal(6.99m, storePrice.StoreRetailPriceValue);
            Assert.Equal(1.11m, product.PurchasePrice);
        }

        [Fact]
        public async Task UpdateDetailsToStorePricesAsync_零售价不为空时_优先使用零售价更新分店零售价()
        {
            await SeedExecutablePriceUpdateAsync();
            await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
                .SetColumns(x => x.RetailPrice == 5.55m)
                .SetColumns(x => x.NewAutoRetailPrice == 6.99m)
                .Where(x => x.DetailGUID == "detail-price")
                .ExecuteCommandAsync();

            var result = await CreateService().UpdateDetailsToStorePricesAsync(
                new UpdateToStorePricesRequest
                {
                    InvoiceGuid = "invoice-execute",
                    DetailGuids = new List<string> { "detail-price" },
                    TargetStoreCodes = new List<string> { "S01" },
                    UpdateFields = new UpdateToStorePricesFields
                    {
                        UpdateRetailPrice = true,
                    },
                },
                "tester"
            );

            var storePrice = await _db.Queryable<StoreRetailPrice>()
                .FirstAsync(x => x.UUID == "SRP-001");

            Assert.True(result.Success);
            Assert.Equal(1, result.Data?.Updated);
            Assert.Equal(5.55m, storePrice.StoreRetailPriceValue);
        }

        [Fact]
        public async Task UpdateDetailsToStorePricesAsync_重复分店商品只更新一次()
        {
            await SeedExecutablePriceUpdateAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-price-duplicate",
                InvoiceGUID = "invoice-execute",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "ITEM-OLD-DUP",
                ProductName = "Existing Product Duplicate",
                Quantity = 1,
                PurchasePrice = 7.77m,
                ProductCode = "P001",
                StoreProductCode = "S01P001",
                ExistingProductCount = 1,
                BarcodeStatus = 2,
                ActivityType = (int)DetailAction.UpdatePurchasePrice,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().UpdateDetailsToStorePricesAsync(
                new UpdateToStorePricesRequest
                {
                    InvoiceGuid = "invoice-execute",
                    DetailGuids = new List<string> { "detail-price", "detail-price-duplicate" },
                    TargetStoreCodes = new List<string> { "S01", "S01" },
                    UpdateFields = new UpdateToStorePricesFields
                    {
                        UpdatePurchasePrice = true,
                    },
                },
                "tester"
            );

            var storePrice = await _db.Queryable<StoreRetailPrice>()
                .FirstAsync(x => x.UUID == "SRP-001");
            var product = await _db.Queryable<Product>()
                .FirstAsync(x => x.ProductCode == "P001");

            Assert.True(result.Success);
            Assert.Equal(1, result.Data?.Updated);
            Assert.Equal(1, result.Data?.UpdatedPurchasePrices);
            Assert.Equal(7.77m, storePrice.PurchasePrice);
            Assert.Equal(7.77m, product.PurchasePrice);
        }

        [Fact]
        public async Task UpdateDetailsToStorePricesAsync_数值为0时跳过且不更新时间戳()
        {
            await SeedExecutablePriceUpdateAsync();
            var originalUpdatedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            await _db.Updateable<StoreRetailPrice>()
                .SetColumns(x => x.PurchasePrice == 1.11m)
                .SetColumns(x => x.UpdatedAt == originalUpdatedAt)
                .SetColumns(x => x.UpdatedBy == "seed")
                .Where(x => x.UUID == "SRP-001")
                .ExecuteCommandAsync();
            await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
                .SetColumns(x => x.PurchasePrice == 0m)
                .Where(x => x.DetailGUID == "detail-price")
                .ExecuteCommandAsync();

            var result = await CreateService().UpdateDetailsToStorePricesAsync(
                new UpdateToStorePricesRequest
                {
                    InvoiceGuid = "invoice-execute",
                    DetailGuids = new List<string> { "detail-price" },
                    TargetStoreCodes = new List<string> { "S01" },
                    UpdateFields = new UpdateToStorePricesFields
                    {
                        UpdatePurchasePrice = true,
                    },
                },
                "tester"
            );

            var storePrice = await _db.Queryable<StoreRetailPrice>()
                .FirstAsync(x => x.UUID == "SRP-001");
            var product = await _db.Queryable<Product>()
                .FirstAsync(x => x.ProductCode == "P001");

            Assert.True(result.Success);
            Assert.Equal(0, result.Data?.Updated);
            Assert.Equal(1, result.Data?.Skipped);
            Assert.Equal(0, result.Data?.UpdatedPurchasePrices);
            Assert.Equal(1.11m, storePrice.PurchasePrice);
            Assert.Equal(1.11m, product.PurchasePrice);
            Assert.Equal(originalUpdatedAt, storePrice.UpdatedAt);
            Assert.Equal("seed", storePrice.UpdatedBy);
        }

        [Fact]
        public async Task UpdateDetailsToStorePricesAsync_布尔False仍然写入()
        {
            await SeedExecutablePriceUpdateAsync();
            await _db.Updateable<StoreRetailPrice>()
                .SetColumns(x => x.IsAutoPricing == true)
                .SetColumns(x => x.IsSpecialProduct == true)
                .Where(x => x.UUID == "SRP-001")
                .ExecuteCommandAsync();
            await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
                .SetColumns(x => x.AutoPricing == false)
                .SetColumns(x => x.IsSpecialProduct == false)
                .Where(x => x.DetailGUID == "detail-price")
                .ExecuteCommandAsync();

            var result = await CreateService().UpdateDetailsToStorePricesAsync(
                new UpdateToStorePricesRequest
                {
                    InvoiceGuid = "invoice-execute",
                    DetailGuids = new List<string> { "detail-price" },
                    TargetStoreCodes = new List<string> { "S01" },
                    UpdateFields = new UpdateToStorePricesFields
                    {
                        UpdateIsAutoPricing = true,
                        UpdateIsSpecialProduct = true,
                    },
                },
                "tester"
            );

            var storePrice = await _db.Queryable<StoreRetailPrice>()
                .FirstAsync(x => x.UUID == "SRP-001");

            Assert.True(result.Success);
            Assert.Equal(1, result.Data?.Updated);
            Assert.Equal(0, result.Data?.Skipped);
        Assert.False(storePrice.IsAutoPricing);
        Assert.False(storePrice.IsSpecialProduct);
    }

        [Fact]
        public async Task UpdateDetailsToStorePricesAsync_自动定价为空时按否写入()
        {
            await SeedExecutablePriceUpdateAsync();
            await _db.Updateable<StoreRetailPrice>()
                .SetColumns(x => x.IsAutoPricing == true)
                .Where(x => x.UUID == "SRP-001")
                .ExecuteCommandAsync();
            await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
                .SetColumns(x => x.AutoPricing == null)
                .Where(x => x.DetailGUID == "detail-price")
                .ExecuteCommandAsync();

            var result = await CreateService().UpdateDetailsToStorePricesAsync(
                new UpdateToStorePricesRequest
                {
                    InvoiceGuid = "invoice-execute",
                    DetailGuids = new List<string> { "detail-price" },
                    TargetStoreCodes = new List<string> { "S01" },
                    UpdateFields = new UpdateToStorePricesFields
                    {
                        UpdateIsAutoPricing = true,
                    },
                },
                "tester"
            );

            var storePrice = await _db.Queryable<StoreRetailPrice>()
                .FirstAsync(x => x.UUID == "SRP-001");

            Assert.True(result.Success);
            Assert.Equal(1, result.Data?.Updated);
            Assert.Equal(0, result.Data?.Skipped);
            Assert.False(storePrice.IsAutoPricing);
            Assert.DoesNotContain(result.Data?.Errors ?? new List<string>(), error => error.Contains("自动定价为空"));
        }

        [Fact]
        public async Task UpdateDetailsToStorePricesAsync_分店价格不存在时按勾选字段新建记录()
        {
            await SeedExecutablePriceUpdateAsync();
            await _db.Deleteable<StoreRetailPrice>()
                .Where(x => x.UUID == "SRP-001")
                .ExecuteCommandAsync();
            await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
                .SetColumns(x => x.RetailPrice == 6.66m)
                .Where(x => x.DetailGUID == "detail-price")
                .ExecuteCommandAsync();

            var result = await CreateService().UpdateDetailsToStorePricesAsync(
                new UpdateToStorePricesRequest
                {
                    InvoiceGuid = "invoice-execute",
                    DetailGuids = new List<string> { "detail-price" },
                    TargetStoreCodes = new List<string> { "S01" },
                    UpdateFields = new UpdateToStorePricesFields
                    {
                        UpdatePurchasePrice = true,
                        UpdateRetailPrice = true,
                        UpdateIsAutoPricing = true,
                    },
                },
                "tester"
            );

            var storePrice = await _db.Queryable<StoreRetailPrice>()
                .SingleAsync(x => x.StoreCode == "S01" && x.ProductCode == "P001");

            Assert.True(result.Success, result.Message);
            Assert.Equal(1, result.Data?.Inserted);
            Assert.Equal(0, result.Data?.Updated);
            Assert.Equal(0, result.Data?.Skipped);
            Assert.Equal("S01P001", storePrice.StoreProductCode);
            Assert.Equal("SUP01", storePrice.SupplierCode);
            Assert.Equal(5.55m, storePrice.PurchasePrice);
            Assert.Equal(6.66m, storePrice.StoreRetailPriceValue);
            Assert.False(storePrice.IsAutoPricing);
            Assert.True(storePrice.IsActive);
            Assert.False(storePrice.IsDeleted);
            Assert.Equal("tester", storePrice.CreatedBy);
            Assert.Equal("tester", storePrice.UpdatedBy);
        }

        [Fact]
        public async Task UpdateDetailsToStorePricesAsync_重复明细缺分店价格时只插入最后一次结果()
        {
            await SeedExecutablePriceUpdateAsync();
            await _db.Deleteable<StoreRetailPrice>()
                .Where(x => x.UUID == "SRP-001")
                .ExecuteCommandAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-price-duplicate",
                InvoiceGUID = "invoice-execute",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "ITEM-OLD-DUP",
                ProductName = "Existing Product Duplicate",
                Quantity = 1,
                PurchasePrice = 7.77m,
                ProductCode = "P001",
                StoreProductCode = "S01P001",
                ExistingProductCount = 1,
                BarcodeStatus = 2,
                ActivityType = (int)DetailAction.UpdatePurchasePrice,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().UpdateDetailsToStorePricesAsync(
                new UpdateToStorePricesRequest
                {
                    InvoiceGuid = "invoice-execute",
                    DetailGuids = new List<string> { "detail-price", "detail-price-duplicate" },
                    TargetStoreCodes = new List<string> { "S01" },
                    UpdateFields = new UpdateToStorePricesFields
                    {
                        UpdatePurchasePrice = true,
                    },
                },
                "tester"
            );

            var storePrices = await _db.Queryable<StoreRetailPrice>()
                .Where(x => x.StoreCode == "S01" && x.ProductCode == "P001")
                .ToListAsync();

            Assert.True(result.Success, result.Message);
            Assert.Equal(1, result.Data?.Inserted);
            Assert.Equal(0, result.Data?.Updated);
            var storePrice = Assert.Single(storePrices);
            Assert.Equal(7.77m, storePrice.PurchasePrice);
        }

        [Fact]
        public async Task UpdateDetailsToStorePricesAsync_缺分店价格且无有效字段时不插入并返回跳过原因()
        {
            await SeedExecutablePriceUpdateAsync();
            await _db.Deleteable<StoreRetailPrice>()
                .Where(x => x.UUID == "SRP-001")
                .ExecuteCommandAsync();
            await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
                .SetColumns(x => x.PurchasePrice == 0m)
                .Where(x => x.DetailGUID == "detail-price")
                .ExecuteCommandAsync();

            var result = await CreateService().UpdateDetailsToStorePricesAsync(
                new UpdateToStorePricesRequest
                {
                    InvoiceGuid = "invoice-execute",
                    DetailGuids = new List<string> { "detail-price" },
                    TargetStoreCodes = new List<string> { "S01" },
                    UpdateFields = new UpdateToStorePricesFields
                    {
                        UpdatePurchasePrice = true,
                    },
                },
                "tester"
            );

            var storePriceCount = await _db.Queryable<StoreRetailPrice>()
                .Where(x => x.StoreCode == "S01" && x.ProductCode == "P001")
                .CountAsync();

            Assert.True(result.Success, result.Message);
            Assert.Equal(0, result.Data?.Inserted);
            Assert.Equal(0, result.Data?.Updated);
            Assert.Equal(1, result.Data?.Skipped);
            Assert.Equal(0, storePriceCount);
            Assert.Contains(result.Data?.Errors ?? new List<string>(), error => error.Contains("进货价为空或为0"));
        }

        [Fact]
        public async Task UpdateLastPurchasePricesAsync_选中明细_强制覆盖已有上次进货价()
        {
            await SeedExecutablePriceUpdateAsync();
            await SeedSecondExecutablePriceUpdateDetailAsync();
            await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
                .SetColumns(x => x.LastPurchasePrice == 9.99m)
                .Where(x => x.DetailGUID == "detail-price")
                .ExecuteCommandAsync();
            await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
                .SetColumns(x => x.LastPurchasePrice == 8.88m)
                .Where(x => x.DetailGUID == "detail-price-2")
                .ExecuteCommandAsync();

            var result = await CreateService().UpdateLastPurchasePricesAsync(
                "invoice-execute",
                new UpdateLastPurchasePricesRequest
                {
                    DetailGuids = new List<string> { "detail-price" },
                },
                "tester"
            );

            var first = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .SingleAsync(x => x.DetailGUID == "detail-price");
            var second = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .SingleAsync(x => x.DetailGUID == "detail-price-2");

            Assert.True(result.Success, result.Message);
            Assert.Equal(1, result.Data?.Total);
            Assert.Equal(1, result.Data?.Updated);
            Assert.Equal(1.11m, first.LastPurchasePrice);
            Assert.Equal(8.88m, second.LastPurchasePrice);
            Assert.Equal("tester", first.UpdatedBy);
        }

        [Fact]
        public async Task UpdateLastPurchasePricesAsync_未传明细_刷新整张单并在分店价缺失时回退商品主档()
        {
            await SeedExecutablePriceUpdateAsync();
            await SeedSecondExecutablePriceUpdateDetailAsync();
            await _db.Deleteable<StoreRetailPrice>()
                .Where(x => x.StoreCode == "S01" && x.ProductCode == "P002")
                .ExecuteCommandAsync();

            var result = await CreateService().UpdateLastPurchasePricesAsync(
                "invoice-execute",
                new UpdateLastPurchasePricesRequest(),
                "tester"
            );

            var first = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .SingleAsync(x => x.DetailGUID == "detail-price");
            var second = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .SingleAsync(x => x.DetailGUID == "detail-price-2");

            Assert.True(result.Success, result.Message);
            Assert.Equal(2, result.Data?.Total);
            Assert.Equal(2, result.Data?.Updated);
            Assert.Equal(1.11m, first.LastPurchasePrice);
            Assert.Equal(2.22m, second.LastPurchasePrice);
        }

        [Fact]
        public async Task UpdateLastPurchasePricesAsync_明细分店为空白时_使用单头分店价()
        {
            await SeedExecutablePriceUpdateAsync();
            await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
                .SetColumns(x => x.StoreCode == "")
                .Where(x => x.DetailGUID == "detail-price")
                .ExecuteCommandAsync();
            await _db.Updateable<Product>()
                .SetColumns(x => x.PurchasePrice == 9.99m)
                .Where(x => x.ProductCode == "P001")
                .ExecuteCommandAsync();

            var result = await CreateService().UpdateLastPurchasePricesAsync(
                "invoice-execute",
                new UpdateLastPurchasePricesRequest
                {
                    DetailGuids = new List<string> { "detail-price" },
                },
                "tester"
            );

            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .SingleAsync(x => x.DetailGUID == "detail-price");

            Assert.True(result.Success, result.Message);
            Assert.Equal(1, result.Data?.Updated);
            Assert.Equal(1.11m, detail.LastPurchasePrice);
        }

        [Fact]
        public async Task UpdateLastPurchasePricesAsync_选中明细不存在时_返回跳过原因()
        {
            await SeedExecutablePriceUpdateAsync();

            var result = await CreateService().UpdateLastPurchasePricesAsync(
                "invoice-execute",
                new UpdateLastPurchasePricesRequest
                {
                    DetailGuids = new List<string> { "detail-price", "detail-missing" },
                },
                "tester"
            );

            Assert.True(result.Success, result.Message);
            Assert.Equal(2, result.Data?.Total);
            Assert.Equal(1, result.Data?.Updated);
            Assert.Equal(1, result.Data?.Skipped);
            Assert.Contains(result.Data?.Errors ?? new List<string>(), error => error.Contains("明细不存在或不属于当前单据"));
        }

        [Fact]
        public async Task UpdateLastPurchasePricesAsync_缺商品编码或无有效价格时跳过并返回原因()
        {
            await SeedExecutablePriceUpdateAsync();
            await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
                .SetColumns(x => x.ProductCode == null)
                .Where(x => x.DetailGUID == "detail-price")
                .ExecuteCommandAsync();
            await _db.Insertable(new Product
            {
                UUID = "UUID-NO-PRICE",
                ProductCode = "P-NO-PRICE",
                ItemNumber = "ITEM-NO-PRICE",
                Barcode = "BAR-NO-PRICE",
                ProductName = "No Price Product",
                LocalSupplierCode = "SUP01",
                PurchasePrice = 0m,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-no-price",
                InvoiceGUID = "invoice-execute",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "ITEM-NO-PRICE",
                Barcode = "BAR-NO-PRICE",
                ProductName = "No Price Product",
                ProductCode = "P-NO-PRICE",
                PurchasePrice = 1m,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var result = await CreateService().UpdateLastPurchasePricesAsync(
                "invoice-execute",
                new UpdateLastPurchasePricesRequest(),
                "tester"
            );

            Assert.True(result.Success, result.Message);
            Assert.Equal(2, result.Data?.Total);
            Assert.Equal(0, result.Data?.Updated);
            Assert.Equal(2, result.Data?.Skipped);
            Assert.Contains(result.Data?.Errors ?? new List<string>(), error => error.Contains("未找到商品编码"));
            Assert.Contains(result.Data?.Errors ?? new List<string>(), error => error.Contains("未找到有效上次进货价"));
        }

        [Fact]
        public async Task UpdateDetailActionAsync_WhenDetailAlreadyCompleted_RejectsChange()
        {
            await SeedExecutablePriceUpdateAsync();
            await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
                .SetColumns(x => x.ActivityType == 99)
                .Where(x => x.DetailGUID == "detail-price")
                .ExecuteCommandAsync();

            var singleResult = await CreateService().UpdateDetailActionAsync(
                "invoice-execute",
                "detail-price",
                (int)DetailAction.UpdatePurchasePrice
            );
            var batchResult = await CreateService().BatchUpdateDetailActionAsync(
                "invoice-execute",
                new BatchUpdateDetailActionRequest
                {
                    DetailGuids = new List<string> { "detail-price" },
                    Action = (int)DetailAction.UpdatePurchasePrice,
                }
            );

            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .FirstAsync(x => x.DetailGUID == "detail-price");

            Assert.False(singleResult.Success);
            Assert.Equal("VALIDATION_ERROR", singleResult.Code);
            Assert.False(batchResult.Success);
            Assert.Equal("VALIDATION_ERROR", batchResult.Code);
            Assert.Equal(99, detail.ActivityType);
        }

        [Fact]
        public async Task BatchExecuteActions_非全店访问用户_禁止执行会扩散到全店的批量操作()
        {
            await SeedExecutablePriceUpdateAsync();
            await SeedSecondActiveStoreAndScopedUserAsync();
            var controller = CreateController(
                new Claim(ClaimTypes.Name, "scoped-user"),
                new Claim("userId", "user-scoped")
            );

            var actionResult = await controller.BatchExecuteActions(
                "invoice-execute",
                new BatchExecuteActionsRequestDto
                {
                    DetailGuids = new List<string> { "detail-price" },
                    ExpectedActions = new List<BatchExecuteExpectedActionDto>
                    {
                        new()
                        {
                            DetailGuid = "detail-price",
                            ActivityType = (int)DetailAction.UpdatePurchasePrice,
                        },
                    },
                    ConfirmedCreateProductCount = 0,
                    ConfirmedAt = DateTime.UtcNow,
                }
            );

            Assert.IsType<ForbidResult>(actionResult);
        }

        [Fact]
        public async Task BatchUpdateDetailAction_非全店访问用户_禁止设置批量执行动作()
        {
            await SeedExecutablePriceUpdateAsync();
            await SeedSecondActiveStoreAndScopedUserAsync();
            var controller = CreateController(
                new Claim(ClaimTypes.Name, "scoped-user"),
                new Claim("userId", "user-scoped")
            );

            var actionResult = await controller.BatchUpdateDetailAction(
                "invoice-execute",
                new BatchUpdateDetailActionRequest
                {
                    DetailGuids = new List<string> { "detail-price" },
                    Action = (int)DetailAction.UpdatePurchasePrice,
                }
            );

            Assert.IsType<ForbidResult>(actionResult);
        }

        [Fact]
        public async Task PasteDetailsJobEndpoints_创建和查询任务委托后台服务()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-job", "INV-JOB", new DateTime(2026, 1, 20));
            var jobService = new Mock<ILocalSupplierInvoiceBatchUpdateJobService>(MockBehavior.Strict);
            jobService
                .Setup(service => service.StartPasteDetailsJobAsync(
                    It.Is<PasteDetailsRequest>(request =>
                        request.InvoiceGuid == "invoice-job"
                        && request.Mode == "append"
                        && request.Items.Count == 1
                    ),
                    "tester",
                    It.IsAny<CancellationToken>()
                ))
                .ReturnsAsync(new LocalSupplierInvoicePasteDetailsJobDto
                {
                    JobId = "paste-job-1",
                    InvoiceGuid = "invoice-job",
                    OperationId = "paste|invoice-job",
                    Status = LocalSupplierInvoiceBatchUpdateJobStatusConstants.Running,
                });
            jobService
                .Setup(service => service.GetPasteDetailsJobAsync("paste-job-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LocalSupplierInvoicePasteDetailsJobDto
                {
                    JobId = "paste-job-1",
                    InvoiceGuid = "invoice-job",
                    OperationId = "paste|invoice-job",
                    Status = LocalSupplierInvoiceBatchUpdateJobStatusConstants.Succeeded,
                    Result = new BatchResultDto { Inserted = 1 },
                });

            var controller = CreateControllerWithJobService(jobService.Object);

            var createResult = await controller.StartPasteDetailsJob(
                "invoice-job",
                new PasteDetailsRequest
                {
                    Mode = "append",
                    Items = new List<PastedDetailItemDto> { new() { ItemNumber = "ITEM-1" } },
                },
                CancellationToken.None
            );
            var getResult = await controller.GetPasteDetailsJob(
                "invoice-job",
                "paste-job-1",
                CancellationToken.None
            );

            Assert.IsType<OkObjectResult>(createResult);
            Assert.IsType<OkObjectResult>(getResult);
            jobService.VerifyAll();
        }

        [Fact]
        public async Task CheckProductsJobEndpoints_创建和查询任务委托后台服务()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-job", "INV-JOB", new DateTime(2026, 1, 20));
            var jobService = new Mock<ILocalSupplierInvoiceBatchUpdateJobService>(MockBehavior.Strict);
            jobService
                .Setup(service => service.StartCheckProductsJobAsync(
                    It.Is<CheckProductsRequest>(request =>
                        request.InvoiceGuid == "invoice-job"
                        && request.DetailGuids != null
                        && request.DetailGuids.SequenceEqual(new[] { "detail-1" })
                    ),
                    It.IsAny<CancellationToken>()
                ))
                .ReturnsAsync(new LocalSupplierInvoiceCheckProductsJobDto
                {
                    JobId = "check-job-1",
                    InvoiceGuid = "invoice-job",
                    OperationId = "check-products|invoice-job",
                    Status = LocalSupplierInvoiceBatchUpdateJobStatusConstants.Running,
                });
            jobService
                .Setup(service => service.GetCheckProductsJobAsync("check-job-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LocalSupplierInvoiceCheckProductsJobDto
                {
                    JobId = "check-job-1",
                    InvoiceGuid = "invoice-job",
                    OperationId = "check-products|invoice-job",
                    Status = LocalSupplierInvoiceBatchUpdateJobStatusConstants.Succeeded,
                    Result = new CheckProductsResponseDto
                    {
                        Summary = new CheckProductsSummaryDto { Total = 1 },
                    },
                });

            var controller = CreateControllerWithJobService(jobService.Object);

            var createResult = await controller.StartCheckProductsJob(
                new CheckProductsRequest
                {
                    InvoiceGuid = "invoice-job",
                    DetailGuids = new List<string> { "detail-1" },
                },
                CancellationToken.None
            );
            var getResult = await controller.GetCheckProductsJob(
                "invoice-job",
                "check-job-1",
                CancellationToken.None
            );

            Assert.IsType<OkObjectResult>(createResult);
            Assert.IsType<OkObjectResult>(getResult);
            jobService.VerifyAll();
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

        private async Task SeedStoreAndSupplierAsync()
        {
            await _db.Insertable(new Store
            {
                StoreGUID = "store-guid-1",
                StoreCode = "S01",
                StoreName = "Sydney Store",
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new HBLocalSupplier
            {
                Guid = "supplier-guid-1",
                LocalSupplierCode = "SUP01",
                Name = "Local Supplier",
                IsDeleted = false,
            }).ExecuteCommandAsync();
        }

        private async Task InsertInvoiceAsync(string invoiceGuid, string invoiceNo, DateTime orderDate)
        {
            await _db.Insertable(new StoreLocalSupplierInvoice
            {
                InvoiceGUID = invoiceGuid,
                StoreCode = "S01",
                SupplierCode = "SUP01",
                InvoiceNo = invoiceNo,
                OrderDate = orderDate,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false,
            }).ExecuteCommandAsync();
        }

        private async Task SeedExecutablePriceUpdateAsync()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-execute", "INV-EXEC", new DateTime(2026, 1, 10));
            await _db.Insertable(new Product
            {
                UUID = "UUID-DIFFERENT",
                ProductCode = "P001",
                ItemNumber = "ITEM-OLD",
                Barcode = "BAR-OLD",
                ProductName = "Existing Product",
                LocalSupplierCode = "SUP01",
                PurchasePrice = 1.11m,
                RetailPrice = 2.22m,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreRetailPrice
            {
                UUID = "SRP-001",
                StoreCode = "S01",
                ProductCode = "P001",
                StoreProductCode = "S01P001",
                SupplierCode = "SUP01",
                PurchasePrice = 1.11m,
                StoreRetailPriceValue = 2.22m,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-price",
                InvoiceGUID = "invoice-execute",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "ITEM-OLD",
                Barcode = "BAR-CONFLICT",
                ProductName = "Existing Product",
                Quantity = 1,
                PurchasePrice = 5.55m,
                ProductCode = "P001",
                StoreProductCode = "S01P001",
                ExistingProductCount = 1,
                BarcodeStatus = 2,
                ActivityType = (int)DetailAction.UpdatePurchasePrice,
                IsDeleted = false,
            }).ExecuteCommandAsync();
        }

        private async Task SeedSecondExecutablePriceUpdateDetailAsync()
        {
            await _db.Insertable(new Product
            {
                UUID = "UUID-DIFFERENT-2",
                ProductCode = "P002",
                ItemNumber = "ITEM-OLD-2",
                Barcode = "BAR-OLD-2",
                ProductName = "Second Existing Product",
                LocalSupplierCode = "SUP01",
                PurchasePrice = 2.22m,
                RetailPrice = 3.33m,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreRetailPrice
            {
                UUID = "SRP-002",
                StoreCode = "S01",
                ProductCode = "P002",
                StoreProductCode = "S01P002",
                SupplierCode = "SUP01",
                PurchasePrice = 2.22m,
                StoreRetailPriceValue = 3.33m,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = "detail-price-2",
                InvoiceGUID = "invoice-execute",
                StoreCode = "S01",
                SupplierCode = "SUP01",
                ItemNumber = "ITEM-OLD-2",
                Barcode = "BAR-CONFLICT-2",
                ProductName = "Second Existing Product",
                Quantity = 1,
                PurchasePrice = 6.66m,
                ProductCode = "P002",
                StoreProductCode = "S01P002",
                ExistingProductCount = 1,
                BarcodeStatus = 2,
                ActivityType = (int)DetailAction.UpdatePurchasePrice,
                IsDeleted = false,
            }).ExecuteCommandAsync();
        }

        private async Task SeedExecutableItemNumberUpdatesAsync()
        {
            await SeedStoreAndSupplierAsync();
            await InsertInvoiceAsync("invoice-item-update", "INV-ITEM-UPD", new DateTime(2026, 1, 12));
            await _db.Insertable(new List<Product>
            {
                new()
                {
                    UUID = "UUID-ITEM-1",
                    ProductCode = "PITEM1",
                    ItemNumber = "ITEM-OLD-1",
                    Barcode = "BAR-ITEM-1",
                    ProductName = "Item Product 1",
                    LocalSupplierCode = "SUP01",
                    PurchasePrice = 1.11m,
                    RetailPrice = 2.22m,
                    IsDeleted = false,
                },
                new()
                {
                    UUID = "UUID-ITEM-2",
                    ProductCode = "PITEM2",
                    ItemNumber = "ITEM-OLD-2",
                    Barcode = "BAR-ITEM-2",
                    ProductName = "Item Product 2",
                    LocalSupplierCode = "SUP01",
                    PurchasePrice = 1.22m,
                    RetailPrice = 2.44m,
                    IsDeleted = false,
                },
            }).ExecuteCommandAsync();
            await _db.Insertable(new List<StoreLocalSupplierInvoiceDetails>
            {
                new()
                {
                    DetailGUID = "detail-item-1",
                    InvoiceGUID = "invoice-item-update",
                    StoreCode = "S01",
                    SupplierCode = "SUP01",
                    ItemNumber = "ITEM-NEW-1",
                    Barcode = "BAR-ITEM-1",
                    ProductName = "Item Product 1",
                    Quantity = 1,
                    PurchasePrice = 1.11m,
                    ProductCode = "PITEM1",
                    ExistingProductCount = 1,
                    BarcodeStatus = 1,
                    ActivityType = (int)DetailAction.UpdateItemNumber,
                    IsDeleted = false,
                },
                new()
                {
                    DetailGUID = "detail-item-2",
                    InvoiceGUID = "invoice-item-update",
                    StoreCode = "S01",
                    SupplierCode = "SUP01",
                    ItemNumber = "ITEM-NEW-2",
                    Barcode = "BAR-ITEM-2",
                    ProductName = "Item Product 2",
                    Quantity = 1,
                    PurchasePrice = 1.22m,
                    ProductCode = "PITEM2",
                    ExistingProductCount = 1,
                    BarcodeStatus = 1,
                    ActivityType = (int)DetailAction.UpdateItemNumber,
                    IsDeleted = false,
                },
            }).ExecuteCommandAsync();
        }

        private LocalSupplierInvoicesReactService CreateService(
            ILocalSupplierInvoiceHqProductSyncService? hqProductSyncService = null
        )
        {
            var autoPricing = new Mock<IAutoPricingService>();
            autoPricing.Setup(x => x.GetAllActiveStrategiesAsync())
                .ReturnsAsync(new List<BlazorApp.Shared.Models.HBweb.PricingStrategy>());
            autoPricing.Setup(x => x.FindStrategyForPriceAsync(It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .ReturnsAsync((BlazorApp.Shared.Models.HBweb.PricingStrategy?)null);
            autoPricing.Setup(x => x.CalculateRate(It.IsAny<decimal>(), It.IsAny<BlazorApp.Shared.Models.HBweb.PricingStrategy?>()))
                .Returns(250m);
            autoPricing.Setup(x => x.CalculateRetailPrice(It.IsAny<decimal>(), It.IsAny<BlazorApp.Shared.Models.HBweb.PricingStrategy?>()))
                .Returns<decimal, BlazorApp.Shared.Models.HBweb.PricingStrategy?>((price, _) => price * 2.5m);

            return new LocalSupplierInvoicesReactService(
                CreateSqlSugarContext(_db),
                CreateHqSqlSugarContext(),
                Mock.Of<IMapper>(),
                NullLogger<LocalSupplierInvoicesReactService>.Instance,
                autoPricing.Object,
                hqProductSyncService
            );
        }

        private ReactLocalSupplierInvoicesController CreateController(params Claim[] claims)
        {
            return CreateControllerCore(null, claims);
        }

        private ReactLocalSupplierInvoicesController CreateControllerWithJobService(
            ILocalSupplierInvoiceBatchUpdateJobService batchUpdateJobService,
            params Claim[] claims
        )
        {
            return CreateControllerCore(batchUpdateJobService, claims);
        }

        private ReactLocalSupplierInvoicesController CreateControllerCore(
            ILocalSupplierInvoiceBatchUpdateJobService? batchUpdateJobService,
            Claim[] claims
        )
        {
            var controller = new ReactLocalSupplierInvoicesController(
                CreateService(),
                CreateSqlSugarContext(_db),
                Mock.Of<ILocalSupplierInvoiceHqSyncService>(),
                Mock.Of<ILocalSupplierInvoiceImportService>(),
                null,
                batchUpdateJobService
            );

            var effectiveClaims = claims.Length > 0
                ? claims
                : new[]
                {
                    new Claim(ClaimTypes.Name, "tester"),
                    new Claim(ClaimTypes.Role, "Admin"),
                };

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            effectiveClaims,
                            "TestAuth"
                        )
                    ),
                },
            };

            return controller;
        }

        private async Task SeedSecondActiveStoreAndScopedUserAsync()
        {
            await _db.Insertable(new Store
            {
                StoreGUID = "store-guid-2",
                StoreCode = "S02",
                StoreName = "Melbourne Store",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new UserStore
            {
                UserStoreGUID = "user-store-scoped-1",
                UserGUID = "user-scoped",
                StoreGUID = "store-guid-1",
                IsPrimary = true,
            }).ExecuteCommandAsync();
        }

        private static T ReadAnonymousProperty<T>(object value, string propertyName)
        {
            var property = value.GetType().GetProperty(propertyName)
                ?? throw new InvalidOperationException($"未找到属性 {propertyName}");
            return (T)property.GetValue(value)!;
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
