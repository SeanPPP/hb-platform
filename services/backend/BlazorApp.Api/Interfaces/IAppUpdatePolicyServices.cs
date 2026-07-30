using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces;

public sealed record AppleAppStoreLookupResult(
    string AppStoreId,
    string BundleIdentifier,
    string Version,
    string AppStoreUrl
);

public interface IAppleAppStoreLookupClient
{
    Task<AppleAppStoreLookupResult?> LookupAsync(
        string appStoreId,
        string storefront,
        CancellationToken cancellationToken = default
    );
}

public interface IIosAppStoreReleaseService
{
    Task<ApiResponse<List<IosAppStoreReleaseDto>>> GetAsync(IosAppStoreReleaseQuery query);

    Task<ApiResponse<IosAppStoreReleaseDto>> CreateAsync(
        IosAppStoreReleaseCreateRequest request,
        string currentUser,
        CancellationToken cancellationToken = default
    );
}

public interface INativeAppUpdatePolicyService
{
    Task<ApiResponse<NativeUpdatePolicyDto>> GetMobileIosPolicyAsync();

    Task<ApiResponse<NativeUpdatePolicyDto>> SetMobileIosPolicyAsync(
        NativeUpdatePolicyRequest request,
        string currentUser
    );

    Task<ApiResponse<NativeUpdatePolicyDto>> GetPosIpadNativePolicyAsync();

    Task<ApiResponse<NativeUpdatePolicyDto>> SetPosIpadNativePolicyAsync(
        PosIpadNativeUpdatePolicyRequest request,
        string currentUser
    );

    Task<ApiResponse<List<AppUpdateTargetStoreOptionDto>>> GetStoreOptionsAsync();

    Task<NativeAppUpdateDecisionDto> GetMobileIosDecisionAsync(
        string? version,
        string? build
    );

    Task<NativeAppUpdateDecisionDto> GetPosIpadNativeDecisionAsync(
        PosIpadNativeDecisionRequest request
    );
}

public interface IPosIpadOtaPolicyService
{
    Task<ApiResponse<List<PosIpadOtaReleaseDto>>> GetReleasesAsync();

    Task<ApiResponse<PosIpadOtaChannelPreflightDto>> PreflightReleaseChannelAsync(
        PosIpadOtaChannelPreflightRequest request
    );

    Task<ApiResponse<PosIpadOtaReleaseDto>> CreateReleaseAsync(
        PosIpadOtaReleaseCreateRequest request,
        string currentUser
    );

    Task<ApiResponse<PosIpadOtaRolloutDto>> GetRolloutAsync();

    Task<ApiResponse<PosIpadOtaRolloutDto>> SetRolloutAsync(
        PosIpadOtaRolloutRequest request,
        string currentUser
    );

    Task<PosIpadOtaDecisionDto> GetDecisionAsync(PosIpadOtaDecisionRequest request);
}
