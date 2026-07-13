using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Health;

namespace Hbpos.Client.Wpf.Services;

public sealed class ApiServerSettingsService
{
    public const string DevelopmentApiBaseAddress = "http://localhost:5159/";
    public const string ReleaseApiBaseAddress = "https://hotbargain.vip/pos-api/";
    internal static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _httpClient;
    private readonly Func<string> _getCurrentAddress;
    private readonly Action<string> _saveUserAddress;

    public ApiServerSettingsService(HttpClient httpClient)
        : this(
            httpClient,
            () => ServiceRegistration.GetApiBaseAddress().ToString(),
            address => Environment.SetEnvironmentVariable(
                "HBPOS_API_BASE_URL",
                address,
                EnvironmentVariableTarget.User))
    {
    }

    internal ApiServerSettingsService(
        HttpClient httpClient,
        Func<string> getCurrentAddress,
        Action<string> saveUserAddress)
    {
        _httpClient = httpClient;
        _getCurrentAddress = getCurrentAddress;
        _saveUserAddress = saveUserAddress;
    }

    public string GetCurrentAddress()
    {
        return NormalizeAddress(_getCurrentAddress());
    }

    public static string NormalizeAddress(string address)
    {
        if (!Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException("服务器地址必须是绝对地址。", nameof(address));
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("服务器地址只支持 HTTP 或 HTTPS。", nameof(address));
        }

        // 公网地址必须使用 HTTPS；HTTP 仅保留给本机开发服务。
        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
        {
            throw new ArgumentException("非本机服务器地址必须使用 HTTPS。", nameof(address));
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("服务器地址不能包含用户信息、查询参数或片段。", nameof(address));
        }

        var normalized = uri.AbsoluteUri;
        return normalized.EndsWith('/') ? normalized : normalized + "/";
    }

    public async Task<bool> TestConnectionAsync(string address, CancellationToken cancellationToken)
    {
        var baseAddress = new Uri(NormalizeAddress(address), UriKind.Absolute);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectionTimeout);

        try
        {
            using var response = await _httpClient.GetAsync(
                new Uri(baseAddress, "api/v1/health"),
                timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<ApiResult<HealthCheckResponse>>(
                cancellationToken: timeout.Token);
            return result?.Success == true && result.Data?.IsOnline == true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return false;
        }
    }

    public void SaveUserAddress(string address)
    {
        _saveUserAddress(NormalizeAddress(address));
    }
}
