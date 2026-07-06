using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface ISalesDashboardReactService
    {
        Task<DashboardSummaryDto?> GetDashboardSummaryAsync(DateRangeDto dateRange);

        Task<List<HourlySalesDto>> GetHourlySalesAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null
        );

        Task<List<StoreSalesRankDto>> GetStoreSalesRankAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null,
            int topN = 50
        );

        Task<List<SupplierSalesRankDto>> GetSupplierSalesRankAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null,
            int topN = 20,
            string? supplierCode = null
        );

        Task<List<ChinaSupplierSalesRankDto>> GetChinaSupplierSalesRankAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null,
            int topN = 20,
            string? supplierCode = null
        );

        Task<List<SupplierStoreSalesDto>> GetSupplierStoreSalesAsync(
            DateRangeDto dateRange,
            List<string> supplierCodes,
            List<string>? branchCodes = null
        );

        Task<List<StoreSupplierSalesDto>> GetStoreSupplierSalesAsync(
            DateRangeDto dateRange,
            List<string> branchCodes,
            int topN = 20
        );

        Task<PagedSalesProductDetailDto> GetSalesProductDetailsAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null,
            List<string>? supplierCodes = null,
            int pageIndex = 1,
            int pageSize = 100
        );

        Task<PagedSalesProductDetailWithDiscountDto> GetEnhancedSalesProductDetailsAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null,
            List<string>? localSupplierCodes = null,
            List<string>? chinaSupplierCodes = null,
            int pageIndex = 1,
            int pageSize = 100,
            string? productSearch = null
        );

        Task<List<ProductBranchSalesDto>> GetProductSalesByAllBranchesAsync(
            DateRangeDto dateRange,
            string productCode,
            List<string>? branchCodes = null
        );

        Task<List<ChinaSupplierStoreSalesDto>> GetChinaSupplierStoreSalesAsync(
            DateRangeDto dateRange,
            List<string> supplierCodes,
            List<string>? branchCodes = null
        );

        Task<
            List<AustralianSupplierStoreSalesDetailDto>
        > GetAustralianSupplierStoreSalesDetailsAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null,
            List<string>? supplierCodes = null
        );

        Task<List<ChinaSupplierStoreSalesDetailDto>> GetChinaSupplierStoreSalesDetailsAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null,
            List<string>? supplierCodes = null
        );

        /// <summary>
        /// 获取 Executive Dashboard 分店业绩排名
        /// 用于 Executive Sales Intelligence 页面
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="topN">返回前N条记录；为空返回全部</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <returns>分店业绩排名列表</returns>
        Task<List<ExecutiveBranchPerformanceDto>> GetExecutiveBranchPerformanceAsync(
            DateRangeDto dateRange,
            int? topN = null,
            List<string>? branchCodes = null
        );

        /// <summary>
        /// 获取 Executive Dashboard 每小时流量密度
        /// 用于 Executive Sales Intelligence 页面的每小时流量展示
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <returns>每小时流量密度列表</returns>
        Task<List<ExecutiveHourlyTrafficDto>> GetExecutiveHourlyTrafficAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null
        );

        /// <summary>
        /// 获取分店每日营业额
        /// 用于移动端周/月营业额下钻明细
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <returns>分店每日营业额列表</returns>
        Task<List<BranchDailyPerformanceDto>> GetBranchDailyPerformanceAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null
        );

        /// <summary>
        /// 获取周业绩层级数据
        /// 用于 Executive Sales Intelligence 页面的 Weekly Performance Hierarchy 组件
        /// 返回周→分店→日期三层嵌套结构
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <returns>周业绩层级数据列表</returns>
        Task<List<WeeklyPerformanceHierarchyDto>> GetWeeklyPerformanceHierarchyAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null
        );

        /// <summary>
        /// 获取分店销售聚合数据
        /// 用于 SalesDetailAnalysisV2 门店分布卡，直接返回分店级别的聚合数据
        /// 避免前端接收大量明细数据后再进行聚合
        /// </summary>
        /// <param name="dateRange">日期范围（当前期）</param>
        /// <param name="compareDateRange">日期范围（去年同期，可选）</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <param name="supplierCodes">供应商代码列表（可选，用于过滤）</param>
        /// <returns>分店销售聚合数据列表</returns>
        Task<List<BranchSalesAggregateDto>> GetBranchSalesAggregateAsync(
            DateRangeDto dateRange,
            DateRangeDto? compareDateRange = null,
            List<string>? branchCodes = null,
            List<string>? supplierCodes = null
        );

        /// <summary>
        /// 获取 Best Sellers 商品列表（销量排名）
        /// 用于前端 StoreFront 的 Best Sellers 页面
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="branchCodes">分店代码列表（可选，为空则查所有可访问分店）</param>
        /// <param name="pageIndex">页码</param>
        /// <param name="pageSize">每页记录数</param>
        /// <returns>Best Sellers 分页响应</returns>
        Task<BestSellerResponseDto> GetBestSellersAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null,
            int pageIndex = 1,
            int pageSize = 50
        );
    }
}
