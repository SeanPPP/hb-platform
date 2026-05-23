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
        public string? IdentityPhotoUrl { get; set; }
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

        [StringLength(500)]
        public string? IdentityPhotoUrl { get; set; }

        [StringLength(500)]
        public string? Address { get; set; }
    }
}
