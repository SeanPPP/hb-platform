using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;

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
        MobileIosNativeUpdatePolicyRequest request,
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

public interface IPosHandheldUpdateDecisionService
{
    Task<PosHandheldNativeDecisionDto?> GetNativeDecisionAsync(
        PosHandheldNativeDecisionRequest request,
        CancellationToken cancellationToken = default
    );

    Task<PosHandheldOtaDecisionDto?> GetOtaDecisionAsync(
        PosHandheldOtaDecisionRequest request,
        CancellationToken cancellationToken = default
    );
}

public sealed class PosHandheldManagedLane
{
    public required PosHandheldUpdatePolicy Policy { get; init; }
    public PosHandheldUpdateCandidateDto? Candidate { get; init; }
    public bool CandidateValid { get; init; }
}

public interface IPosHandheldUpdatePolicyService
{
    Task<ApiResponse<List<PosHandheldUpdatePolicyDto>>> GetPoliciesAsync();

    Task<ApiResponse<List<PosHandheldUpdateCandidateDto>>> GetCandidatesAsync(string lane);

    Task<ApiResponse<PosHandheldUpdatePolicyDto>> SetLaneAsync(
        string lane,
        PosHandheldUpdatePolicyRequest request,
        string currentUser
    );

    Task<ApiResponse<List<PosHandheldUpdatePolicyRevisionDto>>> GetRevisionsAsync(
        string lane
    );

    Task<PosHandheldManagedLane?> ResolveManagedLaneAsync(string lane);
}
