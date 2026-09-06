using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Hbpos.RemoteStatus;

public sealed class RemoteStatusHttpSender(HttpClient httpClient) : IRemoteStatusHeartbeatSender
{
    public async Task<HeartbeatSendResult> SendAsync(
        RemoteHeartbeatPayload payload,
        string heartbeatUrl,
        string monitorToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, heartbeatUrl)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", monitorToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return HeartbeatSendResult.Success();
        }

        return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? HeartbeatSendResult.Unauthorized((int)response.StatusCode)
            : HeartbeatSendResult.Retryable((int)response.StatusCode);
    }
}
