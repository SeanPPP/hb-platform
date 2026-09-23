using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class StaticReportAccessControllerTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public StaticReportAccessControllerTests()
    {
        _connection = new SqliteConnection($"Data Source={_path}");
        _connection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables(typeof(User), typeof(UserStore), typeof(Store));
    }

    [Theory]
    [InlineData("/reports/kfc-uncle-bills/", "/reports/kfc-uncle-bills/")]
    [InlineData("/reports/kfc-uncle-bills/img/../data/all.json", "/reports/kfc-uncle-bills/data/all.json")]
    [InlineData("/reports/kfc-uncle-bills//data/./store-1013.json?x=1", "/reports/kfc-uncle-bills/data/store-1013.json")]
    [InlineData("/reports/kfc-uncle-bills/data/all%2Ejson", "/reports/kfc-uncle-bills/data/all.json")]
    [InlineData("/reports/kfc-uncle-bills/%2e%2e/kfc-uncle-bills/data/all.json", "/reports/kfc-uncle-bills/data/all.json")]
    public void 原始地址按nginx同样的方式规整(string raw, string expected)
    {
        Assert.Equal(expected, StaticReportAccessController.NormalizeOriginalPath(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("reports/kfc-uncle-bills/")]
    [InlineData("/../etc/passwd")]
    [InlineData("/reports/kfc%0d%0aX-Evil: 1")]
    [InlineData("/reports/kfc-uncle-bills/data\\all.json")]
    public void 非法地址规整为空(string? raw)
    {
        Assert.Null(StaticReportAccessController.NormalizeOriginalPath(raw));
    }

    [Theory]
    [InlineData("/reports/kfc-uncle-bills/")]
    [InlineData("/reports/kfc-uncle-bills/index.html")]
    [InlineData("/reports/kfc-uncle-bills/img/lg-0.js")]
    [InlineData("/reports/kfc-uncle-bills/data-images/images.json")]
    public async Task 页面外壳与公共文件有查看权限即放行且不查门店(string uri)
    {
        // 严格模式的 mock 没有任何设置：一旦去查门店范围就会抛错，证明公共文件不走门店判定
        var roles = new Mock<IRoleService>(MockBehavior.Strict);
        var controller = LoggedIn(roles.Object, "u1", uri);

        var result = await controller.KfcUncleBills();

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/reports/other-report/")]
    [InlineData("/api/react/v1/whatever")]
    public async Task 缺少或指向别处的原始地址一律拒绝(string? uri)
    {
        var roles = new Mock<IRoleService>(MockBehavior.Strict);

        var result = await LoggedIn(roles.Object, "u1", uri).KfcUncleBills();

        AssertForbidden(result);
    }

    [Theory]
    [InlineData("/reports/kfc-uncle-bills/data/all.json")]
    [InlineData("/reports/kfc-uncle-bills/data/store-1005.json")]
    [InlineData("/reports/kfc-uncle-bills/data/store-1013.json")]
    public async Task 有全部门店权限可以读任何数据文件(string uri)
    {
        var roles = RolesWithAllStores(true);

        var result = await LoggedIn(roles.Object, "u1", uri).KfcUncleBills();

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task 没有全部门店权限不能读全链汇总()
    {
        await LinkAsync("u1", "1013");
        var roles = RolesWithAllStores(false);

        var result = await LoggedIn(roles.Object, "u1", "/reports/kfc-uncle-bills/data/all.json").KfcUncleBills();

        AssertForbidden(result);
    }

    [Fact]
    public async Task 只能读自己关联门店的数据文件()
    {
        await LinkAsync("u1", "1013");
        var roles = RolesWithAllStores(false);

        Assert.IsType<NoContentResult>(await LoggedIn(roles.Object, "u1", "/reports/kfc-uncle-bills/data/store-1013.json").KfcUncleBills());
        AssertForbidden(await LoggedIn(roles.Object, "u1", "/reports/kfc-uncle-bills/data/store-1005.json").KfcUncleBills());
    }

    [Theory]
    [InlineData("/reports/kfc-uncle-bills/img/../data/all.json")]
    [InlineData("/reports/kfc-uncle-bills/data/all%2Ejson")]
    [InlineData("/reports/kfc-uncle-bills/img/..%2Fdata/store-1005.json")]
    public async Task 用路径跳转或编码伪装成公共文件也绕不过门店判定(string uri)
    {
        await LinkAsync("u1", "1013");
        var roles = RolesWithAllStores(false);

        var result = await LoggedIn(roles.Object, "u1", uri).KfcUncleBills();

        AssertForbidden(result);
    }

    [Fact]
    public async Task data目录下未知文件名一律拒绝()
    {
        var roles = RolesWithAllStores(true);

        var result = await LoggedIn(roles.Object, "u1", "/reports/kfc-uncle-bills/data/secret.json").KfcUncleBills();

        AssertForbidden(result);
    }

    [Fact]
    public async Task 已删除或停用的门店关联不算数()
    {
        await LinkAsync("u1", "2001", storeDeleted: true);
        await LinkAsync("u1", "2002", storeActive: false);
        await LinkAsync("u1", "2003", linkDeleted: true);
        var roles = RolesWithAllStores(false);

        foreach (var code in new[] { "2001", "2002", "2003" })
        {
            AssertForbidden(await LoggedIn(roles.Object, "u1", $"/reports/kfc-uncle-bills/data/store-{code}.json").KfcUncleBills());
        }
    }

    [Fact]
    public async Task 角色服务异常时按拒绝处理而不是500()
    {
        var roles = new Mock<IRoleService>(MockBehavior.Strict);
        roles.Setup(service => service.UserHasPermissionAsync("u1", Permissions.SalesDashboard.KfcRestockSignalAllStores))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await LoggedIn(roles.Object, "u1", "/reports/kfc-uncle-bills/data/all.json").KfcUncleBills();

        AssertForbidden(result);
    }

    [Fact]
    public async Task 缺少用户标识时拒绝读数据文件()
    {
        var roles = new Mock<IRoleService>(MockBehavior.Strict);
        var controller = CreateController(roles.Object);
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(Array.Empty<Claim>(), "test"));
        controller.ControllerContext.HttpContext.Request.Headers["X-Original-URI"] = "/reports/kfc-uncle-bills/data/all.json";

        AssertForbidden(await controller.KfcUncleBills());
    }

    [Fact]
    public async Task 门店范围接口返回全部门店标记()
    {
        var roles = RolesWithAllStores(true);

        var ok = Assert.IsType<OkObjectResult>(await LoggedIn(roles.Object, "u1", null).KfcUncleBillsScope());

        Assert.True(ReadScope(ok).AllStores);
    }

    [Fact]
    public async Task 门店范围接口返回排序后的关联门店()
    {
        await LinkAsync("u1", "1013");
        await LinkAsync("u1", "1005");
        await LinkAsync("u2", "1007");
        var roles = RolesWithAllStores(false);

        var ok = Assert.IsType<OkObjectResult>(await LoggedIn(roles.Object, "u1", null).KfcUncleBillsScope());
        var scope = ReadScope(ok);

        Assert.False(scope.AllStores);
        Assert.Equal(new[] { "1005", "1013" }, scope.StoreCodes);
    }

    [Theory]
    [InlineData(nameof(StaticReportAccessController.KfcUncleBills))]
    [InlineData(nameof(StaticReportAccessController.KfcUncleBillsScope))]
    public void 两个端点都要求查看KFC补货信号权限(string methodName)
    {
        var method = typeof(StaticReportAccessController).GetMethod(methodName)!;
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(Permissions.SalesDashboard.KfcRestockSignalView, authorize!.Policy);
        Assert.Null(typeof(StaticReportAccessController).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void 两个新权限都登记为内置销售看板权限()
    {
        var codes = PermissionSeedData.SalesDashboardPermissions.Select(p => p.Code).ToList();

        Assert.Contains("SalesDashboard.KfcRestockSignal.View", codes);
        Assert.Contains("SalesDashboard.KfcRestockSignal.AllStores", codes);
    }

    private static (bool AllStores, List<string> StoreCodes) ReadScope(OkObjectResult ok)
    {
        var data = ok.Value!.GetType().GetProperty("data")!.GetValue(ok.Value)!;
        var allStores = (bool)data.GetType().GetProperty("allStores")!.GetValue(data)!;
        var storeCodes = (List<string>)data.GetType().GetProperty("storeCodes")!.GetValue(data)!;
        return (allStores, storeCodes);
    }

    private static void AssertForbidden(IActionResult result)
    {
        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
    }

    private static Mock<IRoleService> RolesWithAllStores(bool allowed)
    {
        var roles = new Mock<IRoleService>(MockBehavior.Strict);
        roles.Setup(service => service.UserHasPermissionAsync(It.IsAny<string>(), Permissions.SalesDashboard.KfcRestockSignalAllStores))
            .ReturnsAsync(ApiResponse<bool>.OK(allowed));
        return roles;
    }

    private async Task LinkAsync(string userGuid, string storeCode, bool storeDeleted = false, bool storeActive = true, bool linkDeleted = false)
    {
        var storeGuid = $"store-{storeCode}";
        if (!await _db.Queryable<Store>().AnyAsync(store => store.StoreGUID == storeGuid))
        {
            await _db.Insertable(new Store { StoreGUID = storeGuid, StoreCode = storeCode, StoreName = storeCode, IsActive = storeActive, IsDeleted = storeDeleted }).ExecuteCommandAsync();
        }
        await _db.Insertable(new UserStore { UserStoreGUID = Guid.NewGuid().ToString(), UserGUID = userGuid, StoreGUID = storeGuid, IsDeleted = linkDeleted }).ExecuteCommandAsync();
    }

    private StaticReportAccessController LoggedIn(IRoleService roles, string userGuid, string? originalUri)
    {
        var controller = CreateController(roles);
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userGuid) }, "test"));
        if (originalUri != null)
        {
            controller.ControllerContext.HttpContext.Request.Headers["X-Original-URI"] = originalUri;
        }
        return controller;
    }

    private StaticReportAccessController CreateController(IRoleService roles) =>
        new(roles, Context(_db), NullLogger<StaticReportAccessController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static SqlSugarContext Context(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
        return context;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
