namespace Hbpos.RemoteMaintenance.Setup;

// 固定故障码同时用于普通用户预检和提权 helper 退出码，避免向界面透传异常原文。
public enum RemoteMaintenanceSetupError
{
    InstallationLocationInvalid = 10,
    ComponentsMissing = 11,
    InstallationPermissionsInvalid = 12
}

public sealed class RemoteMaintenanceSetupException(RemoteMaintenanceSetupError error)
    : Exception("远程维护安装环境检查未通过。")
{
    public RemoteMaintenanceSetupError Error { get; } = error;
}
