using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IWarehouseProductFlowAnalysisService
    {
        Task<ApiResponse<WarehouseProductFlowAnalysisOptionsDto>> GetOptionsAsync(
            WarehouseProductFlowAnalysisFilterDto filter,
            List<string>? branchCodes,
            bool forceRefresh = false
        );

        Task<
            ApiResponse<WarehouseProductFlowAnalysisPagedDto<WarehouseProductFlowCandidateDto>>
        > GetCandidatesAsync(WarehouseProductFlowCandidateRequest request);

        Task<ApiResponse<WarehouseProductFlowAnalysisSummaryDto>> GetSummaryAsync(
            WarehouseProductFlowAnalysisRequest request,
            List<string>? branchCodes
        );

        Task<ApiResponse<List<WarehouseProductFlowDailyDto>>> GetProductDailyAsync(
            WarehouseProductFlowAnalysisRequest request,
            List<string>? branchCodes
        );

        Task<ApiResponse<List<WarehouseProductFlowDailyDto>>> GetOrderShipmentDailyAsync(
            WarehouseProductFlowAnalysisRequest request,
            List<string>? branchCodes
        );

        Task<ApiResponse<List<WarehouseProductFlowDailyDto>>> GetSalesDailyAsync(
            WarehouseProductFlowAnalysisRequest request,
            List<string>? branchCodes
        );

        Task<ApiResponse<List<WarehouseProductFlowContainerDto>>> GetContainersAsync(
            WarehouseProductFlowAnalysisRequest request,
            List<string>? branchCodes
        );

        Task<ApiResponse<List<WarehouseProductFlowOrderDto>>> GetOrdersAsync(
            WarehouseProductFlowAnalysisRequest request,
            List<string>? branchCodes
        );

        Task<ApiResponse<List<WarehouseProductFlowShipmentDto>>> GetShipmentsAsync(
            WarehouseProductFlowAnalysisRequest request,
            List<string>? branchCodes
        );

        Task<ApiResponse<List<WarehouseProductFlowBranchDto>>> GetBranchesAsync(
            WarehouseProductFlowAnalysisRequest request,
            List<string>? branchCodes
        );

        Task<ApiResponse<List<WarehouseProductFlowDailyDto>>> GetBranchDailyAsync(
            WarehouseProductFlowAnalysisRequest request,
            List<string>? branchCodes
        );
    }
}
