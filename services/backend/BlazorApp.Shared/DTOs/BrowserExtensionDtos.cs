using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs;

public static class BrowserExtensionMatchStatuses
{
    public const string Matched = "matched";
    public const string NoPurchase = "no-purchase";
    public const string Unmatched = "unmatched";
}

public sealed class BrowserExtensionReleaseNotesDto
{
    public string Zh { get; set; } = string.Empty;
    public string En { get; set; } = string.Empty;
}

public sealed class BrowserExtensionReleaseDto
{
    public string LatestVersion { get; set; } = string.Empty;
    public string MinimumVersion { get; set; } = string.Empty;
    public string ChromeStoreUrl { get; set; } = string.Empty;
    public string EdgeStoreUrl { get; set; } = string.Empty;
    public string SafariStoreUrl { get; set; } = string.Empty;
    public BrowserExtensionReleaseNotesDto ReleaseNotes { get; set; } = new();
}

public sealed class BrowserExtensionItemNumberRuleDto
{
    public string Source { get; set; } = "attribute";
    public string? Selector { get; set; }
    public string? Attribute { get; set; }
    public List<string> Transforms { get; set; } = new();
}

public sealed class BrowserExtensionSupplierProfileDto
{
    public string SupplierCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public List<string> Origins { get; set; } = new();
    public List<string> ListPagePatterns { get; set; } = new();
    public string CardSelector { get; set; } = string.Empty;
    public BrowserExtensionItemNumberRuleDto ItemNumber { get; set; } = new();
    public string MountSelector { get; set; } = string.Empty;
    public string MountPosition { get; set; } = "afterend";
}

public sealed class BrowserExtensionSupplierProfilesDto
{
    public string ConfigVersion { get; set; } = string.Empty;
    public List<BrowserExtensionSupplierProfileDto> Profiles { get; set; } = new();
}

public sealed class BrowserExtensionProductSummaryBatchRequestDto
{
    [Required]
    [StringLength(50)]
    public string StoreCode { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string SupplierCode { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    [MaxLength(100)]
    public List<string> ItemNumbers { get; set; } = new();
}

public sealed class BrowserExtensionProductSummaryDto
{
    public string ItemNumber { get; set; } = string.Empty;
    public string MatchStatus { get; set; } = BrowserExtensionMatchStatuses.Unmatched;
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public DateOnly? LatestPurchaseDate { get; set; }
    public decimal? LatestPurchaseQuantity { get; set; }
    public decimal SalesSinceLatestPurchase { get; set; }
    public DateTime? SalesStatisticLastUpdate { get; set; }
}

public sealed class BrowserExtensionProductSummaryBatchDto
{
    public string StoreCode { get; set; } = string.Empty;
    public string SupplierCode { get; set; } = string.Empty;
    public DateOnly EndDate { get; set; }
    public List<BrowserExtensionProductSummaryDto> Items { get; set; } = new();
}

public sealed class BrowserExtensionPurchaseCyclesRequestDto
{
    [Required]
    [StringLength(50)]
    public string StoreCode { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string SupplierCode { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string ItemNumber { get; set; } = string.Empty;
}

public sealed class BrowserExtensionPurchaseCycleDto
{
    public DateOnly PurchaseDate { get; set; }
    public List<string> InvoiceNumbers { get; set; } = new();
    public decimal PurchaseQuantity { get; set; }
    public decimal? AveragePurchasePrice { get; set; }
    public DateOnly SalesStartDate { get; set; }
    public DateOnly SalesEndDate { get; set; }
    public decimal SalesQuantity { get; set; }
    public decimal? AverageSalePrice { get; set; }
}

public sealed class BrowserExtensionPurchaseCyclesDto
{
    public string StoreCode { get; set; } = string.Empty;
    public string SupplierCode { get; set; } = string.Empty;
    public string ItemNumber { get; set; } = string.Empty;
    public string MatchStatus { get; set; } = BrowserExtensionMatchStatuses.Unmatched;
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public DateOnly EndDate { get; set; }
    public DateTime? SalesStatisticLastUpdate { get; set; }
    public DateOnly? LatestPurchaseDate { get; set; }
    public decimal? LatestPurchaseQuantity { get; set; }
    public decimal SalesSinceLatestPurchase { get; set; }
    public List<BrowserExtensionPurchaseCycleDto> Cycles { get; set; } = new();
}

public sealed class BrowserExtensionStoreOptionDto
{
    public string StoreCode { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
}

public sealed class BrowserExtensionStoreOptionsDto
{
    public List<BrowserExtensionStoreOptionDto> Stores { get; set; } = new();
}

public sealed class BrowserExtensionSupplierTopSalesRequestDto
{
    [Required]
    [StringLength(50)]
    public string SupplierCode { get; set; } = string.Empty;

    [Range(1, 90)]
    public int Days { get; set; } = 60;
}

public sealed class BrowserExtensionSupplierTopSalesItemDto
{
    public int Rank { get; set; }
    public string ItemNumber { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal SalesQuantity { get; set; }
    public decimal? AverageSellingPrice { get; set; }
}

public sealed class BrowserExtensionSupplierTopSalesDto
{
    public string SupplierCode { get; set; } = string.Empty;
    public int Days { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int EnabledStoreCount { get; set; }
    public int TotalProductCount { get; set; }
    public DateTime? SalesStatisticLastUpdate { get; set; }
    public List<BrowserExtensionSupplierTopSalesItemDto> Items { get; set; } = new();
}
