using System.Net.Http;
using System.Text.Json;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Square;

namespace Hbpos.Client.Wpf.Services;

public interface ISquareTokenApiClient
{
    Task<SquareTokenStatusResponse> GetStatusAsync(
        CardTerminalEnvironment environment,
        CancellationToken cancellationToken = default);
}

public sealed class SquareTokenApiClient(HttpClient httpClient) : ISquareTokenApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SquareTokenStatusResponse> GetStatusAsync(
        CardTerminalEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        LogSquareToken($"token status request start environment={environment}");
        using var response = await httpClient.GetAsync(
            $"api/v1/square/token?environment={Uri.EscapeDataString(environment.ToString())}",
            cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        ApiResult<SquareTokenStatusResponse>? result = null;
        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                result = JsonSerializer.Deserialize<ApiResult<SquareTokenStatusResponse>>(content, JsonOptions);
            }
            catch (JsonException ex)
            {
                LogSquareToken($"token status request invalid json environment={environment} http={(int)response.StatusCode}");
                throw new CatalogApiException(
                    "Square token status API returned invalid JSON.",
                    response.StatusCode,
                    errorCode: null,
                    ex);
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            LogSquareToken($"token status request failed environment={environment} http={(int)response.StatusCode} errorCode={LogValue(result?.ErrorCode)}");
            throw new CatalogApiException(
                $"Square token status API request failed with HTTP {(int)response.StatusCode}.",
                response.StatusCode,
                result?.ErrorCode);
        }

        if (result is null)
        {
            LogSquareToken($"token status request failed environment={environment} reason=empty-response");
            throw new CatalogApiException("Square token status API returned an empty response.", response.StatusCode);
        }

        if (!result.Success)
        {
            LogSquareToken($"token status request failed environment={environment} reason=api-failure errorCode={LogValue(result.ErrorCode)}");
            throw new CatalogApiException(
                "Square token status API returned a failure response.",
                response.StatusCode,
                result.ErrorCode);
        }

        if (result.Data is null)
        {
            LogSquareToken($"token status request failed environment={environment} reason=missing-status errorCode={LogValue(result.ErrorCode)}");
            throw new CatalogApiException(
                "Square token status API returned no status.",
                response.StatusCode,
                result.ErrorCode);
        }

        LogSquareToken(
            $"token status request succeeded environment={environment} configured={result.Data.Configured} enabled={result.Data.Enabled}");
        return result.Data;
    }

    private static void LogSquareToken(string message)
    {
        ConsoleLog.Write("Square", message);
    }

    private static string LogValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<null>" : value;
    }
}
