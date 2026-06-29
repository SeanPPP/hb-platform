namespace BlazorApp.Shared.DTOs
{
    public static class StorePriceTransferDirectionConstants
    {
        public const string HqToLocal = "HqToLocal";
        public const string LocalToHq = "LocalToHq";
    }

    public static class StorePriceTransferJobStatusConstants
    {
        public const string Running = "Running";
        public const string Succeeded = "Succeeded";
        public const string Failed = "Failed";
    }

    /// <summary>
    /// HQ/本地分店价格与多码双向同步请求。
    /// </summary>
    public class StorePriceTransferRequest
    {
        public string Direction { get; set; } = StorePriceTransferDirectionConstants.HqToLocal;

        public string? SourceStoreCode { get; set; }

        public string? TargetStoreCode { get; set; }

        public bool SyncRetailPrices { get; set; } = true;

        public bool SyncMultiCodePrices { get; set; } = true;

        public bool SyncPurchasePrice { get; set; }

        public bool SyncRetailPrice { get; set; }

        public bool SyncDiscountRate { get; set; }

        public bool SyncIsAutoPricing { get; set; }

        public bool SyncIsSpecialProduct { get; set; }
    }

    /// <summary>
    /// HQ/本地分店价格与多码双向同步结果。
    /// </summary>
    public class StorePriceTransferResult
    {
        public int TotalProcessed { get; set; }

        public int InsertedCount { get; set; }

        public int UpdatedCount { get; set; }

        public int SkippedCount { get; set; }

        public int FailedCount { get; set; }

        public int RetailPriceInserted { get; set; }

        public int RetailPriceUpdated { get; set; }

        public int RetailPriceSkipped { get; set; }

        public int MultiCodeInserted { get; set; }

        public int MultiCodeUpdated { get; set; }

        public int MultiCodeSkipped { get; set; }

        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// HQ/本地分店价格与多码双向同步后台任务状态。
    /// </summary>
    public class StorePriceTransferJobDto
    {
        public string JobId { get; set; } = string.Empty;

        public string OperationId { get; set; } = string.Empty;

        public string Status { get; set; } = StorePriceTransferJobStatusConstants.Running;

        public bool IsDuplicateRequest { get; set; }

        public StorePriceTransferRequest Request { get; set; } = new();

        public StorePriceTransferResult? Result { get; set; }

        public string? Message { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime StartedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public DateTime? ExpiresAt { get; set; }

        public List<string> Errors { get; set; } = new();
    }
}
