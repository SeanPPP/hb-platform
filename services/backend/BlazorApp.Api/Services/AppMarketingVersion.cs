using System.Text.RegularExpressions;

namespace BlazorApp.Api.Services;

internal readonly record struct AppMarketingVersion(
    int Major,
    int Minor,
    int Patch,
    int Revision
) : IComparable<AppMarketingVersion>
{
    private static readonly Regex Pattern = new(
        "^v?(?<major>\\d+)(?:\\.(?<minor>\\d+))?(?:\\.(?<patch>\\d+))?(?:\\.(?<revision>\\d+))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public static bool TryParse(string? value, out AppMarketingVersion version)
    {
        version = default;
        var match = Pattern.Match((value ?? string.Empty).Trim());
        if (!match.Success)
        {
            return false;
        }

        if (
            !TrySegment(match, "major", out var major)
            || !TrySegment(match, "minor", out var minor)
            || !TrySegment(match, "patch", out var patch)
            || !TrySegment(match, "revision", out var revision)
        )
        {
            return false;
        }

        version = new AppMarketingVersion(major, minor, patch, revision);
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

        return int.TryParse(group.Value, out value) && value >= 0;
    }

    public int CompareTo(AppMarketingVersion other)
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
        return patch != 0 ? patch : Revision.CompareTo(other.Revision);
    }
}
