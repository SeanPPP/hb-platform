using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
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

        public void Dispose()
        {
            _db.Dispose();
            _sqliteConnection.Dispose();

            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
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

        private LocalSupplierInvoicesReactService CreateService()
        {
            return new LocalSupplierInvoicesReactService(
                CreateSqlSugarContext(_db),
                CreateHqSqlSugarContext(),
                Mock.Of<IMapper>(),
                NullLogger<LocalSupplierInvoicesReactService>.Instance,
                Mock.Of<IAutoPricingService>()
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
