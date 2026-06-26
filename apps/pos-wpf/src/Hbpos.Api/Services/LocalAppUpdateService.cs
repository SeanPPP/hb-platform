using System.IO;
using System.Net.Http.Json;
using System.Text.Json;
using Hbpos.Contracts.AppUpdates;
using Hbpos.Contracts.Common;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Services;

public interface ILocalAppUpdateService
{
    Task<AppUpdateCheckResponse> CheckAsync(
        AppUpdateCheckRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class LocalAppUpdateService(
    HttpClient httpClient,
    IOptions<AppUpdateOptions> options,
    ILogger<LocalAppUpdateService> logger) : ILocalAppUpdateService
{
    private const long MaxInstallerFileSizeBytes = 512L * 1024 * 1024;

    private static readonly HashSet<string> ReservedWindowsFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<AppUpdateCheckResponse> CheckAsync(
        AppUpdateCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        var currentVersion = string.IsNullOrWhiteSpace(request.CurrentVersion)
            ? "0.0.0"
            : request.CurrentVersion.Trim();
        var channel = string.IsNullOrWhiteSpace(request.Channel)
            ? options.Value.Channel
            : request.Channel.Trim();

        var centerBaseUrlResolution = ResolveCenterBaseUrl(options.Value.CenterBaseUrl, out var centerBaseUrl);
        if (centerBaseUrlResolution == CenterBaseUrlResolution.Unconfigured)
        {
            // 中文注释：启动闸门不能在更新中心缺失时误判“无更新”，否则强制更新策略会被绕过。
            logger.LogWarning("WPF app update center base URL is not configured; returning check failed.");
            return FailedCheck(
                currentVersion,
                "APP_UPDATE_CENTER_NOT_CONFIGURED",
                "App update center base URL is not configured.");
        }

        if (centerBaseUrlResolution == CenterBaseUrlResolution.Invalid)
        {
            // 中文注释：显式配置了更新中心时，配置错误必须向 WPF 暴露为检查失败，避免门店误判成没有更新。
            logger.LogWarning("WPF app update center base URL is invalid; returning check failed.");
            return FailedCheck(
                currentVersion,
                "APP_UPDATE_CENTER_INVALID_CONFIGURATION",
                "App update center base URL is invalid.");
        }

        var checkUri = BuildCenterCheckUri(centerBaseUrl, channel, currentVersion);
        try
        {
            using var checkRequest = new HttpRequestMessage(HttpMethod.Get, checkUri);
            AddCenterApiKeyHeader(checkRequest, ResolvePreferredCenterApiKey(options.Value));
            using var response = await httpClient.SendAsync(checkRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var failedResponse = await ReadFailedCheckResponseAsync(response, currentVersion, cancellationToken);
                logger.LogWarning(
                    "WPF app update center check failed status={StatusCode} errorCode={ErrorCode}",
                    response.StatusCode,
                    failedResponse?.ErrorCode);
                return failedResponse ??
                    FailedCheck(
                        currentVersion,
                        "APP_UPDATE_CENTER_HTTP_ERROR",
                        "App update center returned an unsuccessful status.");
            }

            var update = await ReadUpdateResponseAsync(response, currentVersion, cancellationToken);
            if (update is null)
            {
                return FailedCheck(
                    currentVersion,
                    "APP_UPDATE_CENTER_EMPTY_RESPONSE",
                    "App update center returned an empty or unsupported response.");
            }

            var normalized = NormalizeResponse(update, currentVersion);
            // 中文注释：只要中心声明“有更新”，先校验安装包合同，避免坏数据直接透传到 WPF。
            if (!TryValidateUpdateContract(normalized, out var validationError))
            {
                logger.LogWarning(
                    "WPF app update center returned invalid app update contract: {ValidationError}",
                    validationError);
                return FailedCheck(
                    currentVersion,
                    "INVALID_UPDATE_CONTRACT",
                    validationError);
            }

            return normalized;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "WPF app update center check failed.");
            return FailedCheck(
                currentVersion,
                "APP_UPDATE_CENTER_UNAVAILABLE",
                "App update center is unavailable.");
        }
    }

    private static CenterBaseUrlResolution ResolveCenterBaseUrl(string? configuredValue, out Uri baseUrl)
    {
        baseUrl = null!;
        var hasConfiguredValue = !string.IsNullOrWhiteSpace(configuredValue);
        var environmentValue = Environment.GetEnvironmentVariable("HBPOS_APP_UPDATE_CENTER_BASE_URL");
        var hasEnvironmentValue = !string.IsNullOrWhiteSpace(environmentValue);
        var value = hasConfiguredValue
            ? configuredValue
            : environmentValue;

        if (!hasConfiguredValue && !hasEnvironmentValue)
        {
            return CenterBaseUrlResolution.Unconfigured;
        }

        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(EnsureTrailingSlash(value.Trim()), UriKind.Absolute, out var uri))
        {
            return CenterBaseUrlResolution.Invalid;
        }

        // 中文注释：生产中心地址必须走 HTTPS；仅允许本机 loopback 走 HTTP 方便本地联调。
        if (uri.Scheme != Uri.UriSchemeHttps &&
            !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        {
            return CenterBaseUrlResolution.Invalid;
        }

        baseUrl = uri;
        return CenterBaseUrlResolution.Valid;
    }

    private static Uri BuildCenterCheckUri(Uri centerBaseUrl, string channel, string currentVersion)
    {
        var relative = $"api/wpf-app-releases/check?channel={Uri.EscapeDataString(channel)}&currentVersion={Uri.EscapeDataString(currentVersion)}";
        return new Uri(centerBaseUrl, relative);
    }

    private enum CenterBaseUrlResolution
    {
        Unconfigured,
        Valid,
        Invalid
    }

    private static void AddCenterApiKeyHeader(HttpRequestMessage request, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        // 中文注释：终端只把密钥发给已校验过的更新中心地址，避免 check 接口匿名暴露发布元数据。
        request.Headers.TryAddWithoutValidation(AppUpdateOptions.CenterApiKeyHeaderName, apiKey.Trim());
    }

    private static string? ResolvePreferredCenterApiKey(AppUpdateOptions options)
    {
        // 中文注释：本地 POS API 先信任显式配置，再回退生产已在用的环境变量，最后兼容旧的 CenterApiKey。
        var configuredCheckApiKey = options.CheckApiKey?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredCheckApiKey))
        {
            return configuredCheckApiKey;
        }

        var environmentCheckApiKey = Environment.GetEnvironmentVariable("HBPOS_APP_UPDATE_CHECK_KEY")?.Trim();
        if (!string.IsNullOrWhiteSpace(environmentCheckApiKey))
        {
            return environmentCheckApiKey;
        }

        var legacyCenterApiKey = options.CenterApiKey?.Trim();
        return string.IsNullOrWhiteSpace(legacyCenterApiKey) ? null : legacyCenterApiKey;
    }

    private static async Task<AppUpdateCheckResponse?> ReadFailedCheckResponseAsync(
        HttpResponseMessage response,
        string currentVersion,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        if (!TryDeserializeJson(json, out AppUpdateCheckResponse? direct))
        {
            return null;
        }

        if (direct?.CheckFailed == true)
        {
            return NormalizeResponse(direct, currentVersion);
        }

        if (!TryDeserializeJson(json, out CenterErrorEnvelope? errorEnvelope))
        {
            return null;
        }

        if (errorEnvelope?.Success == false)
        {
            // 中文注释：后端 401/4xx 仍可能返回旧的 ApiResponse 结构，这里优先保留 code/message，避免 WPF 只看到泛化错误。
            var errorCode = string.IsNullOrWhiteSpace(errorEnvelope.ErrorCode)
                ? errorEnvelope.Code
                : errorEnvelope.ErrorCode;
            return FailedCheck(currentVersion, errorCode, errorEnvelope.Message);
        }

        return null;
    }

    private static bool TryDeserializeJson<T>(string json, out T? value)
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return true;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    private static async Task<AppUpdateCheckResponse?> ReadUpdateResponseAsync(
        HttpResponseMessage response,
        string currentVersion,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var direct = JsonSerializer.Deserialize<AppUpdateCheckResponse>(json, JsonOptions);
        if (direct is not null &&
            (direct.CheckFailed || direct.UpdateAvailable || !string.IsNullOrWhiteSpace(direct.CurrentVersion)))
        {
            return direct;
        }

        var wrapped = JsonSerializer.Deserialize<ApiResult<AppUpdateCheckResponse>>(json, JsonOptions);
        if (wrapped?.Success == true)
        {
            return wrapped.Data;
        }

        if (wrapped?.Success == false)
        {
            // 中文注释：中心策略错误不能伪装成“无更新”，否则目标禁用会导致门店静默停更。
            return AppUpdateCheckResponse.Failed(
                currentVersion,
                wrapped.ErrorCode,
                wrapped.Message);
        }

        return null;
    }

    private static AppUpdateCheckResponse NormalizeResponse(
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

    private static bool TryValidateUpdateContract(
        AppUpdateCheckResponse response,
        out string validationError)
    {
        if (response.CheckFailed || !response.UpdateAvailable)
        {
            validationError = string.Empty;
            return true;
        }

        if (!TryValidateDownloadUrl(response.DownloadUrl, out validationError))
        {
            return false;
        }

        if (!IsSha256Hex(response.Sha256))
        {
            validationError = "sha256 must be a 64-character hex string.";
            return false;
        }

        if (response.FileSize is null or < 1 or > MaxInstallerFileSizeBytes)
        {
            validationError = $"fileSize must be between 1 and {MaxInstallerFileSizeBytes} bytes.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(response.InstallerType) &&
            !string.Equals(response.InstallerType, "exe", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(response.InstallerType, "msi", StringComparison.OrdinalIgnoreCase))
        {
            validationError = "installerType must be empty, exe, or msi.";
            return false;
        }

        if (!TryValidateInstallerIdentity(response.FileName, response.InstallerType, out validationError))
        {
            return false;
        }

        validationError = string.Empty;
        return true;
    }

    private static bool TryValidateInstallerIdentity(
        string? fileName,
        string? installerType,
        out string validationError)
    {
        if (!IsSafeInstallerFileName(fileName, out var resolvedFileName))
        {
            validationError = "fileName must be a safe Windows file name.";
            return false;
        }

        var extension = Path.GetExtension(resolvedFileName).TrimStart('.').ToLowerInvariant();
        if (extension is not ("exe" or "msi"))
        {
            validationError = "fileName must end with .exe or .msi.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(installerType) &&
            !string.Equals(extension, installerType.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            // 中文注释：中心声明的安装类型必须和最终文件扩展名一致，避免下载链路被错误类型误导。
            validationError = "installerType must match the fileName extension.";
            return false;
        }

        validationError = string.Empty;
        return true;
    }

    private static bool IsSafeInstallerFileName(string? fileName, out string normalizedFileName)
    {
        normalizedFileName = string.Empty;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        // 中文注释：本地 API 要在进入 WPF 前就拒绝路径穿越、目录片段和 Windows 保留名，避免危险文件名继续向下游透传。
        if (fileName.IndexOf('/') >= 0 ||
            fileName.IndexOf('\\') >= 0 ||
            fileName.EndsWith(' ') ||
            fileName.EndsWith('.') ||
            ContainsWindowsInvalidFileNameCharacter(fileName))
        {
            return false;
        }

        if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            return false;
        }

        // 中文注释：Windows 设备名即使带扩展也不能作为安装包名，例如 CON.any.exe / NUL.v1.msi。
        if (IsReservedWindowsDeviceFileName(fileName))
        {
            return false;
        }

        normalizedFileName = fileName;
        return true;
    }

    private static bool ContainsWindowsInvalidFileNameCharacter(string fileName)
    {
        foreach (var ch in fileName)
        {
            if (char.IsControl(ch) || ch is '<' or '>' or ':' or '"' or '|' or '?' or '*')
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReservedWindowsDeviceFileName(string fileName)
    {
        var firstDotIndex = fileName.IndexOf('.');
        var deviceName = firstDotIndex < 0 ? fileName : fileName[..firstDotIndex];
        return ReservedWindowsFileNames.Contains(deviceName.TrimEnd(' ', '.'));
    }

    private static bool TryValidateDownloadUrl(string? downloadUrl, out string validationError)
    {
        validationError = string.Empty;
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
        {
            validationError = "downloadUrl must be an absolute URL.";
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        if (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
        {
            return true;
        }

        validationError = "downloadUrl must use https or loopback http.";
        return false;
    }

    private static bool IsSha256Hex(string? sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256) || sha256.Length != 64)
        {
            return false;
        }

        foreach (var ch in sha256)
        {
            var isHexDigit =
                (ch >= '0' && ch <= '9') ||
                (ch >= 'a' && ch <= 'f') ||
                (ch >= 'A' && ch <= 'F');
            if (!isHexDigit)
            {
                return false;
            }
        }

        return true;
    }

    private static string EnsureTrailingSlash(string value) =>
        value.EndsWith('/') ? value : value + "/";

    private static AppUpdateCheckResponse FailedCheck(
        string currentVersion,
        string? errorCode,
        string? message) =>
        AppUpdateCheckResponse.Failed(currentVersion, errorCode, message);

    private sealed class CenterErrorEnvelope
    {
        public bool Success { get; init; }

        public string? ErrorCode { get; init; }

        public string? Code { get; init; }

        public string? Message { get; init; }
    }
}
