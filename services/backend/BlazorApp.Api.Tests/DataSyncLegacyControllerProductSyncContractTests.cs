using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class DataSyncLegacyControllerProductSyncContractTests
{
    [Fact]
    public void 货柜同步BUSY失败助手_返回409重试提示并保留结果()
    {
        var method = typeof(DataSyncController).GetMethod(
            "CreateContainerSyncFailureResponse",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.NotNull(method);
        var controller = (DataSyncController)RuntimeHelpers.GetUninitializedObject(
            typeof(DataSyncController)
        );
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        var result = new SyncResult
        {
            IsSuccess = false,
            Message = "同一货柜正在保存，请稍后重试",
            ErrorCode = ContainerMutationLock.BusyErrorCode,
            ErrorCount = 1,
        };

        var response = Assert.IsAssignableFrom<IActionResult>(
            method!.Invoke(controller, new object[] { result })
        );

        var conflict = Assert.IsType<ConflictObjectResult>(response);
        var body = Assert.IsType<ApiResponse<SyncResult>>(conflict.Value);
        Assert.False(body.Success);
        Assert.Equal(ContainerMutationLock.BusyErrorCode, body.ErrorCode);
        Assert.Same(result, body.Details);
        Assert.Equal("1", controller.Response.Headers.RetryAfter);
        Assert.Equal(
            2,
            CountOccurrences(
                ReadControllerSource(),
                "return CreateContainerSyncFailureResponse(result);"
            )
        );
    }

    [Fact]
    public void 货柜同步已有提交时_锁繁忙保持原400失败语义()
    {
        var method = typeof(DataSyncController).GetMethod(
            "CreateContainerSyncFailureResponse",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.NotNull(method);
        var controller = (DataSyncController)RuntimeHelpers.GetUninitializedObject(
            typeof(DataSyncController)
        );
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        var result = new SyncResult
        {
            IsSuccess = false,
            Message = "部分货柜已同步，后续遇到锁竞争",
            ErrorCode = ContainerMutationLock.BusyErrorCode,
            AddedCount = 2,
            ErrorCount = 1,
        };

        var response = Assert.IsAssignableFrom<IActionResult>(
            method!.Invoke(controller, new object[] { result })
        );

        var badRequest = Assert.IsType<BadRequestObjectResult>(response);
        var body = Assert.IsType<ApiResponse<SyncResult>>(badRequest.Value);
        Assert.False(body.Success);
        Assert.Equal("SYNC_FAILED", body.ErrorCode);
        Assert.Same(result, body.Details);
        Assert.False(controller.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public void 商品同步BUSY响应_零成功返回409_部分成功保留错误码并返回200()
    {
        var source = ReadControllerSource();

        Assert.Equal(
            2,
            CountOccurrences(
                source,
                "return result.TotalCount == 0 ? Conflict(response) : Ok(response);"
            )
        );
        Assert.Equal(
            2,
            CountOccurrences(
                source,
                "result.ErrorCode == SetChildPurchasePriceMutationLock.BusyErrorCode"
            )
        );
        Assert.Equal(
            2,
            CountOccurrences(
                source,
                "ApiResponse<SyncResult>.Error(\n                            result.Message,\n                            result.ErrorCode,\n                            result"
            )
        );
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string ReadControllerSource()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "services/backend/BlazorApp.Api/Controllers/DataSyncController.cs"
            );
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            current = current.Parent;
        }

        throw new FileNotFoundException("未找到 DataSyncController.cs");
    }
}
