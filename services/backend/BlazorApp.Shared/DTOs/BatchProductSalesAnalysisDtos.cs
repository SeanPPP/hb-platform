namespace BlazorApp.Shared.DTOs;

/// <summary>批量货号销量分析的日期和门店范围。</summary>
public class BatchProductSalesScopeDto
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<string>? StoreCodes { get; set; }
}

public sealed class BatchProductSalesQueryRequestDto : BatchProductSalesScopeDto
{
    public List<string>? ItemNumbers { get; set; }
}

public sealed class BatchProductSalesDetailRequestDto : BatchProductSalesScopeDto
{
    public string? ProductCode { get; set; }
}

public sealed class BatchProductSalesStoreDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class BatchProductSalesOptionsDto
{
    public List<BatchProductSalesStoreDto> Stores { get; set; } = [];
    public int MaxItemNumbers { get; set; } = 500;
    public int MaxDays { get; set; } = 366;
}

public sealed class BatchProductSalesMatchDto
{
    public string ItemNumber { get; set; } = string.Empty;
    /// <summary>matched / ambiguous / notFound</summary>
    public string Status { get; set; } = "notFound";
    public List<string> ProductCodes { get; set; } = [];
}

public class BatchProductSalesProductDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ItemNumber { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? EnglishName { get; set; }
    public string? Barcode { get; set; }
    public string? ImageUrl { get; set; }
}

public sealed class BatchProductSalesProductSummaryDto : BatchProductSalesProductDto
{
    public decimal Quantity { get; set; }
}

/// <summary>
/// 所有数量均为有符号净销量；returnQuantity 仅用于展示退货绝对数量，不能再次扣减。
/// </summary>
public sealed class BatchProductSalesMetricsDto
{
    public decimal Quantity { get; set; }
    public decimal RegularQuantity { get; set; }
    public decimal DiscountQuantity { get; set; }
    public decimal UnknownQuantity { get; set; }
    public decimal ReturnQuantity { get; set; }
    public decimal SalesAmount { get; set; }
    /// <summary>complete / partial / unknown / pending（待计算，分类数量不得显示为真实值）</summary>
    public string DiscountStatus { get; set; } = "unknown";
    public decimal? OriginalPriceMin { get; set; }
    public decimal? OriginalPriceMax { get; set; }
    public decimal? DiscountPriceMin { get; set; }
    public decimal? DiscountPriceMax { get; set; }
}

public sealed class BatchProductSalesDailyDto
{
    public DateTime Date { get; set; }
    public BatchProductSalesMetricsDto Metrics { get; set; } = new();
}

public sealed class BatchProductSalesBranchDto
{
    public string BranchCode { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public BatchProductSalesMetricsDto Metrics { get; set; } = new();
    public List<BatchProductSalesDailyDto> Daily { get; set; } = [];
}

public sealed class BatchProductSalesQueryResultDto : BatchProductSalesScopeDto
{
    public List<BatchProductSalesMatchDto> Matches { get; set; } = [];
    public List<BatchProductSalesProductSummaryDto> Products { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public string? StatisticStatus { get; set; }
    public DateTime? StatisticUpdatedAt { get; set; }
}

public sealed class BatchProductSalesDetailDto : BatchProductSalesScopeDto
{
    public string StatisticStatus { get; set; } = "Fresh";
    public DateTime? StatisticUpdatedAt { get; set; }
    public string DiscountStatisticStatus { get; set; } = "Fresh";
    public DateTime? DiscountUpdatedAt { get; set; }
    public BatchProductSalesProductDto Product { get; set; } = new();
    public BatchProductSalesMetricsDto Metrics { get; set; } = new();
    public List<BatchProductSalesDailyDto> Daily { get; set; } = [];
    public List<BatchProductSalesBranchDto> Branches { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
