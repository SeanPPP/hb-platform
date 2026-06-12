using System.Net.Http;
using System.Net.Http.Json;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Health;

namespace Hbpos.Client.Wpf.Services;

public interface IConnectivityApiClient
{
    Task<bool> CheckOnlineAsync(CancellationToken cancellationToken = default);
}

public sealed class ConnectivityApiClient(HttpClient httpClient) : IConnectivityApiClient
{
    public async Task<bool> CheckOnlineAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync("api/v1/health", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<ApiResult<HealthCheckResponse>>(
                cancellationToken);
            return result?.Success == true && result.Data?.IsOnline == true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}
