namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 移动端离线商品目录的行 DTO：一售卖码一行，商品级字段在每行冗余。
    /// 字段顺序是校验和协议的一部分（见 OfflineCatalogChecksum 与移动端 offline-catalog-checksum.ts），
    /// 新增字段必须同步两端。所有时间戳统一为 UTC 毫秒 ISO 字符串。
    /// </summary>
    public class OfflineCatalogItemDto
    {
        public string StoreCode { get; set; } = string.Empty;

        /// <summary>归并/游标/唯一键：LookupCodeNormalized + U+001F + MatchSource + U+001F + ProductCode + U+001F + CodeId。</summary>
        public string LookupKey { get; set; } = string.Empty;
        public string LookupCode { get; set; } = string.Empty;
        public string LookupCodeNormalized { get; set; } = string.Empty;

        /// <summary>ProductBarcode | ItemNumber | ProductCode | SetBarcode | MultiBarcode | ClearanceBarcode。</summary>
        public string MatchSource { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
        public string? ProductImage { get; set; }
        public int? ProductType { get; set; }
        public string? Grade { get; set; }
        public string? LocalSupplierCode { get; set; }
        public string? LocalSupplierName { get; set; }
        public string? StoreName { get; set; }
        public string? StorePriceUuid { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? RetailPrice { get; set; }
        public decimal? DiscountRate { get; set; }
        public bool IsAutoPricing { get; set; }
        public bool IsSpecialProduct { get; set; }
        public decimal? Rate { get; set; }
        public string? StrategySourceLabel { get; set; }
        public string? StrategyRuleLabel { get; set; }
        public string? ClearanceUuid { get; set; }
        public string? ClearanceBarcode { get; set; }
        public decimal? ClearancePrice { get; set; }
        public string? CodeId { get; set; }
        public string? CodeUuid { get; set; }
        public string? CodeProductCode { get; set; }
        public string? CodeItemNumber { get; set; }
        public decimal? CodeRetailPrice { get; set; }
        public decimal? CodePurchasePrice { get; set; }
        public int? CodeQuantity { get; set; }
        public int? CodeType { get; set; }
        public decimal? CodeDiscountRate { get; set; }
        public bool? CodeIsAutoPricing { get; set; }
        public bool? CodeIsSpecialProduct { get; set; }
        public bool? CodeIsActive { get; set; }

        /// <summary>UTC 毫秒 ISO（yyyy-MM-ddTHH:mm:ss.fffZ），无法确定时为 null。</summary>
        public string? UpdatedAt { get; set; }

        /// <summary>业务字段 SHA256（大写十六进制），delta 归并时比较。</summary>
        public string RowVersion { get; set; } = string.Empty;
    }

    public class OfflineCatalogDeletedItemDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public string LookupKey { get; set; } = string.Empty;
        public string? DeletedAt { get; set; }
    }

    public static class OfflineCatalogSyncModes
    {
        public const string Full = "full";
        public const string Delta = "delta";
        public const string NoChange = "noChange";
    }

    public class OfflineCatalogSyncPlanDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public string GeneratedAt { get; set; } = string.Empty;
        public string Mode { get; set; } = OfflineCatalogSyncModes.Full;
        public string? BaseCatalogVersion { get; set; }
        public string TargetCatalogVersion { get; set; } = string.Empty;
        public int TargetTotal { get; set; }
        public string? DownloadLeaseId { get; set; }
        public int? DeltaOperationCount { get; set; }
    }

    public class OfflineCatalogPageDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public string GeneratedAt { get; set; } = string.Empty;
        public string? Cursor { get; set; }
        public List<OfflineCatalogItemDto> Items { get; set; } = new();
        public string? NextCursor { get; set; }
        public bool HasMore { get; set; }
        public int TotalCount { get; set; }
        public string CatalogVersion { get; set; } = string.Empty;
        public string PageChecksum { get; set; } = string.Empty;
        public string? DownloadLeaseId { get; set; }
    }

    public class OfflineCatalogDeltaPageDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public string GeneratedAt { get; set; } = string.Empty;
        public string BaseCatalogVersion { get; set; } = string.Empty;
        public string TargetCatalogVersion { get; set; } = string.Empty;
        public string? Cursor { get; set; }
        public List<OfflineCatalogItemDto> Items { get; set; } = new();
        public List<OfflineCatalogDeletedItemDto> DeletedItems { get; set; } = new();
        public string? NextCursor { get; set; }
        public bool HasMore { get; set; }
        public int TargetTotal { get; set; }
        public string PageChecksum { get; set; } = string.Empty;
        public string? DownloadLeaseId { get; set; }
    }
}
