namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 比较模式枚举
    /// </summary>
    public enum CompareMode
    {
        /// <summary>
        /// 按周比较
        /// </summary>
        ByWeek,

        /// <summary>
        /// 按日期比较
        /// </summary>
        ByDate
    }

    /// <summary>
    /// 日期范围 DTO
    /// 用于指定查询的主日期范围和对比日期范围
    /// </summary>
    public class DateRangeDto
    {
        /// <summary>
        /// 开始日期
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// 结束日期
        /// </summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// 对比开始日期
        /// </summary>
        public DateTime? CompareStartDate { get; set; }

        /// <summary>
        /// 对比结束日期
        /// </summary>
        public DateTime? CompareEndDate { get; set; }

        /// <summary>
        /// 比较模式
        /// </summary>
        public CompareMode CompareMode { get; set; } = CompareMode.ByDate;

        public override string ToString()
        {
            return $"{StartDate:yyyy-MM-dd}|{EndDate:yyyy-MM-dd}|{CompareStartDate:yyyy-MM-dd}|{CompareEndDate:yyyy-MM-dd}|{CompareMode}";
        }
    }

    /// <summary>
    /// 仪表板汇总数据 DTO
    /// 包含总销售额、总销量、订单数等关键指标及其增长率
    /// </summary>
    public class DashboardSummaryDto
    {
        /// <summary>
        /// 统计开始日期
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// 统计结束日期
        /// </summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// 总销售额
        /// </summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// 总销售数量
        /// </summary>
        public int TotalQuantity { get; set; }

        /// <summary>
        /// 订单总数
        /// </summary>
        public int OrderCount { get; set; }

        /// <summary>
        /// SKU 数量
        /// </summary>
        public int SkuCount { get; set; }

        /// <summary>
        /// 客户数量
        /// </summary>
        public int CustomerCount { get; set; }

        /// <summary>
        /// 平均客单价
        /// </summary>
        public decimal AverageOrderValue { get; set; }

        /// <summary>
        /// 对比期间总销售额
        /// </summary>
        public decimal? CompareTotalAmount { get; set; }

        /// <summary>
        /// 对比期间总销售数量
        /// </summary>
        public int? CompareTotalQuantity { get; set; }

        /// <summary>
        /// 对比期间订单总数
        /// </summary>
        public int? CompareOrderCount { get; set; }

        /// <summary>
        /// 对比期间 SKU 数量
        /// </summary>
        public int? CompareSkuCount { get; set; }

        /// <summary>
        /// 对比期间客户数量
        /// </summary>
        public int? CompareCustomerCount { get; set; }

        /// <summary>
        /// 对比期间平均客单价
        /// </summary>
        public decimal? CompareAvgOrderValue { get; set; }

        /// <summary>
        /// 销售额增长率
        /// </summary>
        public decimal? TotalAmountGrowth { get; set; }

        /// <summary>
        /// 销售数量增长率
        /// </summary>
        public decimal? TotalQuantityGrowth { get; set; }

        /// <summary>
        /// 订单数增长率
        /// </summary>
        public decimal? OrderCountGrowth { get; set; }

        /// <summary>
        /// SKU 数量增长率
        /// </summary>
        public decimal? SkuCountGrowth { get; set; }

        /// <summary>
        /// 客户数量增长率
        /// </summary>
        public decimal? CustomerCountGrowth { get; set; }

        /// <summary>
        /// 客单价增长率
        /// </summary>
        public decimal? AvgOrderValueGrowth { get; set; }
    }

    /// <summary>
    /// 分时销售数据 DTO
    /// 用于展示一天中各小时的销售情况
    /// </summary>
    public class HourlySalesDto
    {
        /// <summary>
        /// 日期
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// 小时 (0-23)
        /// </summary>
        public int Hour { get; set; }

        /// <summary>
        /// 分店代码
        /// </summary>
        public string? BranchCode { get; set; }

        /// <summary>
        /// 分店名称
        /// </summary>
        public string? BranchName { get; set; }

        /// <summary>
        /// 总销售额
        /// </summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// 总销售数量
        /// </summary>
        public int TotalQuantity { get; set; }

        /// <summary>
        /// 客户数量
        /// </summary>
        public int CustomerCount { get; set; }

        /// <summary>
        /// 平均客单价
        /// </summary>
        public decimal AverageOrderValue { get; set; }

        /// <summary>
        /// 对比期间总销售额
        /// </summary>
        public decimal? CompareTotalAmount { get; set; }

        /// <summary>
        /// 对比期间总销售数量
        /// </summary>
        public int? CompareTotalQuantity { get; set; }

        /// <summary>
        /// 对比期间客户数量
        /// </summary>
        public int? CompareCustomerCount { get; set; }

        /// <summary>
        /// 对比期间平均客单价
        /// </summary>
        public decimal? CompareAvgOrderValue { get; set; }
    }

    /// <summary>
    /// 分店销售排行 DTO
    /// 用于展示分店销售业绩排名
    /// </summary>
    public class StoreSalesRankDto
    {
        /// <summary>
        /// 统计开始日期
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// 统计结束日期
        /// </summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// 分店代码
        /// </summary>
        public string BranchCode { get; set; } = string.Empty;

        /// <summary>
        /// 分店名称
        /// </summary>
        public string BranchName { get; set; } = string.Empty;

        /// <summary>
        /// 总销售额
        /// </summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// 总销售数量
        /// </summary>
        public int TotalQuantity { get; set; }

        /// <summary>
        /// 订单总数
        /// </summary>
        public int OrderCount { get; set; }

        /// <summary>
        /// 客户数量
        /// </summary>
        public int CustomerCount { get; set; }

        /// <summary>
        /// 平均客单价
        /// </summary>
        public decimal AverageOrderValue { get; set; }

        /// <summary>
        /// 对比期间总销售额
        /// </summary>
        public decimal? CompareTotalAmount { get; set; }

        /// <summary>
        /// 对比期间总销售数量
        /// </summary>
        public int? CompareTotalQuantity { get; set; }

        /// <summary>
        /// 对比期间订单总数
        /// </summary>
        public int? CompareOrderCount { get; set; }

        /// <summary>
        /// 对比期间平均客单价
        /// </summary>
        public decimal? CompareAvgOrderValue { get; set; }

        /// <summary>
        /// 销售额增长率
        /// </summary>
        public decimal? TotalAmountGrowth { get; set; }

        /// <summary>
        /// 销售数量增长率
        /// </summary>
        public decimal? TotalQuantityGrowth { get; set; }

        /// <summary>
        /// 订单数增长率
        /// </summary>
        public decimal? OrderCountGrowth { get; set; }

        /// <summary>
        /// 客单价增长率
        /// </summary>
        public decimal? AvgOrderValueGrowth { get; set; }
    }

    /// <summary>
    /// 供应商销售排行 DTO
    /// 用于展示供应商销售业绩排名
    /// </summary>
    public class SupplierSalesRankDto
    {
        /// <summary>
        /// 统计开始日期
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// 统计结束日期
        /// </summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// 供应商代码
        /// </summary>
        public string SupplierCode { get; set; } = string.Empty;

        /// <summary>
        /// 供应商名称
        /// </summary>
        public string SupplierName { get; set; } = string.Empty;

        /// <summary>
        /// 总销售额
        /// </summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// 总销售数量
        /// </summary>
        public int TotalQuantity { get; set; }

        /// <summary>
        /// 销售该供应商产品的分店数量
        /// </summary>
        public int StoreCount { get; set; }

        /// <summary>
        /// 对比期间总销售额
        /// </summary>
        public decimal? CompareTotalAmount { get; set; }

        /// <summary>
        /// 销售额增长率
        /// </summary>
        public decimal? TotalAmountGrowth { get; set; }
    }

    /// <summary>
    /// 中国供应商销售排行 DTO
    /// 专门用于展示中国供应商的销售业绩排名
    /// </summary>
    public class ChinaSupplierSalesRankDto
    {
        /// <summary>
        /// 统计开始日期
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// 统计结束日期
        /// </summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// 供应商代码
        /// </summary>
        public string SupplierCode { get; set; } = string.Empty;

        /// <summary>
        /// 供应商名称
        /// </summary>
        public string SupplierName { get; set; } = string.Empty;

        /// <summary>
        /// 总销售额
        /// </summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// 总销售数量
        /// </summary>
        public int TotalQuantity { get; set; }

        /// <summary>
        /// 销售该供应商产品的分店数量
        /// </summary>
        public int StoreCount { get; set; }

        /// <summary>
        /// 对比期间总销售额
        /// </summary>
        public decimal? CompareTotalAmount { get; set; }

        /// <summary>
        /// 销售额增长率
        /// </summary>
        public decimal? TotalAmountGrowth { get; set; }
    }

    /// <summary>
    /// 供应商分店销售数据 DTO
    /// 展示特定供应商在各分店的销售情况
    /// </summary>
    public class SupplierStoreSalesDto
    {
        /// <summary>
        /// 统计开始日期
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// 统计结束日期
        /// </summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// 供应商代码
        /// </summary>
        public string SupplierCode { get; set; } = string.Empty;

        /// <summary>
        /// 供应商名称
        /// </summary>
        public string SupplierName { get; set; } = string.Empty;

        /// <summary>
        /// 分店代码
        /// </summary>
        public string BranchCode { get; set; } = string.Empty;

        /// <summary>
        /// 分店名称
        /// </summary>
        public string BranchName { get; set; } = string.Empty;

        /// <summary>
        /// 总销售额
        /// </summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// 总销售数量
        /// </summary>
        public int TotalQuantity { get; set; }

        /// <summary>
        /// 对比期间总销售额
        /// </summary>
        public decimal? CompareTotalAmount { get; set; }

        /// <summary>
        /// 销售额增长率
        /// </summary>
        public decimal? TotalAmountGrowth { get; set; }
    }

    /// <summary>
    /// 分店供应商销售数据 DTO
    /// 展示特定分店中各供应商的销售情况
    /// </summary>
    public class StoreSupplierSalesDto
    {
        /// <summary>
        /// 统计开始日期
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// 统计结束日期
        /// </summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// 分店代码
        /// </summary>
        public string BranchCode { get; set; } = string.Empty;

        /// <summary>
        /// 分店名称
        /// </summary>
        public string BranchName { get; set; } = string.Empty;

        /// <summary>
        /// 供应商代码
        /// </summary>
        public string SupplierCode { get; set; } = string.Empty;

        /// <summary>
        /// 供应商名称
        /// </summary>
        public string SupplierName { get; set; } = string.Empty;

        /// <summary>
        /// 总销售额
        /// </summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// 总销售数量
        /// </summary>
        public int TotalQuantity { get; set; }

        /// <summary>
        /// 对比期间总销售额
        /// </summary>
        public decimal? CompareTotalAmount { get; set; }

        /// <summary>
        /// 销售额增长率
        /// </summary>
        public decimal? TotalAmountGrowth { get; set; }
    }

    /// <summary>
    /// 销售商品明细 DTO
    /// </summary>
    public class SalesProductDetailDto
    {
        /// <summary>
        /// 商品代码
        /// </summary>
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 商品图片URL
        /// </summary>
        public string? ProductImage { get; set; }

        /// <summary>
        /// 商品名称
        /// </summary>
        public string? ProductName { get; set; }

        /// <summary>
        /// 销售数量
        /// </summary>
        public int Quantity { get; set; }

        /// <summary>
        /// 销售金额
        /// </summary>
        public decimal SalesAmount { get; set; }

        /// <summary>
        /// 单价
        /// </summary>
        public decimal UnitPrice { get; set; }

        /// <summary>
        /// 原价（如果有）
        /// </summary>
        public decimal? OriginalPrice { get; set; }
    }

    /// <summary>
    /// 分页的销售商品明细 DTO
    /// </summary>
    public class PagedSalesProductDetailDto
    {
        /// <summary>
        /// 销售商品明细列表
        /// </summary>
        public List<SalesProductDetailDto> Data { get; set; } = new();

        /// <summary>
        /// 总记录数
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// 当前页码
        /// </summary>
        public int PageIndex { get; set; }

        /// <summary>
        /// 每页记录数
        /// </summary>
        public int PageSize { get; set; }
    }

    /// <summary>
    /// 带折扣信息的销售商品明细 DTO
    /// </summary>
    public class SalesProductDetailWithDiscountDto
    {
        /// <summary>
        /// 商品代码
        /// </summary>
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 商品货号
        /// </summary>
        public string? ItemNumber { get; set; }

        /// <summary>
        /// 商品图片URL
        /// </summary>
        public string? ProductImage { get; set; }

        /// <summary>
        /// 商品名称
        /// </summary>
        public string? ProductName { get; set; }

        /// <summary>
        /// 销售总数量
        /// </summary>
        public int Quantity { get; set; }

        /// <summary>
        /// 折扣销售数量
        /// </summary>
        public int DiscountedQuantity { get; set; }

        /// <summary>
        /// 销售总金额
        /// </summary>
        public decimal SalesAmount { get; set; }

        /// <summary>
        /// 平均单价
        /// </summary>
        public decimal AverageUnitPrice { get; set; }

        /// <summary>
        /// 平均原价
        /// </summary>
        public decimal? AverageOriginalPrice { get; set; }

        /// <summary>
        /// 订单数量
        /// </summary>
        public int OrderCount { get; set; }

        /// <summary>
        /// 对比期销售总数量
        /// </summary>
        public int QuantityLY { get; set; }

        /// <summary>
        /// 对比期折扣销售数量
        /// </summary>
        public int DiscountedQuantityLY { get; set; }

        /// <summary>
        /// 对比期销售总金额
        /// </summary>
        public decimal SalesAmountLY { get; set; }

        /// <summary>
        /// 对比期平均单价
        /// </summary>
        public decimal AverageUnitPriceLY { get; set; }

        /// <summary>
        /// 对比期平均原价
        /// </summary>
        public decimal? AverageOriginalPriceLY { get; set; }

        /// <summary>
        /// 对比期订单数量
        /// </summary>
        public int OrderCountLY { get; set; }
    }

    /// <summary>
    /// 商品在各分店销售情况 DTO
    /// </summary>
    public class ProductBranchSalesDto
    {
        /// <summary>
        /// 分店代码
        /// </summary>
        public string BranchCode { get; set; } = string.Empty;

        /// <summary>
        /// 分店名称
        /// </summary>
        public string BranchName { get; set; } = string.Empty;

        /// <summary>
        /// 销售数量
        /// </summary>
        public int Quantity { get; set; }

        /// <summary>
        /// 折扣销售数量
        /// </summary>
        public int DiscountedQuantity { get; set; }
    }

    /// <summary>
    /// 中国供应商分店销售数据 DTO
    /// </summary>
    public class ChinaSupplierStoreSalesDto
    {
        /// <summary>
        /// 统计开始日期
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// 统计结束日期
        /// </summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// 供应商代码
        /// </summary>
        public string SupplierCode { get; set; } = string.Empty;

        /// <summary>
        /// 供应商名称
        /// </summary>
        public string SupplierName { get; set; } = string.Empty;

        /// <summary>
        /// 分店代码
        /// </summary>
        public string BranchCode { get; set; } = string.Empty;

        /// <summary>
        /// 分店名称
        /// </summary>
        public string BranchName { get; set; } = string.Empty;

        /// <summary>
        /// 总销售额
        /// </summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// 总销售数量
        /// </summary>
        public int TotalQuantity { get; set; }
    }

    /// <summary>
    /// 分页的带折扣信息销售商品明细 DTO
    /// </summary>
    public class PagedSalesProductDetailWithDiscountDto
    {
        /// <summary>
        /// 数据列表
        /// </summary>
        public List<SalesProductDetailWithDiscountDto> Data { get; set; } = new();

        /// <summary>
        /// 总记录数
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// 当前页码
        /// </summary>
        public int PageIndex { get; set; }

        /// <summary>
        /// 每页大小
        /// </summary>
        public int PageSize { get; set; }
    }

    /// <summary>
    /// 澳洲供应商分店销售明细 DTO
    /// </summary>
    public class AustralianSupplierStoreSalesDetailDto
    {
        /// <summary>
        /// 销售日期
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// 分店代码
        /// </summary>
        public string BranchCode { get; set; } = string.Empty;

        /// <summary>
        /// 供应商代码
        /// </summary>
        public string SupplierCode { get; set; } = string.Empty;

        /// <summary>
        /// 供应商名称
        /// </summary>
        public string? SupplierName { get; set; }

        /// <summary>
        /// 总销售额
        /// </summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// 总销售数量
        /// </summary>
        public int TotalQuantity { get; set; }

        /// <summary>
        /// 订单数量
        /// </summary>
        public int? OrderCount { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdateTime { get; set; }
    }

    /// <summary>
    /// 中国供应商分店销售明细 DTO
    /// </summary>
    public class ChinaSupplierStoreSalesDetailDto
    {
        /// <summary>
        /// 销售日期
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// 分店代码
        /// </summary>
        public string BranchCode { get; set; } = string.Empty;

        /// <summary>
        /// 供应商代码
        /// </summary>
        public string SupplierCode { get; set; } = string.Empty;

        /// <summary>
        /// 供应商名称
        /// </summary>
        public string? SupplierName { get; set; }

        /// <summary>
        /// 总销售额
        /// </summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// 总销售数量
        /// </summary>
        public int TotalQuantity { get; set; }

        /// <summary>
        /// 订单数量
        /// </summary>
        public int? OrderCount { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdateTime { get; set; }
    }

    /// <summary>
    /// Executive Dashboard 分店业绩 DTO
    /// 用于 Executive Sales Intelligence 页面
    /// </summary>
    public class ExecutiveBranchPerformanceDto
    {
        /// <summary>
        /// 排名
        /// </summary>
        public int Rank { get; set; }

        /// <summary>
        /// 分店代码
        /// </summary>
        public string BranchCode { get; set; } = string.Empty;

        /// <summary>
        /// 分店名称
        /// </summary>
        public string BranchName { get; set; } = string.Empty;

        /// <summary>
        /// 当前期销售额
        /// </summary>
        public decimal Revenue { get; set; }

        /// <summary>
        /// 去年同期销售额
        /// </summary>
        public decimal RevenueLY { get; set; }

        /// <summary>
        /// 当前期订单数
        /// </summary>
        public int OrderCount { get; set; }

        /// <summary>
        /// 去年同期订单数
        /// </summary>
        public int OrderCountLY { get; set; }

        /// <summary>
        /// 当前期平均订单价值
        /// </summary>
        public decimal Aov { get; set; }

        /// <summary>
        /// 去年同期平均订单价值
        /// </summary>
        public decimal AovLY { get; set; }
    }

    /// <summary>
    /// Executive Dashboard 每小时流量 DTO
    /// 用于 Executive Sales Intelligence 页面的每小时流量密度展示
    /// </summary>
    public class ExecutiveHourlyTrafficDto
    {
        /// <summary>
        /// 小时 (格式: "HH:mm")
        /// </summary>
        public string Hour { get; set; } = string.Empty;

        /// <summary>
        /// 分店代码
        /// </summary>
        public string BranchCode { get; set; } = string.Empty;

        /// <summary>
        /// 分店名称
        /// </summary>
        public string BranchName { get; set; } = string.Empty;

        /// <summary>
        /// 当前期销售额
        /// </summary>
        public decimal Revenue { get; set; }

        /// <summary>
        /// 去年同期销售额
        /// </summary>
        public decimal RevenueLY { get; set; }

        /// <summary>
        /// 占高峰期的百分比 (0-100)
        /// </summary>
        public int Percentage { get; set; }

        /// <summary>
        /// 是否为高峰时段
        /// </summary>
        public bool IsPeak { get; set; }
    }

    /// <summary>
    /// 周业绩层级数据 DTO
    /// 用于 Weekly Performance Hierarchy 组件
    /// 支持周、分店、日期三层嵌套结构
    /// </summary>
    public class WeeklyPerformanceHierarchyDto
    {
        /// <summary>
        /// 唯一标识键
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// 层级类型: week | branch | date
        /// </summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>
        /// 层级显示名称 (如: 2023-W42, Nexus Downtown, 2023-10-20)
        /// </summary>
        public string Hierarchy { get; set; } = string.Empty;

        /// <summary>
        /// 当前期销售额
        /// </summary>
        public decimal Revenue { get; set; }

        /// <summary>
        /// 去年同期销售额
        /// </summary>
        public decimal RevenueLY { get; set; }

        /// <summary>
        /// 当前期订单数
        /// </summary>
        public int Orders { get; set; }

        /// <summary>
        /// 去年同期订单数
        /// </summary>
        public int OrdersLY { get; set; }

        /// <summary>
        /// 当前期平均订单价值
        /// </summary>
        public decimal Aov { get; set; }

        /// <summary>
        /// 去年同期平均订单价值
        /// </summary>
        public decimal AovLY { get; set; }

        /// <summary>
        /// 同比变化率 (%)
        /// </summary>
        public decimal? YoYChange { get; set; }

        /// <summary>
        /// 子节点列表
        /// </summary>
        public List<WeeklyPerformanceHierarchyDto>? Children { get; set; }
    }

    /// <summary>
    /// 分店销售聚合数据 DTO
    /// 用于 SalesDetailAnalysisV2 门店分布卡，直接返回分店级别的聚合数据
    /// 避免前端接收大量明细数据后再进行聚合
    /// </summary>
    public class BranchSalesAggregateDto
    {
        /// <summary>
        /// 分店代码
        /// </summary>
        public string BranchCode { get; set; } = string.Empty;

        /// <summary>
        /// 分店名称
        /// </summary>
        public string BranchName { get; set; } = string.Empty;

        /// <summary>
        /// 当前期总销售额
        /// </summary>
        public decimal TotalRevenue { get; set; }

        /// <summary>
        /// 去年同期总销售额
        /// </summary>
        public decimal TotalRevenueLY { get; set; }

        /// <summary>
        /// 当前期总销售数量
        /// </summary>
        public int TotalQuantity { get; set; }

        /// <summary>
        /// 去年同期总销售数量
        /// </summary>
        public int TotalQuantityLY { get; set; }

        /// <summary>
        /// 当前期订单数
        /// </summary>
        public int OrderCount { get; set; }

        /// <summary>
        /// 去年同期订单数
        /// </summary>
        public int OrderCountLY { get; set; }

        /// <summary>
        /// 当前期 Hot Bargain 供应商 (#200) 销售额
        /// </summary>
        public decimal HbRevenue { get; set; }

        /// <summary>
        /// 去年同期 Hot Bargain 供应商 (#200) 销售额
        /// </summary>
        public decimal HbRevenueLY { get; set; }
    }

    /// <summary>
    /// Best Seller 商品 DTO
    /// </summary>
    public class BestSellerProductDto
    {
        /// <summary>
        /// 商品代码
        /// </summary>
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 商品货号
        /// </summary>
        public string? ItemNumber { get; set; }

        /// <summary>
        /// 商品条码
        /// </summary>
        public string? Barcode { get; set; }

        /// <summary>
        /// 商品图片URL
        /// </summary>
        public string? ProductImage { get; set; }

        /// <summary>
        /// 商品名称
        /// </summary>
        public string? ProductName { get; set; }

        /// <summary>
        /// 销售数量
        /// </summary>
        public int Quantity { get; set; }

        /// <summary>
        /// 销售金额
        /// </summary>
        public decimal SalesAmount { get; set; }

        /// <summary>
        /// 排名
        /// </summary>
        public int Rank { get; set; }

        /// <summary>
        /// 仓库库存表上下架状态
        /// </summary>
        public bool? IsActive { get; set; }

        /// <summary>
        /// 仓库库存表最小订货量
        /// </summary>
        public int? MinOrderQuantity { get; set; }

        /// <summary>
        /// 参与当前统计范围的分店数量
        /// </summary>
        public int BranchSalesCount { get; set; }

        /// <summary>
        /// 当前统计范围内的分店销量明细
        /// </summary>
        public List<BestSellerBranchSaleDto> BranchSales { get; set; } = new();
    }

    /// <summary>
    /// Best Seller 分店销量 DTO
    /// </summary>
    public class BestSellerBranchSaleDto
    {
        /// <summary>
        /// 分店代码
        /// </summary>
        public string BranchCode { get; set; } = string.Empty;

        /// <summary>
        /// 分店名称
        /// </summary>
        public string? BranchName { get; set; }

        /// <summary>
        /// 销售数量
        /// </summary>
        public int Quantity { get; set; }
    }

    /// <summary>
    /// Best Sellers 响应 DTO
    /// </summary>
    public class BestSellerResponseDto
    {
        /// <summary>
        /// 商品列表
        /// </summary>
        public List<BestSellerProductDto> Products { get; set; } = new();

        /// <summary>
        /// 总记录数
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// 当前页码
        /// </summary>
        public int PageIndex { get; set; }

        /// <summary>
        /// 每页记录数
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// 总页数
        /// </summary>
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 0;
    }
}
