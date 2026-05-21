using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Common;

namespace Hbpos.Client.Wpf.Services;

public interface ICatalogApiClient
{
    Task<CatalogSyncPageResponse> GetSellableItemsPageAsync(
        string storeCode,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<CatalogCompareResponse> CompareSellableItemsAsync(
        CatalogCompareRequest request,
        CancellationToken cancellationToken = default);

    Task<CatalogLookupResponse?> LookupSellableItemAsync(
        string storeCode,
        string lookupCode,
        CancellationToken cancellationToken = default);
}

public sealed class CatalogApiClient(HttpClient httpClient) : ICatalogApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CatalogSyncPageResponse> GetSellableItemsPageAsync(
        string storeCode,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var requestUri = BuildUri(
            "api/v1/catalog/sellable-items/page",
            ("storeCode", storeCode),
            ("cursor", cursor),
            ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)));

        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        return await ReadApiResultAsync<CatalogSyncPageResponse>(response, cancellationToken);
    }

    public async Task<CatalogCompareResponse> CompareSellableItemsAsync(
        CatalogCompareRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "api/v1/catalog/sellable-items/compare",
            request,
            JsonOptions,
            cancellationToken);

        return await ReadApiResultAsync<CatalogCompareResponse>(response, cancellationToken);
    }

    public async Task<CatalogLookupResponse?> LookupSellableItemAsync(
        string storeCode,
        string lookupCode,
        CancellationToken cancellationToken = default)
    {
        var requestUri = BuildUri(
            "api/v1/catalog/sellable-items/lookup",
            ("storeCode", storeCode),
            ("lookupCode", lookupCode));

        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return await ReadLookupNotFoundAsync(response, cancellationToken);
        }

        return await ReadApiResultAsync<CatalogLookupResponse>(response, cancellationToken);
    }

    private static async Task<CatalogLookupResponse?> ReadLookupNotFoundAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new CatalogApiException(
                "Catalog lookup returned HTTP 404 without a catalog error body.",
                response.StatusCode);
        }

        ApiResult<CatalogLookupResponse>? result;
        try
        {
            result = JsonSerializer.Deserialize<ApiResult<CatalogLookupResponse>>(content, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new CatalogApiException(
                "Catalog lookup returned HTTP 404 with invalid JSON.",
                response.StatusCode,
                errorCode: null,
                ex);
        }

        if (string.Equals(result?.ErrorCode, "LOOKUP_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
        {
            return result?.Data;
        }

        throw new CatalogApiException(
            result?.Message ?? "Catalog lookup returned HTTP 404.",
            response.StatusCode,
            result?.ErrorCode);
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
                    "Catalog API returned invalid JSON.",
                    response.StatusCode,
                    errorCode: null,
                    ex);
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new CatalogApiException(
                result?.Message ?? $"Catalog API request failed with HTTP {(int)response.StatusCode}.",
                response.StatusCode,
                result?.ErrorCode);
        }

        if (result is null)
        {
            throw new CatalogApiException(
                "Catalog API returned an empty response.",
                response.StatusCode);
        }

        if (!result.Success)
        {
            throw new CatalogApiException(
                result.Message ?? "Catalog API returned a failure response.",
                response.StatusCode,
                result.ErrorCode);
        }

        if (result.Data is null)
        {
            throw new CatalogApiException(
                "Catalog API returned no data.",
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

public sealed class CatalogApiException : Exception
{
    public CatalogApiException(
        string message,
        HttpStatusCode? statusCode = null,
        string? errorCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public HttpStatusCode? StatusCode { get; }

    public string? ErrorCode { get; }
}
