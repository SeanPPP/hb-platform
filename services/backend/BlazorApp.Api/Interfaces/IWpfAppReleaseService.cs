using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces
{
    public interface IWpfAppReleaseService
    {
        Task<ApiResponse<PagedResult<WpfAppReleaseDto>>> GetReleasesAsync(
            WpfAppReleaseQuery query
        );

        Task<ApiResponse<WpfAppReleaseDto>> CreateReleaseAsync(
            WpfAppReleaseCreateRequest request,
            string currentUser
        );

        Task<ApiResponse<WpfAppReleaseDto>> UpdateReleaseAsync(
            Guid id,
            WpfAppReleaseUpdateRequest request,
            string currentUser
        );

        Task<ApiResponse<WpfUpdatePolicyDto>> SetPolicyAsync(
            WpfUpdatePolicyRequest request,
            string currentUser
        );

        Task<ApiResponse<WpfUpdateCheckResponse>> CheckUpdateAsync(
            string? channel,
            string? currentVersion
        );

        Task<ApiResponse<WpfAppReleaseUploadInitResponse>> CreateUploadInitAsync(
            WpfAppReleaseUploadInitRequest request,
            CancellationToken cancellationToken = default
        );
    }
}
