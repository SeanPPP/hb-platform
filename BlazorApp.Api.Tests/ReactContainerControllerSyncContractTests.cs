using System.Reflection;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public class ReactContainerControllerSyncContractTests
{
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
        IContainerHqSyncService? syncService = null
    )
    {
        return new ReactContainerController(
            Mock.Of<IContainerReactService>(),
            syncService ?? Mock.Of<IContainerHqSyncService>(),
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
