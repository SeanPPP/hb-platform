namespace BlazorApp.Shared.DTOs
{
    public class HolidayProductDto
    {
        public string GUID { get; set; } = string.Empty;
        public string? Sequence { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ItemNumber { get; set; } = string.Empty;
        public string SupplierCode { get; set; } = string.Empty;
        public string? ProductImage { get; set; }
        public int HolidayType { get; set; }
        public int Year { get; set; }
        public DateTime ImportDate { get; set; }
        public string? ProductName { get; set; }
        public int? Row { get; set; }
    }

    public class HolidayProductImportRequestDto
    {
        public string SupplierCode { get; set; } = string.Empty;
        public int HolidayType { get; set; }
        public int Year { get; set; }
        public List<HolidayProductImportItemDto> Products { get; set; } = new();
    }

    public class HolidayProductImportItemDto
    {
        public string? Sequence { get; set; }
        public string ItemNumber { get; set; } = string.Empty;
        public string? ProductImage { get; set; }
        public int? Row { get; set; }
    }

    public class HolidayProductAnalysisRequestDto
    {
        public string? SupplierCode { get; set; }
        public int? HolidayType { get; set; }
        public int? Year { get; set; }
        public List<string>? StoreCodes { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public class HolidayProductAnalysisResponseDto
    {
        public List<HolidayProductAnalysisItemDto> Items { get; set; } = new();
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string HolidayTypeName { get; set; } = string.Empty;
        public int TotalCount { get; set; }
    }

    public class HolidayProductAnalysisItemDto
    {
        public string? Sequence { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ItemNumber { get; set; } = string.Empty;
        public string? ProductImage { get; set; }
        public string? ProductName { get; set; }

        public int TotalPurchaseQuantity { get; set; }
        public int TotalSalesQuantity { get; set; }
        public decimal TotalSalesAmount { get; set; }
        public decimal TotalOriginalAmount { get; set; }
        public decimal TotalDiscountAmount { get; set; }
        public decimal AverageDiscountRate { get; set; }
        public int TotalDiscountQuantity { get; set; }

        public List<BranchDetailDto> BranchDetails { get; set; } = new();
    }

    public class BranchDetailDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public string StoreName { get; set; } = string.Empty;
        public int PurchaseQuantity { get; set; }
        public int SalesQuantity { get; set; }
        public decimal SalesAmount { get; set; }
        public decimal OriginalAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal DiscountRate { get; set; }
        public int DiscountQuantity { get; set; }
    }

    public class WeeklySalesChartDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public string? ProductName { get; set; }
        public List<WeeklySalesDataDto> WeeklyData { get; set; } = new();
    }

    public class WeeklySalesDataDto
    {
        public int WeekNumber { get; set; }
        public string WeekLabel { get; set; } = string.Empty;
        public DateTime WeekStartDate { get; set; }
        public DateTime WeekEndDate { get; set; }
        public int TotalQuantity { get; set; }
        public decimal TotalAmount { get; set; }
        public List<BranchWeeklySalesDto> BranchData { get; set; } = new();
    }

    public class BranchWeeklySalesDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public string StoreName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Amount { get; set; }
    }

    public enum HolidayType
    {
        Christmas = 0,
        Halloween = 1,
        Custom = 2,
        Easter = 3,
        Diaries = 4
    }

    public static class HolidayTypeExtensions
    {
        public static string GetName(this HolidayType type)
        {
            return type switch
            {
                HolidayType.Christmas => "圣诞节",
                HolidayType.Halloween => "万圣节",
                HolidayType.Custom => "自定义",
                HolidayType.Easter => "复活节",
                HolidayType.Diaries => "Diaries",
                _ => "未知"
            };
        }

        public static (DateTime StartDate, DateTime EndDate) GetDateRange(this HolidayType type, int year)
        {
            return type switch
            {
                HolidayType.Christmas => (new DateTime(year, 9, 1), new DateTime(year, 12, 25)),
                HolidayType.Halloween => (new DateTime(year, 9, 1), new DateTime(year, 11, 1)),
                HolidayType.Easter => (new DateTime(year, 3, 1), new DateTime(year, 4, 30)),
                HolidayType.Diaries => (new DateTime(year, 1, 1), new DateTime(year, 12, 31)),
                _ => (new DateTime(year, 1, 1), new DateTime(year, 12, 31))
            };
        }
    }

    public class HolidayProductListRequestDto
    {
        public string? SupplierCode { get; set; }
        public int? HolidayType { get; set; }
        public int? Year { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public class HolidayProductListResponseDto
    {
        public List<HolidayProductDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
    }

    public class PurchaseDataRequestDto
    {
        public List<string> ProductCodes { get; set; } = new();
        public List<string>? StoreCodes { get; set; }
        public int Year { get; set; }
    }

    public class PurchaseDataResponseDto
    {
        public Dictionary<string, int> ProductPurchaseQuantity { get; set; } = new();
        public Dictionary<string, List<BranchPurchaseDto>> BranchPurchaseData { get; set; } = new();
    }

    public class BranchPurchaseDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public string StoreName { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    public class SalesDataRequestDto
    {
        public List<string> ProductCodes { get; set; } = new();
        public List<string>? StoreCodes { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class SalesDataResponseDto
    {
        public Dictionary<string, ProductSalesDataDto> ProductSalesData { get; set; } = new();
    }

    public class ProductSalesDataDto
    {
        public int TotalSalesQuantity { get; set; }
        public decimal TotalSalesAmount { get; set; }
        public decimal TotalOriginalAmount { get; set; }
        public decimal TotalDiscountAmount { get; set; }
        public decimal AverageDiscountRate { get; set; }
        public int TotalDiscountQuantity { get; set; }
        public List<BranchSalesDto> BranchSalesData { get; set; } = new();
    }

    public class BranchSalesDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public string StoreName { get; set; } = string.Empty;
        public int SalesQuantity { get; set; }
        public decimal SalesAmount { get; set; }
        public decimal OriginalAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal DiscountRate { get; set; }
        public int DiscountQuantity { get; set; }
    }

    public class HolidayProductAnalysisSimpleItemDto
    {
        public string? Sequence { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ItemNumber { get; set; } = string.Empty;
        public string? ProductImage { get; set; }
        public string? ProductName { get; set; }

        public int TotalPurchaseQuantity { get; set; }
        public int TotalSalesQuantity { get; set; }
        public decimal TotalSalesAmount { get; set; }
        public decimal TotalOriginalAmount { get; set; }
        public decimal TotalDiscountAmount { get; set; }
        public decimal AverageDiscountRate { get; set; }
        public int TotalDiscountQuantity { get; set; }
    }

    public class HolidayProductAnalysisSimpleResponseDto
    {
        public List<HolidayProductAnalysisSimpleItemDto> Items { get; set; } = new();
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string HolidayTypeName { get; set; } = string.Empty;
        public int TotalCount { get; set; }
    }

    public class ProductBranchDetailsRequestDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public List<string>? StoreCodes { get; set; }
    }

    public class ProductBranchDetailsResponseDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public List<BranchDetailDto> BranchDetails { get; set; } = new();
    }
}
