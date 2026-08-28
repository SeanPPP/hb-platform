using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.ProductWarehouse;

/// <summary>
/// 导入 Handler：维持旧接口，只负责领域计划校验与查询/命令委派。
/// </summary>
internal sealed class ProductWarehouseImportSlice : IProductWarehouseImportSlice
{
    private const string SystemUpdatedBy = "System";
    private readonly WarehouseProductImportQueryStore _queryStore;
    private readonly WarehouseProductDomesticImportCommandWriter _commandWriter;

    internal ProductWarehouseImportSlice(ProductWarehouseSliceContext context)
    {
        _queryStore = new WarehouseProductImportQueryStore(context);
        _commandWriter = new WarehouseProductDomesticImportCommandWriter(context);
    }

    public Task<ReactTableResponseDto<DomesticProductNotInWarehouseDto>> GetDomesticProductsNotInWarehouseAsync(
        GetDomesticProductsNotInWarehouseRequestDto request
    ) => _queryStore.GetDomesticProductsNotInWarehouseAsync(request);

    public Task<ImportFromDomesticResponseDto> ImportFromDomesticAsync(
        ImportFromDomesticRequestDto request
    ) => ImportFromDomesticAsync(request, SystemUpdatedBy);

    public Task<ImportFromDomesticResponseDto> ImportFromDomesticAsync(
        ImportFromDomesticRequestDto request,
        string? updatedBy
    ) => ExecuteDomesticImportAsync(request, updatedBy);

    private Task<ImportFromDomesticResponseDto> ExecuteDomesticImportAsync(
        ImportFromDomesticRequestDto request,
        string? updatedBy
    )
    {
        if (
            !WarehouseProductDomesticImportPlan.TryCreate(
                request,
                updatedBy,
                out var plan,
                out var planError
            )
        )
        {
            return Task.FromResult(
                WarehouseProductDomesticImportResultAssembler.Reject(planError)
            );
        }

        return _commandWriter.ExecuteDomesticImportAsync(request, plan!);
    }

    public Task<ReactTableResponseDto<NonHotbargainProductNotInWarehouseDto>> GetNonHotbargainProductsNotInWarehouseAsync(
        GetNonHotbargainProductsNotInWarehouseRequestDto request
    ) => _queryStore.GetNonHotbargainProductsNotInWarehouseAsync(request);

    public Task<ImportFromDomesticResponseDto> ImportNonHotbargainProductsAsync(
        ImportNonHotbargainRequestDto request
    ) => _commandWriter.ImportNonHotbargainProductsAsync(request);

    public Task<ImportFromDomesticResponseDto> ImportNonHotbargainProductsAsync(
        ImportNonHotbargainRequestDto request,
        string? updatedBy
    ) => _commandWriter.ImportNonHotbargainProductsAsync(request, updatedBy);
}
