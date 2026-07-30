using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BlazorApp.Api.Interfaces;

namespace BlazorApp.Api.Services;

public sealed class AppleAppStoreLookupClient(
    HttpClient httpClient,
    ILogger<AppleAppStoreLookupClient> logger
) : IAppleAppStoreLookupClient
{
    public async Task<AppleAppStoreLookupResult?> LookupAsync(
        string appStoreId,
        string storefront,
        CancellationToken cancellationToken = default
    )
    {
        var requestUri =
            $"lookup?id={Uri.EscapeDataString(appStoreId)}&country={Uri.EscapeDataString(storefront)}";
        try
        {
            using var response = await httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Apple Lookup 请求失败，StatusCode: {StatusCode}",
                    (int)response.StatusCode
                );
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<LookupResponse>(
                cancellationToken: cancellationToken
            );
            if (payload?.ResultCount != 1 || payload.Results?.Count != 1)
            {
                return null;
            }

            var item = payload.Results[0];
            if (
                item == null
                || item.TrackId <= 0
                || string.IsNullOrWhiteSpace(item.BundleId)
                || string.IsNullOrWhiteSpace(item.Version)
                || string.IsNullOrWhiteSpace(item.TrackViewUrl)
            )
            {
                return null;
            }

            return new AppleAppStoreLookupResult(
                item.TrackId.ToString(),
                item.BundleId.Trim(),
                item.Version.Trim(),
                item.TrackViewUrl.Trim()
            );
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Apple Lookup 请求超时");
            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Apple Lookup 网络请求失败");
            return null;
        }
        catch (System.Text.Json.JsonException ex)
        {
            logger.LogWarning(ex, "Apple Lookup 响应解析失败");
            return null;
        }
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("resultCount")]
        public int ResultCount { get; set; }

        [JsonPropertyName("results")]
        public List<LookupItem>? Results { get; set; }
    }

    private sealed class LookupItem
    {
        [JsonPropertyName("trackId")]
        public long TrackId { get; set; }

        [JsonPropertyName("bundleId")]
        public string? BundleId { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("trackViewUrl")]
        public string? TrackViewUrl { get; set; }
    }
}
