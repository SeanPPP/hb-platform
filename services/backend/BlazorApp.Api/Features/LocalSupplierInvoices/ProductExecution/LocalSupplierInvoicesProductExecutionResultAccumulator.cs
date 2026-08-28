using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.LocalSupplierInvoices
{
    /// <summary>集中维护 DTO 计数与结果映射，命令写入不直接拼装 API DTO。</summary>
    internal sealed class LocalSupplierInvoicesProductExecutionResultAccumulator
    {
        public BatchExecuteActionsResultDto Result { get; } = new();
        public List<string> SuccessfulDetailGuids { get; } = new();
        public HashSet<string> ChangedProductCodes { get; } = new(StringComparer.Ordinal);

        public void Apply(
            DetailAction action,
            LocalSupplierInvoicesProductExecutionStore.BatchOperationResult operation
        )
        {
            switch (action)
            {
                case DetailAction.CreateProduct:
                    Result.CreatedProducts = operation.SuccessCount;
                    Result.AddedMultiCodes += operation.AddedMultiCodeCount;
                    break;
                case DetailAction.UpdatePurchasePrice:
                    Result.UpdatedPurchasePrices = operation.SuccessCount;
                    break;
                case DetailAction.UpdateItemNumber:
                    Result.UpdatedItemNumbers = operation.SuccessCount;
                    break;
                case DetailAction.AddMultiCode:
                    Result.AddedMultiCodes += operation.SuccessCount;
                    break;
            }

            Result.Failed += operation.FailedCount;
            Result.Skipped += operation.SkippedCount;
            Result.Errors.AddRange(operation.Errors);
            SuccessfulDetailGuids.AddRange(operation.SuccessfulDetailGuids);
            ChangedProductCodes.UnionWith(operation.ChangedProductCodes);
        }

        public void AddSkipped(int count) => Result.Skipped += count;
    }

    internal sealed record ProductExecutionCommandResult(
        BatchExecuteActionsResultDto Result,
        string? ErrorMessage = null,
        string? ErrorCode = null
    )
    {
        public ApiResponse<BatchExecuteActionsResultDto> ToApiResponse() =>
            ErrorCode == null
                ? ApiResponse<BatchExecuteActionsResultDto>.OK(Result, "批量执行完成")
                : ApiResponse<BatchExecuteActionsResultDto>.Error(ErrorMessage!, ErrorCode, Result);
    }
}
