using Microsoft.Extensions.Options;

namespace Hbpos.Api.Services;

public sealed class PosIpadOptions
{
    public string? MinimumSupportedVersion { get; set; }

    public string? LatestVersion { get; set; }

    public bool ForceUpdate { get; set; }

    public string? AppStoreUrl { get; set; }

    public string? ReleaseMessage { get; set; }
}

public static class PosIpadAppVersionPolicy
{
    public static bool IsForceUpdateRequired(
        PosIpadOptions options,
        string? version,
        string? build,
        string? runtimeVersion)
    {
        var currentVersion = ResolveCurrentVersion(version, build, runtimeVersion);
        var minimumVersion = ParseVersion(options.MinimumSupportedVersion);
        if (minimumVersion is not null
            && (currentVersion is null || currentVersion < minimumVersion))
        {
            return true;
        }

        if (!options.ForceUpdate)
        {
            return false;
        }

        // 显式强制升级只约束仍低于目标版本的客户端，已到 latest 的设备不应被永久锁死。
        var forcedTargetVersion = ParseVersion(options.LatestVersion) ?? minimumVersion;
        return forcedTargetVersion is null
            || currentVersion is null
            || currentVersion < forcedTargetVersion;
    }

    private static Version? ResolveCurrentVersion(
        string? version,
        string? build,
        string? runtimeVersion)
    {
        var marketingVersion = ParseVersion(version);
        if (marketingVersion is not null)
        {
            return ApplyBuildNumber(marketingVersion, build);
        }

        return ParseVersion(runtimeVersion) ?? ParseBuildOnly(build);
    }

    private static Version ApplyBuildNumber(Version version, string? build)
    {
        if (version.Revision >= 0
            || !int.TryParse((build ?? string.Empty).Trim(), out var buildNumber)
            || buildNumber < 0)
        {
            return Normalize(version);
        }

        return new Version(
            version.Major,
            Math.Max(version.Minor, 0),
            Math.Max(version.Build, 0),
            buildNumber);
    }

    private static Version? ParseBuildOnly(string? build)
    {
        return int.TryParse((build ?? string.Empty).Trim(), out var buildNumber) && buildNumber >= 0
            ? new Version(0, 0, 0, buildNumber)
            : null;
    }

    private static Version? ParseVersion(string? value)
    {
        var candidate = (value ?? string.Empty).Trim();
        if (candidate.StartsWith('v') || candidate.StartsWith('V'))
        {
            candidate = candidate[1..];
        }

        return Version.TryParse(candidate, out var parsed)
            ? Normalize(parsed)
            : null;
    }

    private static Version Normalize(Version version) =>
        new(
            version.Major,
            Math.Max(version.Minor, 0),
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));
}
