using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces
{
    public interface IServiceApiTokenService
    {
        Task<ApiResponse<List<ServiceApiTokenDto>>> ListAsync();

        Task<ApiResponse<ServiceApiTokenCreateResponseDto>> CreateAsync(
            ServiceApiTokenCreateRequestDto request,
            string createdBy
        );

        Task<ApiResponse<ServiceApiTokenDto>> RevokeAsync(Guid id, string revokedBy);

        Task<ServiceApiTokenValidationResult?> ValidateAsync(string token, string? lastUsedIp);
    }

    public sealed class ServiceApiTokenValidationResult
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string TokenPrefix { get; set; } = string.Empty;

        public List<string> Scopes { get; set; } = new();

        public DateTime? ExpiresAt { get; set; }

        public DateTime? LastUsedAt { get; set; }
    }
}
