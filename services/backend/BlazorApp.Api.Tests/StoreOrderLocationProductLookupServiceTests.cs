using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public sealed class StoreOrderLocationProductLookupServiceTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _sqliteConnection;
        private readonly SqlSugarClient _db;

        public StoreOrderLocationProductLookupServiceTests()
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

            // 核心解析只应依赖货位和货位绑定，不需要商品 DTO 或商品主档。
            _db.CodeFirst.InitTables(typeof(Location), typeof(ProductLocation));
        }

        [Fact]
        public async Task LookupAsync_BarcodeWinsOverCode_AndDeduplicatesProductCodes()
        {
            await SeedLocationsAsync();
            var service = CreateService();

            var result = await service.LookupAsync("LOC-PRIORITY");

            Assert.NotNull(result);
            Assert.Equal("locationBarcode", result!.MatchType);
            Assert.Equal(new[] { "P-BARCODE" }, result.ProductCodes);
        }

        [Fact]
        public async Task LookupAsync_MatchesExactEnabledStorageLocation_AndExcludesDeletedBindings()
        {
            await SeedLocationsAsync();
            var service = CreateService();

            var result = await service.LookupAsync("B-01-02-03");

            Assert.NotNull(result);
            Assert.Equal("locationCode", result!.MatchType);
            Assert.Equal(new[] { "P-STORAGE" }, result.ProductCodes);
        }

        [Fact]
        public async Task LookupAsync_DoesNotMatchPartialLocationCodeOrInvalidLocation()
        {
            await SeedLocationsAsync();
            var service = CreateService();

            var partialCodeResult = await service.LookupAsync("B-01-02");
            var disabledLocationResult = await service.LookupAsync("C-01-02-03");
            var invalidTypeResult = await service.LookupAsync("D-01-02-03");
            var deletedLocationResult = await service.LookupAsync("E-01-02-03");

            Assert.Null(partialCodeResult);
            Assert.Null(disabledLocationResult);
            Assert.Null(invalidTypeResult);
            Assert.Null(deletedLocationResult);
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

        private async Task SeedLocationsAsync()
        {
            await _db.Insertable(new[]
            {
                new Location
                {
                    LocationGuid = "location-barcode",
                    LocationType = 1,
                    LocationCode = "A-01-01-01",
                    LocationBarcode = "LOC-PRIORITY",
                    Status = 1,
                    IsDeleted = false,
                },
                new Location
                {
                    LocationGuid = "location-code-collision",
                    LocationType = 2,
                    LocationCode = "LOC-PRIORITY",
                    LocationBarcode = "OTHER-BARCODE",
                    Status = 1,
                    IsDeleted = false,
                },
                new Location
                {
                    LocationGuid = "location-storage",
                    LocationType = 2,
                    LocationCode = "B-01-02-03",
                    LocationBarcode = "LOC-STORAGE",
                    Status = 1,
                    IsDeleted = false,
                },
                new Location
                {
                    LocationGuid = "location-disabled",
                    LocationType = 1,
                    LocationCode = "C-01-02-03",
                    LocationBarcode = "LOC-DISABLED",
                    Status = 0,
                    IsDeleted = false,
                },
                new Location
                {
                    LocationGuid = "location-invalid-type",
                    LocationType = 3,
                    LocationCode = "D-01-02-03",
                    LocationBarcode = "LOC-INVALID-TYPE",
                    Status = 1,
                    IsDeleted = false,
                },
                new Location
                {
                    LocationGuid = "location-deleted",
                    LocationType = 1,
                    LocationCode = "E-01-02-03",
                    LocationBarcode = "LOC-DELETED",
                    Status = 1,
                    IsDeleted = true,
                },
            }).ExecuteCommandAsync();

            await _db.Insertable(new[]
            {
                new ProductLocation
                {
                    Guid = "binding-barcode-1",
                    ProductCode = "P-BARCODE",
                    LocationGuid = "location-barcode",
                    IsDeleted = false,
                },
                new ProductLocation
                {
                    Guid = "binding-barcode-2",
                    ProductCode = "P-BARCODE",
                    LocationGuid = "location-barcode",
                    IsDeleted = false,
                },
                new ProductLocation
                {
                    Guid = "binding-code-collision",
                    ProductCode = "P-CODE-COLLISION",
                    LocationGuid = "location-code-collision",
                    IsDeleted = false,
                },
                new ProductLocation
                {
                    Guid = "binding-storage-1",
                    ProductCode = "P-STORAGE",
                    LocationGuid = "location-storage",
                    IsDeleted = false,
                },
                new ProductLocation
                {
                    Guid = "binding-storage-2",
                    ProductCode = "P-STORAGE",
                    LocationGuid = "location-storage",
                    IsDeleted = false,
                },
                new ProductLocation
                {
                    Guid = "binding-storage-deleted",
                    ProductCode = "P-DELETED-BINDING",
                    LocationGuid = "location-storage",
                    IsDeleted = true,
                },
                new ProductLocation
                {
                    Guid = "binding-disabled",
                    ProductCode = "P-DISABLED",
                    LocationGuid = "location-disabled",
                    IsDeleted = false,
                },
                new ProductLocation
                {
                    Guid = "binding-invalid-type",
                    ProductCode = "P-INVALID-TYPE",
                    LocationGuid = "location-invalid-type",
                    IsDeleted = false,
                },
                new ProductLocation
                {
                    Guid = "binding-deleted-location",
                    ProductCode = "P-DELETED-LOCATION",
                    LocationGuid = "location-deleted",
                    IsDeleted = false,
                },
            }).ExecuteCommandAsync();
        }

        private StoreOrderLocationProductLookupService CreateService()
        {
            return new StoreOrderLocationProductLookupService(CreateSqlSugarContext(_db));
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
