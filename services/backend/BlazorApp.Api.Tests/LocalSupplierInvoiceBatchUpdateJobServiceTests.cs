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

    [Fact]
    public async Task StartPasteDetailsJobAsync_提交后立即返回运行中任务并可查询完成结果()
    {
        var release = new TaskCompletionSource<ApiResponse<BatchResultDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var storeService = new Mock<ILocalSupplierInvoicesReactService>();
        storeService
            .Setup(service => service.PasteDetailsAsync(It.IsAny<PasteDetailsRequest>(), "tester"))
            .Returns(release.Task);

        var service = CreateService(storeService: storeService);

        var started = await service.StartPasteDetailsJobAsync(BuildPasteRequest(), "tester");

        Assert.Equal(LocalSupplierInvoiceBatchUpdateJobStatusConstants.Running, started.Status);
        Assert.False(string.IsNullOrWhiteSpace(started.JobId));
        Assert.Equal("invoice-1", started.InvoiceGuid);

        release.SetResult(ApiResponse<BatchResultDto>.OK(new BatchResultDto { Inserted = 2, Updated = 1 }));
        var completed = await WaitForPasteJobAsync(service, started.JobId);

        Assert.Equal(LocalSupplierInvoiceBatchUpdateJobStatusConstants.Succeeded, completed.Status);
        Assert.Equal(2, completed.Result?.Inserted);
        Assert.Equal(1, completed.Result?.Updated);
    }

    [Fact]
    public async Task StartPasteDetailsJobAsync_后台任务保留多条码副码()
    {
        var capturedRequest = new TaskCompletionSource<PasteDetailsRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var release = new TaskCompletionSource<ApiResponse<BatchResultDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var storeService = new Mock<ILocalSupplierInvoicesReactService>();
        storeService
            .Setup(service => service.PasteDetailsAsync(It.IsAny<PasteDetailsRequest>(), "tester"))
            .Callback<PasteDetailsRequest, string>((request, _) => capturedRequest.SetResult(request))
            .Returns(release.Task);

        var service = CreateService(storeService: storeService);

        var started = await service.StartPasteDetailsJobAsync(BuildPasteRequest(
            barcode: "191554882676",
            additionalBarcodes:
            [
                "191554882690",
                "191554882669",
                "191554888425",
                "191554882706",
                "191554882652",
                "191554882683",
            ]
        ), "tester");
        var captured = await capturedRequest.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var item = Assert.Single(captured.Items);
        Assert.Equal("191554882676", item.Barcode);
        Assert.Equal(
            [
                "191554882690",
                "191554882669",
                "191554888425",
                "191554882706",
                "191554882652",
                "191554882683",
            ],
            item.AdditionalBarcodes
        );

        release.SetResult(ApiResponse<BatchResultDto>.OK(new BatchResultDto { Inserted = 1 }));
        await WaitForPasteJobAsync(service, started.JobId);
    }

    [Fact]
    public async Task StartPasteDetailsJobAsync_同主条码不同副码运行中时视为不同任务()
    {
        var release = new TaskCompletionSource<ApiResponse<BatchResultDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var storeService = new Mock<ILocalSupplierInvoicesReactService>();
        storeService
            .Setup(service => service.PasteDetailsAsync(It.IsAny<PasteDetailsRequest>(), "tester"))
            .Returns(release.Task);

        var service = CreateService(storeService: storeService);
        var first = await service.StartPasteDetailsJobAsync(BuildPasteRequest(
            barcode: "191554882676",
            additionalBarcodes: ["191554882690"]
        ), "tester");
        var conflictingRequest = BuildPasteRequest(
            barcode: "191554882676",
            additionalBarcodes: ["191554882669"]
        );

        var conflict = await Assert.ThrowsAsync<LocalSupplierInvoiceBatchUpdateJobConflictException>(
            () => service.StartPasteDetailsJobAsync(conflictingRequest, "tester")
        );

        Assert.Equal(first.JobId, conflict.ExistingJobId);

        release.SetResult(ApiResponse<BatchResultDto>.OK(new BatchResultDto { Inserted = 1 }));
        await WaitForPasteJobAsync(service, first.JobId);
        storeService.Verify(service => service.PasteDetailsAsync(It.IsAny<PasteDetailsRequest>(), "tester"), Times.Once);
    }

    [Fact]
    public async Task StartCheckProductsJobAsync_提交后立即返回运行中任务并可查询完成结果()
    {
        var release = new TaskCompletionSource<ApiResponse<CheckProductsResponseDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var storeService = new Mock<ILocalSupplierInvoicesReactService>();
        storeService
            .Setup(service => service.CheckProductsAsync(It.IsAny<CheckProductsRequest>()))
            .Returns(release.Task);

        var service = CreateService(storeService: storeService);

        var started = await service.StartCheckProductsJobAsync(BuildCheckProductsRequest());

        Assert.Equal(LocalSupplierInvoiceBatchUpdateJobStatusConstants.Running, started.Status);
        Assert.False(string.IsNullOrWhiteSpace(started.JobId));
        Assert.Equal("invoice-1", started.InvoiceGuid);

        release.SetResult(ApiResponse<CheckProductsResponseDto>.OK(new CheckProductsResponseDto
        {
            Summary = new CheckProductsSummaryDto { Total = 2, ProductExists = 1, ProductNotExists = 1 },
        }));
        var completed = await WaitForCheckProductsJobAsync(service, started.JobId);

        Assert.Equal(LocalSupplierInvoiceBatchUpdateJobStatusConstants.Succeeded, completed.Status);
        Assert.Equal(2, completed.Result?.Summary.Total);
        Assert.Equal(1, completed.Result?.Summary.ProductExists);
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

    private static PasteDetailsRequest BuildPasteRequest(
        string barcode = "BAR-1",
        List<string>? additionalBarcodes = null
    )
    {
        return new PasteDetailsRequest
        {
            InvoiceGuid = "invoice-1",
            Mode = "append",
            Items =
            [
                new PastedDetailItemDto
                {
                    ItemNumber = "ITEM-1",
                    Barcode = barcode,
                    AdditionalBarcodes = additionalBarcodes ?? new List<string>(),
                    ProductName = "Paste Item",
                    Quantity = 2,
                    PurchasePrice = 1.5m,
                },
            ],
        };
    }

    private static CheckProductsRequest BuildCheckProductsRequest()
    {
        return new CheckProductsRequest
        {
            InvoiceGuid = "invoice-1",
            DetailGuids = ["detail-1", "detail-2"],
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

    private static async Task<LocalSupplierInvoicePasteDetailsJobDto> WaitForPasteJobAsync(
        ILocalSupplierInvoiceBatchUpdateJobService service,
        string jobId
    )
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var job = await service.GetPasteDetailsJobAsync(jobId);
            if (job?.Status is LocalSupplierInvoiceBatchUpdateJobStatusConstants.Succeeded or LocalSupplierInvoiceBatchUpdateJobStatusConstants.Failed)
                return job;
            await Task.Delay(20);
        }

        throw new TimeoutException("等待粘贴明细 job 完成超时");
    }

    private static async Task<LocalSupplierInvoiceCheckProductsJobDto> WaitForCheckProductsJobAsync(
        ILocalSupplierInvoiceBatchUpdateJobService service,
        string jobId
    )
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var job = await service.GetCheckProductsJobAsync(jobId);
            if (job?.Status is LocalSupplierInvoiceBatchUpdateJobStatusConstants.Succeeded or LocalSupplierInvoiceBatchUpdateJobStatusConstants.Failed)
                return job;
            await Task.Delay(20);
        }

        throw new TimeoutException("等待商品检测 job 完成超时");
    }
}
