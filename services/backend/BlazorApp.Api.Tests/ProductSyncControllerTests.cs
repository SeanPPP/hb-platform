using BlazorApp.Api.Controllers;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ProductSyncControllerTests
{
    [Fact]
    public async Task BatchUpdateWarehouseProducts_整批业务锁冲突时返回409和失败明细()
    {
        var service = new Mock<IProductSyncService>();
        service.Setup(x => x.BatchUpdateWarehouseProductsAsync(It.IsAny<BatchProductUpdateRequest>()))
            .ReturnsAsync(new BatchProductOperationResponse
            {
                Success = false,
                Message = "套装商品正在被其他操作修改，请稍后重试",
                FailedCount = 1,
                Errors = new List<string>
                {
                    "SET_CHILD_PURCHASE_PRICE_BUSY: 套装商品正在被其他操作修改，请稍后重试",
                },
            });
        var controller = new ProductSyncController(
            service.Object,
            NullLogger<ProductSyncController>.Instance
        );

        var response = await controller.BatchUpdateWarehouseProducts(new BatchProductUpdateRequest
        {
            Items = new List<ProductUpdateItem>
            {
                new() { ProductCode = "P-1", ItemNumber = "ITEM-1" },
            },
        });

        var conflict = Assert.IsType<ConflictObjectResult>(response);
        Assert.Equal(
            "SET_CHILD_PURCHASE_PRICE_BUSY",
            ReadProperty<string>(conflict.Value!, "errorCode")
        );
        var failures = ReadProperty<List<BatchOperationFailureDto>>(
            conflict.Value!,
            "failureDetails"
        );
        Assert.Single(failures);
        Assert.Equal("P-1", failures[0].ItemKey);
        Assert.Equal("SET_CHILD_PURCHASE_PRICE_BUSY", failures[0].ErrorCode);
    }

    private static T ReadProperty<T>(object value, string propertyName)
    {
        return Assert.IsType<T>(value.GetType().GetProperty(propertyName)!.GetValue(value));
    }
}
