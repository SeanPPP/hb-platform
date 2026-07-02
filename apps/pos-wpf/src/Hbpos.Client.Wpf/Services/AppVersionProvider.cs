using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Hbpos.Client.Wpf.Services;

public interface IAppVersionProvider
{
    string CurrentVersion { get; }
}

public sealed class AppVersionProvider : IAppVersionProvider
{
    internal const string VersionOverrideEnvironmentVariable = "HBPOS_WPF_APP_VERSION";

    public string CurrentVersion => ResolveCurrentVersion(
        Environment.GetEnvironmentVariable(VersionOverrideEnvironmentVariable),
        Assembly.GetEntryAssembly(),
        Assembly.GetExecutingAssembly());

    internal static string ResolveCurrentVersion(
        string? configuredVersion,
        Assembly? entryAssembly,
        Assembly executingAssembly)
    {
        var environmentVersion = NormalizeVersionText(configuredVersion);
        if (!string.IsNullOrWhiteSpace(environmentVersion))
        {
            return environmentVersion;
        }

        var assemblies = ResolveVersionAssemblies(entryAssembly, executingAssembly);
        foreach (var assembly in assemblies)
        {
            // 中文注释：发布包版本优先使用 InformationalVersion，承接 CI 注入的语义化版本。
            var informationalVersion = NormalizeVersionText(
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                return informationalVersion;
            }
        }

        foreach (var assembly in assemblies)
        {
            var assemblyVersion = assembly.GetName().Version;
            if (assemblyVersion is not null)
            {
                return FormatAssemblyVersion(assemblyVersion);
            }
        }

        return "0.0.0";
    }

    internal static string NormalizeVersionText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var candidate = trimmed;
        var metadataIndex = candidate.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            candidate = candidate[..metadataIndex];
        }

        var prereleaseIndex = candidate.IndexOf('-', StringComparison.Ordinal);
        if (prereleaseIndex >= 0)
        {
            candidate = candidate[..prereleaseIndex];
        }

        if (candidate.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[1..];
        }

        if (Version.TryParse(candidate, out var version) && version.Build >= 0)
        {
            return version.Revision >= 0
                ? $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}"
                : $"{version.Major}.{version.Minor}.{version.Build}";
        }

        // 中文注释：无法解析的人工配置仍保留，避免把灰度版本号静默降级为 0.0.0。
        return trimmed;
    }

    private static IReadOnlyList<Assembly> ResolveVersionAssemblies(Assembly? entryAssembly, Assembly executingAssembly)
    {
        if (entryAssembly is null || entryAssembly == executingAssembly)
        {
            return [executingAssembly];
        }

        return [entryAssembly, executingAssembly];
    }

    private static string FormatAssemblyVersion(Version version)
    {
        return version.Build >= 0
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : $"{version.Major}.{version.Minor}.0";
    }
}

public interface IAppUpdateChannelProvider
{
    string CurrentChannel { get; }
}

public sealed class AppUpdateChannelProvider(IConfiguration configuration) : IAppUpdateChannelProvider
{
    public const string ChannelEnvironmentVariable = "HBPOS_APP_UPDATE_CHANNEL";

    public string CurrentChannel => NormalizeChannel(
        Environment.GetEnvironmentVariable(ChannelEnvironmentVariable) ??
        configuration["AppUpdate:Channel"]);

    private static string NormalizeChannel(string? value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "production" : value.Trim();
        var chars = source
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-')
            .ToArray();
        var normalized = new string(chars).Trim('-', '.', '_');
        return string.IsNullOrWhiteSpace(normalized) ? "production" : normalized.ToLowerInvariant();
    }
}
