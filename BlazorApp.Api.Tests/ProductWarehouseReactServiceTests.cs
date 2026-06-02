using System;
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

        public void Dispose()
        {
            _db.Dispose();
            _sqliteConnection.Dispose();

            if (File.Exists(_dbPath))
            {
                SqliteTempFileCleanup.DeleteIfExists(_dbPath);
            }
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
