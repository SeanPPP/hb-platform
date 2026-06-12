using System.Reflection;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public class ReactProductStoreSyncJobContractTests
{
    [Fact]
    public async Task StartSyncProductsToStoresJob_要求商品管理权限并返回JobId和状态()
    {
        var authorize = typeof(ReactProductController)
            .GetMethod(nameof(ReactProductController.StartSyncProductsToStoresJob))!
            .GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(Permissions.PosProducts.Manage, authorize?.Policy);

        var jobService = new Mock<IProductStoreSyncJobService>(MockBehavior.Strict);
        jobService
            .Setup(service => service.StartJobAsync(
                It.Is<SyncProductsToStoresRequest>(request =>
                    request.ProductCodes.SequenceEqual(new[] { "P001" })
                    && request.StoreCodes.SequenceEqual(new[] { "S01" })
                    && request.Fields.SequenceEqual(new[] { "retailPrice" })
                ),
                CancellationToken.None
            ))
            .ReturnsAsync(
                new SyncProductsToStoresJobDto
                {
                    JobId = "job-1",
                    OperationId = "sync:P001:S01:retailPrice",
                    Status = ProductStoreSyncJobStatusConstants.Running,
                    Message = "商品同步到分店任务已提交",
                }
            );

        var controller = CreateController(jobService: jobService.Object);

        var response = await controller.StartSyncProductsToStoresJob(
            new SyncProductsToStoresRequest
            {
                ProductCodes = ["P001"],
                StoreCodes = ["S01"],
                Fields = ["retailPrice"],
            },
            CancellationToken.None
        );

        var ok = Assert.IsType<OkObjectResult>(response);
        Assert.True(ReadProperty<bool>(ok.Value!, "success"));
        Assert.Equal("商品同步到分店任务已提交", ReadProperty<string>(ok.Value!, "message"));

        var data = ReadPropertyValue(ok.Value!, "data");
        Assert.Equal("job-1", ReadProperty<string>(data, "jobId"));
        Assert.Equal(ProductStoreSyncJobStatusConstants.Running, ReadProperty<string>(data, "status"));
        jobService.VerifyAll();
    }

    [Fact]
    public async Task GetSyncProductsToStoresJob_要求商品管理权限并返回同步结果()
    {
        var authorize = typeof(ReactProductController)
            .GetMethod(nameof(ReactProductController.GetSyncProductsToStoresJob))!
            .GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(Permissions.PosProducts.Manage, authorize?.Policy);

        var jobService = new Mock<IProductStoreSyncJobService>(MockBehavior.Strict);
        jobService
            .Setup(service => service.GetJobAsync("job-1", CancellationToken.None))
            .ReturnsAsync(
                new SyncProductsToStoresJobDto
                {
                    JobId = "job-1",
                    OperationId = "sync:P001:S01:retailPrice",
                    Status = ProductStoreSyncJobStatusConstants.Succeeded,
                    Result = new SyncProductsToStoresResult
                    {
                        UpdatedCount = 1,
                        Errors = new List<string>(),
                    },
                }
            );

        var controller = CreateController(jobService: jobService.Object);

        var response = await controller.GetSyncProductsToStoresJob("job-1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response);
        Assert.True(ReadProperty<bool>(ok.Value!, "success"));

        var data = ReadPropertyValue(ok.Value!, "data");
        Assert.Equal("job-1", ReadProperty<string>(data, "jobId"));
        Assert.Equal(ProductStoreSyncJobStatusConstants.Succeeded, ReadProperty<string>(data, "status"));

        var result = ReadPropertyValue(data, "result");
        Assert.Equal(1, ReadProperty<int>(result, "updatedCount"));
        jobService.VerifyAll();
    }

    [Fact]
    public async Task StartSyncProductsToStoresJob_非Admin包含越权分店时返回Forbidden且不启动Job()
    {
        var jobService = new Mock<IProductStoreSyncJobService>(MockBehavior.Strict);
        var controller = CreateController(
            scope: new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAdmin = false,
                StoreCodes = ["1005"],
            },
            jobService: jobService.Object
        );

        var response = await controller.StartSyncProductsToStoresJob(
            new SyncProductsToStoresRequest
            {
                ProductCodes = ["P001"],
                StoreCodes = ["1005", "1033"],
                Fields = ["retailPrice"],
            },
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(response);
        jobService.Verify(
            service => service.StartJobAsync(It.IsAny<SyncProductsToStoresRequest>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task StartSyncProductsToStoresJob_运行任务超过上限时返回429和错误码()
    {
        var jobService = new Mock<IProductStoreSyncJobService>(MockBehavior.Strict);
        jobService
            .Setup(service => service.StartJobAsync(
                It.IsAny<SyncProductsToStoresRequest>(),
                CancellationToken.None
            ))
            .ThrowsAsync(new ProductStoreSyncJobConcurrencyLimitExceededException(
                "商品同步到分店任务正在处理较多请求，请稍后重试"
            ));

        var controller = CreateController(jobService: jobService.Object);

        var response = await controller.StartSyncProductsToStoresJob(
            new SyncProductsToStoresRequest
            {
                ProductCodes = ["P001"],
                StoreCodes = ["S01"],
                Fields = ["retailPrice"],
            },
            CancellationToken.None
        );

        var objectResult = Assert.IsType<ObjectResult>(response);
        Assert.Equal(429, objectResult.StatusCode);
        Assert.False(ReadProperty<bool>(objectResult.Value!, "success"));
        Assert.Equal(
            "PRODUCT_STORE_SYNC_JOB_LIMIT_EXCEEDED",
            ReadProperty<string>(objectResult.Value!, "errorCode")
        );
        jobService.VerifyAll();
    }

    [Fact]
    public async Task StartSyncProductsToStoresJob_Admin合法请求会规范化目标分店再启动Job()
    {
        SyncProductsToStoresRequest? captured = null;
        var jobService = new Mock<IProductStoreSyncJobService>(MockBehavior.Strict);
        jobService
            .Setup(service => service.StartJobAsync(
                It.IsAny<SyncProductsToStoresRequest>(),
                CancellationToken.None
            ))
            .Callback<SyncProductsToStoresRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new SyncProductsToStoresJobDto
            {
                JobId = "job-admin",
                Status = ProductStoreSyncJobStatusConstants.Running,
            });

        var controller = CreateController(
            scope: new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAdmin = true,
            },
            jobService: jobService.Object
        );

        var request = new SyncProductsToStoresRequest
        {
            ProductCodes = ["P001"],
            StoreCodes = [" 1005 ", "", "1005", "1033", " 1033 "],
            Fields = ["retailPrice"],
        };

        var response = await controller.StartSyncProductsToStoresJob(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(response);
        Assert.Same(request, captured);
        Assert.Equal(["1005", "1033"], captured!.StoreCodes);
        Assert.Equal(["1005", "1033"], request.StoreCodes);
        jobService.VerifyAll();
    }

    [Fact]
    public async Task StartSyncProductsToStoresJob_非Admin合法请求会规范化目标分店再启动Job()
    {
        SyncProductsToStoresRequest? captured = null;
        var jobService = new Mock<IProductStoreSyncJobService>(MockBehavior.Strict);
        jobService
            .Setup(service => service.StartJobAsync(
                It.IsAny<SyncProductsToStoresRequest>(),
                CancellationToken.None
            ))
            .Callback<SyncProductsToStoresRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new SyncProductsToStoresJobDto
            {
                JobId = "job-manager",
                Status = ProductStoreSyncJobStatusConstants.Running,
            });

        var request = new SyncProductsToStoresRequest
        {
            ProductCodes = ["P001"],
            StoreCodes = [" 1005 ", "1005", "1033"],
            Fields = ["retailPrice"],
        };
        var controller = CreateController(
            scope: new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAdmin = false,
                StoreCodes = ["1005", "1033"],
            },
            jobService: jobService.Object
        );

        var response = await controller.StartSyncProductsToStoresJob(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(response);
        Assert.Same(request, captured);
        Assert.Equal(["1005", "1033"], captured!.StoreCodes);
        Assert.Equal(["1005", "1033"], request.StoreCodes);
        jobService.VerifyAll();
    }

    [Fact]
    public async Task SyncProductsToStores_非Admin包含越权分店时返回Forbidden且不调用旧同步服务()
    {
        var syncService = new Mock<IProductStoreSyncService>(MockBehavior.Strict);
        var controller = CreateController(
            scope: new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAdmin = false,
                StoreCodes = ["1005"],
            },
            syncService: syncService.Object
        );

        var response = await controller.SyncProductsToStores(new SyncProductsToStoresRequest
        {
            ProductCodes = ["P001"],
            StoreCodes = ["1005", "1033"],
            Fields = ["retailPrice"],
        });

        Assert.IsType<ForbidResult>(response);
        syncService.Verify(
            service => service.SyncProductsToStoresAsync(It.IsAny<SyncProductsToStoresRequest>()),
            Times.Never
        );
    }

    [Fact]
    public async Task SyncProductsToStores_Admin合法请求会规范化目标分店再调用旧同步服务()
    {
        SyncProductsToStoresRequest? captured = null;
        var syncService = new Mock<IProductStoreSyncService>(MockBehavior.Strict);
        syncService
            .Setup(service => service.SyncProductsToStoresAsync(It.IsAny<SyncProductsToStoresRequest>()))
            .Callback<SyncProductsToStoresRequest>(request => captured = request)
            .ReturnsAsync(ApiResponse<SyncProductsToStoresResult>.OK(
                new SyncProductsToStoresResult(),
                "同步成功"
            ));

        var request = new SyncProductsToStoresRequest
        {
            ProductCodes = ["P001"],
            StoreCodes = [" 1005 ", "1005", "", "1033"],
            Fields = ["retailPrice"],
        };
        var controller = CreateController(
            scope: new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAdmin = true,
            },
            syncService: syncService.Object
        );

        var response = await controller.SyncProductsToStores(request);

        Assert.IsType<OkObjectResult>(response);
        Assert.Same(request, captured);
        Assert.Equal(["1005", "1033"], captured!.StoreCodes);
        Assert.Equal(["1005", "1033"], request.StoreCodes);
        syncService.VerifyAll();
    }

    private static ReactProductController CreateController(
        CurrentUserManageableStoreScope? scope = null,
        IProductStoreSyncService? syncService = null,
        IProductStoreSyncJobService? jobService = null
    )
    {
        var scopeService = new Mock<ICurrentUserManageableStoreScopeService>();
        scopeService
            .Setup(service => service.GetScopeAsync())
            .ReturnsAsync(scope ?? new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAdmin = true,
            });

        return new ReactProductController(
            Mock.Of<IProductReactService>(),
            syncService ?? Mock.Of<IProductStoreSyncService>(),
            Mock.Of<IProductHqSyncService>(),
            scopeService.Object,
            Mock.Of<ILogger<ReactProductController>>(),
            supplierImageJobService: null,
            productStoreSyncJobService: jobService
        );
    }

    private static T ReadProperty<T>(object instance, string propertyName)
    {
        return Assert.IsAssignableFrom<T>(ReadPropertyValue(instance, propertyName));
    }

    private static object ReadPropertyValue(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase
        );
        Assert.NotNull(property);
        return property!.GetValue(instance)!;
    }
}
