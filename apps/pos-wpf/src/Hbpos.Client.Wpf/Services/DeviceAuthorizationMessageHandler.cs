using System.Net.Http;
using System.Net.Http.Headers;
using Hbpos.Contracts.Devices;

namespace Hbpos.Client.Wpf.Services;

public sealed class DeviceAuthorizationMessageHandler(DeviceAuthorizationState authorizationState) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var context = authorizationState.Current;
        if (context is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AuthorizationCode);
            request.Headers.Remove(DeviceAuthConstants.DeviceCodeHeader);
            request.Headers.Remove(DeviceAuthConstants.StoreCodeHeader);
            request.Headers.Remove(DeviceAuthConstants.HardwareIdHeader);
            request.Headers.TryAddWithoutValidation(DeviceAuthConstants.DeviceCodeHeader, context.DeviceCode);
            request.Headers.TryAddWithoutValidation(DeviceAuthConstants.StoreCodeHeader, context.StoreCode);
            request.Headers.TryAddWithoutValidation(DeviceAuthConstants.HardwareIdHeader, context.HardwareId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
