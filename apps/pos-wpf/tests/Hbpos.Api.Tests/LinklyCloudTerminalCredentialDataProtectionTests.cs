using System.Security.Cryptography;
using PosCredentialDataProtection = Hbpos.Api.Security.LinklyCloudTerminalCredentialDataProtection;

namespace Hbpos.Api.Tests;

public sealed class LinklyCloudTerminalCredentialDataProtectionTests
{
    [Fact]
    public void Providers_using_same_directory_can_unprotect_each_others_values()
    {
        var keysPath = CreateKeysPath();
        var first = PosCredentialDataProtection.CreateProtector(
            PosCredentialDataProtection.CreateProvider(keysPath));
        var second = PosCredentialDataProtection.CreateProtector(
            PosCredentialDataProtection.CreateProvider(keysPath));

        var password = first.ProtectPassword("lane-password");
        var secret = second.ProtectSecret("terminal-secret");

        Assert.Equal("lane-password", second.UnprotectPassword(password));
        Assert.Equal("terminal-secret", first.UnprotectSecret(secret));
        Assert.NotEqual("lane-password", password);
        Assert.NotEqual("terminal-secret", secret);
    }

    [Fact]
    public void Password_and_secret_purposes_are_isolated()
    {
        var protector = PosCredentialDataProtection.CreateProtector(
            PosCredentialDataProtection.CreateProvider(CreateKeysPath()));
        var protectedPassword = protector.ProtectPassword("lane-password");

        Assert.Throws<CryptographicException>(() =>
            protector.UnprotectSecret(protectedPassword));
    }

    [Fact]
    public void Corrupted_ciphertext_is_rejected()
    {
        var protector = PosCredentialDataProtection.CreateProtector(
            PosCredentialDataProtection.CreateProvider(CreateKeysPath()));
        var protectedSecret = protector.ProtectSecret("terminal-secret");
        var corrupted = protectedSecret[..^1] + (protectedSecret[^1] == 'A' ? 'B' : 'A');

        Assert.Throws<CryptographicException>(() => protector.UnprotectSecret(corrupted));
    }

    [Fact]
    public void Pos_contract_matches_shared_application_and_version_constants()
    {
        Assert.Equal(
            BlazorApp.Shared.Security.LinklyCloudTerminalCredentialDataProtection.ApplicationName,
            Hbpos.Api.Security.LinklyCloudTerminalCredentialDataProtection.ApplicationName);
        Assert.Equal(
            BlazorApp.Shared.Security.LinklyCloudTerminalCredentialDataProtection.CurrentVersion,
            Hbpos.Api.Security.LinklyCloudTerminalCredentialDataProtection.CurrentVersion);
        Assert.Equal((byte)0, PosCredentialDataProtection.LegacyPlaintextVersion);
        Assert.Equal((byte)1, PosCredentialDataProtection.CurrentVersion);
    }

    private static string CreateKeysPath() => Path.Combine(
        Path.GetTempPath(),
        "hbpos-linkly-credential-tests",
        Guid.NewGuid().ToString("N"));
}
