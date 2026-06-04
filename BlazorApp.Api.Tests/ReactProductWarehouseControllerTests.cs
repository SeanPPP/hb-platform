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
        public void StartSyncFromHqJob_仅允许Admin调用()
        {
            var method = typeof(ReactProductWarehouseController).GetMethod(
                nameof(ReactProductWarehouseController.StartSyncFromHqJob)
            );

            var authorizeAttribute = Assert.Single(
                method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            );

            Assert.Equal("Admin", ((AuthorizeAttribute)authorizeAttribute).Roles);
        }

        [Fact]
        public void GetSyncFromHqJob_仅允许Admin调用()
        {
            var method = typeof(ReactProductWarehouseController).GetMethod(
                nameof(ReactProductWarehouseController.GetSyncFromHqJob)
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
        public async Task StartSyncFromHqJob_空OperationId_返回400()
        {
            var controller = CreateController(Mock.Of<IProductWarehouseReactService>());

            var result = await controller.StartSyncFromHqJob(
                new WarehouseProductHqSyncJobRequestDto { OperationId = "" },
                CancellationToken.None
            );

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("operationId 不能为空", badRequest.Value?.ToString());
        }

        [Fact]
        public async Task StartSyncFromHqJob_成功时_返回后台任务()
        {
            var expected = new WarehouseProductHqSyncJobDto
            {
                JobId = "warehouse-job-1",
                Status = WarehouseProductHqSyncJobStatusConstants.Running,
                CreatedAt = new DateTime(2026, 6, 4, 1, 2, 3, DateTimeKind.Utc),
            };
            var jobServiceMock = new Mock<IWarehouseProductHqSyncJobService>();
            jobServiceMock
                .Setup(service =>
                    service.StartJobAsync(
                        It.Is<WarehouseProductHqSyncJobRequestDto>(request =>
                            request.OperationId == "warehouse-sync"
                        ),
                        It.IsAny<CancellationToken>()
                    )
                )
                .ReturnsAsync(expected);

            var controller = CreateController(
                Mock.Of<IProductWarehouseReactService>(),
                jobService: jobServiceMock.Object
            );

            var result = await controller.StartSyncFromHqJob(
                new WarehouseProductHqSyncJobRequestDto { OperationId = "warehouse-sync" },
                CancellationToken.None
            );

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Contains("仓库商品同步任务已提交", ok.Value?.ToString());
            var data = ok.Value!.GetType().GetProperty("data")!.GetValue(ok.Value);
            var job = Assert.IsType<WarehouseProductHqSyncJobDto>(data);
            Assert.Equal("warehouse-job-1", job.JobId);
        }

        [Fact]
        public async Task GetSyncFromHqJob_不存在时_返回404()
        {
            var jobServiceMock = new Mock<IWarehouseProductHqSyncJobService>();
            jobServiceMock
                .Setup(service => service.GetJobAsync("missing", It.IsAny<CancellationToken>()))
                .ReturnsAsync((WarehouseProductHqSyncJobDto?)null);

            var controller = CreateController(
                Mock.Of<IProductWarehouseReactService>(),
                jobService: jobServiceMock.Object
            );

            var result = await controller.GetSyncFromHqJob("missing", CancellationToken.None);

            var notFound = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Contains("同步任务不存在或已过期", notFound.Value?.ToString());
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
            TencentCloudUploadService? uploadService = null,
            IWarehouseProductHqSyncJobService? jobService = null
        )
        {
            return new ReactProductWarehouseController(
                service,
                jobService ?? Mock.Of<IWarehouseProductHqSyncJobService>(),
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
