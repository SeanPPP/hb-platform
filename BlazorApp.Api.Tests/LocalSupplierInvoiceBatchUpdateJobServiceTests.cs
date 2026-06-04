using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class LocalSupplierInvoiceBatchUpdateJobServiceTests
{
    [Fact]
    public async Task StartUpdateToStorePricesJobAsync_提交后立即返回运行中任务()
    {
        var release = new TaskCompletionSource<ApiResponse<UpdateToStorePricesResultDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var storeService = new Mock<ILocalSupplierInvoicesReactService>();
        storeService
            .Setup(service => service.UpdateDetailsToStorePricesAsync(It.IsAny<UpdateToStorePricesRequest>(), "tester"))
            .Returns(release.Task);

        var service = CreateService(storeService: storeService);

        var started = await service.StartUpdateToStorePricesJobAsync(
            BuildStoreRequest(),
            "tester"
        );

        Assert.Equal(LocalSupplierInvoiceBatchUpdateJobStatusConstants.Running, started.Status);
        Assert.False(string.IsNullOrWhiteSpace(started.JobId));
        Assert.Equal(["1005", "1033"], started.TargetStoreCodes);

        release.SetResult(ApiResponse<UpdateToStorePricesResultDto>.OK(new UpdateToStorePricesResultDto { Updated = 2 }));
        var completed = await WaitForStoreJobAsync(service, started.JobId);

        Assert.Equal(LocalSupplierInvoiceBatchUpdateJobStatusConstants.Succeeded, completed.Status);
        Assert.Equal(2, completed.Result?.Updated);
    }

    [Fact]
    public async Task StartUpdateToStorePricesJobAsync_相同Operation运行中时复用任务()
    {
        var release = new TaskCompletionSource<ApiResponse<UpdateToStorePricesResultDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var storeService = new Mock<ILocalSupplierInvoicesReactService>();
        storeService
            .Setup(service => service.UpdateDetailsToStorePricesAsync(It.IsAny<UpdateToStorePricesRequest>(), "tester"))
            .Returns(release.Task);

        var service = CreateService(storeService: storeService);

        var first = await service.StartUpdateToStorePricesJobAsync(BuildStoreRequest(), "tester");
        var duplicate = await service.StartUpdateToStorePricesJobAsync(BuildStoreRequest(), "tester");

        Assert.Equal(first.JobId, duplicate.JobId);
        Assert.True(duplicate.IsDuplicateRequest);

        release.SetResult(ApiResponse<UpdateToStorePricesResultDto>.OK(new UpdateToStorePricesResultDto { Updated = 1 }));
        await WaitForStoreJobAsync(service, first.JobId);
        storeService.Verify(service => service.UpdateDetailsToStorePricesAsync(It.IsAny<UpdateToStorePricesRequest>(), "tester"), Times.Once);
    }

    [Fact]
    public async Task StartUpdateToStorePricesJobAsync_同张单不同Operation运行中时拒绝并发写入()
    {
        var release = new TaskCompletionSource<ApiResponse<UpdateToStorePricesResultDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var storeService = new Mock<ILocalSupplierInvoicesReactService>();
        storeService
            .Setup(service => service.UpdateDetailsToStorePricesAsync(It.IsAny<UpdateToStorePricesRequest>(), "tester"))
            .Returns(release.Task);

        var service = CreateService(storeService: storeService);
        var first = await service.StartUpdateToStorePricesJobAsync(BuildStoreRequest(), "tester");
        var conflictingRequest = BuildStoreRequest();
        conflictingRequest.UpdateFields = new UpdateToStorePricesFields { UpdateRetailPrice = true };

        var conflict = await Assert.ThrowsAsync<LocalSupplierInvoiceBatchUpdateJobConflictException>(
            () => service.StartUpdateToStorePricesJobAsync(conflictingRequest, "tester")
        );

        Assert.Equal(first.JobId, conflict.ExistingJobId);

        release.SetResult(ApiResponse<UpdateToStorePricesResultDto>.OK(new UpdateToStorePricesResultDto { Updated = 1 }));
        await WaitForStoreJobAsync(service, first.JobId);
        storeService.Verify(service => service.UpdateDetailsToStorePricesAsync(It.IsAny<UpdateToStorePricesRequest>(), "tester"), Times.Once);
    }

    [Fact]
    public async Task StartUpdateToStorePricesJobAsync_业务失败可查询失败结果()
    {
        var storeService = new Mock<ILocalSupplierInvoicesReactService>();
        storeService
            .Setup(service => service.UpdateDetailsToStorePricesAsync(It.IsAny<UpdateToStorePricesRequest>(), "tester"))
            .ReturnsAsync(ApiResponse<UpdateToStorePricesResultDto>.Error(
                "更新到分店价格失败",
                "UPDATE_ERROR",
                new UpdateToStorePricesResultDto { Failed = 1, Errors = ["测试失败"] }
            ));

        var service = CreateService(storeService: storeService);

        var started = await service.StartUpdateToStorePricesJobAsync(BuildStoreRequest(), "tester");
        var completed = await WaitForStoreJobAsync(service, started.JobId);

        Assert.Equal(LocalSupplierInvoiceBatchUpdateJobStatusConstants.Failed, completed.Status);
        Assert.Equal(1, completed.Result?.Failed);
        Assert.Contains("更新到分店价格失败", completed.Message);
    }

    [Fact]
    public async Task StartUpdateHqProductsJobAsync_成功后可查询结果()
    {
        var hqService = new Mock<ILocalSupplierInvoiceHqProductSyncService>();
        hqService
            .Setup(service => service.UpdateHqProductsAsync("invoice-1", It.IsAny<UpdateHqProductsRequest>(), "tester"))
            .ReturnsAsync(ApiResponse<UpdateHqProductsResult>.OK(new UpdateHqProductsResult { Updated = 3 }));

        var service = CreateService(hqService: hqService);

        var started = await service.StartUpdateHqProductsJobAsync("invoice-1", BuildHqRequest(), "tester");
        var completed = await WaitForHqJobAsync(service, started.JobId);

        Assert.Equal(LocalSupplierInvoiceBatchUpdateJobStatusConstants.Succeeded, completed.Status);
        Assert.Equal(3, completed.Result?.Updated);
        Assert.Equal(["1005", "1033"], completed.TargetStoreCodes);
    }

    [Fact]
    public async Task StartUpdateHqProductsJobAsync_相同Operation运行中时复用任务()
    {
        var release = new TaskCompletionSource<ApiResponse<UpdateHqProductsResult>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var hqService = new Mock<ILocalSupplierInvoiceHqProductSyncService>();
        hqService
            .Setup(service => service.UpdateHqProductsAsync("invoice-1", It.IsAny<UpdateHqProductsRequest>(), "tester"))
            .Returns(release.Task);

        var service = CreateService(hqService: hqService);

        var first = await service.StartUpdateHqProductsJobAsync("invoice-1", BuildHqRequest(), "tester");
        var duplicate = await service.StartUpdateHqProductsJobAsync("invoice-1", BuildHqRequest(), "tester");

        Assert.Equal(first.JobId, duplicate.JobId);
        Assert.True(duplicate.IsDuplicateRequest);

        release.SetResult(ApiResponse<UpdateHqProductsResult>.OK(new UpdateHqProductsResult { Updated = 1 }));
        await WaitForHqJobAsync(service, first.JobId);
        hqService.Verify(service => service.UpdateHqProductsAsync("invoice-1", It.IsAny<UpdateHqProductsRequest>(), "tester"), Times.Once);
    }

    [Fact]
    public async Task StartUpdateHqProductsJobAsync_同张单不同Operation运行中时拒绝并发写入()
    {
        var release = new TaskCompletionSource<ApiResponse<UpdateHqProductsResult>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var hqService = new Mock<ILocalSupplierInvoiceHqProductSyncService>();
        hqService
            .Setup(service => service.UpdateHqProductsAsync("invoice-1", It.IsAny<UpdateHqProductsRequest>(), "tester"))
            .Returns(release.Task);

        var service = CreateService(hqService: hqService);
        var first = await service.StartUpdateHqProductsJobAsync("invoice-1", BuildHqRequest(), "tester");
        var conflictingRequest = BuildHqRequest();
        conflictingRequest.IdempotencyKey = "idem-2";

        var conflict = await Assert.ThrowsAsync<LocalSupplierInvoiceBatchUpdateJobConflictException>(
            () => service.StartUpdateHqProductsJobAsync("invoice-1", conflictingRequest, "tester")
        );

        Assert.Equal(first.JobId, conflict.ExistingJobId);

        release.SetResult(ApiResponse<UpdateHqProductsResult>.OK(new UpdateHqProductsResult { Updated = 1 }));
        await WaitForHqJobAsync(service, first.JobId);
        hqService.Verify(service => service.UpdateHqProductsAsync("invoice-1", It.IsAny<UpdateHqProductsRequest>(), "tester"), Times.Once);
    }

    [Fact]
    public async Task StartUpdateHqProductsJobAsync_异常失败可查询失败结果()
    {
        var hqService = new Mock<ILocalSupplierInvoiceHqProductSyncService>();
        hqService
            .Setup(service => service.UpdateHqProductsAsync("invoice-1", It.IsAny<UpdateHqProductsRequest>(), "tester"))
            .ThrowsAsync(new InvalidOperationException("HQ 连接超时"));

        var service = CreateService(hqService: hqService);

        var started = await service.StartUpdateHqProductsJobAsync("invoice-1", BuildHqRequest(), "tester");
        var completed = await WaitForHqJobAsync(service, started.JobId);

        Assert.Equal(LocalSupplierInvoiceBatchUpdateJobStatusConstants.Failed, completed.Status);
        Assert.Equal(1, completed.Result?.Failed);
        Assert.Contains("HQ 连接超时", completed.Message);
    }

    private static LocalSupplierInvoiceBatchUpdateJobService CreateService(
        Mock<ILocalSupplierInvoicesReactService>? storeService = null,
        Mock<ILocalSupplierInvoiceHqProductSyncService>? hqService = null
    )
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => storeService?.Object ?? Mock.Of<ILocalSupplierInvoicesReactService>());
        services.AddScoped(_ => hqService?.Object ?? Mock.Of<ILocalSupplierInvoiceHqProductSyncService>());
        var provider = services.BuildServiceProvider();

        return new LocalSupplierInvoiceBatchUpdateJobService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<LocalSupplierInvoiceBatchUpdateJobService>.Instance
        );
    }

    private static UpdateToStorePricesRequest BuildStoreRequest()
    {
        return new UpdateToStorePricesRequest
        {
            InvoiceGuid = "invoice-1",
            DetailGuids = ["detail-1", "detail-2"],
            TargetStoreCodes = ["1033", "1005"],
            UpdateFields = new UpdateToStorePricesFields { UpdatePurchasePrice = true },
        };
    }

    private static UpdateHqProductsRequest BuildHqRequest()
    {
        return new UpdateHqProductsRequest
        {
            DetailGuids = ["detail-1", "detail-2"],
            TargetStoreCodes = ["1033", "1005"],
            UpdateFields = new UpdateToStorePricesFields { UpdatePurchasePrice = true },
            IdempotencyKey = "idem-1",
        };
    }

    private static async Task<LocalSupplierInvoiceUpdateToStorePricesJobDto> WaitForStoreJobAsync(
        ILocalSupplierInvoiceBatchUpdateJobService service,
        string jobId
    )
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var job = await service.GetUpdateToStorePricesJobAsync(jobId);
            if (job?.Status is LocalSupplierInvoiceBatchUpdateJobStatusConstants.Succeeded or LocalSupplierInvoiceBatchUpdateJobStatusConstants.Failed)
                return job;
            await Task.Delay(20);
        }

        throw new TimeoutException("等待更新到分店价格 job 完成超时");
    }

    private static async Task<LocalSupplierInvoiceUpdateHqProductsJobDto> WaitForHqJobAsync(
        ILocalSupplierInvoiceBatchUpdateJobService service,
        string jobId
    )
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var job = await service.GetUpdateHqProductsJobAsync(jobId);
            if (job?.Status is LocalSupplierInvoiceBatchUpdateJobStatusConstants.Succeeded or LocalSupplierInvoiceBatchUpdateJobStatusConstants.Failed)
                return job;
            await Task.Delay(20);
        }

        throw new TimeoutException("等待更新HQ商品 job 完成超时");
    }
}
