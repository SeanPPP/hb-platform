using System.Net.Http;
using System.Text.Json;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Stores;

namespace Hbpos.Client.Wpf.Services;

public interface IStoreReceiptProfileApiClient
{
    Task<StoreReceiptProfileDto> GetCurrentAsync(CancellationToken cancellationToken = default);
}

public sealed class StoreReceiptProfileApiClient(HttpClient httpClient) : IStoreReceiptProfileApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StoreReceiptProfileDto> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("api/v1/stores/current/receipt-profile", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        ApiResult<StoreReceiptProfileDto>? result = null;

        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                result = JsonSerializer.Deserialize<ApiResult<StoreReceiptProfileDto>>(content, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new CatalogApiException(
                    "Store receipt profile API returned invalid JSON.",
                    response.StatusCode,
                    errorCode: null,
                    ex);
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new CatalogApiException(
                result?.Message ?? $"Store receipt profile API request failed with HTTP {(int)response.StatusCode}.",
                response.StatusCode,
                result?.ErrorCode);
        }

        if (result is null || !result.Success)
        {
            throw new CatalogApiException(
                result?.Message ?? "Store receipt profile API returned a failure response.",
                response.StatusCode,
                result?.ErrorCode);
        }

        return result.Data ?? throw new CatalogApiException(
            "Store receipt profile API returned an empty profile.",
            response.StatusCode);
    }
}
