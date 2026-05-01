using System.Collections.Generic;
using System.Threading.Tasks;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// React 专用：仅限 Product 与 WarehouseProduct 的商品检测/更新/新建服务
    /// </summary>
    public interface IProductWarehouseReactService
    {
        Task<List<DetectionResultDto>> DetectAsync(List<DetectionItemDto> items);
        Task<BatchOperationResultDto> BatchUpdateAsync(List<UpdateItemDto> items);
        Task<BatchOperationResultDto> BatchCreateAsync(List<CreateItemDto> items);
        Task<ReactTableResponseDto<WarehouseProductReactListDto>> GetAntdTableDataAsync(
            ReactTableRequestDto request
        );
        Task<CreateSingleProductResponseDto> CreateSingleProductAsync(
            CreateSingleProductRequestDto request
        );
        Task<
            ReactTableResponseDto<DomesticProductNotInWarehouseDto>
        > GetDomesticProductsNotInWarehouseAsync(
            GetDomesticProductsNotInWarehouseRequestDto request
        );
        Task<ImportFromDomesticResponseDto> ImportFromDomesticAsync(
            ImportFromDomesticRequestDto request
        );

        /// <summary>
        /// 仓库商品完整更新（六表 + 国内商品联动，同一 db 顺序查与更新）
        /// </summary>
        Task<WarehouseProductFullUpdateResultDto> FullUpdateAsync(
            string productCode,
            WarehouseProductFullUpdateDto dto
        );

        /// <summary>
        /// 获取商品条码对应套装价/进货价列表（商品类型≠0 时编辑弹窗用）
        /// </summary>
        Task<List<BarcodePriceItemDto>> GetBarcodePricesAsync(string productCode);

        Task<
            ReactTableResponseDto<NonHotbargainProductNotInWarehouseDto>
        > GetNonHotbargainProductsNotInWarehouseAsync(
            GetNonHotbargainProductsNotInWarehouseRequestDto request
        );
        Task<ImportFromDomesticResponseDto> ImportNonHotbargainProductsAsync(
            ImportNonHotbargainRequestDto request
        );

        /// <summary>
        /// 从HQ商品库存表同步到本地仓库商品表
        /// 同步规则：没有的添加，多的删除，有变化的更新
        /// </summary>
        Task<WarehouseProductSyncResultDto> SyncFromHqAsync();
    }
}
