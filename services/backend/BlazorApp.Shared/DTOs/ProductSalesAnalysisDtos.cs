namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 商品销量分析通用请求。
    /// 所有 POST 接口共用同一请求体，按端点取用相关字段。
    /// </summary>
    public class ProductSalesAnalysisRequest
    {
        public ProductSalesAnalysisFilterDto Filter { get; set; } = new();

        public ProductSalesAnalysisSelectionDto Selection { get; set; } = new();

        public ProductSalesAnalysisScopeDto? Scope { get; set; }

        /// <summary>branch-daily 接口下的分店代码</summary>
        public string? BranchCode { get; set; }

        public int PageNumber { get; set; } = 1;

        public int PageSize { get; set; } = 50;

        /// <summary>排序字段：quantity / salesAmount / productCode</summary>
        public string? SortBy { get; set; }

        /// <summary>排序方向：asc / desc</summary>
        public string? SortDirection { get; set; }
    }

    /// <summary>日期与商品/供应商过滤条件。</summary>
    public class ProductSalesAnalysisFilterDto
    {
        /// <summary>开始日期（包含首尾）</summary>
        public DateTime StartDate { get; set; }

        /// <summary>结束日期（包含首尾）</summary>
        public DateTime EndDate { get; set; }

        /// <summary>货号/条码/中英文名称关键字</summary>
        public string? Keyword { get; set; }

        public List<string>? AustralianSupplierCodes { get; set; }

        public List<string>? ChinaSupplierCodes { get; set; }
    }

    /// <summary>跨分页商品选择语义。</summary>
    public class ProductSalesAnalysisSelectionDto
    {
        public string Mode { get; set; } = "allFiltered";

        public List<string>? IncludedProductCodes { get; set; }

        public List<string>? ExcludedProductCodes { get; set; }
    }

    /// <summary>右栏/每日查询范围。</summary>
    public class ProductSalesAnalysisScopeDto
    {
        public string? Mode { get; set; }

        public string? ProductCode { get; set; }
    }

    /// <summary>
    /// 商品销量分析统一响应包裹。
    /// </summary>
    public class ProductSalesAnalysisResponse<T>
    {
        public string? StatisticStatus { get; set; }
        public string? StatisticMessage { get; set; }
        public DateTime? StatisticUpdatedAt { get; set; }
        public string? CacheVersion { get; set; }
        public T? Data { get; set; }
    }

    /// <summary>
    /// 分页数据。
    /// </summary>
    public class ProductSalesAnalysisPagedDto<T>
    {
        public List<T> Items { get; set; } = new();
        public int Total { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
    }

    /// <summary>销量/金额指标。</summary>
    public class ProductSalesAnalysisMetricsDto
    {
        public int Quantity { get; set; }
        public decimal SalesAmount { get; set; }
        public decimal? AverageUnitPrice { get; set; }
    }

    /// <summary>供应商 code/name 引用。</summary>
    public class ProductSalesSupplierRefDto
    {
        public string Code { get; set; } = string.Empty;
        public string? Name { get; set; }
    }

    /// <summary>商品销量行。</summary>
    public class ProductSalesProductRowDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
        public string? ProductName { get; set; }
        public string? EnglishName { get; set; }
        public string? ImageUrl { get; set; }
        public List<ProductSalesSupplierRefDto> AustralianSuppliers { get; set; } = new();
        public List<ProductSalesSupplierRefDto> ChinaSuppliers { get; set; } = new();
        public bool ChinaSupplierUnmapped { get; set; }
        public ProductSalesAnalysisMetricsDto Metrics { get; set; } = new();
    }

    /// <summary>商品每日销量。</summary>
    public class ProductSalesDailyDto
    {
        public DateTime Date { get; set; }
        public ProductSalesAnalysisMetricsDto Metrics { get; set; } = new();
    }

    /// <summary>分店销量汇总。</summary>
    public class ProductSalesBranchDto
    {
        public string BranchCode { get; set; } = string.Empty;
        public string? BranchName { get; set; }
        public ProductSalesAnalysisMetricsDto Metrics { get; set; } = new();
    }

    /// <summary>分店每日销量。</summary>
    public class ProductSalesBranchDailyDto
    {
        public DateTime Date { get; set; }
        public ProductSalesAnalysisMetricsDto Metrics { get; set; } = new();
    }

    /// <summary>供应商选项。</summary>
    public class ProductSalesAnalysisOptionsDto
    {
        public List<ProductSalesSupplierRefDto> AustralianSuppliers { get; set; } = new();
        public List<ProductSalesSupplierRefDto> ChinaSuppliers { get; set; } = new();
    }
}
