using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public class ReactProductHqSyncContractTests
{
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
            Mock.Of<ILogger<ReactProductController>>()
        );

        var response = await controller.SyncFromHq();

        Assert.IsType<OkObjectResult>(response);
        hqSyncService.Verify(service => service.SyncIncrementalAsync(null), Times.Once);
        legacyProductService.VerifyNoOtherCalls();
    }
}
