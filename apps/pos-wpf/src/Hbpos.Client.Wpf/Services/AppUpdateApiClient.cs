using System.Net.Http;
using System.Text.Json;
using Hbpos.Contracts.AppUpdates;
using Hbpos.Contracts.Common;

namespace Hbpos.Client.Wpf.Services;

public interface IAppUpdateApiClient
{
    Task<AppUpdateCheckResponse> CheckAsync(
        AppUpdateCheckRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AppUpdateApiClient(HttpClient httpClient) : IAppUpdateApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<AppUpdateCheckResponse> CheckAsync(
        AppUpdateCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        var currentVersion = Uri.EscapeDataString(request.CurrentVersion);
        var channel = Uri.EscapeDataString(string.IsNullOrWhiteSpace(request.Channel) ? "production" : request.Channel);
        using var response = await httpClient.GetAsync(
            $"api/app-update/check?currentVersion={currentVersion}&channel={channel}",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return AppUpdateCheckResponse.Failed(
                request.CurrentVersion,
                "LOCAL_APP_UPDATE_HTTP_ERROR",
                "Local app update check returned an unsuccessful status.");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return AppUpdateCheckResponse.Failed(
                request.CurrentVersion,
                "LOCAL_APP_UPDATE_EMPTY_RESPONSE",
                "Local app update check returned an empty response.");
        }

        var direct = JsonSerializer.Deserialize<AppUpdateCheckResponse>(json, JsonOptions);
        if (direct is not null &&
            (direct.CheckFailed || direct.UpdateAvailable || !string.IsNullOrWhiteSpace(direct.CurrentVersion)))
        {
            return NormalizeCheckResponse(direct, request.CurrentVersion);
        }

        var wrapped = JsonSerializer.Deserialize<ApiResult<AppUpdateCheckResponse>>(json, JsonOptions);
        if (wrapped?.Success == true && wrapped.Data is not null)
        {
            return NormalizeCheckResponse(wrapped.Data, request.CurrentVersion);
        }

        if (wrapped?.Success == false)
        {
            // 中文注释：POS API 已把中心错误包装成 ApiResult，客户端必须保留失败态给设置页展示。
            return AppUpdateCheckResponse.Failed(
                request.CurrentVersion,
                wrapped.ErrorCode,
                wrapped.Message);
        }

        return AppUpdateCheckResponse.Failed(
            request.CurrentVersion,
            "LOCAL_APP_UPDATE_INVALID_RESPONSE",
            "Local app update check returned an unsupported response.");
    }

    private static AppUpdateCheckResponse NormalizeCheckResponse(
        AppUpdateCheckResponse response,
        string currentVersion)
    {
        return response with
        {
            CurrentVersion = string.IsNullOrWhiteSpace(response.CurrentVersion)
                ? currentVersion
                : response.CurrentVersion,
            TargetVersion = string.IsNullOrWhiteSpace(response.TargetVersion)
                ? currentVersion
                : response.TargetVersion
        };
    }
}
