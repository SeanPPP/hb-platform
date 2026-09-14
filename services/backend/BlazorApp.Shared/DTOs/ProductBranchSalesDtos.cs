namespace BlazorApp.Shared.DTOs;

/// <summary>移动端商品跨 POS 分店销量查询结果。</summary>
public sealed class ProductInsightBranchSalesDto
{
    public ProductInsightBranchSalesRangeDto Range { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
    public DateTime? SalesStatisticLastUpdatedAt { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public int TotalPosStoreCount { get; set; }
    public int IncludedStoreCount { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
    public List<ProductInsightBranchSalesRowDto> Rows { get; set; } = new();
}

public sealed class ProductInsightBranchSalesRangeDto
{
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
}

public sealed class ProductInsightBranchSalesRowDto
{
    public string StoreCode { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
}
