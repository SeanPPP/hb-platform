namespace Hbpos.Api.Tests;

public sealed class AttendanceDataProtectionComposeContractTests
{
    private const string ContainerKeysPath = "/app/App_Data/DataProtectionKeys";

    [Fact]
    public async Task PosAndBackendCompose_ShareAttendanceDataProtectionKeyRing()
    {
        var root = FindRepoRoot();
        var posComposePath = Path.Combine(root, "apps/pos-wpf/docker-compose.hotbargain.yml");
        var backendComposePath = Path.Combine(root, "services/backend/docker-compose.yml");
        var posCompose = await File.ReadAllLinesAsync(posComposePath);
        var backendCompose = await File.ReadAllLinesAsync(backendComposePath);

        Assert.Equal(ContainerKeysPath, GetConfiguredKeysPath(posCompose));
        Assert.Equal(ContainerKeysPath, GetConfiguredKeysPath(backendCompose));

        // 关键逻辑：不能只比较 compose 文本，必须按各自文件目录规范化宿主机挂载路径。
        var posHostPath = NormalizeHostKeysPath(posComposePath, posCompose);
        var backendHostPath = NormalizeHostKeysPath(backendComposePath, backendCompose);
        Assert.Equal(backendHostPath, posHostPath);
    }

    [Fact]
    public void PosProtector_UsesSharedApplicationNameAndPurposeConstants()
    {
        Assert.Equal(
            BlazorApp.Shared.Security.AttendanceQrKeyDataProtection.ApplicationName,
            Hbpos.Api.Security.AttendanceQrKeyDataProtection.ApplicationName);
        Assert.Equal(
            BlazorApp.Shared.Security.AttendanceQrKeyDataProtection.Purpose,
            Hbpos.Api.Security.AttendanceQrKeyDataProtection.Purpose);
    }

    private static string GetConfiguredKeysPath(IEnumerable<string> lines)
    {
        const string prefix = "- DataProtection__KeysPath=";
        var setting = Assert.Single(
            lines.Select(line => line.Trim()),
            line => line.StartsWith(prefix, StringComparison.Ordinal));
        return setting[prefix.Length..];
    }

    private static string NormalizeHostKeysPath(string composePath, IEnumerable<string> lines)
    {
        var suffix = $":{ContainerKeysPath}";
        var volume = Assert.Single(
            lines.Select(line => line.Trim()),
            line => line.StartsWith("- ", StringComparison.Ordinal)
                && line.EndsWith(suffix, StringComparison.Ordinal));
        var hostPath = volume[2..^suffix.Length];
        return Path.GetFullPath(hostPath, Path.GetDirectoryName(composePath)!);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "apps"))
                && Directory.Exists(Path.Combine(directory.FullName, "services")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("找不到仓库根目录");
    }
}
