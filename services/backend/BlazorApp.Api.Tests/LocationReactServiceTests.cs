using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public sealed class LocationReactServiceTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _sqliteConnection;
        private readonly SqlSugarClient _db;

        public LocationReactServiceTests()
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
                typeof(Location),
                typeof(ProductLocation),
                typeof(Product)
            );
        }

        [Fact]
        public async Task LookupAsync_StillReturnsLocationWhenKeywordMatchesLocationCode()
        {
            await SeedBoundProductLocationAsync();
            var service = CreateService();

            var result = await service.LookupAsync("B-03-04");

            var item = Assert.Single(result);
            Assert.Equal("loc-001", item.LocationGuid);
            Assert.Equal("B-03-04-07", item.LocationCode);
            Assert.Equal(1, item.ProductCount);
        }

        [Fact]
        public async Task LookupAsync_ReturnsBoundLocationWhenKeywordMatchesProductItemNumber()
        {
            await SeedBoundProductLocationAsync();
            var service = CreateService();

            var result = await service.LookupAsync("HB-246-PP-053");

            var item = Assert.Single(result);
            Assert.Equal("loc-001", item.LocationGuid);
            Assert.Equal("B-03-04-07", item.LocationCode);
            Assert.Equal(1, item.ProductCount);
        }

        [Fact]
        public async Task LookupAsync_ReturnsBoundLocationWhenKeywordMatchesProductBarcode()
        {
            await SeedBoundProductLocationAsync();
            var service = CreateService();

            var result = await service.LookupAsync("9525812461024");

            var item = Assert.Single(result);
            Assert.Equal("loc-001", item.LocationGuid);
            Assert.Equal("B-03-04-07", item.LocationCode);
            Assert.Equal(1, item.ProductCount);
        }

        [Fact]
        public async Task LookupAsync_ReturnsDistinctLocationsBeforeTakingProductMatches()
        {
            await SeedDuplicateProductMatchesAcrossLocationsAsync();
            var service = CreateService();

            var result = await service.LookupAsync("MATCH-ITEM");

            Assert.Equal(2, result.Count);
            Assert.Contains(result, item => item.LocationGuid == "loc-many");
            Assert.Contains(result, item => item.LocationGuid == "loc-other");
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

        private async Task SeedBoundProductLocationAsync()
        {
            await _db.Insertable(new Location
            {
                LocationGuid = "loc-001",
                LocationCode = "B-03-04-07",
                LocationBarcode = "LOC-B030407",
                LocationType = 1,
                Status = 1,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new Product
            {
                UUID = "product-uuid-001",
                ProductCode = "P001",
                ProductName = "Glitter Pom Pom 25MM",
                ItemNumber = "HB-246-PP-053",
                Barcode = "9525812461024",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new ProductLocation
            {
                Guid = "product-location-001",
                ProductCode = "P001",
                LocationGuid = "loc-001",
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new Product
            {
                UUID = "product-uuid-deleted",
                ProductCode = "P-DELETED",
                ProductName = "Deleted product",
                ItemNumber = "HB-DELETED",
                Barcode = "DELETED-BARCODE",
                IsActive = false,
                IsDeleted = true,
            }).ExecuteCommandAsync();

            await _db.Insertable(new ProductLocation
            {
                Guid = "product-location-deleted",
                ProductCode = "P-DELETED",
                LocationGuid = "loc-001",
                IsDeleted = false,
            }).ExecuteCommandAsync();
        }

        private async Task SeedDuplicateProductMatchesAcrossLocationsAsync()
        {
            await _db.Insertable(new[]
            {
                new Location
                {
                    LocationGuid = "loc-many",
                    LocationCode = "A-01-01-01",
                    LocationBarcode = "LOC-MANY",
                    LocationType = 1,
                    Status = 1,
                    IsDeleted = false,
                },
                new Location
                {
                    LocationGuid = "loc-other",
                    LocationCode = "B-01-01-01",
                    LocationBarcode = "LOC-OTHER",
                    LocationType = 1,
                    Status = 1,
                    IsDeleted = false,
                },
            }).ExecuteCommandAsync();

            for (var index = 0; index < 21; index += 1)
            {
                var productCode = $"P-MANY-{index:00}";
                await _db.Insertable(new Product
                {
                    UUID = $"product-uuid-many-{index:00}",
                    ProductCode = productCode,
                    ProductName = $"Many match {index:00}",
                    ItemNumber = $"MATCH-ITEM-{index:00}",
                    Barcode = $"MATCH-BAR-{index:00}",
                    IsActive = true,
                    IsDeleted = false,
                }).ExecuteCommandAsync();

                await _db.Insertable(new ProductLocation
                {
                    Guid = $"product-location-many-{index:00}",
                    ProductCode = productCode,
                    LocationGuid = "loc-many",
                    IsDeleted = false,
                }).ExecuteCommandAsync();
            }

            await _db.Insertable(new Product
            {
                UUID = "product-uuid-other",
                ProductCode = "P-OTHER",
                ProductName = "Other match",
                ItemNumber = "MATCH-ITEM-OTHER",
                Barcode = "MATCH-BAR-OTHER",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            await _db.Insertable(new ProductLocation
            {
                Guid = "product-location-other",
                ProductCode = "P-OTHER",
                LocationGuid = "loc-other",
                IsDeleted = false,
            }).ExecuteCommandAsync();
        }

        private LocationReactService CreateService()
        {
            return new LocationReactService(
                CreateSqlSugarContext(_db),
                Mock.Of<ICurrentUserService>(),
                NullLogger<LocationReactService>.Instance
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
    }
}
