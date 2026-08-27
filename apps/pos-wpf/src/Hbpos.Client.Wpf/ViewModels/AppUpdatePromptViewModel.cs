using Hbpos.Contracts.AppUpdates;

namespace Hbpos.Client.Wpf.ViewModels;

internal sealed class AppUpdatePromptViewModel
{
    public AppUpdatePromptViewModel(AppUpdateCheckResponse update)
    {
        ArgumentNullException.ThrowIfNull(update);

        CurrentVersion = FormatVersion(update.CurrentVersion);
        TargetVersion = FormatVersion(update.TargetVersion);
        ReleaseNotes = ParseReleaseNotes(update.ReleaseNotes);
    }

    public string CurrentVersion { get; }

    public string TargetVersion { get; }

    public IReadOnlyList<string> ReleaseNotes { get; }

    public bool HasReleaseNotes => ReleaseNotes.Count > 0;

    internal static IReadOnlyList<string> ParseReleaseNotes(string? releaseNotes)
    {
        if (string.IsNullOrWhiteSpace(releaseNotes))
        {
            return [];
        }

        return releaseNotes
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(RemoveBulletPrefix)
            .Where(note => !string.IsNullOrWhiteSpace(note))
            .ToArray();
    }

    private static string FormatVersion(string? version)
    {
        return string.IsNullOrWhiteSpace(version)
            ? "-"
            : version.Trim();
    }

    private static string RemoveBulletPrefix(string note)
    {
        if (note.Length < 2 || !char.IsWhiteSpace(note[1]))
        {
            return note;
        }

        return note[0] is '-' or '*' or '\u2022'
            ? note[2..].Trim()
            : note;
    }
}
