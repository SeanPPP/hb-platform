namespace Hbpos.Client.Wpf;

public sealed record AppStartupOptions(
    IReadOnlyList<string> Args,
    bool PreviewMode,
    string? InitialScreen,
    string? InitialCulture)
{
    public static AppStartupOptions FromArgs(IReadOnlyList<string> args)
    {
        var initialScreen = ReadOption(args, "--screen");
        var initialCulture = ReadOption(args, "--culture");
        var previewMode = args.Contains("--preview", StringComparer.OrdinalIgnoreCase) || initialScreen is not null;

        return new AppStartupOptions(args, previewMode, initialScreen, initialCulture);
    }

    private static string? ReadOption(IReadOnlyList<string> args, string name)
    {
        var prefix = name + "=";
        var inline = args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (inline is not null)
        {
            return inline[prefix.Length..];
        }

        var index = args.IndexOf(name);
        return index >= 0 && index + 1 < args.Count ? args[index + 1] : null;
    }
}

internal static class StartupArgumentExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string value)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
