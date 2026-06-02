using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
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
        public async Task UpdateDetailsToStorePricesAsync_未勾选同步HQ时_不调用Hq商品同步服务()
        {
            await SeedExecutablePriceUpdateAsync();
            var hqSync = new Mock<ILocalSupplierInvoiceHqProductSyncService>(MockBehavior.Strict);

            var result = await CreateService(hqSync.Object).UpdateDetailsToStorePricesAsync(
                new UpdateToStorePricesRequest
                {
                    InvoiceGuid = "invoice-execute",
                    DetailGuids = new List<string> { "detail-price" },
                    TargetStoreCodes = new List<string> { "S01" },
                    UpdateHqProduct = false,
                    UpdateFields = new UpdateToStorePricesFields
                    {
                        UpdatePurchasePrice = true,
                    },
                },
                "tester"
            );

            Assert.True(result.Success);
            Assert.Equal(1, result.Data?.Updated);
            Assert.Equal(0, result.Data?.HqSynced);
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
        public async Task UpdateDetailsToStorePricesAsync_勾选同步HQ时_合并Hq统计()
        {
            await SeedExecutablePriceUpdateAsync();
            var hqSync = new Mock<ILocalSupplierInvoiceHqProductSyncService>(MockBehavior.Strict);
            hqSync.Setup(x => x.EnsureHqProductsAsync(
                    "invoice-execute",
                    It.Is<EnsureHqProductsRequest>(request =>
                        request.DetailGuids.SequenceEqual(new[] { "detail-price" })
                        && request.TargetStoreCodes.SequenceEqual(new[] { "S01" })
                    ),
                    "tester"
                ))
                .ReturnsAsync(ApiResponse<EnsureHqProductsResult>.OK(new EnsureHqProductsResult
                {
                    Total = 1,
                    HqExisting = 1,
                    HbwebCreated = 0,
                    HqCreated = 0,
                    HqSynced = 1,
                    HqPurchasePricesUpdated = 1,
                    Failed = 0,
                }));

            var result = await CreateService(hqSync.Object).UpdateDetailsToStorePricesAsync(
                new UpdateToStorePricesRequest
                {
                    InvoiceGuid = "invoice-execute",
                    DetailGuids = new List<string> { "detail-price" },
                    TargetStoreCodes = new List<string> { "S01" },
                    UpdateHqProduct = true,
                    UpdateFields = new UpdateToStorePricesFields
                    {
                        UpdatePurchasePrice = true,
                    },
                },
                "tester"
            );

            Assert.True(result.Success);
            Assert.Equal(1, result.Data?.Updated);
            Assert.Equal(1, result.Data?.HqExisting);
            Assert.Equal(1, result.Data?.HqSynced);
            Assert.Equal(1, result.Data?.HqPurchasePricesUpdated);
            hqSync.VerifyAll();
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
