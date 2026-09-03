namespace BlazorApp.Shared.Security;

/// <summary>
/// Linkly Cloud 多终端凭据的跨进程保护契约。
/// Username 是用于唯一匹配的登录标识，Password 与配对后的 Secret 必须使用独立 purpose 加密保存。
/// </summary>
public static class LinklyCloudTerminalCredentialDataProtection
{
    public const string ApplicationName = "HB.Linkly.CloudTerminalCredentials";
    public const string PasswordPurpose = "HB.Linkly.CloudTerminalCredentials.Password.v1";
    public const string SecretPurpose = "HB.Linkly.CloudTerminalCredentials.Secret.v1";
    public const byte LegacyPlaintextVersion = 0;
    public const byte CurrentVersion = 1;
}

public interface ILinklyCloudTerminalCredentialProtector
{
    string ProtectPassword(string password);

    string UnprotectPassword(string protectedPassword);

    string ProtectSecret(string secret);

    string UnprotectSecret(string protectedSecret);
}
