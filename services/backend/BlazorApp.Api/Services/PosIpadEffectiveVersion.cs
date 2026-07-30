using System.Globalization;
using System.Text.RegularExpressions;

namespace BlazorApp.Api.Services;

internal readonly record struct PosIpadEffectiveVersion(
    int Major,
    int Minor,
    int Patch,
    int Build
) : IComparable<PosIpadEffectiveVersion>
{
    private static readonly Regex MarketingVersionPattern = new(
        "^(?<major>\\d+)(?:\\.(?<minor>\\d+))?(?:\\.(?<patch>\\d+))?$",
        RegexOptions.Compiled
    );

    internal static bool TryCreate(
        string? marketingVersion,
        string? buildNumber,
        out PosIpadEffectiveVersion version
    )
    {
        version = default;
        return TryParseMarketing(marketingVersion, out var marketing)
            && TryParseBuild(buildNumber, out var build)
            && TryCreate(marketing, build, out version);
    }

    internal static bool TryCreate(
        string? marketingVersion,
        int buildNumber,
        out PosIpadEffectiveVersion version
    )
    {
        version = default;
        return TryParseMarketing(marketingVersion, out var marketing)
            && TryCreate(marketing, buildNumber, out version);
    }

    internal static bool TryParseMarketing(
        string? value,
        out (int Major, int Minor, int Patch) marketing
    )
    {
        marketing = default;
        var match = MarketingVersionPattern.Match((value ?? string.Empty).Trim());
        if (
            !match.Success
            || !TrySegment(match, "major", out var major)
            || !TrySegment(match, "minor", out var minor)
            || !TrySegment(match, "patch", out var patch)
        )
        {
            return false;
        }

        marketing = (major, minor, patch);
        return true;
    }

    internal static bool TryParseBuild(string? value, out int build)
    {
        var normalized = (value ?? string.Empty).Trim();
        return int.TryParse(
                normalized,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out build
            )
            && build >= 0;
    }

    private static bool TryCreate(
        (int Major, int Minor, int Patch) marketing,
        int build,
        out PosIpadEffectiveVersion version
    )
    {
        version = default;
        if (build < 0)
        {
            return false;
        }

        version = new PosIpadEffectiveVersion(
            marketing.Major,
            marketing.Minor,
            marketing.Patch,
            build
        );
        return true;
    }

    private static bool TrySegment(Match match, string name, out int value)
    {
        var group = match.Groups[name];
        if (!group.Success)
        {
            value = 0;
            return true;
        }

        return int.TryParse(
                group.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value
            )
            && value >= 0;
    }

    public int CompareTo(PosIpadEffectiveVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        if (minor != 0)
        {
            return minor;
        }

        var patch = Patch.CompareTo(other.Patch);
        return patch != 0 ? patch : Build.CompareTo(other.Build);
    }

    internal int CompareMarketingTo(PosIpadEffectiveVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Major}.{Minor}.{Patch}.{Build}"
        );
}
