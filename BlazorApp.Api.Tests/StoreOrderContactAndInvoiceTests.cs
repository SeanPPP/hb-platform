using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class StoreOrderContactAndInvoiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;

    public StoreOrderContactAndInvoiceTests()
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
            typeof(WareHouseOrder),
            typeof(WareHouseOrderDetails),
            typeof(Product),
            typeof(WarehouseProduct),
            typeof(DomesticProduct),
            typeof(ProductLocation),
            typeof(Location),
            typeof(ProductGrade),
            typeof(User),
            typeof(UserStore)
        );
    }

    [Fact]
    public async Task GetStoreByCodeAsync_ReturnsContactEmailFromStore()
    {
        await _db.Insertable(
            new Store
            {
                StoreGUID = "store-1",
                StoreCode = "S001",
                StoreName = "Test Store",
                Address = "1 Test Street",
                Phone = "123456",
                ContactEmail = "store@example.com",
                IsActive = true,
            }
        ).ExecuteCommandAsync();

        var service = CreateStoreService();

        var result = await service.GetStoreByCodeAsync("S001");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("store@example.com", result.Data!.ContactEmail);
    }

    [Fact]
    public async Task GetOrderDetailAsync_AndFull_ReturnStoreContactEmail()
    {
        await SeedStoreOrderGraphAsync();
        var service = CreateStoreOrderService();

        var detailResult = await service.GetOrderDetailAsync("order-1");
        var fullResult = await service.GetOrderDetailFullAsync("order-1");

        Assert.True(detailResult.Success);
        Assert.Equal("1 Test Street", detailResult.Data!.StoreAddress);
        Assert.Equal("store@example.com", detailResult.Data.StoreContactEmail);
        Assert.True(fullResult.Success);
        Assert.Equal("store@example.com", fullResult.Data!.StoreContactEmail);
    }

    [Fact]
    public async Task UpdateStoreContactAsync_UpdatesStoreAddressAndEmail()
    {
        await SeedStoreOrderGraphAsync();
        var service = CreateStoreOrderService("tester");

        var result = await service.UpdateStoreContactAsync(
            new UpdateStoreOrderStoreContactDto
            {
                OrderGUID = "order-1",
                StoreCode = "S001",
                Address = "99 Updated Road",
                ContactEmail = "updated@example.com",
            }
        );

        var store = await _db.Queryable<Store>().Where(x => x.StoreGUID == "store-1").FirstAsync();

        Assert.True(result.Success);
        Assert.Equal("99 Updated Road", store!.Address);
        Assert.Equal("updated@example.com", store.ContactEmail);
        Assert.Equal("tester", store.UpdatedBy);
        Assert.NotNull(store.UpdatedAt);
    }

    [Fact]
    public async Task UpdateStoreContactAsync_WhenAddressOmitted_PreservesExistingAddress()
    {
        await SeedStoreOrderGraphAsync();
        var service = CreateStoreOrderService("tester");

        var result = await service.UpdateStoreContactAsync(
            new UpdateStoreOrderStoreContactDto
            {
                OrderGUID = "order-1",
                StoreCode = "S001",
                ContactEmail = "email-only@example.com",
            }
        );

        var store = await _db.Queryable<Store>().Where(x => x.StoreGUID == "store-1").FirstAsync();

        Assert.True(result.Success);
        Assert.Equal("1 Test Street", store!.Address);
        Assert.Equal("email-only@example.com", store.ContactEmail);
    }

    [Fact]
    public async Task SendInvoiceEmailAsync_WhenSmtpNotConfigured_ReturnsClearFailure()
    {
        await SeedStoreOrderGraphAsync();
        var service = CreateStoreOrderService(
            invoiceEmailService: new InvoiceEmailService(
                NullLogger<InvoiceEmailService>.Instance,
                Options.Create(new InvoiceEmailOptions())
            )
        );

        var result = await service.SendInvoiceEmailAsync(
            new SendStoreOrderInvoiceEmailDto
            {
                OrderGUID = "order-1",
                ToEmail = "customer@example.com",
                PdfFileName = "invoice.pdf",
                PdfBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }),
            }
        );

        Assert.False(result.Success);
        Assert.Equal("未配置发票邮件 SMTP，请先完成 InvoiceEmail 配置", result.Message);
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

    private StoreService CreateStoreService()
    {
        return new StoreService(CreateSqlSugarContext(_db), NullLogger<StoreService>.Instance);
    }

    private StoreOrderReactService CreateStoreOrderService(
        string? username = null,
        IInvoiceEmailService? invoiceEmailService = null
    )
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = CreateUser(username),
            },
        };

        return new StoreOrderReactService(
            CreateSqlSugarContext(_db),
            NullLogger<StoreOrderReactService>.Instance,
            httpContextAccessor,
            Mock.Of<IOrderNumberGenerator>(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IMapper>(),
            invoiceEmailService ?? Mock.Of<IInvoiceEmailService>()
        );
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        var dbField = typeof(SqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, db);
        return context;
    }

    private async Task SeedStoreOrderGraphAsync()
    {
        await _db.Insertable(
            new Store
            {
                StoreGUID = "store-1",
                StoreCode = "S001",
                StoreName = "Test Store",
                Address = "1 Test Street",
                ContactEmail = "store@example.com",
                Phone = "123456",
                IsActive = true,
            }
        ).ExecuteCommandAsync();

        await _db.Insertable(
            new WareHouseOrder
            {
                OrderGUID = "order-1",
                OrderNo = "SO-001",
                StoreCode = "S001",
                FlowStatus = 1,
                OrderDate = new DateTime(2026, 6, 4),
                OEMTotalAmount = 25m,
                ShippingFee = 2m,
                Remarks = "test",
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        await _db.Insertable(
            new Product
            {
                ProductCode = "P001",
                ItemNumber = "ITEM-001",
                Barcode = "BAR-001",
                ProductName = "Product 1",
                ProductImage = "image.png",
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        await _db.Insertable(
            new WarehouseProduct
            {
                ProductCode = "P001",
                IsActive = true,
                IsDeleted = false,
                MinOrderQuantity = 1,
                ImportPrice = 3m,
            }
        ).ExecuteCommandAsync();

        await _db.Insertable(
            new DomesticProduct
            {
                ProductCode = "P001",
                UnitVolume = 1.5m,
                PackingQuantity = 3,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        await _db.Insertable(
            new WareHouseOrderDetails
            {
                DetailGUID = "detail-1",
                OrderGUID = "order-1",
                StoreCode = "S001",
                ProductCode = "P001",
                Quantity = 5,
                AllocQuantity = 4,
                OEMPrice = 5m,
                OEMAmount = 25m,
                ImportPrice = 3m,
                ImportAmount = 12m,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private static ClaimsPrincipal CreateUser(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        return new ClaimsPrincipal(
            new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                    new Claim(ClaimTypes.Name, username),
                },
                "TestAuth"
            )
        );
    }
}
