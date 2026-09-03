using BlazorApp.Shared.Security;
using Microsoft.AspNetCore.DataProtection;

namespace BlazorApp.Api.Security;

public static class LinklyCloudTerminalCredentialDataProtection
{
    public static string ApplicationName =>
        BlazorApp.Shared.Security.LinklyCloudTerminalCredentialDataProtection.ApplicationName;

    public static byte LegacyPlaintextVersion =>
        BlazorApp.Shared.Security.LinklyCloudTerminalCredentialDataProtection.LegacyPlaintextVersion;

    public static byte CurrentVersion =>
        BlazorApp.Shared.Security.LinklyCloudTerminalCredentialDataProtection.CurrentVersion;

    public static IDataProtectionProvider CreateProvider(string keysPath)
    {
        if (string.IsNullOrWhiteSpace(keysPath) || !Path.IsPathRooted(keysPath))
        {
            throw new ArgumentException("Linkly Cloud 凭据 Data Protection 路径必须是绝对路径", nameof(keysPath));
        }

        Directory.CreateDirectory(keysPath);
        return DataProtectionProvider.Create(
            new DirectoryInfo(keysPath),
            builder => builder.SetApplicationName(ApplicationName));
    }

    public static ILinklyCloudTerminalCredentialProtector CreateProtector(
        IDataProtectionProvider provider
    ) => new LinklyCloudTerminalCredentialProtector(
        provider.CreateProtector(
            BlazorApp.Shared.Security.LinklyCloudTerminalCredentialDataProtection.PasswordPurpose),
        provider.CreateProtector(
            BlazorApp.Shared.Security.LinklyCloudTerminalCredentialDataProtection.SecretPurpose)
    );
}

public sealed class LinklyCloudTerminalCredentialProtector(
    IDataProtector passwordProtector,
    IDataProtector secretProtector
) : ILinklyCloudTerminalCredentialProtector
{
    public string ProtectPassword(string password) => passwordProtector.Protect(password);

    public string UnprotectPassword(string protectedPassword) => passwordProtector.Unprotect(protectedPassword);

    public string ProtectSecret(string secret) => secretProtector.Protect(secret);

    public string UnprotectSecret(string protectedSecret) => secretProtector.Unprotect(protectedSecret);
}
