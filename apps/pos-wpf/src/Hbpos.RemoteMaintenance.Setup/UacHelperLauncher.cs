using System.Diagnostics;

namespace Hbpos.RemoteMaintenance.Setup;

public interface IRemoteMaintenanceUacHelperLauncher
{
    Task<int> RunAsync(string helperPath, string journalPath, Guid operationId, string stage, CancellationToken cancellationToken = default);
}

public sealed class WindowsRemoteMaintenanceUacHelperLauncher : IRemoteMaintenanceUacHelperLauncher
{
    public async Task<int> RunAsync(string helperPath, string journalPath, Guid operationId, string stage, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(helperPath) || !File.Exists(helperPath))
        {
            throw new FileNotFoundException("远程维护 UAC helper 不存在。", helperPath);
        }

        if (stage is not ("install" or "configure" or "fail-closed")) throw new ArgumentException("无效的 helper 阶段。", nameof(stage));
        if (!Path.IsPathFullyQualified(journalPath)) throw new ArgumentException("journal 路径必须是绝对路径。", nameof(journalPath));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = helperPath,
                // 传入原始用户 profile 的 journal，避免 runas 后切换到另一管理员的 LocalAppData。
                // journal 路径不是秘密；密码仍只在 machine-DPAPI journal 中保存。
                Arguments = $"--operation {operationId:D} --stage {stage} --journal {Quote(journalPath)}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };
        // ShellExecute 可能等待 UAC 决策；放到工作线程，保持下载完成提示与界面可刷新。
        await Task.Run(() => process.Start(), cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
