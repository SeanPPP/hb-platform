using System;
using System.Reflection;
using System.Threading.Tasks;
using AutoMapper;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Models;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public class ReactProductWarehouseControllerTests
    {
        [Fact]
        public void SyncFromHq_仅允许Admin调用()
        {
            var method = typeof(ReactProductWarehouseController).GetMethod(
                nameof(ReactProductWarehouseController.SyncFromHq)
            );

            var authorizeAttribute = Assert.Single(
                method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            );

            Assert.Equal("Admin", ((AuthorizeAttribute)authorizeAttribute).Roles);
        }

        [Fact]
        public async Task SyncFromHq_成功时_返回统一响应()
        {
            var expected = new SyncResult
            {
                IsSuccess = true,
                Message = "仓库商品同步成功",
                AddedCount = 3,
            };
            var serviceMock = new Mock<IProductWarehouseReactService>();
            serviceMock.Setup(service => service.SyncFromHqAsync()).ReturnsAsync(expected);

            var controller = CreateController(serviceMock.Object);

            var result = await controller.SyncFromHq();

            var ok = Assert.IsType<OkObjectResult>(result);
            var payload = Assert.IsType<ApiResponse<SyncResult>>(ok.Value);
            Assert.True(payload.Success);
            Assert.Equal("仓库商品同步成功", payload.Message);
            Assert.Same(expected, payload.Data);
        }

        [Fact]
        public async Task SyncFromHq_服务抛异常时_返回500统一错误响应()
        {
            var serviceMock = new Mock<IProductWarehouseReactService>();
            serviceMock.Setup(service => service.SyncFromHqAsync()).ThrowsAsync(new Exception("boom"));

            var controller = CreateController(serviceMock.Object);

            var result = await controller.SyncFromHq();

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, objectResult.StatusCode);

            var payload = Assert.IsType<ApiResponse<SyncResult>>(objectResult.Value);
            Assert.False(payload.Success);
            Assert.Equal("仓库商品同步异常", payload.Message);
            Assert.Equal("INTERNAL_ERROR", payload.ErrorCode);
        }

        [Fact]
        public async Task SetMobileProductLocation_WhenLocationIsInvalid_ReturnsBadRequest()
        {
            var serviceMock = new Mock<IProductWarehouseReactService>();
            serviceMock
                .Setup(service => service.SetMobileProductLocationAsync("P001", "LOC-404"))
                .ThrowsAsync(new InvalidOperationException("货位不存在"));

            var uploadService = new TencentCloudUploadService(
                Options.Create(new TencentCloudSettings()),
                Mock.Of<ILogger<TencentCloudUploadService>>(),
                new System.Net.Http.HttpClient()
            );

            var controller = CreateController(serviceMock.Object, uploadService);

            var result = await controller.SetMobileProductLocation(
                "P001",
                new SetWarehouseProductLocationDto { LocationGuid = "LOC-404" }
            );

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            var payload = badRequest.Value?.ToString() ?? string.Empty;
            Assert.Contains("货位不存在", payload);
        }

        private static ReactProductWarehouseController CreateController(
            IProductWarehouseReactService service,
            TencentCloudUploadService? uploadService = null
        )
        {
            return new ReactProductWarehouseController(
                service,
                Mock.Of<ILogger<ReactProductWarehouseController>>(),
                Mock.Of<IDeviceRegistrationService>(),
                Mock.Of<IMapper>(),
                uploadService ?? CreateUploadService()
            );
        }

        private static TencentCloudUploadService CreateUploadService()
        {
            return new TencentCloudUploadService(
                Options.Create(new TencentCloudSettings()),
                Mock.Of<ILogger<TencentCloudUploadService>>(),
                new System.Net.Http.HttpClient()
            );
        }
    }
}
