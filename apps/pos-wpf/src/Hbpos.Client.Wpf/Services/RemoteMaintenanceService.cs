using System.IO;
using Hbpos.Client.Wpf.Models;
using Hbpos.RemoteMaintenance.Setup;

namespace Hbpos.Client.Wpf.Services;

public sealed record RemoteMaintenanceProvisionResult(
    bool Succeeded,
    string Message,
    RemoteMaintenanceStatus Status);

public interface IRemoteMaintenanceService
{
    Task<RemoteMaintenanceStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<RemoteMaintenanceProvisionResult> InstallAsync(
        PosSessionState session,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// WPF 仅调用这个业务服务；HTTP、下载、DPAPI journal 和安装事务均不进入 ViewModel。
/// </summary>
public sealed class RemoteMaintenanceService(
    IRemoteMaintenanceApiClient apiClient,
    IRemoteMaintenanceArtifactDownloader artifactDownloader,
    IRemoteMaintenanceInstaller installer,
    IRemoteMaintenanceUacHelperLauncher uacHelperLauncher,
    RemoteMaintenanceJournal journal,
    IRemoteMaintenanceSecretProtector secretProtector) : IRemoteMaintenanceService
{
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly string _helperPath = Path.Combine(AppContext.BaseDirectory, "Hbpos.RemoteMaintenance.Setup.exe");
    private readonly string _journalPath = journal.FilePath;

    public Task<RemoteMaintenanceStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        installer.GetStatusAsync(cancellationToken);

    public async Task<RemoteMaintenanceProvisionResult> InstallAsync(
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(session.StoreCode) || string.IsNullOrWhiteSpace(session.DeviceCode))
        {
            return new RemoteMaintenanceProvisionResult(
                false,
                "当前设备尚未完成激活，无法配置远程维护。",
                await GetStatusAsync(cancellationToken));
        }

        await _operationGate.WaitAsync(cancellationToken);
        RemoteMaintenanceInstallationResult? installation = null;
        var operationId = Guid.Empty;
        try
        {
            var existing = await journal.ReadAsync(cancellationToken);
            if (existing?.State == RemoteMaintenanceOperationState.InstalledPendingCommit)
            {
                return await ResumePendingCommitAsync(existing, cancellationToken);
            }

            if (existing?.State == RemoteMaintenanceOperationState.Committed)
            {
                var existingConfigureCode = await uacHelperLauncher.RunAsync(
                    _helperPath,
                    _journalPath,
                    existing.OperationId,
                    "configure",
                    cancellationToken);
                return new RemoteMaintenanceProvisionResult(
                    existingConfigureCode == 0,
                    existingConfigureCode == 0 ? "远程维护状态服务已恢复。" : "远程维护状态服务恢复失败，请稍后重试。",
                    await GetStatusAsync(cancellationToken));
            }

            operationId = existing?.State is RemoteMaintenanceOperationState.Prepared or RemoteMaintenanceOperationState.InstalledPendingCommit
                ? existing.OperationId
                : Guid.NewGuid();
            var password = existing is null
                ? RemoteMaintenancePasswordGenerator.Create()
                : secretProtector.Unprotect(existing.ProtectedPassword);
            if (string.IsNullOrWhiteSpace(password))
            {
                // Prepared 记录代表同一项已向中心登记的操作；无法解密原密码时
                // 不能生成新密码/新 operationId，否则会留下中心孤儿操作。
                return new RemoteMaintenanceProvisionResult(
                    false,
                    "远程维护恢复记录无法解密，请联系管理员清理后重试。",
                    await GetSafeStatusAsync());
            }

            var prepare = await apiClient.PrepareAsync(
                new RemoteMaintenancePrepareRequest(operationId, Environment.MachineName),
                cancellationToken);
            if (prepare.OperationId != operationId)
            {
                throw new RemoteMaintenanceApiException("远程维护操作标识不一致。", 409, "REMOTE_MAINTENANCE_OPERATION_CONFLICT");
            }

            await journal.WriteAsync(
                new RemoteMaintenanceJournalState(
                    operationId,
                    prepare.DeviceId,
                    RemoteMaintenanceOperationState.Prepared,
                    string.Empty,
                    prepare.ArtifactManifest.RustDesk.Version,
                    secretProtector.Protect(password),
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    prepare.Config,
                    null,
                    null,
                    null,
                    prepare.ArtifactManifest),
                cancellationToken);

            var dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HBPOS",
                "RemoteMaintenance",
                operationId.ToString("N"));
            var rustDeskPath = await artifactDownloader.DownloadAndVerifyAsync(
                prepare.ArtifactManifest.RustDesk,
                dataDirectory,
                cancellationToken);
            var agentPath = await artifactDownloader.DownloadAndVerifyAsync(
                prepare.ArtifactManifest.StatusAgent,
                dataDirectory,
                cancellationToken);

            await journal.WriteAsync(
                new RemoteMaintenanceJournalState(
                    operationId,
                    prepare.DeviceId,
                    RemoteMaintenanceOperationState.Prepared,
                    string.Empty,
                    prepare.ArtifactManifest.RustDesk.Version,
                    secretProtector.Protect(password),
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    prepare.Config,
                    rustDeskPath,
                    agentPath,
                    dataDirectory,
                    prepare.ArtifactManifest),
                cancellationToken);

            // helper 会把实际触及的服务写回 journal；预检查拒绝时两个标志保持 false，
            // 部分安装失败时则由 helper 在第一次修改前写入对应的清理标志。
            installation = new RemoteMaintenanceInstallationResult(false, false, string.Empty, prepare.ArtifactManifest.RustDesk.Version, dataDirectory);
            var helperCode = await uacHelperLauncher.RunAsync(_helperPath, _journalPath, operationId, "install", cancellationToken);
            if (helperCode != 0)
            {
                throw new InvalidOperationException("远程维护 UAC 安装未完成。");
            }
            // helper 以管理员身份运行，直接由它读取 RustDesk ID 并回写 journal；普通用户
            // 不再依赖 RustDesk IPC/配置权限，也不会因 runas 切换 profile 而读错位置。
            var installedState = await journal.ReadAsync(cancellationToken);
            var rustdeskId = installedState?.RustdeskId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rustdeskId))
            {
                throw new InvalidOperationException("RustDesk ID 读取失败。");
            }
            installation = new RemoteMaintenanceInstallationResult(true, true, rustdeskId, prepare.ArtifactManifest.RustDesk.Version, dataDirectory);
            await journal.WriteAsync(
                new RemoteMaintenanceJournalState(
                    operationId,
                    prepare.DeviceId,
                    RemoteMaintenanceOperationState.InstalledPendingCommit,
                    rustdeskId,
                    prepare.ArtifactManifest.RustDesk.Version,
                    secretProtector.Protect(password),
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    prepare.Config,
                    rustDeskPath,
                    agentPath,
                    dataDirectory,
                    prepare.ArtifactManifest,
                    true,
                    true),
                cancellationToken);

            var commit = await apiClient.CommitAsync(
                new RemoteMaintenanceCommitRequest(
                    operationId,
                    rustdeskId,
                    prepare.ArtifactManifest.RustDesk.Version,
                    password),
                cancellationToken);
            await journal.WriteAsync(
                new RemoteMaintenanceJournalState(
                    operationId,
                    prepare.DeviceId,
                    RemoteMaintenanceOperationState.Committed,
                    rustdeskId,
                    prepare.ArtifactManifest.RustDesk.Version,
                    secretProtector.Protect(password),
                    secretProtector.Protect(commit.MonitorToken),
                    commit.HeartbeatUrl,
                    DateTimeOffset.UtcNow,
                    prepare.Config,
                    rustDeskPath,
                    agentPath,
                    dataDirectory,
                    prepare.ArtifactManifest,
                    true,
                    true),
                cancellationToken);
            var configureCode = await uacHelperLauncher.RunAsync(_helperPath, _journalPath, operationId, "configure", cancellationToken);
            if (configureCode != 0)
            {
                throw new InvalidOperationException("远程维护状态服务配置未完成。");
            }
            return new RemoteMaintenanceProvisionResult(
                true,
                "远程维护已完成配置，状态服务将在后台发送心跳。",
                new RemoteMaintenanceStatus(true, rustdeskId, prepare.ArtifactManifest.RustDesk.Version, "running"));
        }
        catch (OperationCanceledException)
        {
            if (installation is not null && operationId != Guid.Empty)
            {
                try { await uacHelperLauncher.RunAsync(_helperPath, _journalPath, operationId, "fail-closed", CancellationToken.None); }
                catch { /* 取消也不能把未提交的无人值守服务静默留下，helper 失败由下一次恢复读取 journal。 */ }
            }
            throw;
        }
        catch (Exception ex)
        {
            if (installation is not null)
            {
                try
                {
                    await uacHelperLauncher.RunAsync(_helperPath, _journalPath, operationId, "fail-closed", CancellationToken.None);
                }
                catch
                {
                    // fail closed 尽力执行；保留本地 journal 让后续人工重试可见。
                }
            }

            // 状态提示只给用户可理解的固定文案；异常原文可能包含服务器细节或路径。
            return new RemoteMaintenanceProvisionResult(
                false,
                ex is RemoteMaintenanceApiException api && api.StatusCode is 401 or 403
                    ? "远程维护授权已失效，请重新激活设备后重试。"
                    : "远程维护配置未完成，请稍后重试。",
                await GetSafeStatusAsync());
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<RemoteMaintenanceProvisionResult> ResumePendingCommitAsync(
        RemoteMaintenanceJournalState state,
        CancellationToken cancellationToken)
    {
        if (state.Config is null || state.ArtifactManifest is null ||
            state.RustDeskArtifactPath is null || state.StatusAgentArtifactPath is null ||
            string.IsNullOrWhiteSpace(state.RustdeskId) || string.IsNullOrWhiteSpace(state.ClientVersion))
        {
            return new RemoteMaintenanceProvisionResult(false, "远程维护恢复记录不完整，请重新安装。", await GetSafeStatusAsync());
        }

        var password = secretProtector.Unprotect(state.ProtectedPassword);
        if (string.IsNullOrWhiteSpace(password))
        {
            return new RemoteMaintenanceProvisionResult(false, "远程维护恢复记录无法解密，请重新安装。", await GetSafeStatusAsync());
        }

        var commit = await apiClient.CommitAsync(
            new RemoteMaintenanceCommitRequest(state.OperationId, state.RustdeskId, state.ClientVersion, password),
            cancellationToken);
        await journal.WriteAsync(state with
        {
            State = RemoteMaintenanceOperationState.Committed,
            ProtectedMonitorToken = secretProtector.Protect(commit.MonitorToken),
            HeartbeatUrl = commit.HeartbeatUrl,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        }, cancellationToken);
        var helperCode = await uacHelperLauncher.RunAsync(_helperPath, _journalPath, state.OperationId, "configure", cancellationToken);
        return new RemoteMaintenanceProvisionResult(
            helperCode == 0,
            helperCode == 0 ? "远程维护已恢复。" : "远程维护已登记，但状态服务待恢复。",
            await GetSafeStatusAsync());
    }

    private async Task<RemoteMaintenanceStatus> GetSafeStatusAsync()
    {
        try { return await GetStatusAsync(CancellationToken.None); }
        catch { return new RemoteMaintenanceStatus(false, string.Empty, string.Empty, "checkFailed", "无法读取本机服务状态。"); }
    }
}
