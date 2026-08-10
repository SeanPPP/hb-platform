using System.Reflection;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public class ReactProductHqSyncContractTests
{
    [Fact]
    public void PushProductsToHqRequest_TargetStoreCodes默认null_保持全部分店旧行为()
    {
        var request = new PushProductsToHqRequest();

        Assert.Null(request.TargetStoreCodes);
    }

    [Fact]
    public async Task SyncFromHq_兼容旧路由但委托新商品HQ增量服务()
    {
        var legacyProductService = new Mock<IProductReactService>(MockBehavior.Strict);
        var hqSyncService = new Mock<IProductHqSyncService>(MockBehavior.Strict);
        hqSyncService
            .Setup(service => service.SyncIncrementalAsync(null))
            .ReturnsAsync(
                ApiResponse<HqProductSyncResult>.OK(
                    new HqProductSyncResult { ProductsAdded = 1 },
                    "商品HQ增量同步完成"
                )
            );

        var controller = new ReactProductController(
            legacyProductService.Object,
            Mock.Of<IProductStoreSyncService>(),
            hqSyncService.Object,
            Mock.Of<ICurrentUserManageableStoreScopeService>(),
            Mock.Of<ILogger<ReactProductController>>()
        );

        var response = await controller.SyncFromHq();

        Assert.IsType<OkObjectResult>(response);
        hqSyncService.Verify(service => service.SyncIncrementalAsync(null), Times.Once);
        legacyProductService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncSelectedFromHq_要求Admin权限并委托选中商品HQ同步服务()
    {
        var authorize = typeof(ReactProductController)
            .GetMethod(nameof(ReactProductController.SyncSelectedFromHq))!
            .GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("Admin", authorize?.Roles);

        var legacyProductService = new Mock<IProductReactService>(MockBehavior.Strict);
        var hqSyncService = new Mock<IProductHqSyncService>(MockBehavior.Strict);
        hqSyncService
            .Setup(service => service.SyncSelectedFromHqAsync(
                It.Is<List<string>>(codes => codes.SequenceEqual(new[] { "HB001" }))
            ))
            .ReturnsAsync(
                ApiResponse<HqProductSyncResult>.OK(
                    new HqProductSyncResult { ProductsUpdated = 1 },
                    "选中商品HQ同步完成"
                )
            );

        var controller = new ReactProductController(
            legacyProductService.Object,
            Mock.Of<IProductStoreSyncService>(),
            hqSyncService.Object,
            Mock.Of<ICurrentUserManageableStoreScopeService>(),
            Mock.Of<ILogger<ReactProductController>>()
        );

        var response = await controller.SyncSelectedFromHq(new SyncSelectedProductsFromHqRequest
        {
            ProductCodes = new List<string> { " HB001 ", "HB001" },
        });

        Assert.IsType<OkObjectResult>(response);
        hqSyncService.VerifyAll();
        legacyProductService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetPushToHqStoreOptions_要求PosProductsManage权限并返回统一ApiResponse包装()
    {
        var httpGet = typeof(ReactProductController)
            .GetMethod(nameof(ReactProductController.GetPushToHqStoreOptions))!
            .GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(httpGet);
        Assert.Equal("push-to-hq/store-options", httpGet!.Template);
        var authorize = typeof(ReactProductController)
            .GetMethod(nameof(ReactProductController.GetPushToHqStoreOptions))!
            .GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(Permissions.PosProducts.Manage, authorize?.Policy);

        var hqSyncService = new Mock<IProductHqSyncService>(MockBehavior.Strict);
        hqSyncService
            .Setup(service => service.GetHqStoreOptionsAsync())
            .ReturnsAsync(new List<ProductHqStoreOptionDto>
            {
                new() { StoreCode = "S01", StoreName = "一店" },
            });

        var controller = new ReactProductController(
            Mock.Of<IProductReactService>(),
            Mock.Of<IProductStoreSyncService>(),
            hqSyncService.Object,
            Mock.Of<ICurrentUserManageableStoreScopeService>(),
            Mock.Of<ILogger<ReactProductController>>()
        );

        var response = await controller.GetPushToHqStoreOptions();

        var ok = Assert.IsType<OkObjectResult>(response);
        var payload = Assert.IsType<ApiResponse<List<ProductHqStoreOptionDto>>>(ok.Value);
        Assert.True(payload.Success);
        Assert.Single(payload.Data!);
        Assert.Equal("S01", payload.Data![0].StoreCode);
        Assert.Equal("一店", payload.Data![0].StoreName);
        hqSyncService.VerifyAll();
    }
}
