using System.Security.Claims;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ReactStoreManagerProductsControllerTests
{
    [Fact]
    public async Task UpdateStorePrice_业务锁冲突时返回409()
    {
        var service = new Mock<IStoreManagerProductReactService>();
        service.Setup(x => x.UpdateStorePriceAsync(
                "price-uuid",
                It.IsAny<StoreManagerUpdatePriceDto>(),
                "manager"
            ))
            .ReturnsAsync(ApiResponse<StoreManagerStorePriceDto>.Error(
                "套装子项成本正在被其他操作更新，请稍后重试",
                "SET_CHILD_PURCHASE_PRICE_BUSY"
            ));
        var controller = CreateController(service.Object);

        var response = await controller.UpdateStorePrice(
            "price-uuid",
            new StoreManagerUpdatePriceDto()
        );

        var conflict = Assert.IsType<ConflictObjectResult>(response);
        var body = Assert.IsType<ApiResponse<StoreManagerStorePriceDto>>(conflict.Value);
        Assert.Equal("SET_CHILD_PURCHASE_PRICE_BUSY", body.ErrorCode);
    }

    [Fact]
    public async Task BatchUpdateMultiCodePrices_整批锁冲突时返回409并保留结果()
    {
        var result = new BatchOperationReactResult
        {
            FailedCount = 1,
            Errors = new List<string> { "uuid-1: 套装子项成本正在被其他操作更新，请稍后重试" },
        };
        var service = new Mock<IStoreManagerProductReactService>();
        service.Setup(x => x.BatchUpdateMultiCodePricesAsync(
                It.IsAny<List<StoreManagerUpdateMultiCodePriceDto>>(),
                "manager"
            ))
            .ReturnsAsync(new ApiResponse<BatchOperationReactResult>
            {
                Success = false,
                ErrorCode = "SET_CHILD_PURCHASE_PRICE_BUSY",
                Message = "套装子项成本正在被其他操作更新，请稍后重试",
                Data = result,
            });
        var controller = CreateController(service.Object);

        var response = await controller.BatchUpdateMultiCodePrices(
            new List<StoreManagerUpdateMultiCodePriceDto> { new() { UUID = "uuid-1" } }
        );

        var conflict = Assert.IsType<ConflictObjectResult>(response);
        var body = Assert.IsType<ApiResponse<BatchOperationReactResult>>(conflict.Value);
        Assert.Same(result, body.Data);
    }

    private static ReactStoreManagerProductsController CreateController(
        IStoreManagerProductReactService service
    ) => new(service, NullLogger<ReactStoreManagerProductsController>.Instance)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Name, "manager") },
                    "Test"
                )),
            },
        },
    };
}
