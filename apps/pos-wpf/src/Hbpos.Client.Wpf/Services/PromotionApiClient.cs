using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Promotions;

namespace Hbpos.Client.Wpf.Services;

public interface IPromotionApiClient
{
    Task<PromotionRulesResponse> GetRulesAsync(
        string storeCode,
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default);
}

public sealed class PromotionApiClient(HttpClient httpClient) : IPromotionApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PromotionRulesResponse> GetRulesAsync(
        string storeCode,
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedStoreCode = NormalizeStoreCode(storeCode);
        var requestUri = BuildUri(
            "api/v1/promotions/rules",
            ("storeCode", normalizedStoreCode),
            ("asOf", asOf?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));

        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        return await ReadApiResultAsync<PromotionRulesResponse>(response, cancellationToken);
    }

    private static string NormalizeStoreCode(string? storeCode)
    {
        return (storeCode ?? string.Empty).Trim();
    }

    private static async Task<T> ReadApiResultAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        ApiResult<T>? result = null;

        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                result = JsonSerializer.Deserialize<ApiResult<T>>(content, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new CatalogApiException(
                    "Promotion API returned invalid JSON.",
                    response.StatusCode,
                    errorCode: null,
                    ex);
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new CatalogApiException(
                result?.Message ?? $"Promotion API request failed with HTTP {(int)response.StatusCode}.",
                response.StatusCode,
                result?.ErrorCode);
        }

        if (result is null)
        {
            throw new CatalogApiException(
                "Promotion API returned an empty response.",
                response.StatusCode);
        }

        if (!result.Success)
        {
            throw new CatalogApiException(
                result.Message ?? "Promotion API returned a failure response.",
                response.StatusCode,
                result.ErrorCode);
        }

        if (result.Data is null)
        {
            throw new CatalogApiException(
                "Promotion API returned no data.",
                response.StatusCode,
                result.ErrorCode);
        }

        return result.Data;
    }

    private static string BuildUri(string path, params (string Name, string? Value)[] query)
    {
        var queryString = string.Join(
            "&",
            query
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .Select(x => $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(x.Value!)}"));

        return string.IsNullOrEmpty(queryString)
            ? path
            : $"{path}?{queryString}";
    }
}
