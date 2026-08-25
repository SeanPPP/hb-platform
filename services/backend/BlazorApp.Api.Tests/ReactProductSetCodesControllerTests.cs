using System.Security.Claims;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ReactProductSetCodesControllerTests
{
    [Fact]
    public async Task BatchCreateWithStoreSync_下游业务锁冲突时返回409并保留错误码()
    {
        var service = new Mock<IProductSetCodeReactService>();
        service.Setup(x => x.BatchCreateWithStoreSyncAsync(
                It.IsAny<List<CreateSetCodeWithStoreSyncDto>>(),
                "admin"
            ))
            .ReturnsAsync(ApiResponse<BatchResultDto>.Error(
                "套装商品正在被其他操作修改，请稍后重试",
                "SET_CHILD_PURCHASE_PRICE_BUSY"
            ));
        var controller = CreateController(service.Object);

        var response = await controller.BatchCreateWithStoreSync(
            new BatchCreateSetCodeWithStoreSyncDto
            {
                Items = new List<CreateSetCodeWithStoreSyncDto> { new() },
            }
        );

        var conflict = Assert.IsType<ConflictObjectResult>(response);
        var body = Assert.IsType<ApiResponse<BatchResultDto>>(conflict.Value);
        Assert.Equal("SET_CHILD_PURCHASE_PRICE_BUSY", body.ErrorCode);
    }

    [Fact]
    public async Task ProductUpdate_业务锁冲突时返回409并保留错误码()
    {
        var productService = new Mock<IProductReactService>();
        productService.Setup(x => x.UpdateAsync("P-1", It.IsAny<UpdateProductDto>()))
            .ReturnsAsync(ApiResponse<ProductDto>.Error(
                "套装商品正在被其他操作修改，请稍后重试",
                "SET_CHILD_PURCHASE_PRICE_BUSY"
            ));
        var controller = CreateProductController(productService.Object);

        var response = await controller.Update("P-1", new UpdateProductDto());

        var conflict = Assert.IsType<ConflictObjectResult>(response);
        var body = Assert.IsType<ApiResponse<ProductDto>>(conflict.Value);
        Assert.Equal("SET_CHILD_PURCHASE_PRICE_BUSY", body.ErrorCode);
    }

    [Fact]
    public async Task StoreMultiCodeBatchUpsert_业务锁冲突时返回409并保留错误码()
    {
        var service = new Mock<IStoreMultiCodePricesReactService>();
        service.Setup(x => x.BatchUpsertAsync(
                It.IsAny<List<StoreMultiCodePriceUpsertItemDto>>(),
                "admin"
            ))
            .ReturnsAsync(ApiResponse<BatchResultDtoMC>.Error(
                "套装商品正在被其他操作修改，请稍后重试",
                "SET_CHILD_PURCHASE_PRICE_BUSY"
            ));
        var controller = new ReactStoreMultiCodePricesController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(),
            },
        };

        var response = await controller.BatchUpsert(new List<StoreMultiCodePriceUpsertItemDto>());

        var conflict = Assert.IsType<ConflictObjectResult>(response);
        var body = Assert.IsType<ApiResponse<BatchResultDtoMC>>(conflict.Value);
        Assert.Equal("SET_CHILD_PURCHASE_PRICE_BUSY", body.ErrorCode);
    }

    [Fact]
    public async Task BatchStatus_业务锁冲突时返回409和可识别错误码()
    {
        var service = new Mock<IProductSetCodeReactService>();
        service.Setup(x => x.BatchUpdateStatusAsync(
                It.IsAny<List<string>>(),
                false,
                "admin",
                It.IsAny<List<string>?>()
            ))
            .ReturnsAsync(ApiResponse<bool>.Error(
                "套装商品正在被其他操作修改，请稍后重试",
                "SET_CHILD_PURCHASE_PRICE_BUSY"
            ));
        var controller = CreateController(service.Object);

        var response = await controller.BatchStatus(new BatchUpdateStatusWithStoreDto
        {
            Ids = new List<string> { "SET-1" },
            IsActive = false,
        });

        var conflict = Assert.IsType<ConflictObjectResult>(response);
        var body = Assert.IsType<ApiResponse<bool>>(conflict.Value);
        Assert.Equal("SET_CHILD_PURCHASE_PRICE_BUSY", body.ErrorCode);
    }

    [Fact]
    public async Task BatchDeleteWithStoreSync_业务锁冲突时返回409并保留批量结果()
    {
        var data = new BatchResultDto { Failed = 1, Errors = new List<string> { "SET-1: busy" } };
        var service = new Mock<IProductSetCodeReactService>();
        service.Setup(x => x.BatchDeleteWithStoreSyncAsync(
                It.IsAny<List<string>>(),
                It.IsAny<List<string>>(),
                "admin"
            ))
            .ReturnsAsync(new ApiResponse<BatchResultDto>
            {
                Success = false,
                ErrorCode = "SET_CHILD_PURCHASE_PRICE_BUSY",
                Message = "套装商品正在被其他操作修改，请稍后重试",
                Data = data,
            });
        var controller = CreateController(service.Object);

        var response = await controller.BatchDeleteWithStoreSync(
            new BatchDeleteSetCodeWithStoreSyncDto
            {
                Ids = new List<string> { "SET-1" },
                StoreCodes = new List<string> { "S01" },
            }
        );

        var conflict = Assert.IsType<ConflictObjectResult>(response);
        var body = Assert.IsType<ApiResponse<BatchResultDto>>(conflict.Value);
        Assert.Same(data, body.Data);
    }

    private static ReactProductSetCodesController CreateController(
        IProductSetCodeReactService service
    ) => new(service, NullLogger<ReactProductSetCodesController>.Instance)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = CreateHttpContext(),
        },
    };

    private static ReactProductController CreateProductController(IProductReactService service) => new(
        service,
        new Mock<IProductStoreSyncService>().Object,
        new Mock<IProductHqSyncService>().Object,
        new Mock<ICurrentUserManageableStoreScopeService>().Object,
        NullLogger<ReactProductController>.Instance,
        new Mock<ICurrentUserService>().Object
    )
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = CreateHttpContext(),
        },
    };

    private static DefaultHttpContext CreateHttpContext() => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, "admin") },
            "Test"
        )),
    };
}
