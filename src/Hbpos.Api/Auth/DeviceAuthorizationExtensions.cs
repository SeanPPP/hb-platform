using System.Security.Claims;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Auth;

public static class DeviceAuthorizationExtensions
{
    public static bool IsDeviceScopeAllowed(this ControllerBase controller, string storeCode, string? deviceCode = null)
    {
        var user = controller.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return true;
        }

        var authorizedStoreCode = user.FindFirstValue(DeviceAuthConstants.StoreCodeClaim);
        if (!string.Equals(authorizedStoreCode, storeCode, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(deviceCode))
        {
            var authorizedDeviceCode = user.FindFirstValue(DeviceAuthConstants.DeviceCodeClaim);
            return string.Equals(authorizedDeviceCode, deviceCode, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    public static ActionResult<ApiResult<T>> DeviceScopeForbidden<T>(string message)
    {
        return new ObjectResult(ApiResult<T>.Fail("DEVICE_SCOPE_FORBIDDEN", message))
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }
}
