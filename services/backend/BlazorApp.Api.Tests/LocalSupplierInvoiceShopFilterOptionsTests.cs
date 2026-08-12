using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class LocalSupplierInvoiceShopFilterOptionsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public LocalSupplierInvoiceShopFilterOptionsTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = _connection.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        _db.CodeFirst.InitTables<HBLocalSupplier, StoreLocalSupplierInvoice>();
    }

    [Fact]
    public async Task GetFilterOptionsAsync_ScopesStoresAndIncludesInactiveHistoricalSuppliers()
    {
        await _db.Insertable(
            new[]
            {
                new HBLocalSupplier
                {
                    Guid = "supplier-a",
                    LocalSupplierCode = "SUP-A",
                    Name = "Alpha Supplier",
                    Status = 1,
                    IsDeleted = false,
                },
                new HBLocalSupplier
                {
                    Guid = "supplier-z",
                    LocalSupplierCode = "SUP-Z",
                    Name = "Zebra Supplier",
                    Status = 0,
                    IsDeleted = false,
                },
                new HBLocalSupplier
                {
                    Guid = "supplier-b",
                    LocalSupplierCode = "SUP-B",
                    Name = "Beta Supplier",
                    Status = 1,
                    IsDeleted = false,
                },
                new HBLocalSupplier
                {
                    Guid = "supplier-d",
                    LocalSupplierCode = "SUP-D",
                    Name = "Deleted Invoice Supplier",
                    Status = 1,
                    IsDeleted = false,
                },
            }
        ).ExecuteCommandAsync();
        await _db.Insertable(
            new[]
            {
                Invoice("invoice-a", "S01", "SUP-A"),
                Invoice("invoice-a-duplicate", "S01", "SUP-A"),
                Invoice("invoice-z", "S01", "SUP-Z"),
                Invoice("invoice-other-store", "S02", "SUP-B"),
                Invoice("invoice-deleted", "S01", "SUP-D", isDeleted: true),
            }
        ).ExecuteCommandAsync();

        var result = await CreateService().GetFilterOptionsAsync(
            new List<string> { "S01" },
            storeCode: null
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(
            new[] { ("SUP-A", "Alpha Supplier"), ("SUP-Z", "Zebra Supplier") },
            result.Data!.Suppliers.Select(option => (option.Value, option.Label)).ToArray()
        );
    }

    [Fact]
    public async Task GetFilterOptionsAsync_SelectedStoreOutsideAllowedScopeReturnsNoSuppliers()
    {
        await _db.Insertable(
            new HBLocalSupplier
            {
                Guid = "supplier-b",
                LocalSupplierCode = "SUP-B",
                Name = "Beta Supplier",
                Status = 1,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
        await _db.Insertable(Invoice("invoice-b", "S02", "SUP-B")).ExecuteCommandAsync();

        var result = await CreateService().GetFilterOptionsAsync(
            new List<string> { "S01" },
            "S02"
        );

        Assert.True(result.Success, result.Message);
        Assert.Empty(result.Data!.Suppliers);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static StoreLocalSupplierInvoice Invoice(
        string invoiceGuid,
        string storeCode,
        string supplierCode,
        bool isDeleted = false
    ) =>
        new()
        {
            InvoiceGUID = invoiceGuid,
            StoreCode = storeCode,
            SupplierCode = supplierCode,
            IsDeleted = isDeleted,
        };

    private LocalSupplierInvoicesReactService CreateService()
    {
        return new LocalSupplierInvoicesReactService(
            CreateSqlSugarContext(_db),
            CreateHqSqlSugarContext(),
            Mock.Of<IMapper>(),
            NullLogger<LocalSupplierInvoicesReactService>.Instance,
            Mock.Of<IAutoPricingService>(),
            WarehouseProductChangeHistoryTestDouble.CreateNoop()
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
        )!;
        dbField.SetValue(context, db);
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
        )!;
        dbField.SetValue(context, Mock.Of<ISqlSugarClient>());
        return context;
    }
}
