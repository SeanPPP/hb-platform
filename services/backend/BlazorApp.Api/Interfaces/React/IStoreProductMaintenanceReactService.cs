using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IStoreProductMaintenanceReactService
    {
        Task<ApiResponse<List<StoreProductLookupItemDto>>> LookupAsync(
            StoreProductLookupRequestDto request,
            List<string>? accessibleStoreCodes
        );

        Task<ApiResponse<StoreProductDetailDto>> GetDetailAsync(
            string productCode,
            string? storeCode,
            List<string>? accessibleStoreCodes,
            bool includeCodes = true
        );

        Task<ApiResponse<StoreProductDetailDto>> GetFastDetailAsync(
            string productCode,
            string? storeCode,
            List<string>? accessibleStoreCodes
        );

        Task<ApiResponse<StoreProductCodePageDto<StoreProductSetCodeDto>>> GetSetCodesAsync(
            string productCode,
            string? storeCode,
            int page,
            int pageSize,
            string? keyword,
            List<string>? accessibleStoreCodes
        );

        Task<ApiResponse<StoreProductCodePageDto<StoreProductMultiCodeDto>>> GetMultiCodesAsync(
            string productCode,
            string? storeCode,
            int page,
            int pageSize,
            string? keyword,
            List<string>? accessibleStoreCodes
        );

        Task<ApiResponse<EvaluateStoreProductAutoPricingResultDto>> EvaluateAutoPricingAsync(
            EvaluateStoreProductAutoPricingDto request,
            List<string>? accessibleStoreCodes
        );

        Task<ApiResponse<StoreProductTypeUpdateResultDto>> UpdateProductTypeAsync(
            string productCode,
            UpdateStoreProductTypeDto request,
            string updatedBy,
            List<string>? accessibleStoreCodes
        );

        Task<ApiResponse<StoreProductStorePriceDto>> UpdateStorePriceAsync(
            string uuid,
            UpdateStoreProductPriceDto request,
            string updatedBy,
            List<string>? accessibleStoreCodes
        );

        /// <summary>
        /// 与上面的重载行为一致，但允许调用方决定是否入队 HQ 投影。
        /// 目前仅"价格更新通知"使用：其 HQ 同步由配置开关控制，后期可能关闭。
        /// </summary>
        Task<ApiResponse<StoreProductStorePriceDto>> UpdateStorePriceAsync(
            string uuid,
            UpdateStoreProductPriceDto request,
            string updatedBy,
            List<string>? accessibleStoreCodes,
            bool enqueueHqProjection
        );

        Task<ApiResponse<SyncStoreProductWarehousePriceResultDto>> SyncWarehousePriceAsync(
            string uuid,
            SyncStoreProductWarehousePriceRequestDto request,
            string updatedBy,
            List<string>? accessibleStoreCodes
        );

        Task<ApiResponse<StoreProductMultiCodeDto>> UpdateMultiCodeAsync(
            string uuid,
            UpdateStoreProductMultiCodeDto request,
            string updatedBy,
            List<string>? accessibleStoreCodes
        );

        Task<ApiResponse<StoreProductSetCodeDto>> CreateSetCodeAsync(
            CreateStoreProductSetCodeDto request,
            string updatedBy,
            List<string>? accessibleStoreCodes
        );

        Task<ApiResponse<StoreProductSetCodeDto>> UpdateSetCodeAsync(
            string setCodeId,
            UpdateStoreProductSetCodeDto request,
            string updatedBy,
            List<string>? accessibleStoreCodes
        );

        Task<ApiResponse<SaveStoreProductSetCodeSnapshotResultDto>> SaveSetCodeSnapshotAsync(
            SaveStoreProductSetCodeSnapshotDto request,
            string updatedBy,
            List<string>? accessibleStoreCodes
        );

        Task<ApiResponse<DeleteStoreProductSetCodeResultDto>> DeleteSetCodeAsync(
            string setCodeId,
            string updatedBy,
            List<string>? accessibleStoreCodes
        );

        Task<ApiResponse<StoreProductClearancePriceDto>> UpsertClearancePriceAsync(
            string productCode,
            UpsertStoreProductClearancePriceDto request,
            string updatedBy,
            List<string>? accessibleStoreCodes
        );
    }
}
