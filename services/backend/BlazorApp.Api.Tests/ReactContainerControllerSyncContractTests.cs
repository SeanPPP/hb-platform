using System.Reflection;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public class ReactContainerControllerSyncContractTests
{
    [Fact]
    public async Task GetComingSoonContainerSummaries_多用户共享30分钟缓存()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var containerService = new Mock<IContainerReactService>();
        containerService
            .Setup(service => service.GetContainersAsync(It.IsAny<ContainerQueryRequest>()))
            .ReturnsAsync(
                new ContainerListResponse
                {
                    Containers = new List<ContainerMainDto>
                    {
                        new ContainerMainDto { HGUID = "CACHED-SUMMARY", 货柜编号 = "CS-1" },
                    },
                    TotalCount = 1,
                    Page = 1,
                    PageSize = 100,
                }
            );

        var firstController = CreateController(containerService: containerService.Object, cache: cache);
        var secondController = CreateController(containerService: containerService.Object, cache: cache);

        var firstResponse = await firstController.GetComingSoonContainerSummaries();
        var secondResponse = await secondController.GetComingSoonContainerSummaries();

        Assert.IsType<OkObjectResult>(firstResponse);
        Assert.IsType<OkObjectResult>(secondResponse);
        containerService.Verify(service => service.GetContainersAsync(It.IsAny<ContainerQueryRequest>()), Times.Exactly(2));
        Assert.Equal(TimeSpan.FromMinutes(30), ReactContainerController.ComingSoonCacheDuration);
    }

    [Fact]
    public async Task GetComingSoonContainerSummaries_同一请求内顺序查询避免共享连接并发()
    {
        var activeCalls = 0;
        var maxActiveCalls = 0;
        var containerService = new Mock<IContainerReactService>();
        containerService
            .Setup(service => service.GetContainersAsync(It.IsAny<ContainerQueryRequest>()))
            .Returns(async (ContainerQueryRequest request) =>
            {
                var currentCalls = Interlocked.Increment(ref activeCalls);
                maxActiveCalls = Math.Max(maxActiveCalls, currentCalls);
                await Task.Delay(20);
                Interlocked.Decrement(ref activeCalls);

                return new ContainerListResponse
                {
                    Containers = new List<ContainerMainDto>
                    {
                        new ContainerMainDto
                        {
                            HGUID = request.DateType,
                            货柜编号 = request.DateType,
                        },
                    },
                    TotalCount = 1,
                    Page = 1,
                    PageSize = 100,
                };
            });
        var controller = CreateController(containerService: containerService.Object);

        var response = await controller.GetComingSoonContainerSummaries();

        Assert.IsType<OkObjectResult>(response);
        Assert.Equal(1, maxActiveCalls);
        containerService.Verify(service => service.GetContainersAsync(It.IsAny<ContainerQueryRequest>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetComingSoonContainerProducts_同一货柜共享缓存且不同货柜独立()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var containerService = new Mock<IContainerReactService>();
        containerService
            .Setup(service => service.GetContainerProductsAsync("CONTAINER-A"))
            .ReturnsAsync(new List<ContainerDetailDto> { new ContainerDetailDto { HGUID = "DETAIL-A" } });
        containerService
            .Setup(service => service.GetContainerProductsAsync("CONTAINER-B"))
            .ReturnsAsync(new List<ContainerDetailDto> { new ContainerDetailDto { HGUID = "DETAIL-B" } });

        var firstController = CreateController(containerService: containerService.Object, cache: cache);
        var secondController = CreateController(containerService: containerService.Object, cache: cache);

        await firstController.GetComingSoonContainerProducts("CONTAINER-A");
        await secondController.GetComingSoonContainerProducts("CONTAINER-A");
        await secondController.GetComingSoonContainerProducts("CONTAINER-B");

        containerService.Verify(service => service.GetContainerProductsAsync("CONTAINER-A"), Times.Once);
        containerService.Verify(service => service.GetContainerProductsAsync("CONTAINER-B"), Times.Once);
        Assert.Equal(TimeSpan.FromMinutes(30), ReactContainerController.ComingSoonCacheDuration);
    }

    [Theory]
    [InlineData(nameof(ReactContainerController.GetComingSoonContainerSummaries))]
    [InlineData(nameof(ReactContainerController.GetComingSoonContainerProducts))]
    [InlineData(nameof(ReactContainerController.GetComingSoonContainers))]
    public void ComingSoonReadEndpoints_允许仓库员工访问(string methodName)
    {
        var method = typeof(ReactContainerController).GetMethod(methodName);

        var authorizeAttribute = Assert.IsType<AuthorizeAttribute>(Assert.Single(
            method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
        ));

        // 订货前台由仓库员工日常查看到货计划，拆分接口必须和仓库员工账号权限保持一致。
        Assert.Equal("Admin,WarehouseManager,WarehouseStaff,User", authorizeAttribute.Roles);
    }

    [Fact]
    public async Task QueryContainerProducts_应使用路由货柜GUID并返回标准响应()
    {
        ContainerDetailQueryDto? actualRequest = null;
        var expectedResult = new ContainerDetailQueryResultDto
        {
            Items = new List<ContainerDetailDto> { new() { HGUID = "DETAIL-1" } },
            ItemsTotal = 1,
            PageNumber = 1,
            PageSize = 50,
            HasMore = false,
            TotalComputed = true,
            StatsComputed = true,
            TagStats = new ContainerDetailTagStatsDto { All = 1 },
        };
        var containerService = new Mock<IContainerReactService>();
        containerService
            .Setup(service => service.QueryContainerDetailsAsync(It.IsAny<ContainerDetailQueryDto>()))
            .Callback<ContainerDetailQueryDto>(request => actualRequest = request)
            .ReturnsAsync(expectedResult);
        var controller = CreateController(containerService: containerService.Object);

        var response = await controller.QueryContainerProducts(
            "ROUTE-GUID",
            new ContainerDetailQueryDto
            {
                ContainerGuid = "BODY-GUID",
                PageNumber = 1,
                PageSize = 50,
                IncludeTotal = false,
                IncludeStats = false,
            }
        );

        var ok = Assert.IsType<OkObjectResult>(response);
        Assert.NotNull(actualRequest);
        Assert.Equal("ROUTE-GUID", actualRequest!.ContainerGuid);
        Assert.False(actualRequest.IncludeTotal);
        Assert.False(actualRequest.IncludeStats);
        AssertPayload(ok.Value, true, "获取货柜商品明细成功", expectedResult);
    }

    [Fact]
    public void SyncContainersFromHq_使用货柜编辑权限策略()
    {
        var method = typeof(ReactContainerController).GetMethod(
            nameof(ReactContainerController.SyncContainersFromHq)
        );

        var authorizeAttribute = Assert.Single(
            method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
        );

        Assert.Equal(
            Permissions.Container.Edit,
            ((AuthorizeAttribute)authorizeAttribute).Policy
        );
    }

    [Fact]
    public async Task SyncContainersFromHq_请求体为空时_仍返回成功响应并透传空开始日期()
    {
        DateTime? actualStartDate = DateTime.MinValue;
        var syncResult = new SyncResult { IsSuccess = true, Message = "同步成功" };
        var syncService = new Mock<IContainerHqSyncService>();
        syncService
            .Setup(service => service.SyncIncrementalAsync(It.IsAny<DateTime?>()))
            .Callback<DateTime?>(startDate => actualStartDate = startDate)
            .ReturnsAsync(syncResult);

        var controller = CreateController(syncService.Object);

        var response = await controller.SyncContainersFromHq(null);

        var ok = Assert.IsType<OkObjectResult>(response);
        Assert.Equal(200, ok.StatusCode ?? 200);
        Assert.Null(actualStartDate);
        AssertPayload(ok.Value, true, "同步成功", syncResult);
    }

    [Fact]
    public async Task SyncContainersFromHq_传入开始日期时_应透传给同步服务()
    {
        var expectedStartDate = new DateTime(2026, 5, 31, 9, 30, 0, DateTimeKind.Utc);
        DateTime? actualStartDate = null;
        var syncService = new Mock<IContainerHqSyncService>();
        syncService
            .Setup(service => service.SyncIncrementalAsync(It.IsAny<DateTime?>()))
            .Callback<DateTime?>(startDate => actualStartDate = startDate)
            .ReturnsAsync(new SyncResult { IsSuccess = true, Message = "同步成功" });

        var controller = CreateController(syncService.Object);

        await controller.SyncContainersFromHq(
            new SyncFromHqRequestDto { StartDate = expectedStartDate }
        );

        Assert.Equal(expectedStartDate, actualStartDate);
    }

    [Fact]
    public async Task SyncContainersFromHq_并发冲突时_返回409和标准错误码()
    {
        var syncService = new Mock<IContainerHqSyncService>();
        syncService
            .Setup(service => service.SyncIncrementalAsync(It.IsAny<DateTime?>()))
            .ReturnsAsync(
                new SyncResult
                {
                    IsSuccess = false,
                    Message = "同步任务正在执行",
                    ErrorCode = ContainerHqSyncErrorCodes.Conflict,
                }
            );

        var controller = CreateController(syncService.Object);

        var response = await controller.SyncContainersFromHq(null);

        var conflict = Assert.IsType<ConflictObjectResult>(response);
        Assert.Equal(409, conflict.StatusCode);
        AssertPayload(conflict.Value, false, "同步任务正在执行", null, "CONTAINER_SYNC_CONFLICT");
    }

    [Fact]
    public async Task SyncContainersFromHq_HQ源数据异常时_返回422和标准错误码()
    {
        var syncService = new Mock<IContainerHqSyncService>();
        syncService
            .Setup(service => service.SyncIncrementalAsync(It.IsAny<DateTime?>()))
            .ReturnsAsync(
                new SyncResult
                {
                    IsSuccess = false,
                    Message = "HQ源数据异常",
                    ErrorCode = ContainerHqSyncErrorCodes.InvalidSourceData,
                }
            );

        var controller = CreateController(syncService.Object);

        var response = await controller.SyncContainersFromHq(null);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(response);
        Assert.Equal(422, unprocessable.StatusCode);
        AssertPayload(
            unprocessable.Value,
            false,
            "HQ源数据异常",
            null,
            "CONTAINER_SYNC_INVALID_SOURCE_DATA"
        );
    }

    [Fact]
    public async Task SyncContainersFromHq_未预期异常时_返回500和内部错误码()
    {
        var syncService = new Mock<IContainerHqSyncService>();
        syncService
            .Setup(service => service.SyncIncrementalAsync(It.IsAny<DateTime?>()))
            .ThrowsAsync(new Exception("boom"));

        var controller = CreateController(syncService.Object);

        var response = await controller.SyncContainersFromHq(null);

        var serverError = Assert.IsType<ObjectResult>(response);
        Assert.Equal(500, serverError.StatusCode);
        AssertPayload(serverError.Value, false, "服务器内部错误", null, "INTERNAL_ERROR");
    }

    private static ReactContainerController CreateController(
        IContainerHqSyncService? syncService = null,
        IContainerReactService? containerService = null,
        IMemoryCache? cache = null
    )
    {
        return new ReactContainerController(
            containerService ?? Mock.Of<IContainerReactService>(),
            syncService ?? Mock.Of<IContainerHqSyncService>(),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<ReactContainerController>>()
        );
    }

    private static void AssertPayload(
        object? payload,
        bool expectedSuccess,
        string expectedMessage,
        object? expectedData,
        string? expectedErrorCode = null
    )
    {
        Assert.NotNull(payload);

        var success = GetPropertyValue<bool>(payload!, "success");
        var message = GetPropertyValue<string>(payload!, "message");
        var data = GetPropertyValue<object?>(payload!, "data");

        Assert.Equal(expectedSuccess, success);
        Assert.Equal(expectedMessage, message);

        if (expectedErrorCode is null)
        {
            Assert.Same(expectedData, data);
            return;
        }

        var syncResult = Assert.IsType<SyncResult>(data);
        Assert.Equal(expectedErrorCode, syncResult.ErrorCode);
    }

    private static T GetPropertyValue<T>(object source, string propertyName)
    {
        var property = source
            .GetType()
            .GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase
            );

        Assert.NotNull(property);
        return (T)property!.GetValue(source)!;
    }
}
