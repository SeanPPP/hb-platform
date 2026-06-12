using System.Collections.Generic;

namespace BlazorApp.Shared.DTOs
{
    public class CopyStoreDataDto
    {
        public string SourceStoreCode { get; set; } = string.Empty;
        public List<string> TargetStoreCodes { get; set; } = new List<string>();
        public string Mode { get; set; } = "Overwrite";

        public bool SyncPurchasePrice { get; set; } = true;
        public bool SyncRetailPrice { get; set; } = true;
        public bool SyncIsAutoPricing { get; set; } = true;
        public bool SyncIsSpecialProduct { get; set; } = true;
        public bool SyncDiscountRate { get; set; } = true;

        public bool SyncMultiCode { get; set; } = true;
        public bool SyncMultiCodeRetailPrice { get; set; } = true;
    }

    public class CopyStoreDataResultDto
    {
        public int StoreRetailPriceCopied { get; set; }
        public int StoreMultiCodeProductCopied { get; set; }
    }

    public class CopyProgressDto
    {
        public string EventType { get; set; } = string.Empty;
        public string StoreCode { get; set; } = string.Empty;
        public int StoreIndex { get; set; }
        public int TotalStores { get; set; }
        public int RetailPriceCopied { get; set; }
        public int MultiCodeCopied { get; set; }
        public string Message { get; set; } = string.Empty;
        public int BatchCount { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
