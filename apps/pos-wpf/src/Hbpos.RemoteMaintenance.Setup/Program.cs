namespace Hbpos.RemoteMaintenance.Setup;

using System.Text.Json;

/// <summary>
/// 发布包中的 UAC helper 入口。真实安装请求应通过受保护的本地 journal 传递，
/// 命令行只携带 operationId 与原用户 journal 路径，不携带密码/token。
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 6 || !string.Equals(args[0], "--operation", StringComparison.Ordinal) ||
            !string.Equals(args[2], "--stage", StringComparison.Ordinal) ||
            args[3] is not ("install" or "configure" or "fail-closed") ||
            !string.Equals(args[4], "--journal", StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(args[5]))
        {
            return 2;
        }

        if (!Guid.TryParse(args[1], out var operationId)) return 2;
        if (!IsSafeJournalPath(args[5])) return Emit(false, args[3], operationId, 2);
        try
        {
            var journal = new RemoteMaintenanceJournal(
                args[5],
                new DpapiRemoteMaintenanceSecretProtector());
            var state = journal.ReadAsync().GetAwaiter().GetResult();
            if (state is null || state.OperationId != operationId || state.Config is null ||
                state.RustDeskArtifactPath is null || state.StatusAgentArtifactPath is null ||
                state.DataDirectory is null || state.ArtifactManifest is null)
            {
                return Emit(false, args[3], operationId, 3);
            }

            var installer = new WindowsRemoteMaintenanceInstaller(
                new WindowsRemoteMaintenanceCommandRunner(),
                new WindowsRemoteMaintenanceServiceControl(new WindowsRemoteMaintenanceCommandRunner()));
            if (args[3] == "install")
            {
                var password = new DpapiRemoteMaintenanceSecretProtector().Unprotect(state.ProtectedPassword);
                if (string.IsNullOrWhiteSpace(password)) return Emit(false, "install", operationId, 3);
                var prepare = new RemoteMaintenancePrepareResponse(
                    state.OperationId,
                    state.DeviceId,
                    state.Config,
                    state.ArtifactManifest!);
                var result = installer.InstallAsync(
                    new RemoteMaintenanceInstallationRequest(
                        prepare,
                        state.RustDeskArtifactPath,
                        state.StatusAgentArtifactPath,
                        password,
                        state.RustdeskId,
                        state.ClientVersion,
                        state.DataDirectory)).GetAwaiter().GetResult();
                // InstallAsync 只在受保护 Program Files EXE 上完成 --get-id，并将实际 ID
                // 放入结果；helper 不得再次执行 journal 指向的用户可写安装包。
                var rustdeskId = result.RustdeskId;
                if (string.IsNullOrWhiteSpace(rustdeskId)) return Emit(false, "install", operationId, 4);
                journal.WriteAsync(state with
                {
                    State = RemoteMaintenanceOperationState.InstalledPendingCommit,
                    RustdeskId = rustdeskId,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                }).GetAwaiter().GetResult();
                return Emit(result.RustDeskInstalled && result.StatusAgentInstalled, "install", operationId,
                    result.RustDeskInstalled && result.StatusAgentInstalled ? 0 : 4);
            }

            if (args[3] == "fail-closed")
            {
                installer.FailClosedAsync(new RemoteMaintenanceInstallationResult(
                    true, true, state.RustdeskId, state.ClientVersion, state.DataDirectory)).GetAwaiter().GetResult();
                return Emit(true, "fail-closed", operationId, 0);
            }

            if (state.ProtectedMonitorToken is null || state.HeartbeatUrl is null) return Emit(false, "configure", operationId, 3);
            var monitorToken = new DpapiRemoteMaintenanceSecretProtector().Unprotect(state.ProtectedMonitorToken);
            if (string.IsNullOrWhiteSpace(monitorToken)) return Emit(false, "configure", operationId, 3);
            var configurePrepare = new RemoteMaintenancePrepareResponse(
                state.OperationId,
                state.DeviceId,
                state.Config,
                state.ArtifactManifest!);

            installer.ConfigureStatusAgentAsync(
                configurePrepare,
                new RemoteMaintenanceCommitResponse(state.DeviceId, monitorToken, state.HeartbeatUrl),
                state.RustdeskId,
                state.ClientVersion).GetAwaiter().GetResult();
            return Emit(true, "configure", operationId, 0);
        }
        catch
        {
            // helper 输出只含阶段与退出码，绝不暴露密码、token、异常原文或命令行参数。
            return Emit(false, args[3], operationId, 4);
        }
    }

    private static int Emit(bool ok, string stage, Guid operationId, int exitCode)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { ok, stage, operationId, exitCode }));
        return exitCode;
    }

    private static bool IsSafeJournalPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(path), "remote-maintenance.journal", StringComparison.OrdinalIgnoreCase))
            return false;

        var parent = Directory.GetParent(path);
        if (parent is null || !string.Equals(parent.Name, "Hbpos.Client", StringComparison.OrdinalIgnoreCase)) return false;
        for (var current = new DirectoryInfo(parent.FullName); current is not null; current = current.Parent)
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
        }

        return File.Exists(path);
    }

}
