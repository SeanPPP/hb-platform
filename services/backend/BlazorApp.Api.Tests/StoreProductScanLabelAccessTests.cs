using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using AutoMapper;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class StoreProductScanLabelAccessTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"scan-scope-{Guid.NewGuid():N}.db");
    private readonly SqlSugarClient database;
    private readonly ReactStoreProductMaintenanceController controller;
    private List<string>? resolvedScope;
    private int queryCount;

    public StoreProductScanLabelAccessTests()
    {
        database = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = $"Data Source={path}", DbType = DbType.Sqlite,
            InitKeyType = InitKeyType.Attribute, IsAutoCloseConnection = false,
        });
        database.CodeFirst.InitTables<Store, UserStore>();
        database.Insertable(new Store { StoreGUID = "store-id", StoreCode = "live-store", StoreName = "Live", IsActive = true }).ExecuteCommand();
        database.Insertable(new UserStore { UserStoreGUID = "relation-id", UserGUID = "user-id", StoreGUID = "store-id" }).ExecuteCommand();
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, database);
        var service = new Mock<IStoreProductMaintenanceReactService>(MockBehavior.Strict);
        service.Setup(value => value.ScanLabelAsync(It.IsAny<StoreProductLookupRequestDto>(), It.IsAny<List<string>?>()))
            .Callback<StoreProductLookupRequestDto, List<string>?>((_, scope) => resolvedScope = scope)
            .ReturnsAsync(ApiResponse<StoreProductScanLabelResultDto>.OK(new()));
        service.Setup(value => value.LookupAsync(It.IsAny<StoreProductLookupRequestDto>(), It.IsAny<List<string>?>()))
            .Callback<StoreProductLookupRequestDto, List<string>?>((_, scope) => resolvedScope = scope)
            .ReturnsAsync(ApiResponse<List<StoreProductLookupItemDto>>.OK(new()));
        controller = new ReactStoreProductMaintenanceController(
            service.Object, Mock.Of<IDeviceRegistrationService>(), Mock.Of<IMapper>(), context,
            NullLogger<ReactStoreProductMaintenanceController>.Instance, Mock.Of<IAuthorizationService>());
        BeginRequest();
        database.Aop.OnLogExecuting = (_, _) => queryCount++;
    }

    private void BeginRequest() => controller.ControllerContext = new ControllerContext
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "user-id") }, "test")),
        },
    };

    [Theory]
    [InlineData(true, "user-id", 0, "validated-store")]
    [InlineData(true, "other-user", 1, "live-store")]
    [InlineData(false, "user-id", 1, "live-store")]
    public async Task ScanLabel_仅复用当前用户成功认证的范围(bool isValid, string userGuid, int expectedQueries, string expectedStore)
    {
        controller.HttpContext.Items[typeof(AuthMobileDeviceValidationResult)] =
            new AuthMobileDeviceValidationResult(isValid, [], ["validated-store"], userGuid);
        Assert.IsType<OkObjectResult>(await controller.ScanLabel(new() { Keyword = "barcode" }));
        Assert.Equal(expectedQueries, queryCount);
        Assert.Equal(expectedStore, Assert.Single(resolvedScope!));
    }

    [Fact]
    public async Task ScanLabel_新请求不复用旧请求权限()
    {
        controller.HttpContext.Items[typeof(AuthMobileDeviceValidationResult)] =
            new AuthMobileDeviceValidationResult(true, [], ["validated-store"], "user-id");
        await controller.ScanLabel(new() { Keyword = "barcode" });
        Assert.Equal(0, queryCount);
        BeginRequest();
        await controller.ScanLabel(new() { Keyword = "barcode" });
        Assert.Equal(1, queryCount);
        Assert.Equal("live-store", Assert.Single(resolvedScope!));
    }

    [Fact]
    public async Task Lookup_其他入口保留原门店查询()
    {
        controller.HttpContext.Items[typeof(AuthMobileDeviceValidationResult)] =
            new AuthMobileDeviceValidationResult(true, [], ["validated-store"], "user-id");
        Assert.IsType<OkObjectResult>(await controller.Lookup(new() { Keyword = "barcode" }));
        Assert.Equal(1, queryCount);
        Assert.Equal("live-store", Assert.Single(resolvedScope!));
    }

    public void Dispose()
    {
        database.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(path);
    }
}
