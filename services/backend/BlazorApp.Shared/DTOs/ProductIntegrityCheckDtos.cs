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
        public List<string> OrphanedProductCodes { get; set; } = new();
        public List<string> MissingProductCodes { get; set; } = new();
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
        public int ErrorCount { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
