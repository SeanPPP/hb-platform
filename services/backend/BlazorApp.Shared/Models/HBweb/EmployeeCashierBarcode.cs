using SqlSugar;

namespace BlazorApp.Shared.Models
{
    [SugarTable("EmployeeCashierBarcodes")]
    public sealed class EmployeeCashierBarcode
    {
        [SugarColumn(IsPrimaryKey = true, Length = 50)]
        public string HGUID { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false, Length = 50)]
        public string UserGUID { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false, Length = 13)]
        public string Barcode { get; set; } = string.Empty;

        public int PrintCount { get; set; }
        public bool Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        [SugarColumn(IsNullable = true, Length = 100)]
        public string? UpdatedBy { get; set; }
    }

    [SugarTable("EmployeeCashierBarcodePrintAttempts")]
    public sealed class EmployeeCashierBarcodePrintAttempt
    {
        [SugarColumn(IsPrimaryKey = true)]
        public Guid PrintAttemptId { get; set; }
        [SugarColumn(IsNullable = false, Length = 50)]
        public string BarcodeHGUID { get; set; } = string.Empty;
        [SugarColumn(IsNullable = false, Length = 50)]
        public string UserGUID { get; set; } = string.Empty;
        [SugarColumn(IsNullable = false, Length = 13)]
        public string Barcode { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public enum EmployeeImageUploadStatus
    {
        Pending = 0,
        Processing = 1,
        Promoted = 2,
        Completed = 3,
        Failed = 4,
        Cleaning = 5,
    }

    public enum EmployeeImageObjectCleanupStatus
    {
        None = 0,
        Pending = 1,
        Processing = 2,
        Completed = 3,
    }

    [SugarTable("EmployeeImageUploadTickets")]
    public sealed class EmployeeImageUploadTicket
    {
        [SugarColumn(IsPrimaryKey = true, Length = 500)]
        public string PendingObjectKey { get; set; } = string.Empty;
        [SugarColumn(IsNullable = false, Length = 50)]
        public string UserGUID { get; set; } = string.Empty;
        [SugarColumn(IsNullable = false, Length = 20)]
        public string Kind { get; set; } = string.Empty;
        [SugarColumn(IsNullable = false, Length = 100)]
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public EmployeeImageUploadStatus Status { get; set; }
        [SugarColumn(IsNullable = true, Length = 500)]
        public string? FinalObjectKey { get; set; }
        [SugarColumn(IsNullable = true)]
        public DateTime? ProcessingStartedAt { get; set; }
        [SugarColumn(IsNullable = true)]
        public DateTime? PromotedAt { get; set; }
        [SugarColumn(IsNullable = true)]
        public DateTime? CompletedAt { get; set; }
        [SugarColumn(IsNullable = true)]
        public DateTime? FailedAt { get; set; }
        [SugarColumn(IsNullable = true)]
        public DateTime? StageChangedAt { get; set; }
        [SugarColumn(IsNullable = true, Length = 500)]
        public string? PreviousObjectKey { get; set; }
        public EmployeeImageObjectCleanupStatus PreviousObjectCleanupStatus { get; set; }
        [SugarColumn(IsNullable = true)]
        public DateTime? PreviousObjectCleanupStartedAt { get; set; }
    }
}
