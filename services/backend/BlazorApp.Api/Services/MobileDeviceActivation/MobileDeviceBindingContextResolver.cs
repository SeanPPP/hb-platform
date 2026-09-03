using System.Globalization;
using System.Security.Claims;

namespace BlazorApp.Api.Services.MobileDeviceActivation;

public static class MobileDeviceBindingContextResolver
{
    public static bool TryResolve(
        ClaimsPrincipal? principal,
        out MobileDeviceBindingContext context)
    {
        context = new MobileDeviceBindingContext(
            Guid.Empty,
            0,
            0,
            string.Empty,
            string.Empty);
        if (principal?.Identity?.IsAuthenticated != true
            || !string.Equals(
                principal.FindFirst("token_use")?.Value,
                MobileDeviceAccountTokenIssuer.TokenUse,
                StringComparison.Ordinal)
            || !Guid.TryParseExact(
                principal.FindFirst(MobileDeviceAccountTokenIssuer.BindingIdClaim)?.Value,
                "N",
                out var bindingId)
            || !int.TryParse(
                principal.FindFirst(MobileDeviceAccountTokenIssuer.BindingVersionClaim)?.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var bindingVersion)
            || !int.TryParse(
                principal.FindFirst(
                    MobileDeviceAccountTokenIssuer.DeviceRegistrationIdClaim)?.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var registrationId))
        {
            return false;
        }

        var hardwareId = principal.FindFirst(
            MobileDeviceAccountTokenIssuer.HardwareIdClaim)?.Value;
        var userGuid = principal.FindFirst("userGuid")?.Value
            ?? principal.FindFirst("userId")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (bindingId == Guid.Empty
            || bindingVersion <= 0
            || registrationId <= 0
            || string.IsNullOrWhiteSpace(hardwareId)
            || hardwareId.Length > 100
            || string.IsNullOrWhiteSpace(userGuid))
        {
            return false;
        }

        context = new MobileDeviceBindingContext(
            bindingId,
            bindingVersion,
            registrationId,
            hardwareId,
            userGuid);
        return true;
    }
}
