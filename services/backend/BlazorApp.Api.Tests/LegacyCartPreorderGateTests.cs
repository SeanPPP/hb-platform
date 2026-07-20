using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

[Collection("PreorderMutationLock")]
public sealed class LegacyCartPreorderGateTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public LegacyCartPreorderGateTests()
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
            typeof(Store), typeof(UserStore), typeof(Cart), typeof(CartItem),
            typeof(PreorderActivation), typeof(PreorderActivationStore),
            typeof(PreorderWarehouseOrder)
        );
    }

    [Fact]
    public async Task 旧购物车提交遇到未完成Preorder时拒绝且不更新状态()
    {
        await SeedStoreCartAndPendingPreorderAsync();
        var service = CreateService("user-1", permissions: Permissions.Orders.Create);

        var error = await Assert.ThrowsAsync<PreorderBusinessException>(() =>
            service.SubmitCartAsync("user-1", new SubmitCartRequest { StoreGUID = "store-1" })
        );

        Assert.Equal("PREORDER_REQUIRED", error.ErrorCode);
        var cart = await _db.Queryable<Cart>().FirstAsync(item => item.CartGUID == "cart-1");
        Assert.Equal("Active", cart.CartStatus);
        Assert.Null(cart.OrderNumber);
    }

    [Fact]
    public async Task 旧购物车提交拒绝未分配分店且不更新状态()
    {
        await SeedStoreAndCartAsync("user-1", assignStore: false);
        var service = CreateService("user-1", permissions: Permissions.Orders.Create);

        var error = await Assert.ThrowsAsync<PreorderBusinessException>(() =>
            service.SubmitCartAsync("user-1", new SubmitCartRequest { StoreGUID = "store-1" })
        );

        Assert.Equal(StatusCodes.Status403Forbidden, error.StatusCode);
        Assert.Equal("STORE_ACCESS_DENIED", error.ErrorCode);
        Assert.Equal("Active", (await _db.Queryable<Cart>().FirstAsync()).CartStatus);
    }

    [Fact]
    public async Task WarehouseStaff可绕过Preorder和分店分配并提交旧购物车()
    {
        await SeedStoreAndCartAsync("warehouse-user", assignStore: false);
        await SeedPendingPreorderAsync();
        var service = CreateService(
            "warehouse-user",
            "WarehouseStaff",
            Permissions.Orders.Create
        );

        var orderNumber = await service.SubmitCartAsync(
            "warehouse-user",
            new SubmitCartRequest { StoreGUID = "store-1" }
        );

        Assert.Equal("2026-1000", orderNumber);
        var cart = await _db.Queryable<Cart>().FirstAsync();
        Assert.Equal("Submitted", cart.CartStatus);
        Assert.Equal("store-1", cart.StoreGUID);
    }

    [Fact]
    public async Task 已完成对应Preorder后允许普通用户提交旧购物车()
    {
        await SeedStoreCartAndPendingPreorderAsync();
        await _db.Insertable(new PreorderWarehouseOrder
        {
            OrderGuid = "preorder-order-1", ActivationGuid = "activation-1",
            StoreGuid = "store-1", StoreCode = "S01", StoreName = "一店",
            OrderNo = "PRE-1-S01", Status = PreorderWarehouseOrderStatuses.Submitted,
        }).ExecuteCommandAsync();

        var orderNumber = await CreateService(
            "user-1",
            permissions: Permissions.Orders.Create
        ).SubmitCartAsync(
            "user-1",
            new SubmitCartRequest { StoreGUID = "store-1" }
        );

        Assert.Equal("2026-1000", orderNumber);
        Assert.Equal("Submitted", (await _db.Queryable<Cart>().FirstAsync()).CartStatus);
    }

    [Fact]
    public async Task 等待StoreGate期间新批次生效后旧提交会重新检查并拒绝()
    {
        await SeedStoreAndCartAsync("user-1", assignStore: true);
        IAsyncDisposable? heldLock = await PreorderMutationLock.AcquireProcessAsync(
            "PreorderStoreGate:store-1"
        );
        try
        {
            var submitTask = CreateService(
                "user-1",
                permissions: Permissions.Orders.Create
            ).SubmitCartAsync(
                "user-1",
                new SubmitCartRequest { StoreGUID = "store-1" }
            );
            await Task.Delay(50);
            Assert.False(submitTask.IsCompleted);

            await SeedPendingPreorderAsync();
            await heldLock.DisposeAsync();
            heldLock = null;

            var error = await Assert.ThrowsAsync<PreorderBusinessException>(() => submitTask);
            Assert.Equal("PREORDER_REQUIRED", error.ErrorCode);
            Assert.Equal("Active", (await _db.Queryable<Cart>().FirstAsync()).CartStatus);
        }
        finally
        {
            if (heldLock != null)
            {
                await heldLock.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task 旧提交控制器透传Preorder稳定错误码和Http状态()
    {
        var cartService = new Mock<ICartService>();
        cartService
            .Setup(item => item.SubmitCartAsync("user-1", It.IsAny<SubmitCartRequest>()))
            .ThrowsAsync(new PreorderBusinessException(
                "请先完成当前有效的 Preorder，再提交普通订货",
                "PREORDER_REQUIRED",
                StatusCodes.Status409Conflict
            ));
        var controller = new CartController(
            cartService.Object,
            NullLogger<CartController>.Instance
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext("user-1", role: null),
            },
        };

        var result = Assert.IsType<ObjectResult>(await controller.SubmitCart(
            new SubmitCartRequest { StoreGUID = "store-1" }
        ));

        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        var errorCode = result.Value?.GetType().GetProperty("errorCode")?.GetValue(result.Value);
        Assert.Equal("PREORDER_REQUIRED", errorCode);
    }

    [Fact]
    public async Task WarehouseStaff缺少OrdersCreate时不能提交或仅凭角色绕过()
    {
        await SeedStoreAndCartAsync("warehouse-user", assignStore: false);
        await SeedPendingPreorderAsync();

        var error = await Assert.ThrowsAsync<PreorderBusinessException>(() =>
            CreateService("warehouse-user", "WarehouseStaff").SubmitCartAsync(
                "warehouse-user",
                new SubmitCartRequest { StoreGUID = "store-1" }
            )
        );

        Assert.Equal(StatusCodes.Status403Forbidden, error.StatusCode);
        Assert.Equal("CART_SUBMIT_FORBIDDEN", error.ErrorCode);
        Assert.Equal("Active", (await _db.Queryable<Cart>().FirstAsync()).CartStatus);
    }

    [Fact]
    public async Task 普通用户即使已分配分店也必须具备正式购物车动作权限()
    {
        await SeedStoreAndCartAsync("user-1", assignStore: true);

        var error = await Assert.ThrowsAsync<PreorderBusinessException>(() =>
            CreateService("user-1").SubmitCartAsync(
                "user-1",
                new SubmitCartRequest { StoreGUID = "store-1" }
            )
        );

        Assert.Equal(StatusCodes.Status403Forbidden, error.StatusCode);
        Assert.Equal("CART_SUBMIT_FORBIDDEN", error.ErrorCode);
    }

    [Fact]
    public async Task WarehouseManager仅有动作权限但无全局仓库权限时仍受分店和Preorder门禁()
    {
        await SeedStoreCartAndPendingPreorderAsync();

        var error = await Assert.ThrowsAsync<PreorderBusinessException>(() =>
            CreateService(
                "user-1",
                "WarehouseManager",
                Permissions.Orders.Create
            ).SubmitCartAsync(
                "user-1",
                new SubmitCartRequest { StoreGUID = "store-1" }
            )
        );

        Assert.Equal("PREORDER_REQUIRED", error.ErrorCode);
    }

    [Fact]
    public async Task 全局仓库订货权限允许跨分店并绕过Preorder门禁()
    {
        await SeedStoreAndCartAsync("global-user", assignStore: false);
        await SeedPendingPreorderAsync();

        var orderNumber = await CreateService(
            "global-user",
            permissions: Permissions.Warehouse.ManageOrders
        ).SubmitCartAsync(
            "global-user",
            new SubmitCartRequest { StoreGUID = "store-1" }
        );

        Assert.Equal("2026-1000", orderNumber);
        Assert.Equal("Submitted", (await _db.Queryable<Cart>().FirstAsync()).CartStatus);
    }

    [Fact]
    public async Task Admin通过动作权限后可跨分店并绕过Preorder门禁()
    {
        await SeedStoreAndCartAsync("admin-user", assignStore: false);
        await SeedPendingPreorderAsync();

        var orderNumber = await CreateService(
            "admin-user",
            "Admin",
            Permissions.Orders.Create
        ).SubmitCartAsync(
            "admin-user",
            new SubmitCartRequest { StoreGUID = "store-1" }
        );

        Assert.Equal("2026-1000", orderNumber);
        Assert.Equal("Submitted", (await _db.Queryable<Cart>().FirstAsync()).CartStatus);
    }

    [Fact]
    public async Task StoreGuid大小写变体解析到数据库CanonicalGuid并共用StoreGate()
    {
        await SeedStoreAndCartAsync(
            "user-1",
            assignStore: true,
            canonicalStoreGuid: "Store-AbC"
        );

        var orderNumber = await CreateService(
            "user-1",
            permissions: Permissions.Orders.Create
        ).SubmitCartAsync(
            "user-1",
            new SubmitCartRequest { StoreGUID = "store-abc" }
        );

        Assert.Equal("2026-1000", orderNumber);
        Assert.Equal("Store-AbC", (await _db.Queryable<Cart>().FirstAsync()).StoreGUID);
    }

    private CartService CreateService(
        string userGuid,
        string? role = null,
        params string[] permissions
    )
    {
        var orderNumberGenerator = new Mock<IOrderNumberGenerator>();
        orderNumberGenerator.Setup(item => item.GetNextOrderNoAsync()).ReturnsAsync("2026-1000");
        var authorization = new Mock<IAuthorizationService>();
        authorization
            .Setup(item => item.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(),
                It.IsAny<string>()
            ))
            .ReturnsAsync((ClaimsPrincipal _, object? _, string policy) =>
                permissions.Contains(policy, StringComparer.OrdinalIgnoreCase)
                    ? AuthorizationResult.Success()
                    : AuthorizationResult.Failed()
            );
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = CreateHttpContext(userGuid, role),
        };
        return new CartService(
            CreateSqlSugarContext(_db),
            Mock.Of<IMapper>(),
            NullLogger<CartService>.Instance,
            Mock.Of<IWarehouseProductService>(),
            orderNumberGenerator.Object,
            authorization.Object,
            httpContextAccessor
        );
    }

    private async Task SeedStoreCartAndPendingPreorderAsync()
    {
        await SeedStoreAndCartAsync("user-1", assignStore: true);
        await SeedPendingPreorderAsync();
    }

    private async Task SeedStoreAndCartAsync(
        string userGuid,
        bool assignStore,
        string canonicalStoreGuid = "store-1"
    )
    {
        await _db.Insertable(new Store
        {
            StoreGUID = canonicalStoreGuid, StoreCode = "S01", StoreName = "一店", IsActive = true,
        }).ExecuteCommandAsync();
        if (assignStore)
        {
            await _db.Insertable(new UserStore
            {
                UserStoreGUID = "user-store-1", UserGUID = userGuid, StoreGUID = canonicalStoreGuid,
            }).ExecuteCommandAsync();
        }
        await _db.Insertable(new Cart
        {
            CartGUID = "cart-1", UserGUID = userGuid, CartStatus = "Active",
        }).ExecuteCommandAsync();
        await _db.Insertable(new CartItem
        {
            CartItemGUID = "cart-item-1", CartGUID = "cart-1", ProductCode = "P1", Quantity = 1,
        }).ExecuteCommandAsync();
    }

    private async Task SeedPendingPreorderAsync()
    {
        var now = DateTime.UtcNow;
        await _db.Insertable(new PreorderActivation
        {
            ActivationGuid = "activation-1", TemplateGuid = "template-1", PeriodNumber = 1,
            ActivationCode = "PRE-1", TemplateNameSnapshot = "测试预订", SourceTemplateRevision = 1,
            StartAtUtc = now.AddMinutes(-5), EndAtUtc = now.AddHours(1),
            Status = PreorderActivationStatuses.Active,
        }).ExecuteCommandAsync();
        await _db.Insertable(new PreorderActivationStore
        {
            ActivationStoreGuid = "activation-store-1", ActivationGuid = "activation-1",
            StoreGuid = "store-1", StoreCode = "S01", StoreName = "一店",
        }).ExecuteCommandAsync();
    }

    private static DefaultHttpContext CreateHttpContext(string userGuid, string? role)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userGuid) };
        if (!string.IsNullOrWhiteSpace(role))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
        };
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
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
