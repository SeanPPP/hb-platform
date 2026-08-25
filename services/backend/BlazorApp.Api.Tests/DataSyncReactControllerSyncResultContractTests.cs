using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class DataSyncReactControllerSyncResultContractTests
{
    [Fact]
    public async Task SyncProductSetCodes_全BUSY且零提交时返回409并保留Data()
    {
        var result = new SyncResult
        {
            IsSuccess = false,
            Message = "套装成本正在更新",
            ErrorCode = SetChildPurchasePriceMutationLock.BusyErrorCode,
            ErrorCount = 1,
        };
        var fullSyncService = new Mock<IDataSyncFullService>(MockBehavior.Strict);
        fullSyncService
            .Setup(service => service.SyncProductSetCodesFromHqAsync(50000, 10000, 8))
            .ReturnsAsync(result);

        var response = await CreateController(fullSyncService.Object).SyncProductSetCodes();

        var conflict = Assert.IsType<ConflictObjectResult>(response);
        var body = Assert.IsType<ApiResponse<SyncResult>>(conflict.Value);
        Assert.False(body.Success);
        Assert.Equal(SetChildPurchasePriceMutationLock.BusyErrorCode, body.ErrorCode);
        Assert.Same(result, body.Data);
    }

    [Fact]
    public async Task SyncStoreMultiCodeProductsIncremental_部分提交后BUSY返回200失败包络()
    {
        var result = new SyncResult
        {
            IsSuccess = false,
            Message = "部分门店同步失败",
            ErrorCode = SetChildPurchasePriceMutationLock.BusyErrorCode,
            AddedCount = 2,
            ErrorCount = 1,
        };
        var incrementalSyncService = new Mock<IDataSyncIncrementalService>(MockBehavior.Strict);
        incrementalSyncService
            .Setup(service => service.SyncStoreMultiCodeProductsFromHqIncrementalAsync(null, null))
            .ReturnsAsync(result);

        var response =
            await CreateController(incrementalSyncService: incrementalSyncService.Object)
                .SyncStoreMultiCodeProductsIncremental();

        var ok = Assert.IsType<OkObjectResult>(response);
        var body = Assert.IsType<ApiResponse<SyncResult>>(ok.Value);
        Assert.False(body.Success);
        Assert.Equal("PARTIAL_FAILURE", body.ErrorCode);
        Assert.Same(result, body.Data);
        Assert.Equal(2, body.Data!.AddedCount);
    }

    [Fact]
    public async Task SyncWarehouseProducts_普通失败返回200失败包络并保留Data()
    {
        var result = new SyncResult
        {
            IsSuccess = false,
            Message = "仓库商品同步失败",
            ErrorCount = 1,
        };
        var fullSyncService = new Mock<IDataSyncFullService>(MockBehavior.Strict);
        fullSyncService
            .Setup(service => service.SyncWarehouseProductsFromHqAsync(
                50000,
                10000,
                "user-guid-001",
                "同步操作员"
            ))
            .ReturnsAsync(result);

        var response = await CreateController(fullSyncService.Object).SyncWarehouseProducts();

        var ok = Assert.IsType<OkObjectResult>(response);
        var body = Assert.IsType<ApiResponse<SyncResult>>(ok.Value);
        Assert.False(body.Success);
        Assert.Equal("SYNC_FAILED", body.ErrorCode);
        Assert.Same(result, body.Data);
    }

    [Fact]
    public async Task SyncWarehouseProductsIncremental_成功时保持200成功包络()
    {
        var result = new SyncResult
        {
            IsSuccess = true,
            Message = "同步成功",
            AddedCount = 3,
        };
        var incrementalSyncService = new Mock<IDataSyncIncrementalService>(MockBehavior.Strict);
        incrementalSyncService
            .Setup(service => service.SyncWarehouseProductsFromHqIncrementalAsync(null))
            .ReturnsAsync(result);

        var response =
            await CreateController(incrementalSyncService: incrementalSyncService.Object)
                .SyncWarehouseProductsIncremental();

        var ok = Assert.IsType<OkObjectResult>(response);
        var body = Assert.IsType<ApiResponse<SyncResult>>(ok.Value);
        Assert.True(body.Success);
        Assert.Same(result, body.Data);
    }

    [Fact]
    public async Task SyncProductSetCodes_BUSY与普通错误混合时返回200和SYNC_FAILED()
    {
        var result = new SyncResult
        {
            IsSuccess = false,
            Message = "同步存在混合错误",
            ErrorCode = SetChildPurchasePriceMutationLock.BusyErrorCode,
            ErrorCount = 2,
            BusyErrorCount = 1,
        };
        var fullSyncService = new Mock<IDataSyncFullService>(MockBehavior.Strict);
        fullSyncService
            .Setup(service => service.SyncProductSetCodesFromHqAsync(50000, 10000, 8))
            .ReturnsAsync(result);

        var response = await CreateController(fullSyncService.Object).SyncProductSetCodes();

        var ok = Assert.IsType<OkObjectResult>(response);
        var body = Assert.IsType<ApiResponse<SyncResult>>(ok.Value);
        Assert.False(body.Success);
        Assert.Equal("SYNC_FAILED", body.ErrorCode);
        Assert.Same(result, body.Data);
    }

    [Fact]
    public async Task SyncDomesticProducts_直接返回型端点也使用统一失败包络()
    {
        var result = new SyncResult
        {
            IsSuccess = false,
            Message = "国货同步失败",
            ErrorCode = "DOMESTIC_SYNC_FAILED",
            ErrorCount = 1,
        };
        var fullSyncService = new Mock<IDataSyncFullService>(MockBehavior.Strict);
        fullSyncService
            .Setup(service => service.SyncDomesticProductsFromHqAsync(50000, 10000))
            .ReturnsAsync(result);

        var response = await CreateController(fullSyncService.Object).SyncDomesticProducts();

        var ok = Assert.IsType<OkObjectResult>(response);
        var body = Assert.IsType<ApiResponse<SyncResult>>(ok.Value);
        Assert.False(body.Success);
        Assert.Equal("DOMESTIC_SYNC_FAILED", body.ErrorCode);
        Assert.Same(result, body.Data);
    }

    [Fact]
    public async Task SyncWarehouseProductsIncremental_显式全BUSY计数返回409()
    {
        var result = new SyncResult
        {
            IsSuccess = false,
            Message = "成本锁繁忙",
            ErrorCode = SetChildPurchasePriceMutationLock.BusyErrorCode,
            ErrorCount = 1,
            BusyErrorCount = 1,
        };
        var incrementalSyncService = new Mock<IDataSyncIncrementalService>(MockBehavior.Strict);
        incrementalSyncService
            .Setup(service => service.SyncWarehouseProductsFromHqIncrementalAsync(null))
            .ReturnsAsync(result);

        var response =
            await CreateController(incrementalSyncService: incrementalSyncService.Object)
                .SyncWarehouseProductsIncremental();

        var conflict = Assert.IsType<ConflictObjectResult>(response);
        var body = Assert.IsType<ApiResponse<SyncResult>>(conflict.Value);
        Assert.False(body.Success);
        Assert.Equal(SetChildPurchasePriceMutationLock.BusyErrorCode, body.ErrorCode);
        Assert.Same(result, body.Data);
    }

    [Theory]
    [InlineData(-2, 0)]
    [InlineData(5, 1)]
    public async Task SyncProductSetCodes_BUSY计数始终约束在错误总数内(
        int busyErrorCount,
        int expected
    )
    {
        var result = new SyncResult
        {
            IsSuccess = false,
            Message = "同步失败",
            ErrorCode = "SYNC_FAILED",
            ErrorCount = 1,
            BusyErrorCount = busyErrorCount,
            AddedCount = 1,
        };
        var fullSyncService = new Mock<IDataSyncFullService>(MockBehavior.Strict);
        fullSyncService
            .Setup(service => service.SyncProductSetCodesFromHqAsync(50000, 10000, 8))
            .ReturnsAsync(result);

        var response = await CreateController(fullSyncService.Object).SyncProductSetCodes();

        var ok = Assert.IsType<OkObjectResult>(response);
        var body = Assert.IsType<ApiResponse<SyncResult>>(ok.Value);
        Assert.Equal(expected, body.Data!.BusyErrorCount);
        Assert.InRange(body.Data.BusyErrorCount, 0, body.Data.ErrorCount);
    }

    [Fact]
    public void FailWithData_序列化保留Data和BusyErrorCount()
    {
        var result = new SyncResult
        {
            IsSuccess = false,
            ErrorCount = 1,
            BusyErrorCount = 1,
        };
        var response = ApiResponse<SyncResult>.FailWithData(
            result,
            "同步失败",
            SetChildPurchasePriceMutationLock.BusyErrorCode
        );

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"success\":false", json, StringComparison.Ordinal);
        Assert.Contains("\"data\":", json, StringComparison.Ordinal);
        Assert.Contains("\"busyErrorCount\":1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DataSyncReactController_所有SyncResult失败分支禁止使用成功工厂()
    {
        var source = ReadControllerSource();

        Assert.DoesNotContain(
            "return Ok(ApiResponse<SyncResult>.OK(result, result.Message));",
            source,
            StringComparison.Ordinal
        );
        Assert.DoesNotMatch(
            new Regex(
                "ApiResponse<SyncResult>\\.OK\\(result,\\s*\\\"[^\\\"]*存在错误",
                RegexOptions.CultureInvariant
            ),
            source
        );
        Assert.Contains("ApiResponse<SyncResult>.FailWithData(", source, StringComparison.Ordinal);
    }

    private static DataSyncReactController CreateController(
        IDataSyncFullService? fullSyncService = null,
        IDataSyncIncrementalService? incrementalSyncService = null
    )
    {
        var currentUserService = new Mock<ICurrentUserService>(MockBehavior.Strict);
        currentUserService.Setup(service => service.GetCurrentUserGuid()).Returns("user-guid-001");
        currentUserService.Setup(service => service.GetCurrentUsername()).Returns("同步操作员");

        return new DataSyncReactController(
            fullSyncService ?? Mock.Of<IDataSyncFullService>(),
            incrementalSyncService ?? Mock.Of<IDataSyncIncrementalService>(),
            Mock.Of<IProductHqSyncService>(),
            Mock.Of<ILogger<DataSyncReactController>>(),
            currentUserService.Object
        );
    }

    private static string ReadControllerSource()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "services/backend/BlazorApp.Api/Controllers/React/DataSyncReactController.cs"
            );
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            current = current.Parent;
        }

        throw new FileNotFoundException("未找到 DataSyncReactController.cs");
    }
}
