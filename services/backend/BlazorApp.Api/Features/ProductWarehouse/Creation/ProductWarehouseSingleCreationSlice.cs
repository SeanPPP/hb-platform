using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.ProductWarehouse;

/// <summary>
/// 单商品创建 Handler：只做纯规则校验、计划创建与命令编排。
/// </summary>
internal sealed class ProductWarehouseSingleCreationSlice : IProductWarehouseSingleCreationSlice
{
    private const string SystemUpdatedBy = "System";
    private readonly WarehouseProductSingleCreationCommandWriter _commandWriter;

    internal ProductWarehouseSingleCreationSlice(ProductWarehouseSliceContext context)
    {
        _commandWriter = new WarehouseProductSingleCreationCommandWriter(context);
    }

    public Task<CreateSingleProductResponseDto> CreateSingleProductAsync(
        CreateSingleProductRequestDto request
    ) => CreateSingleProductAsync(request, SystemUpdatedBy);

    public Task<CreateSingleProductResponseDto> CreateSingleProductAsync(
        CreateSingleProductRequestDto request,
        string? updatedBy
    ) => ExecuteSingleCreationAsync(request, updatedBy);

    private Task<CreateSingleProductResponseDto> ExecuteSingleCreationAsync(
        CreateSingleProductRequestDto request,
        string? updatedBy
    )
    {
        var validationError = WarehouseProductSingleCreationValidator.ValidateIdentityPrerequisites(
            request
        );
        if (validationError != null)
        {
            return Task.FromResult(
                WarehouseProductSingleCreationResultAssembler.Reject(validationError)
            );
        }

        var plan = WarehouseProductSingleCreationPlan.Create(updatedBy);
        return _commandWriter.ExecuteAsync(request, plan);
    }
}
