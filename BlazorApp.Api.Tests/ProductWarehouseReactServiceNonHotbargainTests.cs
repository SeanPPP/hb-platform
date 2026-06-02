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
    public sealed class ProductWarehouseReactServiceNonHotbargainTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _sqliteConnection;
        private readonly SqlSugarClient _db;

        public ProductWarehouseReactServiceNonHotbargainTests()
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
                typeof(HBLocalSupplier),
                typeof(ProductLocation)
            );
        }

        [Fact]
        public async Task GetNonHotbargainProductsNotInWarehouseAsync_IncludesProductsFromSupplier200()
        {
            await _db.Insertable(new HBLocalSupplier
            {
                Guid = "supplier-200-guid",
                LocalSupplierCode = "200",
                Name = "默认供应商",
                Status = 1,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new Product
            {
                UUID = "product-200",
                ProductCode = "P200",
                ItemNumber = "ITEM-200",
                ProductName = "供应商200商品",
                LocalSupplierCode = "200",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var service = CreateService();

            var result = await service.GetNonHotbargainProductsNotInWarehouseAsync(
                new GetNonHotbargainProductsNotInWarehouseRequestDto { Page = 1, PageSize = 20 }
            );

            var item = Assert.Single(result.Items);
            Assert.Equal("P200", item.ProductCode);
            Assert.Equal("200", item.LocalSupplierCode);
            Assert.Equal("默认供应商", item.LocalSupplierName);
        }

        [Fact]
        public async Task ImportNonHotbargainProductsAsync_AllowsReimportWhenWarehouseRecordIsSoftDeleted()
        {
            await _db.Insertable(new Product
            {
                UUID = "product-soft-delete",
                ProductCode = "P201",
                ItemNumber = "ITEM-201",
                ProductName = "可重新导入商品",
                PurchasePrice = 15.5m,
                LocalSupplierCode = "201",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var oldCreatedAt = DateTime.UtcNow.AddDays(-10);
            await _db.Insertable(new WarehouseProduct
            {
                ProductCode = "P201",
                ImportPrice = 9.9m,
                MinOrderQuantity = 6,
                StockValue = 99m,
                StockAlertQuantity = 3,
                Volume = 1.23m,
                PackingQuantity = 12,
                IsActive = false,
                IsDeleted = true,
                CreatedAt = oldCreatedAt,
                CreatedBy = "old-user",
                UpdatedBy = "old-updater",
            }).ExecuteCommandAsync();

            await _db.Insertable(new ProductLocation
            {
                Guid = "location-link-201",
                ProductCode = "P201",
                LocationGuid = "location-201",
                IsDeleted = false,
            }).ExecuteCommandAsync();

            var service = CreateService();

            var result = await service.ImportNonHotbargainProductsAsync(
                new ImportNonHotbargainRequestDto { ProductCodes = { "P201" } }
            );

            Assert.True(result.Success);
            Assert.Equal(1, result.SuccessCount);
            Assert.Equal(0, result.FailedCount);
            Assert.Single(result.Results);
            Assert.True(result.Results[0].Success);

            var warehouseProduct = await _db
                .Queryable<WarehouseProduct>()
                .Where(wp => wp.ProductCode == "P201")
                .SingleAsync();

            Assert.False(warehouseProduct.IsDeleted);
            Assert.True(warehouseProduct.IsActive);
            Assert.Equal(15.5m, warehouseProduct.ImportPrice);
            Assert.Equal(0, warehouseProduct.DomesticPrice);
            Assert.Equal(0, warehouseProduct.OEMPrice);
            Assert.Equal(0, warehouseProduct.StockQuantity);
            Assert.Null(warehouseProduct.MinOrderQuantity);
            Assert.Null(warehouseProduct.StockValue);
            Assert.Null(warehouseProduct.StockAlertQuantity);
            Assert.Null(warehouseProduct.Volume);
            Assert.Null(warehouseProduct.PackingQuantity);
            Assert.Null(warehouseProduct.CreatedBy);
            Assert.Null(warehouseProduct.UpdatedBy);
            Assert.True(warehouseProduct.CreatedAt > oldCreatedAt);

            var locationCount = await _db
                .Queryable<ProductLocation>()
                .Where(pl => pl.ProductCode == "P201")
                .CountAsync();
            Assert.Equal(0, locationCount);
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
