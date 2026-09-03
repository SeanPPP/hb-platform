using LinklyCredentialDataProtection = BlazorApp.Api.Security.LinklyCloudTerminalCredentialDataProtection;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class LinklyCloudTerminalCredentialDataProtectionTests : IDisposable
{
    private readonly string _keysPath = Path.Combine(
        Path.GetTempPath(),
        $"hb-linkly-cloud-credential-keys-{Guid.NewGuid():N}"
    );

    [Fact]
    public void Protectors_with_the_same_ring_can_decrypt_each_other_and_isolate_purposes()
    {
        var first = LinklyCredentialDataProtection.CreateProtector(
            LinklyCredentialDataProtection.CreateProvider(_keysPath)
        );
        var second = LinklyCredentialDataProtection.CreateProtector(
            LinklyCredentialDataProtection.CreateProvider(_keysPath)
        );

        var protectedPassword = first.ProtectPassword("password-sentinel");
        var protectedSecret = first.ProtectSecret("secret-sentinel");

        Assert.Equal("password-sentinel", second.UnprotectPassword(protectedPassword));
        Assert.Equal("secret-sentinel", second.UnprotectSecret(protectedSecret));
        Assert.ThrowsAny<Exception>(() => second.UnprotectSecret(protectedPassword));
        Assert.ThrowsAny<Exception>(() => second.UnprotectPassword(protectedSecret));
    }

    [Fact]
    public void Corrupt_ciphertext_fails_closed_without_returning_a_value()
    {
        var protector = LinklyCredentialDataProtection.CreateProtector(
            LinklyCredentialDataProtection.CreateProvider(_keysPath)
        );

        Assert.ThrowsAny<Exception>(() => protector.UnprotectPassword("not-a-valid-linkly-ciphertext"));
        Assert.Equal(1, LinklyCredentialDataProtection.CurrentVersion);
        Assert.Equal(0, LinklyCredentialDataProtection.LegacyPlaintextVersion);
    }

    [Fact]
    public void Program_and_compose_require_a_dedicated_shared_linkly_credential_ring()
    {
        var repoRoot = FindRepoRoot();
        var program = File.ReadAllText(Path.Combine(
            repoRoot,
            "services/backend/BlazorApp.Api/Program.cs"
        ));
        var compose = File.ReadAllText(Path.Combine(repoRoot, "services/backend/docker-compose.yml"));

        Assert.Contains("LinklyCloudCredentialDataProtection:KeysPath", program);
        Assert.Contains("builder.Environment.IsProduction()", program);
        Assert.Contains("LinklyCloudCredentialDataProtection__KeysPath", compose);
        Assert.Contains("LINKLY_CLOUD_CREDENTIAL_DATA_PROTECTION_KEYS_HOST_PATH:?required", compose);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "services/backend/BlazorApp.Api/Program.cs"
                )))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到仓库根目录");
    }

    public void Dispose()
    {
        if (Directory.Exists(_keysPath))
        {
            Directory.Delete(_keysPath, recursive: true);
        }
    }
}
