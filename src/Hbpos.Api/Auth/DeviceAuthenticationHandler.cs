using System.Security.Claims;
using System.Text.Encodings.Web;
using Hbpos.Api.Services;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Auth;

public sealed class DeviceAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IDeviceAuthorizationService deviceAuthorizationService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorizationHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith(DeviceAuthConstants.BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var authorizationCode = authorizationHeader[DeviceAuthConstants.BearerPrefix.Length..].Trim();
        var deviceCode = Request.Headers[DeviceAuthConstants.DeviceCodeHeader].ToString();
        var storeCode = Request.Headers[DeviceAuthConstants.StoreCodeHeader].ToString();
        var hardwareId = Request.Headers[DeviceAuthConstants.HardwareIdHeader].ToString();

        var result = await deviceAuthorizationService.ValidateAsync(
            authorizationCode,
            deviceCode,
            storeCode,
            hardwareId,
            Context.RequestAborted);

        if (result is null)
        {
            return AuthenticateResult.Fail("Invalid POS device authorization.");
        }

        var claims = new[]
        {
            new Claim(DeviceAuthConstants.DeviceCodeClaim, result.DeviceCode),
            new Claim(DeviceAuthConstants.StoreCodeClaim, result.StoreCode),
            new Claim(DeviceAuthConstants.HardwareIdClaim, result.HardwareId)
        };
        var identity = new ClaimsIdentity(claims, DeviceAuthConstants.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, DeviceAuthConstants.Scheme);

        return AuthenticateResult.Success(ticket);
    }
}
