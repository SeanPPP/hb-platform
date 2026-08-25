namespace BlazorApp.Shared.DTOs
{
    public class ProductIntegrityCheckResultDto
    {
        public List<StoreIntegrityReport> StoreReports { get; set; } = new();
        public TableIntegrityReport? ProductSetCodeReport { get; set; }
        public DateTime CheckTime { get; set; } = DateTime.UtcNow;
        public double DurationSeconds { get; set; }
    }

    public class StoreIntegrityReport
    {
        public string StoreCode { get; set; } = "";
        public string StoreName { get; set; } = "";
        public List<TableIntegrityReport> TableReports { get; set; } = new();
    }

    public class TableIntegrityReport
    {
        public string TableName { get; set; } = "";
        public int TotalChecked { get; set; }
        public int OrphanedCount { get; set; }
        public int MissingCount { get; set; }
        public int InvalidKeyCount { get; set; }
        public List<string> OrphanedProductCodes { get; set; } = new();
        public List<string> MissingProductCodes { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    public class ProductIntegrityFixRequestDto
    {
        public bool FixStoreRetailPrice { get; set; } = true;
        public bool FixStoreMultiCodeProduct { get; set; } = true;
        public bool FixProductSetCode { get; set; } = true;
        public List<string>? SelectedStoreCodes { get; set; }
        public bool DryRun { get; set; } = false;
    }

    public class ProductIntegrityFixResultDto
    {
        public List<TableFixReport> Reports { get; set; } = new();
        public DateTime FixTime { get; set; } = DateTime.UtcNow;
        public double DurationSeconds { get; set; }
        public bool IsDryRun { get; set; }
    }

    public class TableFixReport
    {
        public string TableName { get; set; } = "";
        public int DeletedCount { get; set; }
        public int AddedCount { get; set; }
        public int SuccessfulGroupCount { get; set; }
        public int ErrorCount { get; set; }
        public string? ErrorCode { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<BatchOperationFailureDto> FailureDetails { get; set; } = new();
    }

    /// <summary>
    /// 套装子项进货价预览/回写筛选条件。空集合表示全部有效套装。
    /// </summary>
    public class SetChildPurchasePriceWritebackRequestDto
    {
        public List<string>? ProductCodes { get; set; }
        public List<string>? StoreCodes { get; set; }
    }

    public class SetChildPurchasePriceWritebackResultDto
    {
        public bool IsDryRun { get; set; }
        public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
        public double DurationSeconds { get; set; }
        public SetChildPurchasePriceTableReport ProductSetCode { get; set; } = new()
        {
            TableName = "ProductSetCode",
        };
        public SetChildPurchasePriceTableReport StoreMultiCodeProduct { get; set; } = new()
        {
            TableName = "StoreMultiCodeProduct",
        };
        public List<SetChildPurchasePriceChangeSample> Samples { get; set; } = new();
        public List<SetChildPurchasePriceWritebackError> Errors { get; set; } = new();
    }

    public class SetChildPurchasePriceTableReport
    {
        public string TableName { get; set; } = string.Empty;
        public int ScannedGroupCount { get; set; }
        public int EligibleGroupCount { get; set; }
        public int PendingUpdateCount { get; set; }
        public int UpdatedCount { get; set; }
        public int UnchangedCount { get; set; }
        public int SkippedGroupCount { get; set; }
    }

    public class SetChildPurchasePriceChangeSample
    {
        public string TableName { get; set; } = string.Empty;
        public string? StoreCode { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ChildProductCode { get; set; } = string.Empty;
        public decimal? CurrentPurchasePrice { get; set; }
        public decimal ExpectedPurchasePrice { get; set; }
        public decimal ParentPurchasePrice { get; set; }
        public decimal ChildRetailPrice { get; set; }
        public decimal TotalChildRetailPrice { get; set; }
    }

    public class SetChildPurchasePriceWritebackError
    {
        public string TableName { get; set; } = string.Empty;
        public string? StoreCode { get; set; }
        public string? ProductCode { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
