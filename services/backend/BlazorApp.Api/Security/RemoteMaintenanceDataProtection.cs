using Microsoft.AspNetCore.DataProtection;

namespace BlazorApp.Api.Security;

/// <summary>复用主后端持久化 key ring，仅以独立 purpose 隔离远程维护 secret。</summary>
public static class RemoteMaintenanceDataProtection
{
    public const string PasswordPurpose = "HB.Platform.RemoteMaintenance.Password.v1";
    public const string OperationResponsePurpose = "HB.Platform.RemoteMaintenance.OperationResponse.v1";

    public static RemoteMaintenanceSecretProtector CreateProtector(IDataProtectionProvider provider) =>
        new(provider.CreateProtector(PasswordPurpose), provider.CreateProtector(OperationResponsePurpose));
}

public sealed class RemoteMaintenanceSecretProtector(
    IDataProtector password,
    IDataProtector operationResponse)
{
    public string ProtectPassword(string value) => password.Protect(value);
    public string UnprotectPassword(string value) => password.Unprotect(value);
    public string ProtectOperationResponse(string value) => operationResponse.Protect(value);
    public string UnprotectOperationResponse(string value) => operationResponse.Unprotect(value);
}
