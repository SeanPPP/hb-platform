using System.Security.Claims;
using System.Text.Encodings.Web;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
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
    private const string AuthenticationFailureCodeItem =
        "Hbpos.DeviceAuthentication.FailureCode";

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

        if (result.Device is null)
        {
            Context.Items[AuthenticationFailureCodeItem] =
                result.FailureCode ?? DeviceAuthorizationFailureCodes.Invalid;
            return AuthenticateResult.Fail("Invalid POS device authorization.");
        }

        var device = result.Device;
        var claims = new[]
        {
            new Claim(DeviceAuthConstants.DeviceCodeClaim, device.DeviceCode),
            new Claim(DeviceAuthConstants.StoreCodeClaim, device.StoreCode),
            new Claim(DeviceAuthConstants.HardwareIdClaim, device.HardwareId),
            new Claim(DeviceAuthConstants.DeviceSystemClaim, device.DeviceSystem),
            new Claim(
                DeviceAuthConstants.AllowTransactionsClaim,
                device.AllowTransactions ? bool.TrueString : bool.FalseString,
                ClaimValueTypes.Boolean)
        };
        var identity = new ClaimsIdentity(claims, DeviceAuthConstants.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, DeviceAuthConstants.Scheme);

        return AuthenticateResult.Success(ticket);
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Response.HasStarted)
        {
            await base.HandleChallengeAsync(properties);
            return;
        }

        var failureCode =
            Context.Items[AuthenticationFailureCodeItem] as string
            ?? "DEVICE_AUTH_REQUIRED";
        var message = failureCode == DeviceAuthorizationFailureCodes.DeviceDisabled
            ? "POS device is disabled."
            : "POS device authorization is required.";

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/json";
        await Response.WriteAsJsonAsync(
            ApiResult<object>.Fail(failureCode, message),
            Context.RequestAborted);
    }
}
