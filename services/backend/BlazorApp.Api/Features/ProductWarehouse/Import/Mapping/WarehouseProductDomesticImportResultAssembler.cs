using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.ProductWarehouse;

internal static class WarehouseProductDomesticImportResultAssembler
{
    internal static ImportFromDomesticResponseDto CreatePending() =>
        new() { Success = true, Message = "导入完成" };

    internal static ImportFromDomesticResponseDto Reject(string message) =>
        new() { Success = false, Message = message };

    internal static ImportResultDetailDto CreateDetail(string productCode) =>
        new() { ProductCode = productCode };

    internal static void AddFailure(
        ImportFromDomesticResponseDto response,
        ImportResultDetailDto detail,
        string message
    )
    {
        detail.Success = false;
        detail.Message = message;
        response.Results.Add(detail);
        response.FailedCount++;
    }

    internal static void AddSuccess(
        ImportFromDomesticResponseDto response,
        ImportResultDetailDto detail
    )
    {
        detail.Success = true;
        detail.Message = "导入成功";
        response.Results.Add(detail);
        response.SuccessCount++;
    }

    internal static void Complete(ImportFromDomesticResponseDto response)
    {
        if (response.SuccessCount != 0 || response.FailedCount == 0)
            return;

        response.Success = false;
        var firstFailed = response.Results.FirstOrDefault(result => !result.Success);
        response.Message = firstFailed != null
            ? $"导入失败：{firstFailed.Message}"
            : "导入失败";
    }

    internal static void RejectExecution(
        ImportFromDomesticResponseDto response,
        string error
    )
    {
        response.Success = false;
        response.SuccessCount = 0;
        response.Message = "导入失败: " + error;
    }

    internal static void CompleteNonHotbargain(ImportFromDomesticResponseDto response)
    {
        if (response.SuccessCount == 0 && response.FailedCount > 0)
        {
            response.Success = false;
            response.Message = "所有商品导入失败";
        }
    }
}
