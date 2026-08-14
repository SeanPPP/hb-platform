using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace BlazorApp.Api.Services;

public sealed class PosHandheldUpdatePolicyOptions
{
    public bool Enabled { get; set; }

    public string PolicyVersion { get; set; } = "none";

    public string? EasProjectName { get; set; }

    public string AndroidProfile { get; set; } = "android-internal";

    public string? AndroidMinimumSupportedVersion { get; set; }

    public int? AndroidMinimumSupportedBuild { get; set; }

    public bool AndroidRequired { get; set; }

    public string? AndroidPackageName { get; set; }

    public string? AndroidSigningCertificateSha256 { get; set; }

    public string? IosLatestVersion { get; set; }

    public string? IosLatestBuild { get; set; }

    public string? IosMinimumSupportedVersion { get; set; }

    public int? IosMinimumSupportedBuild { get; set; }

    public bool IosRequired { get; set; }

    public string IosDistribution { get; set; } = "app-store";

    public string? IosDownloadUrl { get; set; }

    public string? IosBundleIdentifier { get; set; }

    public string? IosAppStoreId { get; set; }

    public string OtaChannel { get; set; } = "pos-handheld-production";

    public bool OtaRequired { get; set; }

    public string? ReleaseMessage { get; set; }
}

public sealed partial class PosHandheldUpdatePolicyOptionsValidator
    : IValidateOptions<PosHandheldUpdatePolicyOptions>
{
    private const string AndroidProfile = "android-internal";
    private const string AndroidPackageName = "com.hbweb.poshandheld";
    private const string OtaChannel = "pos-handheld-production";
    private const long JavaScriptSafeIntegerMax = 9007199254740991;

    public ValidateOptionsResult Validate(
        string? name,
        PosHandheldUpdatePolicyOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (Normalize(options.PolicyVersion) is "" or "none")
        {
            return ValidateOptionsResult.Fail(
                "PosHandheldUpdatePolicy:PolicyVersion must identify an enabled policy."
            );
        }

        if (Normalize(options.EasProjectName).Length == 0)
        {
            return ValidateOptionsResult.Fail(
                "PosHandheldUpdatePolicy:EasProjectName is required when updates are enabled."
            );
        }

        if (!string.Equals(
                Normalize(options.AndroidProfile),
                AndroidProfile,
                StringComparison.Ordinal
            ))
        {
            return ValidateOptionsResult.Fail(
                $"PosHandheldUpdatePolicy:AndroidProfile must be {AndroidProfile}."
            );
        }

        if (!string.Equals(
                Normalize(options.AndroidPackageName),
                AndroidPackageName,
                StringComparison.Ordinal
            ))
        {
            return ValidateOptionsResult.Fail(
                $"PosHandheldUpdatePolicy:AndroidPackageName must be {AndroidPackageName}."
            );
        }

        if (!IsSha256(options.AndroidSigningCertificateSha256))
        {
            return ValidateOptionsResult.Fail(
                "PosHandheldUpdatePolicy:AndroidSigningCertificateSha256 must contain a SHA-256 fingerprint."
            );
        }

        if (!IsVersion(options.IosLatestVersion) || !IsBuild(options.IosLatestBuild))
        {
            return ValidateOptionsResult.Fail(
                "PosHandheldUpdatePolicy iOS latest version and build are required."
            );
        }

        if (!IsOptionalVersion(options.AndroidMinimumSupportedVersion)
            || !IsOptionalBuild(options.AndroidMinimumSupportedBuild)
            || !IsOptionalVersion(options.IosMinimumSupportedVersion)
            || !IsOptionalBuild(options.IosMinimumSupportedBuild))
        {
            return ValidateOptionsResult.Fail(
                "PosHandheldUpdatePolicy minimum versions and builds must be valid positive values."
            );
        }

        if (!string.Equals(
                Normalize(options.OtaChannel),
                OtaChannel,
                StringComparison.Ordinal
            ))
        {
            return ValidateOptionsResult.Fail(
                $"PosHandheldUpdatePolicy:OtaChannel must be {OtaChannel}."
            );
        }

        var distribution = PosHandheldIosUpdateIdentity.NormalizeDistribution(
            options.IosDistribution
        );
        var bundleIdentifier = PosHandheldIosUpdateIdentity.Normalize(
            options.IosBundleIdentifier
        );
        var appStoreId = PosHandheldIosUpdateIdentity.Normalize(options.IosAppStoreId);
        if (!string.Equals(
                bundleIdentifier,
                PosHandheldIosUpdateIdentity.BundleIdentifier,
                StringComparison.Ordinal
            ))
        {
            return ValidateOptionsResult.Fail(
                $"PosHandheldUpdatePolicy:IosBundleIdentifier must be {PosHandheldIosUpdateIdentity.BundleIdentifier}."
            );
        }

        if (!PosHandheldIosUpdateIdentity.IsValidAppStoreId(appStoreId))
        {
            return ValidateOptionsResult.Fail(
                "PosHandheldUpdatePolicy:IosAppStoreId must contain 5 to 20 digits."
            );
        }

        if (distribution is not ("app-store" or "testflight")
            || !PosHandheldIosUpdateIdentity.IsValidDistributionUrl(
                options.IosDownloadUrl,
                distribution,
                appStoreId
            ))
        {
            return ValidateOptionsResult.Fail(
                "PosHandheldUpdatePolicy iOS distribution URL does not match its distribution identity."
            );
        }

        if (distribution == "testflight"
            && PosHandheldIosUpdateIdentity.CanProduceRequiredDecision(options))
        {
            return ValidateOptionsResult.Fail(
                "PosHandheldUpdatePolicy TestFlight distribution must remain optional."
            );
        }

        return ValidateOptionsResult.Success;
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();

    private static bool IsVersion(string? value) =>
        Version.TryParse(Normalize(value).TrimStart('v', 'V'), out _);

    private static bool IsBuild(string? value)
    {
        var normalized = Normalize(value);
        // 配置中的 latest build 是客户端合同的一部分，禁止前导零和不精确整数。
        return BuildPattern().IsMatch(normalized)
            && long.TryParse(normalized, out var build)
            && build <= JavaScriptSafeIntegerMax;
    }

    private static bool IsOptionalVersion(string? value) =>
        Normalize(value).Length == 0 || IsVersion(value);

    private static bool IsOptionalBuild(int? value) => value is null or > 0;

    private static bool IsSha256(string? value)
    {
        var normalized = Normalize(value).Replace(":", string.Empty);
        return normalized.Length == 64 && normalized.All(char.IsAsciiHexDigit);
    }

    [GeneratedRegex("^[1-9]\\d{0,15}$", RegexOptions.CultureInvariant)]
    private static partial Regex BuildPattern();
}

internal static class PosHandheldIosUpdateIdentity
{
    internal const string BundleIdentifier = "com.hbweb.poshandheld";

    internal static string Normalize(string? value) => (value ?? string.Empty).Trim();

    internal static string NormalizeDistribution(string? value) =>
        Normalize(value).ToLowerInvariant();

    internal static bool IsValidAppStoreId(string value) =>
        value.Length is >= 5 and <= 20
        && value.All(character => character is >= '0' and <= '9');

    internal static bool CanProduceRequiredDecision(
        PosHandheldUpdatePolicyOptions options) =>
        options.IosRequired
        || Normalize(options.IosMinimumSupportedVersion).Length > 0
        || options.IosMinimumSupportedBuild is > 0;

    internal static bool IsValidDistributionUrl(
        string? value,
        string distribution,
        string appStoreId)
    {
        if (!Uri.TryCreate(Normalize(value), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var pathSegments = uri.AbsolutePath.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries
        );
        if (distribution == "app-store")
        {
            return IsAppStoreHost(uri.Host)
                && pathSegments.Length > 0
                && string.Equals(
                    pathSegments[^1],
                    $"id{appStoreId}",
                    StringComparison.Ordinal
                );
        }

        if (distribution != "testflight"
            || !string.Equals(
                uri.Host,
                "testflight.apple.com",
                StringComparison.OrdinalIgnoreCase
            )
            || !string.IsNullOrEmpty(uri.Query)
            || pathSegments.Length != 2
            || !string.Equals(pathSegments[0], "join", StringComparison.Ordinal))
        {
            return false;
        }

        var joinCode = pathSegments[1];
        return joinCode.Length is >= 4 and <= 64
            && joinCode.All(char.IsAsciiLetterOrDigit)
            && string.Equals(
                uri.AbsolutePath,
                $"/join/{joinCode}",
                StringComparison.Ordinal
            );
    }

    private static bool IsAppStoreHost(string host) =>
        string.Equals(host, "apps.apple.com", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "itunes.apple.com", StringComparison.OrdinalIgnoreCase);
}
