using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BlazorApp.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace BlazorApp.Api.Services.MobileDeviceActivation;

public sealed record MobileDeviceAccountTokenSubject(
    string UserGuid,
    string Username,
    string Email,
    string? FullName,
    Guid BindingId,
    int DeviceRegistrationId,
    string HardwareId,
    int BindingVersion,
    IReadOnlyList<string> Roles);

public sealed record MobileDeviceIssuedToken(string AccessToken, DateTime ExpiresAtUtc);

public interface IMobileDeviceAccountTokenIssuer
{
    MobileDeviceIssuedToken Issue(MobileDeviceAccountTokenSubject subject);
}

public sealed class MobileDeviceAccountTokenIssuer(
    IConfiguration configuration,
    TimeProvider? timeProvider = null) : IMobileDeviceAccountTokenIssuer
{
    public const string TokenUse = "mobile_bound_account";
    public const string BindingIdClaim = "mobile_binding_id";
    public const string BindingVersionClaim = "mobile_binding_version";
    public const string DeviceRegistrationIdClaim = "mobile_device_registration_id";
    public const string HardwareIdClaim = "mobile_hardware_id";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public MobileDeviceIssuedToken Issue(MobileDeviceAccountTokenSubject subject)
    {
        var settings = configuration.GetSection("Jwt").Get<JwtSettings>()
            ?? throw new InvalidOperationException("Jwt configuration is missing.");
        if (string.IsNullOrWhiteSpace(settings.Key)
            || string.IsNullOrWhiteSpace(settings.Issuer)
            || string.IsNullOrWhiteSpace(settings.Audience))
        {
            throw new InvalidOperationException("Jwt configuration is incomplete.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expires = now.Add(Lifetime);
        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: BuildClaims(subject),
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),
                SecurityAlgorithms.HmacSha256));
        return new MobileDeviceIssuedToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            expires);
    }

    public static IReadOnlyList<Claim> BuildClaims(MobileDeviceAccountTokenSubject subject)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject.UserGuid),
            new(JwtRegisteredClaimNames.UniqueName, subject.Username),
            new(JwtRegisteredClaimNames.Email, subject.Email),
            new(ClaimTypes.Name, subject.Username),
            new(ClaimTypes.NameIdentifier, subject.UserGuid),
            new("uid", subject.UserGuid),
            new("userId", subject.UserGuid),
            new("userGuid", subject.UserGuid),
            new("fullName", subject.FullName ?? subject.Username),
            new("token_use", TokenUse),
            new(BindingIdClaim, subject.BindingId.ToString("N")),
            new(DeviceRegistrationIdClaim, subject.DeviceRegistrationId.ToString(CultureInfo.InvariantCulture)),
            new(HardwareIdClaim, subject.HardwareId),
            new(BindingVersionClaim, subject.BindingVersion.ToString(CultureInfo.InvariantCulture)),
        };
        claims.AddRange(subject.Roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(role => new Claim(ClaimTypes.Role, role)));
        return claims;
    }
}
