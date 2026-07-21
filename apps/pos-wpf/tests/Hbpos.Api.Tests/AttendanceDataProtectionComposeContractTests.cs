using System.Security.Cryptography;
using Hbpos.Api.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hbpos.Api.Tests;

public sealed class AttendanceDataProtectionComposeContractTests
{
    private const string PosContainerKeysPath = "/app/App_Data/DataProtectionKeys";
    private const string AttendanceContainerKeysPath =
        "/app/App_Data/AttendanceQrDataProtectionKeys";
    private const string AttendanceHostKeysPathExpression =
        "${ATTENDANCE_QR_DATA_PROTECTION_KEYS_HOST_PATH:?required}";
    private const string LegacyProtectedKeyCutoffExpression =
        "${ATTENDANCE_QR_LEGACY_PROTECTED_KEY_CUTOFF_UTC:-}";

    [Fact]
    public async Task PosCompose_SeparatesGlobalAndAttendanceDataProtectionKeyRings()
    {
        var root = FindRepoRoot();
        var posComposePath = Path.Combine(root, "apps/pos-wpf/docker-compose.hotbargain.yml");
        var posCompose = await File.ReadAllLinesAsync(posComposePath);

        Assert.Equal(PosContainerKeysPath, GetConfiguredKeysPath(posCompose, "DataProtection__KeysPath"));
        Assert.Equal(
            AttendanceContainerKeysPath,
            GetConfiguredKeysPath(posCompose, "AttendanceQrDataProtection__KeysPath"));
        Assert.Equal(
            LegacyProtectedKeyCutoffExpression,
            GetConfiguredKeysPath(posCompose, "AttendanceQrDataProtection__LegacyProtectedKeyCutoffUtc"));
        Assert.Equal("hbpos-api-data-protection-keys", GetHostKeysPath(posCompose, PosContainerKeysPath));
        Assert.Equal(
            AttendanceHostKeysPathExpression,
            GetHostKeysPath(posCompose, AttendanceContainerKeysPath));
        Assert.DoesNotContain(
            "services/backend/data-protection-keys",
            string.Join('\n', posCompose),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AddHbposApiServices_ProductionWithoutAttendanceKeysPath_FailsClosed()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddHbposApiServices(configuration));

        Assert.Equal(
            "Production requires AttendanceQrDataProtection:KeysPath.",
            exception.Message);
    }

    [Fact]
    public void AddHbposApiServices_DevelopmentWithoutAttendanceKeysPath_UsesDefault()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddHbposApiServices(configuration);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(AttendanceQrKeyProtector));
    }

    [Fact]
    public void AddHbposApiServices_UsesIndependentGlobalAndAttendanceKeyDirectories()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            $"hbpos-data-protection-{Guid.NewGuid():N}");
        var globalKeysPath = Path.Combine(testRoot, "global");
        var attendanceKeysPath = Path.Combine(testRoot, "attendance");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DataProtection:KeysPath"] = globalKeysPath,
                    ["AttendanceQrDataProtection:KeysPath"] = attendanceKeysPath,
                })
                .Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHbposApiServices(configuration);

            using var provider = services.BuildServiceProvider();
            var globalProtector = provider
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("Hbpos.Api.Tests.Global");
            var attendanceProtector = provider.GetRequiredService<AttendanceQrKeyProtector>();

            // 关键逻辑：两个保护器分别写入独立目录，不能再依赖主 backend 的全局 key ring。
            _ = globalProtector.Protect(new byte[] { 1, 2, 3 });
            _ = attendanceProtector.Protect(new byte[32]);

            Assert.NotEmpty(Directory.EnumerateFiles(globalKeysPath, "key-*.xml"));
            Assert.NotEmpty(Directory.EnumerateFiles(attendanceKeysPath, "key-*.xml"));
            Assert.Throws<CryptographicException>(() =>
                globalProtector.Unprotect(Convert.FromBase64String(attendanceProtector.Protect(new byte[32]))));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
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

    private static string GetConfiguredKeysPath(IEnumerable<string> lines, string settingName)
    {
        var prefix = $"- {settingName}=";
        var setting = Assert.Single(
            lines.Select(line => line.Trim()),
            line => line.StartsWith(prefix, StringComparison.Ordinal));
        return setting[prefix.Length..];
    }

    private static string GetHostKeysPath(IEnumerable<string> lines, string containerKeysPath)
    {
        var suffix = $":{containerKeysPath}";
        var volume = Assert.Single(
            lines.Select(line => line.Trim()),
            line => line.StartsWith("- ", StringComparison.Ordinal)
                && line.EndsWith(suffix, StringComparison.Ordinal));
        return volume[2..^suffix.Length];
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
