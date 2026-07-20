using System.Reflection;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class PdaCartToOrderStoreIsolationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public PdaCartToOrderStoreIsolationTests()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables(
            typeof(Cart), typeof(CartItem), typeof(Store), typeof(WareHouseOrder),
            typeof(WareHouseOrderDetails), typeof(PreorderActivation),
            typeof(PreorderActivationStore), typeof(PreorderWarehouseOrder)
        );
    }

    [Fact]
    public async Task 跨店CartGuid返回403业务错误且不转换普通订单()
    {
        await SeedCartAsync("cart-s2", "store-2", "S02");
        var service = CreateService("store-2", "S02");

        var result = await service.ConvertCartToOrderAsync(
            new CartToOrderRequestDto { CartGUID = "cart-s2" },
            "device-1",
            "S01"
        );

        Assert.False(result.Success);
        Assert.Equal("PDA_CART_STORE_MISMATCH", result.ErrorCode);
        Assert.False(await _db.Queryable<WareHouseOrder>().AnyAsync());
        Assert.False((await _db.Queryable<Cart>().FirstAsync(item => item.CartGUID == "cart-s2")).IsDeleted);
    }

    [Fact]
    public async Task 跨店空购物车仍优先返回403业务错误()
    {
        await SeedCartAsync("empty-cart-s2", "store-2", "S02", addItem: false);
        var service = CreateService("store-2", "S02");

        var result = await service.ConvertCartToOrderAsync(
            new CartToOrderRequestDto { CartGUID = "empty-cart-s2" },
            "device-1",
            "S01"
        );

        Assert.False(result.Success);
        Assert.Equal("PDA_CART_STORE_MISMATCH", result.ErrorCode);
        Assert.False(await _db.Queryable<WareHouseOrder>().AnyAsync());
    }

    [Fact]
    public async Task 同店且无待完成Preorder时正常转换订单()
    {
        await SeedCartAsync("cart-s1", "store-1", "S01");
        var service = CreateService("store-1", "S01");

        var result = await service.ConvertCartToOrderAsync(
            new CartToOrderRequestDto { CartGUID = "cart-s1" },
            "device-1",
            "S01"
        );

        Assert.True(result.Success);
        var order = await _db.Queryable<WareHouseOrder>().SingleAsync();
        Assert.Equal("S01", order.StoreCode);
        Assert.True((await _db.Queryable<Cart>().FirstAsync(item => item.CartGUID == "cart-s1")).IsDeleted);
    }

    [Fact]
    public async Task 同店有待完成Preorder时仍可把Pda购物车转换为普通草稿()
    {
        await SeedCartAsync("cart-preorder", "store-1", "S01");
        await _db.Insertable(new PreorderActivation
        {
            ActivationGuid = "pda-cart-active",
            TemplateGuid = "pda-cart-template",
            PeriodNumber = 1,
            ActivationCode = "PRE-PDA-CART",
            TemplateNameSnapshot = "PDA 扫码门禁",
            SourceTemplateRevision = 1,
            StartAtUtc = DateTime.UtcNow.AddHours(-1),
            EndAtUtc = DateTime.UtcNow.AddHours(1),
            Status = PreorderActivationStatuses.Active,
        }).ExecuteCommandAsync();
        await _db.Insertable(new PreorderActivationStore
        {
            ActivationStoreGuid = "pda-cart-store",
            ActivationGuid = "pda-cart-active",
            StoreGuid = "store-1",
            StoreCode = "S01",
            StoreName = "S01",
        }).ExecuteCommandAsync();
        var service = CreateService("store-1", "S01");

        var result = await service.ConvertCartToOrderAsync(
            new CartToOrderRequestDto { CartGUID = "cart-preorder" },
            "device-1",
            "S01"
        );

        Assert.True(result.Success);
        var order = await _db.Queryable<WareHouseOrder>().SingleAsync();
        // PDA 可继续扫码和生成普通草稿，只有最终提交才受 Preorder 门禁限制。
        Assert.Equal(0, order.FlowStatus);
        Assert.True((await _db.Queryable<Cart>()
            .FirstAsync(item => item.CartGUID == "cart-preorder")).IsDeleted);
    }

    private PDACartToOrderService CreateService(string storeGuid, string storeCode)
    {
        var storeService = new Mock<IStoreService>(MockBehavior.Strict);
        storeService
            .Setup(item => item.GetStoreByGuidAsync(storeGuid))
            .ReturnsAsync(ApiResponse<StoreDetailDto>.OK(new StoreDetailDto
            {
                StoreGUID = storeGuid,
                StoreCode = storeCode,
                StoreName = storeCode,
            }));
        var numberGenerator = new Mock<IOrderNumberGenerator>(MockBehavior.Strict);
        numberGenerator.Setup(item => item.GetNextOrderNoAsync()).ReturnsAsync("ORD-PDA-1");
        return new PDACartToOrderService(
            CreateSqlSugarContext(_db),
            NullLogger<PDACartToOrderService>.Instance,
            storeService.Object,
            numberGenerator.Object
        );
    }

    private async Task SeedCartAsync(
        string cartGuid,
        string storeGuid,
        string storeCode,
        bool addItem = true
    )
    {
        await _db.Insertable(new Store
        {
            StoreGUID = storeGuid,
            StoreCode = storeCode,
            StoreName = storeCode,
            IsActive = true,
        }).ExecuteCommandAsync();
        await _db.Insertable(new Cart
        {
            CartGUID = cartGuid,
            UserGUID = "user-1",
            StoreGUID = storeGuid,
        }).ExecuteCommandAsync();
        if (!addItem)
        {
            return;
        }
        await _db.Insertable(new CartItem
        {
            CartItemGUID = $"{cartGuid}-item",
            CartGUID = cartGuid,
            ProductCode = "P1",
            ItemNumber = "I1",
            ProductName = "商品",
            Quantity = 1,
            UnitPrice = 2m,
        }).ExecuteCommandAsync();
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }
}
