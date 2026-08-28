using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.ProductWarehouse;

/// <summary>
/// 批量更新 Handler：只负责请求校验、领域计划创建与命令写入编排。
/// </summary>
internal sealed class ProductWarehouseBatchUpdateSlice : IProductWarehouseBatchUpdateSlice
{
    private const string SystemUpdatedBy = "System";
    private readonly WarehouseProductBatchUpdateCommandWriter _commandWriter;

    internal ProductWarehouseBatchUpdateSlice(ProductWarehouseSliceContext context)
    {
        _commandWriter = new WarehouseProductBatchUpdateCommandWriter(context);
    }

    public Task<BatchOperationResultDto> BatchUpdateAsync(List<UpdateItemDto> items) =>
        BatchUpdateAsync(items, SystemUpdatedBy);

    public async Task<BatchOperationResultDto> BatchUpdateAsync(
        List<UpdateItemDto> items,
        string? updatedBy
    ) => await BatchUpdateAsync(items, updatedBy, new WarehouseProductBatchUpdateOptionsDto());

    public Task<WarehouseProductBatchUpdateResultDto> BatchUpdateAsync(
        List<UpdateItemDto> items,
        string? updatedBy,
        WarehouseProductBatchUpdateOptionsDto options
    ) =>
        ExecuteBatchUpdateAsync(
            items,
            updatedBy,
            options ?? new WarehouseProductBatchUpdateOptionsDto()
        );

    private Task<WarehouseProductBatchUpdateResultDto> ExecuteBatchUpdateAsync(
        List<UpdateItemDto> items,
        string? updatedBy,
        WarehouseProductBatchUpdateOptionsDto options
    )
    {
        var result = WarehouseProductBatchUpdateResultAssembler.CreateSuccess();
        if (items == null || items.Count == 0)
            return Task.FromResult(result);

        if (
            !WarehouseProductBatchUpdatePlan.TryCreate(
                options,
                updatedBy,
                out var plan,
                out var planError
            )
        )
        {
            return Task.FromResult(
                WarehouseProductBatchUpdateResultAssembler.Reject(
                    result,
                    planError,
                    items.Count
                )
            );
        }

        return _commandWriter.ExecuteAsync(items, options, plan!, result);
    }
}
