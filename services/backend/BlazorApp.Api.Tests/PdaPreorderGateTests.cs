using BlazorApp.Api.Controllers;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.POSM;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class PdaPreorderGateTests
{
    [Fact]
    public async Task PdaWarehouseCreate_WhenPreorderPending_AllowsSavingDraft()
    {
        var request = new CreatePDAWarehouseOrderRequestDto { StoreCode = "client-store" };
        var service = new Mock<IPDAWarehouseOrderService>(MockBehavior.Strict);
        service
            .Setup(item => item.CreateOrderAsync(request, "device-1"))
            .ReturnsAsync(new PDAWarehouseOrderResponseDto { Success = true, OrderGUID = "draft-1" });
        var deviceService = CreateDeviceService();
        var controller = CreateWarehouseController(service, deviceService);

        var result = await controller.CreateOrder(request);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("S01", request.StoreCode);
        service.Verify(item => item.CreateOrderAsync(request, "device-1"), Times.Once);
    }

    [Fact]
    public async Task PdaWarehouseSubmit_UsesAtomicServiceGateResultAndMapsConflict()
    {
        var request = new SubmitPDAWarehouseOrderRequestDto { OrderGUID = "draft-1" };
        var service = new Mock<IPDAWarehouseOrderService>(MockBehavior.Strict);
        service
            .Setup(item => item.SubmitOrderAsync(request, "S01", "device-1"))
            .ReturnsAsync(new PDAWarehouseOrderResponseDto
            {
                Success = false,
                ErrorCode = "PREORDER_REQUIRED",
                Message = "请先完成 Preorder",
            });
        var deviceService = CreateDeviceService();
        var controller = CreateWarehouseController(service, deviceService);

        var result = await controller.SubmitOrder(request);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var response = Assert.IsType<ApiResponse<object>>(conflict.Value);
        Assert.Equal("PREORDER_REQUIRED", response.ErrorCode);
        service.Verify(item => item.SubmitOrderAsync(request, "S01", "device-1"), Times.Once);
    }

    [Fact]
    public async Task PdaWarehouseSubmit_CrossStoreServiceResultReturnsForbidden()
    {
        var request = new SubmitPDAWarehouseOrderRequestDto { OrderGUID = "draft-s02" };
        var service = new Mock<IPDAWarehouseOrderService>(MockBehavior.Strict);
        service
            .Setup(item => item.SubmitOrderAsync(request, "S01", "device-1"))
            .ReturnsAsync(new PDAWarehouseOrderResponseDto
            {
                Success = false,
                ErrorCode = "PDA_ORDER_STORE_MISMATCH",
                Message = "订单不属于当前设备绑定分店",
            });
        var controller = CreateWarehouseController(service, CreateDeviceService());

        var result = await controller.SubmitOrder(request);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        var response = Assert.IsType<ApiResponse<object>>(forbidden.Value);
        Assert.Equal("PDA_ORDER_STORE_MISMATCH", response.ErrorCode);
        service.Verify(item => item.SubmitOrderAsync(request, "S01", "device-1"), Times.Once);
    }

    [Fact]
    public async Task PdaCartConvert_AllowsCreatingNormalDraft()
    {
        var request = new CartToOrderRequestDto { CartGUID = "cart-1" };
        var service = new Mock<IPDACartToOrderService>(MockBehavior.Strict);
        service
            .Setup(item => item.ConvertCartToOrderAsync(request, "device-1", "S01"))
            .ReturnsAsync(new CartToOrderResponseDto
            {
                Success = true,
                OrderGUID = "draft-1",
                OrderNo = "ORD-DRAFT-1",
            });
        var deviceService = CreateDeviceService();
        var controller = new PDACartToOrderController(
            service.Object,
            deviceService.Object,
            Mock.Of<ILogger<PDACartToOrderController>>()
        );
        SetDeviceHeaders(controller);

        var result = await controller.ConvertCartToOrder(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.ConvertCartToOrderAsync(request, "device-1", "S01"), Times.Once);
    }

    [Fact]
    public async Task PdaCartConvert_CrossStoreServiceResultReturnsForbidden()
    {
        var request = new CartToOrderRequestDto { CartGUID = "cart-s02" };
        var service = new Mock<IPDACartToOrderService>(MockBehavior.Strict);
        service
            .Setup(item => item.ConvertCartToOrderAsync(request, "device-1", "S01"))
            .ReturnsAsync(new CartToOrderResponseDto
            {
                Success = false,
                ErrorCode = "PDA_CART_STORE_MISMATCH",
                Message = "购物车不属于当前设备绑定分店",
            });
        var controller = new PDACartToOrderController(
            service.Object,
            CreateDeviceService().Object,
            Mock.Of<ILogger<PDACartToOrderController>>()
        );
        SetDeviceHeaders(controller);

        var result = await controller.ConvertCartToOrder(request);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        var response = Assert.IsType<ApiResponse<object>>(forbidden.Value);
        Assert.Equal("PDA_CART_STORE_MISMATCH", response.ErrorCode);
        service.Verify(item => item.ConvertCartToOrderAsync(request, "device-1", "S01"), Times.Once);
    }

    private static PDAWarehouseOrderController CreateWarehouseController(
        Mock<IPDAWarehouseOrderService> service,
        Mock<IDeviceRegistrationService> deviceService
    )
    {
        var controller = new PDAWarehouseOrderController(
            service.Object,
            deviceService.Object,
            Mock.Of<ILogger<PDAWarehouseOrderController>>()
        );
        SetDeviceHeaders(controller);
        return controller;
    }

    private static Mock<IDeviceRegistrationService> CreateDeviceService()
    {
        var service = new Mock<IDeviceRegistrationService>(MockBehavior.Strict);
        service
            .Setup(item => item.ValidateDeviceAuthCodeAsync("device-1", "auth-1"))
            .ReturnsAsync(true);
        service
            .Setup(item => item.GetDeviceByHardwareIdAsync("device-1"))
            .ReturnsAsync(new POSM_设备注册信息表 { 设备硬件识别码 = "device-1", 分店代码 = "S01" });
        return service;
    }

    private static void SetDeviceHeaders(ControllerBase controller)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Device-Id"] = "device-1";
        httpContext.Request.Headers["X-Auth-Code"] = "auth-1";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }
}
