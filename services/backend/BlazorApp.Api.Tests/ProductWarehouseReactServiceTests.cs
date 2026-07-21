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
                typeof(WarehouseCategory),
                typeof(HBLocalSupplier)
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
        public async Task GetAntdTableDataAsync_FiltersWarehouseTextAndLookupColumns()
        {
            await SeedWarehouseCategoryAsync("cat-filter-a", null, "厨房用品");
            await SeedWarehouseCategoryAsync("cat-filter-b", null, "卧室用品");
            await SeedWarehouseTableProductAsync(
                "P-FILTER-001",
                "ITEM-MUG-001",
                "大理石马克杯",
                "cat-filter-a",
                englishName: "Marble Mug",
                barcode: "BAR-MUG-001",
                supplierCode: "CN-001",
                supplierName: "义乌杯厂",
                localSupplierCode: "AU-001",
                localSupplierName: "Sydney Local Trading"
            );
            await SeedWarehouseTableProductAsync(
                "P-FILTER-002",
                "ITEM-LAMP-001",
                "北欧台灯",
                "cat-filter-b",
                englishName: "Nordic Lamp",
                barcode: "BAR-LAMP-002",
                supplierCode: "CN-002",
                supplierName: "义乌灯饰",
                localSupplierCode: "AU-002",
                localSupplierName: "Melbourne Supply"
            );

            var service = CreateService();

            var productNameResult = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["productName"] = new[] { "__filter:contains:马克" },
                },
            });
            var barcodeResult = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["barcode"] = new[] { "__filter:ends:002" },
                },
            });
            var itemNumberResult = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["itemNumber"] = new[] { "__filter:starts:ITEM-MUG" },
                },
            });
            var nameEnResult = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["nameEn"] = new[] { "__filter:eq:Nordic Lamp" },
                },
            });
            var supplierResult = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["domesticSupplierCode"] = new[] { "CN-001" },
                    ["domesticSupplierName"] = new[] { "杯厂" },
                    ["localSupplierCode"] = new[] { "AU-001" },
                    ["localSupplierName"] = new[] { "Sydney" },
                    ["categoryName"] = new[] { "厨房" },
                },
            });

            var productNameItem = Assert.Single(productNameResult.Items);
            Assert.Equal("P-FILTER-001", productNameItem.ProductCode);

            var barcodeItem = Assert.Single(barcodeResult.Items);
            Assert.Equal("P-FILTER-002", barcodeItem.ProductCode);

            var itemNumberItem = Assert.Single(itemNumberResult.Items);
            Assert.Equal("P-FILTER-001", itemNumberItem.ProductCode);

            var nameEnItem = Assert.Single(nameEnResult.Items);
            Assert.Equal("P-FILTER-002", nameEnItem.ProductCode);

            var supplierItem = Assert.Single(supplierResult.Items);
            Assert.Equal("P-FILTER-001", supplierItem.ProductCode);
            Assert.Equal("Sydney Local Trading", supplierItem.LocalSupplierName);
        }

        [Fact]
        public async Task GetAntdTableDataAsync_TreatsLegacyPrefixedTextAsLiteralContains()
        {
            await SeedWarehouseTableProductAsync(
                "P-LEGACY-PREFIX-001",
                "ITEM-LEGACY-PREFIX-001",
                "eq:ABC 旧值商品",
                null
            );
            await SeedWarehouseTableProductAsync(
                "P-LEGACY-PREFIX-002",
                "ITEM-LEGACY-PREFIX-002",
                "ABC",
                null
            );

            var service = CreateService();

            var legacyResult = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["productName"] = new[] { "eq:ABC" },
                },
            });
            var exactResult = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["productName"] = new[] { "__filter:eq:ABC" },
                },
            });

            var legacyItem = Assert.Single(legacyResult.Items);
            Assert.Equal("P-LEGACY-PREFIX-001", legacyItem.ProductCode);

            var exactItem = Assert.Single(exactResult.Items);
            Assert.Equal("P-LEGACY-PREFIX-002", exactItem.ProductCode);
        }

        [Fact]
        public async Task GetAntdTableDataAsync_SupplierCodeFiltersUseExactMatch()
        {
            await SeedWarehouseTableProductAsync(
                "P-SUPPLIER-EXACT-001",
                "ITEM-SUPPLIER-EXACT-001",
                "供应商精确命中商品",
                null,
                supplierCode: "CN-001",
                supplierName: "义乌精确厂",
                localSupplierCode: "AU-001",
                localSupplierName: "Exact Local"
            );
            await SeedWarehouseTableProductAsync(
                "P-SUPPLIER-EXACT-002",
                "ITEM-SUPPLIER-EXACT-002",
                "供应商前缀碰撞商品",
                null,
                supplierCode: "CN-001A",
                supplierName: "义乌精确厂",
                localSupplierCode: "AU-001A",
                localSupplierName: "Exact Local Branch"
            );

            var service = CreateService();

            var domesticResult = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["domesticSupplierCode"] = new[] { "CN-001" },
                },
            });
            var localResult = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["localSupplierCode"] = new[] { "AU-001" },
                },
            });

            var domesticItem = Assert.Single(domesticResult.Items);
            Assert.Equal("P-SUPPLIER-EXACT-001", domesticItem.ProductCode);

            var localItem = Assert.Single(localResult.Items);
            Assert.Equal("P-SUPPLIER-EXACT-001", localItem.ProductCode);
        }

        [Fact]
        public async Task GetAntdTableDataAsync_ReturnsPickingLocationCodesOnly()
        {
            await SeedWarehouseTableProductAsync(
                "P-LOCATION-LIST-001",
                "ITEM-LOCATION-LIST-001",
                "货位列表商品",
                null
            );
            await SeedLocationAsync("loc-picking-list-002", "PICK-A-02", 1);
            await SeedLocationAsync("loc-picking-list-001", "PICK-A-01", 1);
            await SeedLocationAsync("loc-storage-list-001", "STOCK-A-99", 2);
            await SeedProductLocationAsync("P-LOCATION-LIST-001", "loc-picking-list-002");
            await SeedProductLocationAsync("P-LOCATION-LIST-001", "loc-picking-list-001");
            await SeedProductLocationAsync("P-LOCATION-LIST-001", "loc-storage-list-001");

            var service = CreateService();

            var result = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
            });

            var item = Assert.Single(result.Items);
            Assert.Equal(1, result.Total);
            Assert.Equal(new[] { "PICK-A-01", "PICK-A-02" }, item.LocationCodes);
            Assert.DoesNotContain("STOCK-A-99", item.LocationCodes);
        }

        [Fact]
        public async Task GetAntdTableDataAsync_FiltersByPickingLocationCodeAndBarcode()
        {
            await SeedWarehouseTableProductAsync(
                "P-LOCATION-FILTER-001",
                "ITEM-LOCATION-FILTER-001",
                "货位筛选命中商品",
                null
            );
            await SeedWarehouseTableProductAsync(
                "P-LOCATION-FILTER-002",
                "ITEM-LOCATION-FILTER-002",
                "货位筛选未命中商品",
                null
            );
            await SeedLocationAsync("loc-picking-filter-001", "PICK-FILTER-01", 1);
            await SeedLocationAsync("loc-picking-filter-002", "PICK-FILTER-02", 1);
            await SeedLocationAsync("loc-storage-filter-001", "STOCK-FILTER-01", 2);
            await SeedProductLocationAsync("P-LOCATION-FILTER-001", "loc-picking-filter-001");
            await SeedProductLocationAsync("P-LOCATION-FILTER-002", "loc-picking-filter-002");
            await SeedProductLocationAsync("P-LOCATION-FILTER-002", "loc-storage-filter-001");

            var service = CreateService();

            var codeResult = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["locationCodes"] = new[] { "__filter:eq:PICK-FILTER-01" },
                },
            });
            var barcodeResult = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["locationCodes"] = new[] { "PICK-FILTER-02-BAR" },
                },
            });
            var storageResult = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["locationCodes"] = new[] { "STOCK-FILTER-01" },
                },
            });

            Assert.Equal("P-LOCATION-FILTER-001", Assert.Single(codeResult.Items).ProductCode);
            Assert.Equal("P-LOCATION-FILTER-002", Assert.Single(barcodeResult.Items).ProductCode);
            Assert.Empty(storageResult.Items);
        }

        [Fact]
        public async Task GetAntdTableDataAsync_GlobalSearchMatchesPickingLocationCodeAndBarcode()
        {
            await SeedWarehouseTableProductAsync(
                "P-LOCATION-SEARCH-001",
                "ITEM-LOCATION-SEARCH-001",
                "货位搜索命中商品",
                null
            );
            await SeedWarehouseTableProductAsync(
                "P-LOCATION-SEARCH-002",
                "ITEM-LOCATION-SEARCH-002",
                "货位搜索未命中商品",
                null
            );
            await SeedLocationAsync("loc-picking-search-001", "PICK-SEARCH-01", 1);
            await SeedLocationAsync("loc-storage-search-001", "STOCK-SEARCH-01", 2);
            await SeedProductLocationAsync("P-LOCATION-SEARCH-001", "loc-picking-search-001");
            await SeedProductLocationAsync("P-LOCATION-SEARCH-002", "loc-storage-search-001");

            var service = CreateService();

            var codeResult = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                GlobalSearch = "PICK-SEARCH",
            });
            var barcodeResult = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                GlobalSearch = "PICK-SEARCH-01-BAR",
            });
            var storageResult = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                GlobalSearch = "STOCK-SEARCH",
            });

            Assert.Equal("P-LOCATION-SEARCH-001", Assert.Single(codeResult.Items).ProductCode);
            Assert.Equal("P-LOCATION-SEARCH-001", Assert.Single(barcodeResult.Items).ProductCode);
            Assert.Empty(storageResult.Items);
        }

        [Fact]
        public async Task GetAntdTableDataAsync_GlobalSearchMatchesCodeLikeProductFields()
        {
            await SeedWarehouseTableProductAsync(
                "P-GLOBAL-CODE-001",
                "HB246-BD-001",
                "代码型搜索命中商品",
                null,
                barcode: "9525812460744",
                supplierCode: "CN246-BD",
                supplierName: "代码供应商",
                localSupplierCode: "AU246-BD",
                localSupplierName: "Local Code Supplier"
            );
            await SeedWarehouseTableProductAsync(
                "P-GLOBAL-CODE-002",
                "HB999-ZZ-002",
                "代码型搜索未命中商品",
                null,
                barcode: "9525819999999",
                supplierCode: "CN999-ZZ",
                localSupplierCode: "AU999-ZZ"
            );
            var service = CreateService();
            var searches = new[]
            {
                "P-GLOBAL-CODE-001",
                "HB246-BD",
                "246-bd",
                "9525812460744",
                "CN246-BD",
                "AU246-BD",
            };

            foreach (var search in searches)
            {
                var result = await service.GetAntdTableDataAsync(new ReactTableRequestDto
                {
                    Page = 1,
                    PageSize = 20,
                    GlobalSearch = search,
                });

                Assert.Equal("P-GLOBAL-CODE-001", Assert.Single(result.Items).ProductCode);
            }

            var omittedHbPrefixResult = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                GlobalSearch = "246-bd",
            });

            // 派生 HB 前缀只用于 HB 货号，不扩散到条码、供应商或商品名称。
            Assert.Equal("P-GLOBAL-CODE-001", Assert.Single(omittedHbPrefixResult.Items).ProductCode);
        }

        [Fact]
        public async Task GetAntdTableDataAsync_OmittedHbPrefixOnlyMatchesItemNumber()
        {
            await SeedWarehouseTableProductAsync(
                "P-OMITTED-HB-001",
                "HB246-BD-001",
                "省略 HB 前缀命中货号",
                null
            );
            await SeedWarehouseTableProductAsync(
                "P-OMITTED-HB-002",
                "ITEM-GLOBAL-HB-ONLY-002",
                "HB246-BD 干扰商品",
                null,
                barcode: "HB246-BD-BAR",
                supplierCode: "HB246-BD-SUP",
                localSupplierCode: "HB246-BD-LOCAL"
            );

            var service = CreateService();

            var result = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                GlobalSearch = "246-bd",
            });

            // 省略 HB 前缀只补到货号字段；其它代码列即使以 HB246-BD 开头也不应被派生命中。
            Assert.Equal("P-OMITTED-HB-001", Assert.Single(result.Items).ProductCode);
        }

        [Fact]
        public async Task GetAntdTableDataAsync_GlobalSearchTreatsCodeLikeKeywordAsCodeOnly()
        {
            await SeedWarehouseTableProductAsync(
                "P-GLOBAL-TEXT-001",
                "ITEM-GLOBAL-TEXT-001",
                "夜光珠 5.5MM",
                null
            );
            await SeedWarehouseTableProductAsync(
                "P-GLOBAL-TEXT-002",
                "ITEM-GLOBAL-TEXT-002",
                "普通圆珠",
                null
            );

            var service = CreateService();

            var result = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                GlobalSearch = "5.5",
            });

            Assert.Empty(result.Items);
        }

        [Fact]
        public async Task GetAntdTableDataAsync_GlobalSearchKeepsTextMatchForNameKeyword()
        {
            await SeedWarehouseTableProductAsync(
                "P-GLOBAL-NAME-001",
                "ITEM-GLOBAL-NAME-001",
                "夜光珠",
                null
            );
            await SeedWarehouseTableProductAsync(
                "P-GLOBAL-NAME-002",
                "ITEM-GLOBAL-NAME-002",
                "普通圆珠",
                null
            );

            var service = CreateService();

            var result = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                GlobalSearch = "夜光珠",
            });

            Assert.Equal("P-GLOBAL-NAME-001", Assert.Single(result.Items).ProductCode);
        }

        [Fact]
        public async Task GetAntdTableDataAsync_FiltersWarehouseStatusTypeAndRanges()
        {
            var baseUpdatedAt = new DateTime(2026, 6, 16, 10, 30, 0, DateTimeKind.Utc);
            await SeedWarehouseTableProductAsync(
                "P-RANGE-001",
                "ITEM-RANGE-001",
                "范围命中商品",
                null,
                supplierCode: "CN-010",
                supplierName: "义乌范围厂",
                localSupplierCode: "AU-010",
                localSupplierName: "Adelaide Local",
                productType: 2,
                isActive: true,
                domesticPrice: 12.50m,
                oemPrice: 25.80m,
                importPrice: 9.60m,
                minOrderQuantity: 6,
                packingQuantity: 24,
                volume: 0.45m,
                updatedAt: baseUpdatedAt
            );
            await SeedWarehouseTableProductAsync(
                "P-RANGE-002",
                "ITEM-RANGE-002",
                "范围未命中商品",
                null,
                supplierCode: "CN-011",
                supplierName: "义乌范围厂二",
                localSupplierCode: "AU-011",
                localSupplierName: "Perth Local",
                productType: 1,
                isActive: false,
                domesticPrice: 45.10m,
                oemPrice: 60.00m,
                importPrice: 30.00m,
                minOrderQuantity: 30,
                packingQuantity: 120,
                volume: 2.35m,
                updatedAt: baseUpdatedAt.AddDays(-10)
            );

            var service = CreateService();

            var result = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["isActive"] = new[] { "true" },
                    ["productType"] = new[] { "2" },
                    ["minOrderQuantity"] = new[] { "gte:5", "lte:10" },
                    ["domesticPrice"] = new[] { "gte:10", "lte:15" },
                    ["oemPrice"] = new[] { "gte:20", "lte:30" },
                    ["importPrice"] = new[] { "gte:8", "lte:12" },
                    ["packingQuantity"] = new[] { "gte:20", "lte:30" },
                    ["volume"] = new[] { "gte:0.4", "lte:0.5" },
                    ["updatedAt"] = new[] { "gte:2026-06-15", "lte:2026-06-16" },
                },
            });

            var item = Assert.Single(result.Items);
            Assert.Equal("P-RANGE-001", item.ProductCode);
            Assert.Equal("Adelaide Local", item.LocalSupplierName);
        }

        [Fact]
        public async Task GetAntdTableDataAsync_FiltersWarehouseNumberColumnsByEqualToken()
        {
            await SeedWarehouseTableProductAsync(
                "P-NUM-EQ-001",
                "ITEM-NUM-EQ-001",
                "数值等于命中商品",
                null,
                supplierCode: "CN-NUM-EQ-001",
                domesticPrice: 12.50m,
                oemPrice: 25.80m,
                importPrice: 9.60m,
                minOrderQuantity: 6,
                packingQuantity: 24,
                volume: 0.45m
            );
            await SeedWarehouseTableProductAsync(
                "P-NUM-EQ-002",
                "ITEM-NUM-EQ-002",
                "数值等于未命中商品",
                null,
                supplierCode: "CN-NUM-EQ-002",
                domesticPrice: 13.50m,
                oemPrice: 26.80m,
                importPrice: 10.60m,
                minOrderQuantity: 8,
                packingQuantity: 36,
                volume: 0.60m
            );

            var service = CreateService();

            var result = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["minOrderQuantity"] = new[] { "__filter:eq:6" },
                    ["domesticPrice"] = new[] { "__filter:eq:12.50" },
                    ["oemPrice"] = new[] { "__filter:eq:25.80" },
                    ["importPrice"] = new[] { "__filter:eq:9.60" },
                    ["packingQuantity"] = new[] { "__filter:eq:24" },
                    ["volume"] = new[] { "__filter:eq:0.45" },
                },
            });

            var item = Assert.Single(result.Items);
            Assert.Equal("P-NUM-EQ-001", item.ProductCode);
        }

        [Fact]
        public async Task GetAntdTableDataAsync_FiltersUpdatedAtByEqualDateToken()
        {
            await SeedWarehouseTableProductAsync(
                "P-DATE-EQ-001",
                "ITEM-DATE-EQ-001",
                "日期等于命中商品",
                null,
                updatedAt: new DateTime(2026, 6, 16, 10, 30, 0, DateTimeKind.Utc)
            );
            await SeedWarehouseTableProductAsync(
                "P-DATE-EQ-002",
                "ITEM-DATE-EQ-002",
                "日期等于未命中商品",
                null,
                updatedAt: new DateTime(2026, 6, 15, 23, 59, 0, DateTimeKind.Utc)
            );

            var service = CreateService();

            var result = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["updatedAt"] = new[] { "__filter:eq:2026-06-16" },
                },
            });

            var item = Assert.Single(result.Items);
            Assert.Equal("P-DATE-EQ-001", item.ProductCode);
        }

        [Fact]
        public async Task GetAntdTableDataAsync_PackingQuantityFilterMatchesDisplayedDomesticValue()
        {
            await SeedWarehouseTableProductAsync(
                "P-PACK-VISIBLE-001",
                "ITEM-PACK-VISIBLE-001",
                "装箱数显示命中商品",
                null,
                supplierCode: "CN-PACK-001",
                packingQuantity: 24,
                warehousePackingQuantity: 120
            );
            await SeedWarehouseTableProductAsync(
                "P-PACK-VISIBLE-002",
                "ITEM-PACK-VISIBLE-002",
                "装箱数仓库值碰撞商品",
                null,
                supplierCode: "CN-PACK-002",
                packingQuantity: 120,
                warehousePackingQuantity: 24
            );

            var service = CreateService();

            var result = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["packingQty"] = new[] { "gte:20", "lte:30" },
                },
            });

            var item = Assert.Single(result.Items);
            Assert.Equal("P-PACK-VISIBLE-001", item.ProductCode);
            Assert.Equal(24, item.PackingQuantity);
            Assert.False(item.IsPackingQuantityFallback);
        }

        [Fact]
        public async Task GetAntdTableDataAsync_PackingQuantityFallsBackToWarehouseValue()
        {
            await SeedWarehouseTableProductAsync(
                "P-PACK-FALLBACK-001",
                "ITEM-PACK-FALLBACK-001",
                "装箱数仓库回退商品",
                null,
                warehousePackingQuantity: 36
            );

            var service = CreateService();

            var result = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["packingQty"] = new[] { "gte:36", "lte:36" },
                },
            });

            var item = Assert.Single(result.Items);
            Assert.Equal(36, item.PackingQuantity);
            Assert.True(item.IsPackingQuantityFallback);
        }

        [Fact]
        public async Task GetAntdTableDataAsync_NumberFilter_RangeAndExactUseOrSemantics()
        {
            await SeedWarehouseTableProductAsync(
                "P-MIX-001",
                "ITEM-MIX-001",
                "范围命中商品",
                null,
                domesticPrice: 12.5m
            );
            await SeedWarehouseTableProductAsync(
                "P-MIX-002",
                "ITEM-MIX-002",
                "精确命中商品",
                null,
                domesticPrice: 25m
            );
            await SeedWarehouseTableProductAsync(
                "P-MIX-003",
                "ITEM-MIX-003",
                "不命中商品",
                null,
                domesticPrice: 18m
            );

            var service = CreateService();

            var result = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["domesticPrice"] = new[] { "gte:10", "lte:15", "25" },
                },
            });

            Assert.Equal(2, result.Total);
            Assert.Contains(result.Items, item => item.ProductCode == "P-MIX-001");
            Assert.Contains(result.Items, item => item.ProductCode == "P-MIX-002");
            Assert.DoesNotContain(result.Items, item => item.ProductCode == "P-MIX-003");
        }

        [Fact]
        public async Task GetAntdTableDataAsync_IntNumberFilter_RangeAndExactUseOrSemantics()
        {
            await SeedWarehouseTableProductAsync(
                "P-INT-MIX-001",
                "ITEM-INT-MIX-001",
                "整数范围命中商品",
                null,
                minOrderQuantity: 12
            );
            await SeedWarehouseTableProductAsync(
                "P-INT-MIX-002",
                "ITEM-INT-MIX-002",
                "整数精确命中商品",
                null,
                minOrderQuantity: 25
            );
            await SeedWarehouseTableProductAsync(
                "P-INT-MIX-003",
                "ITEM-INT-MIX-003",
                "整数不命中商品",
                null,
                minOrderQuantity: 18
            );

            var service = CreateService();

            var result = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["minOrderQuantity"] = new[] { "gte:10", "lte:15", "25" },
                },
            });

            Assert.Equal(2, result.Total);
            Assert.Contains(result.Items, item => item.ProductCode == "P-INT-MIX-001");
            Assert.Contains(result.Items, item => item.ProductCode == "P-INT-MIX-002");
            Assert.DoesNotContain(result.Items, item => item.ProductCode == "P-INT-MIX-003");
        }

        [Fact]
        public async Task GetAntdTableDataAsync_LocalSupplierNameFallsBackToCodeWhenLookupMissing()
        {
            await SeedWarehouseTableProductAsync(
                "P-LOCAL-FALLBACK",
                "ITEM-LOCAL-FALLBACK",
                "本地供应商回退商品",
                null,
                localSupplierCode: "AU-FALLBACK",
                seedLocalSupplierRow: false
            );
            await SeedWarehouseTableProductAsync(
                "P-LOCAL-DELETED",
                "ITEM-LOCAL-DELETED",
                "本地供应商软删商品",
                null,
                localSupplierCode: "AU-DELETED",
                localSupplierName: "Deleted Supplier",
                localSupplierIsDeleted: true
            );

            var service = CreateService();

            var fallbackResult = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["itemNumber"] = new[] { "LOCAL-FALLBACK" },
                },
            });
            var deletedResult = await service.GetAntdTableDataAsync(new ReactTableRequestDto
            {
                Page = 1,
                PageSize = 20,
                Filters = new Dictionary<string, string[]>
                {
                    ["itemNumber"] = new[] { "LOCAL-DELETED" },
                },
            });

            var fallbackItem = Assert.Single(fallbackResult.Items);
            Assert.Equal("AU-FALLBACK", fallbackItem.LocalSupplierName);

            var deletedItem = Assert.Single(deletedResult.Items);
            Assert.Equal("AU-DELETED", deletedItem.LocalSupplierName);
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
        public async Task DetectAsync_货号候选编码不一致_应返回商品编码冲突字段()
        {
            await _db.Insertable(new Product
            {
                UUID = "product-uuid-local-align",
                ProductCode = "LOCAL-CODE-001",
                ProductName = "本地主档商品",
                ItemNumber = "ITEM-ALIGN-001",
                LocalSupplierCode = "200",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new Product
            {
                UUID = "product-uuid-local-align-other",
                ProductCode = "OTHER-CODE-001",
                ProductName = "其他供应商同货号商品",
                ItemNumber = "ITEM-ALIGN-001",
                LocalSupplierCode = "999",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new WarehouseProduct
            {
                ProductCode = "LOCAL-CODE-001",
                OEMPrice = 6.99m,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new WarehouseProduct
            {
                ProductCode = "OTHER-CODE-001",
                OEMPrice = 9.99m,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new DomesticProduct
            {
                ProductCode = "DOM-CODE-001",
                HBProductNo = "ITEM-ALIGN-001",
                SupplierCode = "200",
                ProductName = "国内旧编码商品",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new DomesticProduct
            {
                ProductCode = "DOM-CODE-999",
                HBProductNo = "ITEM-ALIGN-001",
                SupplierCode = "999",
                ProductName = "其他供应商国内商品",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var service = CreateService();

            var result = await service.DetectAsync(new List<DetectionItemDto>
            {
                new() { ProductCode = "DOM-CODE-001", ItemNumber = "ITEM-ALIGN-001", SupplierCode = "200" },
            });

            var item = Assert.Single(result);
            Assert.True(item.Exists);
            Assert.Equal("item_number", item.MatchType);
            Assert.Equal("200", item.SupplierCode);
            Assert.Equal("LOCAL-CODE-001", item.LocalProductCode);
            Assert.Equal("DOM-CODE-001", item.DomesticProductCode);
            Assert.True(item.HasProductCodeConflict);
            Assert.Equal("国内商品编码与本地主档商品编码不一致", item.ConflictReason);
        }

        [Fact]
        public async Task DetectAsync_同货号不同供应商_国内候选应按供应商代码筛选()
        {
            await _db.Insertable(new Product
            {
                UUID = "product-uuid-supplier-item-200",
                ProductCode = "LOCAL-SUP-200",
                ProductName = "供应商200本地主档",
                ItemNumber = "ITEM-SHARED",
                LocalSupplierCode = "200",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new Product
            {
                UUID = "product-uuid-supplier-item-999",
                ProductCode = "LOCAL-SUP-999",
                ProductName = "供应商999本地主档",
                ItemNumber = "ITEM-SHARED",
                LocalSupplierCode = "999",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new List<WarehouseProduct>
            {
                new()
                {
                    ProductCode = "LOCAL-SUP-200",
                    IsActive = true,
                    IsDeleted = false,
                },
                new()
                {
                    ProductCode = "LOCAL-SUP-999",
                    IsActive = true,
                    IsDeleted = false,
                },
            }).ExecuteCommandAsync();
            await _db.Insertable(new List<DomesticProduct>
            {
                new()
                {
                    ProductCode = "DOM-SUP-999",
                    HBProductNo = "ITEM-SHARED",
                    SupplierCode = "999",
                    ProductName = "供应商999国内商品",
                    IsActive = true,
                    IsDeleted = false,
                },
                new()
                {
                    ProductCode = "DOM-SUP-200",
                    HBProductNo = "ITEM-SHARED",
                    SupplierCode = "200",
                    ProductName = "供应商200国内商品",
                    IsActive = true,
                    IsDeleted = false,
                },
            }).ExecuteCommandAsync();

            var service = CreateService();

            var result = await service.DetectAsync(new List<DetectionItemDto>
            {
                new() { ProductCode = "DOM-MISSING", ItemNumber = "ITEM-SHARED", SupplierCode = "200" },
            });

            var item = Assert.Single(result);
            Assert.True(item.Exists);
            Assert.Equal("LOCAL-SUP-200", item.LocalProductCode);
            Assert.Equal("DOM-SUP-200", item.DomesticProductCode);
            Assert.Equal("供应商200国内商品", item.ProductName);
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
        public async Task BatchUpdateAsync_WhenStorePurchaseSyncDisabled_DoesNotUpdateStoreRetailPurchasePrice()
        {
            await SeedPriceSyncProductAsync(
                "P-BATCH-IMPORT-NO-STORE",
                purchasePrice: 4.28m,
                retailPrice: 11.99m,
                importPrice: 4.28m,
                oemPrice: 11.99m
            );
            await SeedStoreRetailPriceAsync("S01", "P-BATCH-IMPORT-NO-STORE", purchasePrice: 4.28m, retailPrice: 11.99m);
            await _db.Updateable<WarehouseProduct>()
                .SetColumns(x => new WarehouseProduct { IsActive = false })
                .Where(x => x.ProductCode == "P-BATCH-IMPORT-NO-STORE")
                .ExecuteCommandAsync();
            var service = CreateService();

            var result = await service.BatchUpdateAsync(
                new List<UpdateItemDto>
                {
                    new()
                    {
                        ProductCode = "P-BATCH-IMPORT-NO-STORE",
                        ImportPrice = 6.66m,
                        SyncStorePurchasePrice = false,
                    },
                }
            );

            var product = await _db.Queryable<Product>().SingleAsync(x => x.ProductCode == "P-BATCH-IMPORT-NO-STORE");
            var warehouseProduct = await _db.Queryable<WarehouseProduct>().SingleAsync(x => x.ProductCode == "P-BATCH-IMPORT-NO-STORE");
            var storePrice = await _db.Queryable<StoreRetailPrice>().SingleAsync(x => x.ProductCode == "P-BATCH-IMPORT-NO-STORE");

            Assert.True(result.Success);
            Assert.Equal(1, result.SuccessCount);
            Assert.Equal(6.66m, warehouseProduct.ImportPrice);
            Assert.False(warehouseProduct.IsActive);
            Assert.Equal(6.66m, product.PurchasePrice);
            Assert.Equal(4.28m, storePrice.PurchasePrice);
            Assert.Equal(11.99m, storePrice.StoreRetailPriceValue);
        }

        [Fact]
        public async Task BatchUpdateAsync_WhenAllSevenFieldsProvided_UpdatesWarehouseDomesticAndImportLinkedPrices()
        {
            const string productCode = "P-BATCH-ALL-FIELDS";
            await SeedPriceSyncProductAsync(
                productCode,
                purchasePrice: 4.28m,
                retailPrice: 11.99m,
                importPrice: 4.28m,
                oemPrice: 11.99m
            );
            await SeedStoreRetailPriceAsync(
                "S01",
                productCode,
                purchasePrice: 4.28m,
                retailPrice: 11.99m
            );
            await _db.Insertable(new DomesticProduct
            {
                ProductCode = productCode,
                ProductName = "七字段批量更新商品",
                PackingQuantity = 12,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            var service = CreateService();

            var result = await service.BatchUpdateAsync(
                new List<UpdateItemDto>
                {
                    new()
                    {
                        ProductCode = productCode,
                        DomesticPrice = 8.88m,
                        OEMPrice = 15.55m,
                        ImportPrice = 6.66m,
                        Volume = 0.125m,
                        PackingQuantity = 24,
                        MinOrderQuantity = 3,
                        IsActive = false,
                    },
                }
            );

            var warehouseProduct = await _db.Queryable<WarehouseProduct>()
                .SingleAsync(x => x.ProductCode == productCode);
            var domesticProduct = await _db.Queryable<DomesticProduct>()
                .SingleAsync(x => x.ProductCode == productCode);
            var product = await _db.Queryable<Product>()
                .SingleAsync(x => x.ProductCode == productCode);
            var storeRetailPrice = await _db.Queryable<StoreRetailPrice>()
                .SingleAsync(x => x.ProductCode == productCode);

            Assert.True(result.Success);
            Assert.Equal(1, result.SuccessCount);
            Assert.Equal(8.88m, warehouseProduct.DomesticPrice);
            Assert.Equal(15.55m, warehouseProduct.OEMPrice);
            Assert.Equal(6.66m, warehouseProduct.ImportPrice);
            Assert.Equal(0.125m, warehouseProduct.Volume);
            Assert.Equal(24, warehouseProduct.PackingQuantity);
            Assert.Equal(3, warehouseProduct.MinOrderQuantity);
            Assert.False(warehouseProduct.IsActive);
            Assert.Equal(24, domesticProduct.PackingQuantity);
            Assert.Equal(6.66m, product.PurchasePrice);
            Assert.Equal(6.66m, storeRetailPrice.PurchasePrice);
            Assert.Equal(11.99m, storeRetailPrice.StoreRetailPriceValue);
        }

        [Fact]
        public async Task BatchUpdateAsync_UpdatesPackingAndMinOrderQuantityAndKeepsZeroOnNullPatch()
        {
            await SeedPriceSyncProductAsync(
                "P-BATCH-QUANTITY-EXISTING",
                purchasePrice: 4.28m,
                retailPrice: 11.99m,
                importPrice: 4.28m,
                oemPrice: 11.99m
            );
            await _db.Insertable(new DomesticProduct
            {
                ProductCode = "P-BATCH-QUANTITY-EXISTING",
                ProductName = "批量数量更新商品",
                PackingQuantity = 12,
                IsActive = true,
                IsDeleted = false,
                UpdatedAt = new DateTime(2020, 1, 1),
            }).ExecuteCommandAsync();
            await _db.Updateable<WarehouseProduct>()
                .SetColumns(x => new WarehouseProduct
                {
                    PackingQuantity = 12,
                    MinOrderQuantity = 3,
                })
                .Where(x => x.ProductCode == "P-BATCH-QUANTITY-EXISTING")
                .ExecuteCommandAsync();
            var service = CreateService();

            var zeroResult = await service.BatchUpdateAsync(
                new List<UpdateItemDto>
                {
                    new()
                    {
                        ProductCode = "P-BATCH-QUANTITY-EXISTING",
                        PackingQuantity = 0,
                        MinOrderQuantity = 0,
                    },
                }
            );
            var nullResult = await service.BatchUpdateAsync(
                new List<UpdateItemDto>
                {
                    new()
                    {
                        ProductCode = "P-BATCH-QUANTITY-EXISTING",
                        DomesticPrice = 9.99m,
                    },
                }
            );

            var warehouseProduct = await _db.Queryable<WarehouseProduct>()
                .SingleAsync(x => x.ProductCode == "P-BATCH-QUANTITY-EXISTING");
            var domesticProduct = await _db.Queryable<DomesticProduct>()
                .SingleAsync(x => x.ProductCode == "P-BATCH-QUANTITY-EXISTING");

            Assert.True(zeroResult.Success);
            Assert.True(nullResult.Success);
            Assert.Equal(0, warehouseProduct.PackingQuantity);
            Assert.Equal(0, warehouseProduct.MinOrderQuantity);
            Assert.Equal(0, domesticProduct.PackingQuantity);
            Assert.True(domesticProduct.UpdatedAt > new DateTime(2020, 1, 1));
            Assert.Equal(9.99m, warehouseProduct.DomesticPrice);
        }

        [Fact]
        public async Task BatchUpdateAsync_WhenWarehouseProductMissing_CreatesQuantitiesWithoutUpdatingDeletedDomesticProduct()
        {
            const string productCode = "P-BATCH-QUANTITY-NEW";
            await _db.Insertable(new Product
            {
                UUID = $"product-uuid-{productCode}",
                ProductCode = productCode,
                ProductName = "批量新建仓库商品",
                ItemNumber = "ITEM-BATCH-QUANTITY-NEW",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new DomesticProduct
            {
                ProductCode = productCode,
                ProductName = "已删除国内商品",
                PackingQuantity = 7,
                IsActive = false,
                IsDeleted = true,
            }).ExecuteCommandAsync();
            var service = CreateService();

            var result = await service.BatchUpdateAsync(
                new List<UpdateItemDto>
                {
                    new()
                    {
                        ItemNumber = "ITEM-BATCH-QUANTITY-NEW",
                        PackingQuantity = 18,
                        MinOrderQuantity = 4,
                    },
                }
            );

            var warehouseProduct = await _db.Queryable<WarehouseProduct>()
                .SingleAsync(x => x.ProductCode == productCode);
            var deletedDomesticProduct = await _db.Queryable<DomesticProduct>()
                .SingleAsync(x => x.ProductCode == productCode);

            Assert.True(result.Success);
            Assert.Equal(1, result.SuccessCount);
            Assert.Equal(18, warehouseProduct.PackingQuantity);
            Assert.Equal(4, warehouseProduct.MinOrderQuantity);
            Assert.True(warehouseProduct.IsActive);
            Assert.Equal(7, deletedDomesticProduct.PackingQuantity);
        }

        [Fact]
        public async Task BatchUpdateAsync_WhenExistingProductCodeRepeats_ProcessesFirstAndKeepsOtherItems()
        {
            const string firstCode = "P-BATCH-DUPLICATE-EXISTING";
            const string otherCode = "P-BATCH-DUPLICATE-OTHER";
            await SeedPriceSyncProductAsync(firstCode, 4.28m, 11.99m, 4.28m, 11.99m);
            await SeedPriceSyncProductAsync(otherCode, 4.28m, 11.99m, 4.28m, 11.99m);
            var service = CreateService();

            var result = await service.BatchUpdateAsync(
                new List<UpdateItemDto>
                {
                    new() { ProductCode = firstCode, DomesticPrice = 8.88m },
                    new()
                    {
                        ProductCode = firstCode.ToLowerInvariant(),
                        OEMPrice = 99.99m,
                    },
                    new() { ProductCode = otherCode, DomesticPrice = 7.77m },
                }
            );

            var first = await _db.Queryable<WarehouseProduct>()
                .SingleAsync(x => x.ProductCode == firstCode);
            var other = await _db.Queryable<WarehouseProduct>()
                .SingleAsync(x => x.ProductCode == otherCode);

            Assert.True(result.Success);
            Assert.Equal(2, result.SuccessCount);
            Assert.Equal(1, result.FailedCount);
            Assert.Contains(result.Errors, error => error.Contains("批次内商品编码重复"));
            Assert.Equal(8.88m, first.DomesticPrice);
            Assert.Equal(11.99m, first.OEMPrice);
            Assert.Equal(7.77m, other.DomesticPrice);
        }

        [Fact]
        public async Task BatchUpdateAsync_WhenMissingProductResolvesByItemAndCode_RejectsDuplicateCreateOnly()
        {
            const string firstCode = "P-BATCH-DUPLICATE-NEW";
            const string otherCode = "P-BATCH-DUPLICATE-NEW-OTHER";
            await _db.Insertable(new List<Product>
            {
                new()
                {
                    UUID = $"product-uuid-{firstCode}",
                    ProductCode = firstCode,
                    ProductName = "待新建重复商品",
                    ItemNumber = "ITEM-BATCH-DUPLICATE-NEW",
                    IsActive = true,
                    IsDeleted = false,
                },
                new()
                {
                    UUID = $"product-uuid-{otherCode}",
                    ProductCode = otherCode,
                    ProductName = "待新建其他商品",
                    ItemNumber = "ITEM-BATCH-DUPLICATE-NEW-OTHER",
                    IsActive = true,
                    IsDeleted = false,
                },
            }).ExecuteCommandAsync();
            var service = CreateService();

            var result = await service.BatchUpdateAsync(
                new List<UpdateItemDto>
                {
                    new()
                    {
                        ItemNumber = "ITEM-BATCH-DUPLICATE-NEW",
                        DomesticPrice = 8.88m,
                    },
                    new() { ProductCode = firstCode, OEMPrice = 99.99m },
                    new() { ProductCode = otherCode, DomesticPrice = 7.77m },
                }
            );

            var first = await _db.Queryable<WarehouseProduct>()
                .SingleAsync(x => x.ProductCode == firstCode);
            var other = await _db.Queryable<WarehouseProduct>()
                .SingleAsync(x => x.ProductCode == otherCode);

            Assert.True(result.Success);
            Assert.Equal(2, result.SuccessCount);
            Assert.Equal(1, result.FailedCount);
            Assert.Contains(result.Errors, error => error.Contains("批次内商品编码重复"));
            Assert.Equal(8.88m, first.DomesticPrice);
            Assert.Null(first.OEMPrice);
            Assert.Equal(7.77m, other.DomesticPrice);
        }

        [Theory]
        [InlineData(-1, null)]
        [InlineData(null, -1)]
        public async Task BatchUpdateAsync_WhenQuantityIsNegative_RejectsWholeItem(
            int? packingQuantity,
            int? minOrderQuantity
        )
        {
            const string productCode = "P-BATCH-NEGATIVE-QUANTITY";
            await SeedPriceSyncProductAsync(
                productCode,
                purchasePrice: 4.28m,
                retailPrice: 11.99m,
                importPrice: 4.28m,
                oemPrice: 11.99m
            );
            await _db.Insertable(new DomesticProduct
            {
                ProductCode = productCode,
                ProductName = "负数校验商品",
                PackingQuantity = 12,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Updateable<WarehouseProduct>()
                .SetColumns(x => new WarehouseProduct
                {
                    DomesticPrice = 5.55m,
                    PackingQuantity = 12,
                    MinOrderQuantity = 3,
                    IsActive = true,
                })
                .Where(x => x.ProductCode == productCode)
                .ExecuteCommandAsync();
            var service = CreateService();

            var result = await service.BatchUpdateAsync(
                new List<UpdateItemDto>
                {
                    new()
                    {
                        ProductCode = productCode,
                        DomesticPrice = 9.99m,
                        PackingQuantity = packingQuantity,
                        MinOrderQuantity = minOrderQuantity,
                        IsActive = false,
                    },
                }
            );

            var warehouseProduct = await _db.Queryable<WarehouseProduct>()
                .SingleAsync(x => x.ProductCode == productCode);
            var domesticProduct = await _db.Queryable<DomesticProduct>()
                .SingleAsync(x => x.ProductCode == productCode);

            Assert.Equal(0, result.SuccessCount);
            Assert.Equal(1, result.FailedCount);
            Assert.Contains(result.Errors, error => error.Contains("不能为负数"));
            Assert.Equal(5.55m, warehouseProduct.DomesticPrice);
            Assert.Equal(12, warehouseProduct.PackingQuantity);
            Assert.Equal(3, warehouseProduct.MinOrderQuantity);
            Assert.True(warehouseProduct.IsActive);
            Assert.Equal(12, domesticProduct.PackingQuantity);
        }

        [Fact]
        public async Task BatchUpdateAsync_WhenWarehouseProductMissing_SyncsZeroPackingToActiveDomesticProduct()
        {
            const string productCode = "P-BATCH-QUANTITY-NEW-ACTIVE";
            await _db.Insertable(new Product
            {
                UUID = $"product-uuid-{productCode}",
                ProductCode = productCode,
                ProductName = "新建仓库数量双写商品",
                ItemNumber = "ITEM-BATCH-QUANTITY-NEW-ACTIVE",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            await _db.Insertable(new DomesticProduct
            {
                ProductCode = productCode,
                ProductName = "活跃国内商品",
                PackingQuantity = 7,
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            var service = CreateService();

            var result = await service.BatchUpdateAsync(
                new List<UpdateItemDto>
                {
                    new()
                    {
                        ItemNumber = "ITEM-BATCH-QUANTITY-NEW-ACTIVE",
                        PackingQuantity = 0,
                        MinOrderQuantity = 0,
                    },
                }
            );

            var warehouseProduct = await _db.Queryable<WarehouseProduct>()
                .SingleAsync(x => x.ProductCode == productCode);
            var domesticProduct = await _db.Queryable<DomesticProduct>()
                .SingleAsync(x => x.ProductCode == productCode);

            Assert.True(result.Success);
            Assert.Equal(1, result.SuccessCount);
            Assert.Equal(0, warehouseProduct.PackingQuantity);
            Assert.Equal(0, warehouseProduct.MinOrderQuantity);
            Assert.Equal(0, domesticProduct.PackingQuantity);
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

            Assert.Equal("零售价和RRP不一致", error.Message);
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
            string? warehouseCategoryGuid,
            string? englishName = null,
            string? barcode = null,
            string? supplierCode = null,
            string? supplierName = null,
            string? localSupplierCode = null,
            string? localSupplierName = null,
            int? productType = null,
            bool isActive = true,
            decimal? domesticPrice = null,
            decimal? oemPrice = null,
            decimal? importPrice = null,
            int? minOrderQuantity = null,
            int? packingQuantity = null,
            int? warehousePackingQuantity = null,
            decimal? volume = null,
            bool seedLocalSupplierRow = true,
            bool localSupplierIsDeleted = false,
            DateTime? createdAt = null,
            DateTime? updatedAt = null
        )
        {
            if (!string.IsNullOrWhiteSpace(supplierCode))
            {
                var existingSupplier = await _db.Queryable<ChinaSupplier>()
                    .AnyAsync(s => s.SupplierCode == supplierCode);
                if (!existingSupplier)
                {
                    await _db
                        .Insertable(new ChinaSupplier
                        {
                            Guid = $"{supplierCode}-guid",
                            SupplierCode = supplierCode,
                            SupplierName = supplierName ?? supplierCode,
                            Status = 1,
                            IsDeleted = false,
                        })
                        .ExecuteCommandAsync();
                }

                await _db
                    .Insertable(new DomesticProduct
                    {
                        ProductCode = productCode,
                        SupplierCode = supplierCode,
                        ProductName = productName,
                        EnglishProductName = englishName,
                        HBProductNo = itemNumber,
                        Barcode = barcode,
                        ProductType = productType ?? 0,
                        PackingQuantity = packingQuantity,
                        UnitVolume = volume,
                        IsActive = isActive,
                        IsDeleted = false,
                    })
                    .ExecuteCommandAsync();
            }

            if (!string.IsNullOrWhiteSpace(localSupplierCode) && seedLocalSupplierRow)
            {
                var existingLocalSupplier = await _db.Queryable<HBLocalSupplier>()
                    .AnyAsync(s => s.LocalSupplierCode == localSupplierCode);
                if (!existingLocalSupplier)
                {
                    await _db
                        .Insertable(new HBLocalSupplier
                        {
                            Guid = $"{localSupplierCode}-guid",
                            LocalSupplierCode = localSupplierCode,
                            Name = localSupplierName ?? localSupplierCode,
                            Status = 1,
                            IsDeleted = localSupplierIsDeleted,
                        })
                        .ExecuteCommandAsync();
                }
            }

            await _db.Insertable(new Product
            {
                UUID = $"{productCode}-uuid",
                ProductCode = productCode,
                ItemNumber = itemNumber,
                ProductName = productName,
                EnglishName = englishName,
                Barcode = barcode,
                LocalSupplierCode = localSupplierCode,
                WarehouseCategoryGUID = warehouseCategoryGuid,
                ProductType = productType,
                IsActive = isActive,
                CreatedAt = createdAt ?? DateTime.UtcNow,
                UpdatedAt = updatedAt ?? DateTime.UtcNow,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new WarehouseProduct
            {
                ProductCode = productCode,
                DomesticPrice = domesticPrice,
                OEMPrice = oemPrice,
                ImportPrice = importPrice,
                MinOrderQuantity = minOrderQuantity,
                PackingQuantity = warehousePackingQuantity ?? packingQuantity,
                Volume = volume,
                IsActive = isActive,
                CreatedAt = createdAt ?? DateTime.UtcNow,
                UpdatedAt = updatedAt ?? DateTime.UtcNow,
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

        private async Task SeedProductLocationAsync(string productCode, string locationGuid)
        {
            await _db.Insertable(new ProductLocation
            {
                Guid = $"{productCode}-{locationGuid}",
                ProductCode = productCode,
                LocationGuid = locationGuid,
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
