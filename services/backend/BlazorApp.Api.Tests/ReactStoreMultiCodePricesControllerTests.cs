using System.Security.Claims;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ReactStoreMultiCodePricesControllerTests
{
    [Fact]
    public void 批量多码保存_两类套装均按完整父子键识别()
    {
        var sourcePath = Path.Combine(
            Environment.GetEnvironmentVariable("HB_PLATFORM_ROOT")
                ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."),
            "BlazorApp.Api",
            "Services",
            "React",
            "StoreMultiCodePricesReactService.cs"
        );
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("candidateProductCodes.Contains(x.ProductCode)", source);
        Assert.Contains("candidateChildCodes.Contains(x.SetProductCode)", source);
        Assert.Contains("x.SetType == 1 || x.SetType == 2", source);
    }

    [Fact]
    public async Task UpsertForActiveStores_套装成本锁冲突时返回409()
    {
        var service = new Mock<IStoreMultiCodePricesReactService>(MockBehavior.Strict);
        service.Setup(x => x.UpsertForActiveStoresAsync(
                It.IsAny<List<StoreMultiCodePriceUpsertForActiveStoresItemDto>>(),
                "测试用户"
            ))
            .ReturnsAsync(ApiResponse<BatchResultDtoMC>.Error(
                "套装商品正在被其他操作修改，请稍后重试",
                "SET_CHILD_PURCHASE_PRICE_BUSY"
            ));
        var controller = new ReactStoreMultiCodePricesController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.Name, "测试用户") },
                        "Test"
                    )),
                },
            },
        };

        var result = await controller.UpsertForActiveStores(new());

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var body = Assert.IsType<ApiResponse<BatchResultDtoMC>>(conflict.Value);
        Assert.Equal("SET_CHILD_PURCHASE_PRICE_BUSY", body.ErrorCode);
    }
}
