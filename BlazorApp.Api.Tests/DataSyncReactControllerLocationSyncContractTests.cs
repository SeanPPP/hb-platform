using System.Reflection;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public class DataSyncReactControllerLocationSyncContractTests
{
    [Fact]
    public void LocationIncrementalEndpoints_允许管理员和仓库经理执行()
    {
        var controllerAuthorize = Assert.Single(
            typeof(DataSyncReactController).GetCustomAttributes<AuthorizeAttribute>(
                inherit: false
            )
        );

        Assert.True(string.IsNullOrWhiteSpace(controllerAuthorize.Roles));

        AssertAuthorizeRoles(
            nameof(DataSyncReactController.SyncLocationsIncremental),
            "Admin,WarehouseManager"
        );
        AssertAuthorizeRoles(
            nameof(DataSyncReactController.SyncProductLocationsIncremental),
            "Admin,WarehouseManager"
        );
    }

    [Fact]
    public void 除货位增量同步外_DataSyncReactController其他Action仍限制Admin()
    {
        var publicActions = typeof(DataSyncReactController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Where(method => method.ReturnType == typeof(Task<IActionResult>));

        foreach (var method in publicActions)
        {
            if (
                method.Name == nameof(DataSyncReactController.SyncLocationsIncremental)
                || method.Name == nameof(DataSyncReactController.SyncProductLocationsIncremental)
                || method.GetCustomAttributes<AllowAnonymousAttribute>(inherit: false).Any()
            )
            {
                continue;
            }

            var authorizeAttribute = Assert.Single(
                method.GetCustomAttributes<AuthorizeAttribute>(inherit: false)
            );

            Assert.Equal("Admin", authorizeAttribute.Roles);
        }
    }

    [Fact]
    public async Task SyncLocationsIncremental_调用货位增量同步服务并透传开始日期()
    {
        var expectedStartDate = new DateTime(2026, 6, 4, 9, 30, 0, DateTimeKind.Utc);
        DateTime? actualStartDate = null;
        var incrementalService = new Mock<IDataSyncIncrementalService>();
        var syncResult = new SyncResult { IsSuccess = true, Message = "库位增量同步成功", AddedCount = 2, UpdatedCount = 3 };
        incrementalService
            .Setup(service => service.SyncLocationsFromHqIncrementalAsync(It.IsAny<DateTime?>()))
            .Callback<DateTime?>(startDate => actualStartDate = startDate)
            .ReturnsAsync(syncResult);

        var controller = CreateController(incrementalService.Object);

        var response = await controller.SyncLocationsIncremental(
            new DataSyncReactController.IncrementalSyncRequest { StartDate = expectedStartDate }
        );

        Assert.IsType<OkObjectResult>(response);
        Assert.Equal(expectedStartDate, actualStartDate);
        incrementalService.Verify(
            service => service.SyncLocationsFromHqIncrementalAsync(expectedStartDate),
            Times.Once
        );
    }

    [Fact]
    public async Task SyncProductLocationsIncremental_调用商品货位增量同步服务并透传开始日期()
    {
        var expectedStartDate = new DateTime(2026, 6, 4, 10, 0, 0, DateTimeKind.Utc);
        DateTime? actualStartDate = null;
        var incrementalService = new Mock<IDataSyncIncrementalService>();
        var syncResult = new SyncResult { IsSuccess = true, Message = "商品库位增量同步成功", AddedCount = 4, UpdatedCount = 5 };
        incrementalService
            .Setup(service => service.SyncProductLocationsFromHqIncrementalAsync(It.IsAny<DateTime?>()))
            .Callback<DateTime?>(startDate => actualStartDate = startDate)
            .ReturnsAsync(syncResult);

        var controller = CreateController(incrementalService.Object);

        var response = await controller.SyncProductLocationsIncremental(
            new DataSyncReactController.IncrementalSyncRequest { StartDate = expectedStartDate }
        );

        Assert.IsType<OkObjectResult>(response);
        Assert.Equal(expectedStartDate, actualStartDate);
        incrementalService.Verify(
            service => service.SyncProductLocationsFromHqIncrementalAsync(expectedStartDate),
            Times.Once
        );
    }

    private static void AssertAuthorizeRoles(string methodName, string expectedRoles)
    {
        var method = typeof(DataSyncReactController).GetMethod(methodName);
        Assert.NotNull(method);

        var authorizeAttribute = Assert.Single(
            method!.GetCustomAttributes<AuthorizeAttribute>(inherit: false)
        );

        Assert.Equal(expectedRoles, authorizeAttribute.Roles);
    }

    private static DataSyncReactController CreateController(
        IDataSyncIncrementalService incrementalService
    )
    {
        return new DataSyncReactController(
            Mock.Of<IDataSyncFullService>(),
            incrementalService,
            Mock.Of<IProductHqSyncService>(),
            Mock.Of<ILogger<DataSyncReactController>>()
        );
    }
}
