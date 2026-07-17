using System.Net.Http;
using System.Net.Http.Headers;
using Hbpos.Contracts.Cashiers;
using Hbpos.Contracts.Devices;

namespace Hbpos.Client.Wpf.Services;

public sealed class DeviceAuthorizationMessageHandler(
    DeviceAuthorizationState authorizationState,
    ICashierSessionContext cashierSessionContext) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
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

        request.Headers.Remove(CashierAuthorizationConstants.HeaderName);
        // 中文注释：只有受控授权 scope 生效时才临时使用授权者票据，不修改当前收银员会话。
        var cashierSession = OperationAuthorizationScope.CurrentAuthorizingSession
            ?? cashierSessionContext.CurrentSession;
        if (!string.IsNullOrWhiteSpace(cashierSession?.AuthorizationToken) &&
            cashierSession.AuthorizationExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            // 收银员授权与设备授权分头传递，后端仍会按当前数据库权限实时复核。
            request.Headers.TryAddWithoutValidation(
                CashierAuthorizationConstants.HeaderName,
                cashierSession.AuthorizationToken);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden &&
            cashierSessionContext.CurrentSession?.IsEmergencyOverride == true)
        {
            // 在线撤销或设备停用被服务端拒绝后，立即退出紧急会话；普通离线缓存不受影响。
            cashierSessionContext.Clear();
        }

        return response;
    }
}
