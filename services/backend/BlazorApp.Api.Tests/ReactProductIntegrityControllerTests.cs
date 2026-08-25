using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using System.Security.Claims;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ReactProductIntegrityControllerTests
{
    [Fact]
    public async Task FixIntegrity_全部目标组锁冲突时返回409并保留结果明细()
    {
        var details = new ProductIntegrityFixResultDto
        {
            Reports = new List<TableFixReport>
            {
                new()
                {
                    TableName = "StoreMultiCodeProduct",
                    ErrorCode = "SET_CHILD_PURCHASE_PRICE_BUSY",
                    FailureDetails = new List<BatchOperationFailureDto>
                    {
                        new()
                        {
                            ItemKey = "S001|P001",
                            Message = "套装商品正在被其他操作修改，请稍后重试",
                            ErrorCode = "SET_CHILD_PURCHASE_PRICE_BUSY",
                        },
                    },
                },
            },
        };
        var service = new Mock<IProductIntegrityService>();
        service.Setup(x => x.FixIntegrityAsync(It.IsAny<ProductIntegrityFixRequestDto>()))
            .ReturnsAsync(ApiResponse<ProductIntegrityFixResultDto>.Error(
                "套装商品正在被其他操作修改，请稍后重试",
                "SET_CHILD_PURCHASE_PRICE_BUSY",
                details
            ));
        var controller = new ReactProductIntegrityController(
            service.Object,
            NullLogger<ReactProductIntegrityController>.Instance
        );

        var result = await controller.FixIntegrity(new ProductIntegrityFixRequestDto());

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var body = Assert.IsType<ApiResponse<ProductIntegrityFixResultDto>>(conflict.Value);
        Assert.Equal("SET_CHILD_PURCHASE_PRICE_BUSY", body.ErrorCode);
        Assert.Same(details, body.Details);
    }

    [Fact]
    public async Task FixIntegrity_部分组锁冲突时保持200并通过分组明细表达()
    {
        var details = new ProductIntegrityFixResultDto
        {
            Reports = new List<TableFixReport>
            {
                new()
                {
                    TableName = "StoreMultiCodeProduct",
                    FailureDetails = new List<BatchOperationFailureDto>
                    {
                        new()
                        {
                            ItemKey = "S001|P001",
                            Message = "套装商品正在被其他操作修改，请稍后重试",
                            ErrorCode = "SET_CHILD_PURCHASE_PRICE_BUSY",
                        },
                    },
                },
            },
        };
        var service = new Mock<IProductIntegrityService>();
        service.Setup(x => x.FixIntegrityAsync(It.IsAny<ProductIntegrityFixRequestDto>()))
            .ReturnsAsync(ApiResponse<ProductIntegrityFixResultDto>.OK(details, "部分完成"));
        var controller = new ReactProductIntegrityController(
            service.Object,
            NullLogger<ReactProductIntegrityController>.Instance
        );

        var result = await controller.FixIntegrity(new ProductIntegrityFixRequestDto());

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ApiResponse<ProductIntegrityFixResultDto>>(ok.Value);
        Assert.Equal("SET_CHILD_PURCHASE_PRICE_BUSY", body.Data!.Reports[0].FailureDetails[0].ErrorCode);
    }

    [Theory]
    [InlineData(nameof(ReactProductIntegrityController.PreviewSetChildPurchasePrices))]
    [InlineData(nameof(ReactProductIntegrityController.WritebackSetChildPurchasePrices))]
    public void 套装成本接口允许空请求体(string methodName)
    {
        var parameter = typeof(ReactProductIntegrityController)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)!
            .GetParameters()
            .Single();
        var attribute = parameter.GetCustomAttribute<FromBodyAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(
            Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow,
            attribute!.EmptyBodyBehavior
        );
    }

    [Fact]
    public async Task PreviewSetChildPurchasePrices_业务锁冲突时返回409()
    {
        var service = new Mock<IProductIntegrityService>();
        service.Setup(x => x.PreviewSetChildPurchasePricesAsync(
                It.IsAny<SetChildPurchasePriceWritebackRequestDto>()
            ))
            .ReturnsAsync(ApiResponse<SetChildPurchasePriceWritebackResultDto>.Error(
                "套装子项成本正在被其他操作更新，请稍后重试",
                "SET_CHILD_PURCHASE_PRICE_BUSY"
            ));
        var controller = new ReactProductIntegrityController(
            service.Object,
            NullLogger<ReactProductIntegrityController>.Instance
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.Name, "测试管理员") },
                        "Test"
                    )),
                },
            },
        };

        var result = await controller.PreviewSetChildPurchasePrices(null);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task WritebackSetChildPurchasePrices_业务锁冲突时返回409()
    {
        var service = new Mock<IProductIntegrityService>();
        service.Setup(x => x.WritebackSetChildPurchasePricesAsync(
                It.IsAny<SetChildPurchasePriceWritebackRequestDto>(),
                It.IsAny<string>()
            ))
            .ReturnsAsync(ApiResponse<SetChildPurchasePriceWritebackResultDto>.Error(
                "套装子项成本正在被其他操作更新，请稍后重试",
                "SET_CHILD_PURCHASE_PRICE_BUSY"
            ));
        var controller = new ReactProductIntegrityController(
            service.Object,
            NullLogger<ReactProductIntegrityController>.Instance
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.Name, "测试管理员") },
                        "Test"
                    )),
                },
            },
        };

        var result = await controller.WritebackSetChildPurchasePrices(null);

        Assert.IsType<ConflictObjectResult>(result);
    }
}
