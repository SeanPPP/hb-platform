using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces;

/// <summary>
/// WPF 更新策略的定向写入、设备身份解析和受限目标选项查询。
/// </summary>
public interface IWpfAppReleaseTargetingService
{
    Task<ApiResponse<WpfUpdatePolicyDto>> SetPolicyAsync(
        WpfUpdatePolicyRequest request,
        string currentUser
    );

    Task<ApiResponse<WpfUpdateCheckResponse>> CheckUpdateAsync(
        string? channel,
        string? currentVersion,
        string? deviceId,
        string? authCode
    );

    Task<ApiResponse<WpfUpdateTargetStoreOptionsResponse>> GetStoreOptionsAsync();

    Task<ApiResponse<PagedResult<WpfUpdateTargetDeviceOptionDto>>> GetDeviceOptionsAsync(
        int page,
        int pageSize,
        string? keyword
    );
}
