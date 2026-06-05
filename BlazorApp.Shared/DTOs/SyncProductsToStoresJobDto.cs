namespace BlazorApp.Shared.DTOs
{
    public static class ProductStoreSyncJobStatusConstants
    {
        public const string Running = "Running";
        public const string Succeeded = "Succeeded";
        public const string Failed = "Failed";
    }

    /// <summary>
    /// 商品同步到分店后台任务状态。
    /// </summary>
    public class SyncProductsToStoresJobDto
    {
        public string JobId { get; set; } = string.Empty;
        public string OperationId { get; set; } = string.Empty;
        public string Status { get; set; } = ProductStoreSyncJobStatusConstants.Running;
        public List<string> ProductCodes { get; set; } = new();
        public List<string> StoreCodes { get; set; } = new();
        public bool IsDuplicateRequest { get; set; }
        public SyncProductsToStoresResult? Result { get; set; }
        public string? Message { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }
}
