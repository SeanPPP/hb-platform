using System.Collections.Generic;
using System.Threading.Tasks;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// React 专用：仅限 Product 与 WarehouseProduct 的商品检测/更新/新建服务
    /// </summary>
    public interface IProductWarehouseReactService
    {
        Task<List<DetectionResultDto>> DetectAsync(List<DetectionItemDto> items);
        Task<BatchOperationResultDto> BatchUpdateAsync(List<UpdateItemDto> items);
        Task<BatchOperationResultDto> BatchUpdateAsync(List<UpdateItemDto> items, string? updatedBy);
        Task<BatchOperationResultDto> BatchCreateAsync(
            List<CreateItemDto> items,
            bool useTransaction = true
        );
        Task<BatchOperationResultDto> BatchCreateAsync(
            List<CreateItemDto> items,
            bool useTransaction,
            string? updatedBy
        );
        Task<BatchOperationResultDto> BatchCreateAsync(
            List<CreateItemDto> items,
            bool useTransaction,
            string? updatedBy,
            string auditSource,
            string? sourceReference,
            System.Guid? batchGuid
        );
        Task<BatchOperationResultDto> BatchCreateAsync(
            List<CreateItemDto> items,
            bool useTransaction,
            string? updatedBy,
            string auditSource,
            string? sourceReference,
            System.Guid? batchGuid,
            string? actorUserGuid
        );
        Task<ReactTableResponseDto<WarehouseProductReactListDto>> GetAntdTableDataAsync(
            ReactTableRequestDto request
        );
        Task<CreateSingleProductResponseDto> CreateSingleProductAsync(CreateSingleProductRequestDto request);
        Task<CreateSingleProductResponseDto> CreateSingleProductAsync(
            CreateSingleProductRequestDto request,
            string? updatedBy
        );
        Task<
            ReactTableResponseDto<DomesticProductNotInWarehouseDto>
        > GetDomesticProductsNotInWarehouseAsync(
            GetDomesticProductsNotInWarehouseRequestDto request
        );
        Task<ImportFromDomesticResponseDto> ImportFromDomesticAsync(ImportFromDomesticRequestDto request);
        Task<ImportFromDomesticResponseDto> ImportFromDomesticAsync(
            ImportFromDomesticRequestDto request,
            string? updatedBy
        );

        /// <summary>
        /// 仓库商品完整更新（六表 + 国内商品联动，同一 db 顺序查与更新）
        /// </summary>
        Task<WarehouseProductFullUpdateResultDto> FullUpdateAsync(
            string productCode,
            WarehouseProductFullUpdateDto dto
        );
        Task<WarehouseProductFullUpdateResultDto> FullUpdateAsync(
            string productCode,
            WarehouseProductFullUpdateDto dto,
            string? updatedBy
        );

        /// <summary>
        /// 仓库商品窄列 PATCH：一次只更新一个非负字段，事务内窄列更新并记录当前操作人。
        /// Product 或 WarehouseProduct 不存在时返回 null。
        /// </summary>
        Task<WarehouseProductPatchResultDto?> PatchAsync(
            string productCode,
            WarehouseProductPatchDto dto
        );
        Task<WarehouseProductPatchResultDto?> PatchAsync(
            string productCode,
            WarehouseProductPatchDto dto,
            string? updatedBy
        );

        /// <summary>
        /// 获取商品条码对应套装价/进货价列表（商品类型≠0 时编辑弹窗用）
        /// </summary>
        Task<List<BarcodePriceItemDto>> GetBarcodePricesAsync(string productCode);

        Task<BatchToggleWarehouseProductsActiveResultDto> BatchToggleActiveAsync(
            BatchToggleWarehouseProductsActiveRequestDto request
        );
        Task<BatchToggleWarehouseProductsActiveResultDto> BatchToggleActiveAsync(
            BatchToggleWarehouseProductsActiveRequestDto request,
            string? updatedBy
        );

        Task<
            ReactTableResponseDto<NonHotbargainProductNotInWarehouseDto>
        > GetNonHotbargainProductsNotInWarehouseAsync(
            GetNonHotbargainProductsNotInWarehouseRequestDto request
        );
        Task<ImportFromDomesticResponseDto> ImportNonHotbargainProductsAsync(
            ImportNonHotbargainRequestDto request
        );
        Task<ImportFromDomesticResponseDto> ImportNonHotbargainProductsAsync(
            ImportNonHotbargainRequestDto request,
            string? updatedBy
        );

        /// <summary>
        /// 从 HQ 商品库存表全量同步到本地仓库商品表
        /// </summary>
        Task<SyncResult> SyncFromHqAsync();
        Task<SyncResult> SyncFromHqAsync(string? actorUserGuid, string? actorName);
        Task<List<WarehouseMobileProductDto>> LookupMobileProductsAsync(string keyword);
        Task<WarehouseMobileProductDto?> GetMobileProductAsync(string productCode);
        Task<WarehouseMobileProductDto?> PatchMobileProductAsync(
            string productCode,
            WarehouseMobileProductPatchDto dto
        );
        Task<WarehouseMobileProductDto?> PatchMobileProductAsync(
            string productCode,
            WarehouseMobileProductPatchDto dto,
            string? updatedBy
        );
        Task<WarehouseMobileProductDto?> SetMobileProductLocationAsync(
            string productCode,
            string? locationGuid
        );
        Task<WarehouseProductLabelPrintDto?> GetMobileProductPrintPayloadAsync(string productCode);
        Task<WarehouseLocationLabelPrintDto?> GetMobileLocationPrintPayloadAsync(string productCode);
    }
}
