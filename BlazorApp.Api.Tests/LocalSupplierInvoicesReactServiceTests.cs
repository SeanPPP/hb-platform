using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
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

            Assert.True(result.Success);
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

            Assert.True(result.Success);
            Assert.Equal(2, result.Total);
            Assert.Equal(new[] { "INV-3", "INV-2" }, result.Items?.Select(item => item.InvoiceNo).ToArray());
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

            Assert.True(result.Success);
            Assert.Equal(55, result.Total);
            Assert.Equal(50, result.Items?.Count);
            Assert.Equal("detail-55", result.Items?.First().DetailGUID);
        }

        [Fact]
        public async Task CheckProductsAsync_WhenProductNoLongerMatches_ClearsStaleDetectionFields()
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
            Assert.Null(detail.LastPurchasePrice);
            Assert.Null(detail.AutoPricing);
            Assert.Null(detail.IsSpecialProduct);
            Assert.Null(detail.DiscountRate);
            Assert.Null(detail.PricingFloatRate);
            Assert.Null(detail.NewAutoRetailPrice);
            Assert.Equal(0, detail.ExistingProductCount);
            Assert.Equal(1, detail.BarcodeStatus);
            Assert.Equal(0, detail.BarcodeMatchCount);
            Assert.Equal((int)DetailAction.CreateProduct, detail.ActivityType);
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

            var result = await CreateService().BatchExecuteActionsAsync(
                "invoice-execute",
                new List<string> { "detail-price" },
                "tester"
            );

            Assert.True(result.Success);
            Assert.Equal(1, result.Data?.UpdatedPurchasePrices);
            Assert.Equal(0, result.Data?.AddedMultiCodes);

            var product = await _db.Queryable<Product>().FirstAsync(x => x.ProductCode == "P001");
            var price = await _db.Queryable<StoreRetailPrice>().FirstAsync(x => x.ProductCode == "P001");
            var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .FirstAsync(x => x.DetailGUID == "detail-price");
            var multiCodeCount = await _db.Queryable<StoreMultiCodeProduct>().CountAsync();

            Assert.Equal(5.55m, product.PurchasePrice);
            Assert.Equal(5.55m, price.PurchasePrice);
            Assert.Equal(99, detail.ActivityType);
            Assert.Equal(0, multiCodeCount);
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
        public async Task UpdateDetailsToStorePricesAsync_只更新分店价格_不调用Hq商品同步服务()
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

            Assert.True(result.Success);
            Assert.Equal(1, result.Data?.Updated);
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

            Assert.True(result.Success);
            Assert.Equal(1, result.Data?.Updated);
            Assert.Equal(6.99m, storePrice.StoreRetailPriceValue);
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

        private LocalSupplierInvoicesReactService CreateService(
            ILocalSupplierInvoiceHqProductSyncService? hqProductSyncService = null
        )
        {
            var autoPricing = new Mock<IAutoPricingService>();
            autoPricing.Setup(x => x.GetAllActiveStrategiesAsync())
                .ReturnsAsync(new List<BlazorApp.Shared.Models.HBweb.PricingStrategy>());
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
            var controller = new ReactLocalSupplierInvoicesController(
                CreateService(),
                CreateSqlSugarContext(_db),
                Mock.Of<ILocalSupplierInvoiceHqSyncService>(),
                null
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
