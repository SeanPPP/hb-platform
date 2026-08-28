using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.ProductWarehouse;

internal static class WarehouseProductBatchUpdateResultAssembler
{
    internal static WarehouseProductBatchUpdateResultDto CreateSuccess() =>
        new() { Success = true, Message = "更新完成" };

    internal static WarehouseProductBatchUpdateResultDto Reject(
        WarehouseProductBatchUpdateResultDto result,
        string error,
        int failedCount
    )
    {
        result.Success = false;
        result.Message = error;
        result.FailedCount = failedCount;
        result.Errors.Add(error);
        return result;
    }

    internal static void AddFailure(
        WarehouseProductBatchUpdateResultDto result,
        string error
    )
    {
        result.Errors.Add(error);
        result.FailedCount++;
    }

    internal static void AddSuccesses(
        WarehouseProductBatchUpdateResultDto result,
        int count
    ) => result.SuccessCount += count;

    internal static void SetImageUpdates(
        WarehouseProductBatchUpdateResultDto result,
        IReadOnlyDictionary<string, string> imageUrlByCode
    )
    {
        result.ImageUpdatedCount = imageUrlByCode.Count;
        result.ImageUpdates = WarehouseProductBatchUpdateEntityMapper.MapImageUpdates(
            imageUrlByCode
        );
    }

    internal static void RejectExecution(
        WarehouseProductBatchUpdateResultDto result,
        string error
    )
    {
        result.Success = false;
        result.SuccessCount = 0;
        result.ImageUpdatedCount = 0;
        result.ImageUpdates.Clear();
        result.Message = "批量更新失败: " + error;
    }
}
