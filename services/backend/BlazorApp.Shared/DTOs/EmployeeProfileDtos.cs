using System.ComponentModel.DataAnnotations;
using BlazorApp.Shared.Models;

namespace BlazorApp.Shared.DTOs
{
    public class EmployeeProfileQueryDto
    {
        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 10;

        public string? Search { get; set; }

        public string? EmployeeType { get; set; }

        public bool? HasProfile { get; set; }
    }

    public class EmployeeProfileListItemDto
    {
        public int? EmployeeInfoId { get; set; }
        public string UserGUID { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public bool HasProfile { get; set; }
        public string? Phone { get; set; }
        public string? BankBsb { get; set; }
        public string? BankAccountNumber { get; set; }
        public string? SuperannuationCompanyName { get; set; }
        public string? SuperannuationCompanyCode { get; set; }
        public string? SuperannuationAccountNumber { get; set; }
        public string? Gender { get; set; }
        public string? EmployeeType { get; set; }
        public DateTime? Birthday { get; set; }
        public string? AvatarUrl { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class EmployeeProfileDetailDto
    {
        public int? EmployeeInfoId { get; set; }
        public string UserGUID { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public string? Phone { get; set; }
        public string? BankBsb { get; set; }
        public string? BankAccountNumber { get; set; }
        public string? SuperannuationCompanyName { get; set; }
        public string? SuperannuationCompanyCode { get; set; }
        public string? SuperannuationAccountNumber { get; set; }
        public DateTime? Birthday { get; set; }
        public string? Gender { get; set; }
        public string? EmploymentType { get; set; }
        public string? AvatarUrl { get; set; }
        public string? IdentityId { get; set; }
        public string? IdentityType { get; set; }
        public string? IdentityPhotoUrl { get; set; }
        public DateTime? IdentityPhotoUrlExpiresAt { get; set; }
        public string? Address { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public class EmployeeProfileUpsertDto
    {
        public string? UserGUID { get; set; }

        [StringLength(50)]
        public string? Phone { get; set; }

        [StringLength(20)]
        public string? BankBsb { get; set; }

        [StringLength(50)]
        public string? BankAccountNumber { get; set; }

        [StringLength(200)]
        public string? SuperannuationCompanyName { get; set; }

        [StringLength(100)]
        public string? SuperannuationCompanyCode { get; set; }

        [StringLength(100)]
        public string? SuperannuationAccountNumber { get; set; }

        public DateTime? Birthday { get; set; }

        public string? Gender { get; set; }

        public string? EmploymentType { get; set; }

        [StringLength(500)]
        public string? AvatarUrl { get; set; }

        [StringLength(100)]
        public string? IdentityId { get; set; }

        [StringLength(50)]
        public string? IdentityType { get; set; }

        [StringLength(500)]
        public string? IdentityPhotoUrl { get; set; }

        [StringLength(500)]
        public string? Address { get; set; }
    }

    public sealed class EmployeeImageUploadSignatureRequest
    {
        [Required]
        public string Kind { get; set; } = string.Empty;
        [Required]
        public string FileName { get; set; } = string.Empty;
        [Required]
        public string ContentType { get; set; } = string.Empty;
        [Range(1, 5 * 1024 * 1024)]
        public long FileSize { get; set; }
    }

    public sealed class EmployeeImageCompleteRequest
    {
        [Required]
        public string Kind { get; set; } = string.Empty;
        [Required]
        public string ObjectKey { get; set; } = string.Empty;
    }

    public sealed class EmployeeCashierBarcodeDto
    {
        public bool Exists { get; set; }
        public string? Barcode { get; set; }
        public string Format { get; set; } = "EAN-13";
        public int PrintCount { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public sealed class EmployeeProfileSensitiveChangeUpsertDto
    {
        [StringLength(20)]
        public string? BankBsb { get; set; }
        [StringLength(50)]
        public string? BankAccountNumber { get; set; }
        [StringLength(200)]
        public string? SuperannuationCompanyName { get; set; }
        [StringLength(100)]
        public string? SuperannuationCompanyCode { get; set; }
        [StringLength(100)]
        public string? SuperannuationAccountNumber { get; set; }
        [StringLength(50)]
        public string? IdentityType { get; set; }
        [StringLength(100)]
        public string? IdentityId { get; set; }
    }

    public sealed class EmployeeProfileSensitiveReviewDto
    {
        [StringLength(1000)]
        public string? Reason { get; set; }
    }

    public sealed class EmployeeProfileSensitiveRejectDto
    {
        [Required]
        [StringLength(1000, MinimumLength = 1)]
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class EmployeeProfileSensitiveChangeQueryDto
    {
        public int Page { get; set; } = 1;
        [Range(1, 100)]
        public int PageSize { get; set; } = 20;
        public string? Status { get; set; }
        public string? Search { get; set; }
    }

    public class EmployeeProfileSensitiveChangeSummaryDto
    {
        public int RequestId { get; set; }
        public string UserGuid { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? BankBsb { get; set; }
        public string? BankAccountSummary { get; set; }
        public string? SuperannuationCompanyName { get; set; }
        public string? SuperannuationCompanyCode { get; set; }
        public string? SuperannuationAccountSummary { get; set; }
        public string? IdentityType { get; set; }
        public string? IdentityIdSummary { get; set; }
        public bool HasIdentityPhoto { get; set; }
        public int BaseSensitiveRevision { get; set; }
        public DateTime SubmittedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewReason { get; set; }
        public List<string> ChangedFields { get; set; } = new();
    }

    public sealed class EmployeeProfileSensitiveChangeDetailDto
        : EmployeeProfileSensitiveChangeSummaryDto
    {
        public string? BankAccountNumber { get; set; }
        public string? SuperannuationAccountNumber { get; set; }
        public string? IdentityId { get; set; }
        public string? IdentityPhotoUrl { get; set; }
        public DateTime? IdentityPhotoUrlExpiresAt { get; set; }
        public string? SubmittedBy { get; set; }
        public string? ReviewedBy { get; set; }
    }

    public sealed class EmployeeCashierBarcodePrintConfirmationRequest
    {
        [Required]
        [StringLength(13, MinimumLength = 13)]
        public string Barcode { get; set; } = string.Empty;
        [Required]
        public Guid PrintAttemptId { get; set; }
    }
}
