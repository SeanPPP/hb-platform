using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    public sealed class ServiceApiTokenDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string TokenPrefix { get; set; } = string.Empty;

        public List<string> Scopes { get; set; } = new();

        public string Status { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime? ExpiresAt { get; set; }

        public DateTime? RevokedAt { get; set; }

        public DateTime? LastUsedAt { get; set; }

        public string? LastUsedIp { get; set; }
    }

    public sealed class ServiceApiTokenCreateRequestDto
    {
        [Required]
        [StringLength(120, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;
    }

    public sealed class ServiceApiTokenCreateResponseDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string TokenPrefix { get; set; } = string.Empty;

        public List<string> Scopes { get; set; } = new();

        public string Status { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime? ExpiresAt { get; set; }

        public DateTime? RevokedAt { get; set; }

        public DateTime? LastUsedAt { get; set; }

        public string? LastUsedIp { get; set; }

        public string Token { get; set; } = string.Empty;
    }

    public sealed class ServiceApiTokenCurrentDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string TokenPrefix { get; set; } = string.Empty;

        public List<string> Scopes { get; set; } = new();

        public DateTime? ExpiresAt { get; set; }

        public DateTime? LastUsedAt { get; set; }
    }
}
