using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class DataSyncReactControllerProductSyncContractTests
{
    [Fact]
    public async Task SyncProducts_向商品HQ服务传入当前请求用户身份()
    {
        var hqSyncService = new Mock<IProductHqSyncService>(MockBehavior.Strict);
        hqSyncService
            .Setup(service => service.SyncFullAsync("user-guid-001", "同步操作员"))
            .ReturnsAsync(
                ApiResponse<HqProductSyncResult>.OK(
                    new HqProductSyncResult { ProductsAdded = 1 },
                    "商品同步成功"
                )
            );

        var controller = CreateController(hqSyncService.Object);

        var response = await controller.SyncProducts();

        Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(response);
        hqSyncService.Verify(
            service => service.SyncFullAsync("user-guid-001", "同步操作员"),
            Times.Once
        );
    }

    [Fact]
    public async Task SyncProductsIncremental_显式传入开始时间和当前请求用户身份()
    {
        var startDate = new DateTime(2026, 8, 12, 1, 2, 3, DateTimeKind.Utc);
        var hqSyncService = new Mock<IProductHqSyncService>(MockBehavior.Strict);
        hqSyncService
            .Setup(service => service.SyncIncrementalAsync(
                startDate,
                "user-guid-001",
                "同步操作员"
            ))
            .ReturnsAsync(
                ApiResponse<HqProductSyncResult>.OK(
                    new HqProductSyncResult { ProductsUpdated = 1 },
                    "商品增量同步成功"
                )
            );

        var controller = CreateController(hqSyncService.Object);

        var response = await controller.SyncProductsIncremental(
            new DataSyncReactController.IncrementalSyncRequest { StartDate = startDate }
        );

        Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(response);
        hqSyncService.Verify(
            service => service.SyncIncrementalAsync(
                startDate,
                "user-guid-001",
                "同步操作员"
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task SyncProducts_套装成本忙且无成功数量时返回409与统一错误码()
    {
        var hqSyncService = new Mock<IProductHqSyncService>(MockBehavior.Strict);
        hqSyncService
            .Setup(service => service.SyncFullAsync("user-guid-001", "同步操作员"))
            .ReturnsAsync(
                ApiResponse<HqProductSyncResult>.Error(
                    "套装子项成本正在被其他操作更新，请稍后重试",
                    "SET_CHILD_PURCHASE_PRICE_BUSY"
                )
            );

        var response = await CreateController(hqSyncService.Object).SyncProducts();

        var conflict = Assert.IsType<Microsoft.AspNetCore.Mvc.ConflictObjectResult>(response);
        var body = Assert.IsType<ApiResponse<SyncResult>>(conflict.Value);
        Assert.False(body.Success);
        Assert.Equal("SET_CHILD_PURCHASE_PRICE_BUSY", body.ErrorCode);
        Assert.NotNull(body.Data);
        Assert.Equal(1, body.Data!.BusyErrorCount);
    }

    [Fact]
    public async Task SyncProducts_普通失败保持200但不再包装为成功响应()
    {
        var hqSyncService = new Mock<IProductHqSyncService>(MockBehavior.Strict);
        hqSyncService
            .Setup(service => service.SyncFullAsync("user-guid-001", "同步操作员"))
            .ReturnsAsync(
                ApiResponse<HqProductSyncResult>.Error("HQ不可用", "PRODUCT_HQ_FULL_SYNC_ERROR")
            );

        var response = await CreateController(hqSyncService.Object).SyncProducts();

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(response);
        var body = Assert.IsType<ApiResponse<SyncResult>>(ok.Value);
        Assert.False(body.Success);
        Assert.Equal("PRODUCT_HQ_FULL_SYNC_ERROR", body.ErrorCode);
        Assert.NotNull(body.Data);
        Assert.Equal(1, body.Data!.ErrorCount);
    }

    [Fact]
    public async Task SyncProductsIncremental_套装成本忙且无成功数量时返回409()
    {
        var hqSyncService = new Mock<IProductHqSyncService>(MockBehavior.Strict);
        hqSyncService
            .Setup(service => service.SyncIncrementalAsync(null, "user-guid-001", "同步操作员"))
            .ReturnsAsync(
                ApiResponse<HqProductSyncResult>.Error(
                    "套装子项成本正在被其他操作更新，请稍后重试",
                    "SET_CHILD_PURCHASE_PRICE_BUSY"
                )
            );

        var response = await CreateController(hqSyncService.Object).SyncProductsIncremental();

        var conflict = Assert.IsType<Microsoft.AspNetCore.Mvc.ConflictObjectResult>(response);
        var body = Assert.IsType<ApiResponse<SyncResult>>(conflict.Value);
        Assert.False(body.Success);
        Assert.Equal("SET_CHILD_PURCHASE_PRICE_BUSY", body.ErrorCode);
    }

    private static DataSyncReactController CreateController(IProductHqSyncService hqSyncService)
    {
        var currentUserService = new Mock<ICurrentUserService>(MockBehavior.Strict);
        currentUserService.Setup(service => service.GetCurrentUserGuid()).Returns("user-guid-001");
        currentUserService.Setup(service => service.GetCurrentUsername()).Returns("同步操作员");

        return new DataSyncReactController(
            Mock.Of<IDataSyncFullService>(),
            Mock.Of<IDataSyncIncrementalService>(),
            hqSyncService,
            Mock.Of<ILogger<DataSyncReactController>>(),
            currentUserService.Object
        );
    }
}
