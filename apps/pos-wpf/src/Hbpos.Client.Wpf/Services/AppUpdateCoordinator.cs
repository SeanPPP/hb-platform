using System.Net.Http;
using System.Text.Json;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Contracts.AppUpdates;

namespace Hbpos.Client.Wpf.Services;

public enum AppUpdateCoordinatorStatus
{
    NoUpdate,
    AlreadyRunning,
    CheckFailed,
    PolicyFailed,
    OptionalDeclined,
    OptionalReady,
    ForceReady,
    ForcePendingInstall,
    DownloadFailed,
    InstallFailed,
    Installed
}

public sealed record AppUpdateCoordinatorResult(
    AppUpdateCoordinatorStatus Status,
    string StatusKey,
    object[] StatusArgs)
{
    public static AppUpdateCoordinatorResult NoUpdate() => FromStatus(AppUpdateCoordinatorStatus.NoUpdate);

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public static AppUpdateCoordinatorResult CheckFailed(
        string? errorCode = null,
        string? errorMessage = null)
    {
        return FromStatus(AppUpdateCoordinatorStatus.CheckFailed) with
        {
            ErrorCode = NormalizeDetail(errorCode),
            ErrorMessage = NormalizeDetail(errorMessage)
        };
    }

    public static AppUpdateCoordinatorResult PolicyFailed(
        string? errorCode = null,
        string? errorMessage = null)
    {
        return FromStatus(AppUpdateCoordinatorStatus.PolicyFailed) with
        {
            ErrorCode = NormalizeDetail(errorCode),
            ErrorMessage = NormalizeDetail(errorMessage)
        };
    }

    public static AppUpdateCoordinatorResult FromStatus(
        AppUpdateCoordinatorStatus status,
        params object[] args)
    {
        return new AppUpdateCoordinatorResult(status, StatusKeyFor(status), args);
    }

    private static string StatusKeyFor(AppUpdateCoordinatorStatus status)
    {
        return status switch
        {
            AppUpdateCoordinatorStatus.NoUpdate => "settings.status.appUpdateLatest",
            AppUpdateCoordinatorStatus.AlreadyRunning => "settings.status.appUpdateAlreadyRunning",
            AppUpdateCoordinatorStatus.CheckFailed => "settings.status.appUpdateCheckFailed",
            AppUpdateCoordinatorStatus.PolicyFailed => "settings.status.appUpdatePolicyFailed",
            AppUpdateCoordinatorStatus.OptionalDeclined => "settings.status.appUpdateOptionalDeclined",
            AppUpdateCoordinatorStatus.OptionalReady => "settings.status.appUpdateReady",
            AppUpdateCoordinatorStatus.ForceReady => "settings.status.appUpdateForceReady",
            AppUpdateCoordinatorStatus.ForcePendingInstall => "settings.status.appUpdateForceReady",
            AppUpdateCoordinatorStatus.DownloadFailed => "settings.status.appUpdateDownloadFailed",
            AppUpdateCoordinatorStatus.InstallFailed => "settings.status.appUpdateInstallFailed",
            AppUpdateCoordinatorStatus.Installed => "settings.status.appUpdateInstalling",
            _ => "settings.status.appUpdateCheckFailed"
        };
    }

    private static string? NormalizeDetail(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}

public interface IAppUpdateCoordinator
{
    Task<AppUpdateCoordinatorResult> CheckForUpdatesAsync(
        bool manual,
        CancellationToken cancellationToken = default);
}

public sealed class AppUpdateCoordinator(
    IAppVersionProvider versionProvider,
    IAppUpdateApiClient apiClient,
    IAppUpdateDownloadService downloadService,
    IAppUpdateInstallerLauncher installerLauncher,
    IAppUpdateInstallSafetyGuard installSafetyGuard,
    IAppUpdatePromptService promptService,
    AppUpdateState state,
    IApplicationExitService exitService,
    IAppUpdateChannelProvider channelProvider) : IAppUpdateCoordinator
{
    private const string ActiveTransactionStatusKey = "appUpdate.install.activeTransaction";
    private static readonly StringComparison ErrorCodeComparison = StringComparison.OrdinalIgnoreCase;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AppUpdateCoordinatorResult> CheckForUpdatesAsync(
        bool manual,
        CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return AppUpdateCoordinatorResult.FromStatus(AppUpdateCoordinatorStatus.AlreadyRunning);
        }

        try
        {
            return await CheckForUpdatesCoreAsync(manual, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AppUpdateCoordinatorResult> CheckForUpdatesCoreAsync(
        bool manual,
        CancellationToken cancellationToken)
    {
        AppUpdateCheckResponse update;
        try
        {
            update = await apiClient.CheckAsync(
                new AppUpdateCheckRequest
                {
                    CurrentVersion = versionProvider.CurrentVersion,
                    Channel = channelProvider.CurrentChannel
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            if (manual)
            {
                state.SetStatus("settings.status.appUpdateCheckFailed");
            }

            return AppUpdateCoordinatorResult.CheckFailed();
        }

        if (update.CheckFailed)
        {
            if (IsPolicyCheckFailure(update))
            {
                if (manual)
                {
                    state.SetStatus("settings.status.appUpdatePolicyFailed");
                }

                return AppUpdateCoordinatorResult.PolicyFailed(update.ErrorCode, update.ErrorMessage);
            }

            // 中文注释：本地 API / 更新中心的 HTTP、空响应、合同校验失败都属于“检查失败”，必须保留给启动重试逻辑识别。
            if (manual)
            {
                state.SetStatus("settings.status.appUpdateCheckFailed");
            }

            return AppUpdateCoordinatorResult.CheckFailed(update.ErrorCode, update.ErrorMessage);
        }

        if (!update.UpdateAvailable)
        {
            if (manual)
            {
                state.SetStatus("settings.status.appUpdateLatest");
            }

            return AppUpdateCoordinatorResult.NoUpdate();
        }

        if (!update.ForceUpdate)
        {
            return await HandleOptionalUpdateAsync(update, manual, cancellationToken);
        }

        return await HandleForceUpdateAsync(update, cancellationToken);
    }

    private async Task<AppUpdateCoordinatorResult> HandleOptionalUpdateAsync(
        AppUpdateCheckResponse update,
        bool manual,
        CancellationToken cancellationToken)
    {
        if (!await promptService.ConfirmOptionalDownloadAndInstallAsync(update, cancellationToken))
        {
            if (manual)
            {
                state.SetStatus("settings.status.appUpdateOptionalDeclined");
            }

            return AppUpdateCoordinatorResult.FromStatus(AppUpdateCoordinatorStatus.OptionalDeclined);
        }

        var progress = new AppUpdateProgressSink(state.UpdateDownloadProgress);
        var download = await downloadService.DownloadAsync(update, progress, cancellationToken);
        if (!download.Success || string.IsNullOrWhiteSpace(download.FilePath))
        {
            if (manual)
            {
                state.SetStatus("settings.status.appUpdateDownloadFailed", download.ErrorMessage ?? string.Empty);
            }

            return AppUpdateCoordinatorResult.FromStatus(
                AppUpdateCoordinatorStatus.DownloadFailed,
                download.ErrorMessage ?? string.Empty);
        }

        // 中文注释：可选更新安装动作晚于检查流程，不能捕获检查流程的取消令牌。
        Func<Task<ProcessLaunchResult>> installAsync = () => installerLauncher.LaunchAsync(download.FilePath, update, CancellationToken.None);
        state.ShowOptionalUpdateReady(update, download.FilePath, installAsync);
        var launchResult = await installAsync();
        state.ApplyInstallFailure(launchResult);
        if (launchResult.Success)
        {
            state.ClearOptionalUpdateAfterSuccessfulInstall();
        }

        return launchResult.Success
            ? AppUpdateCoordinatorResult.FromStatus(AppUpdateCoordinatorStatus.Installed)
            : AppUpdateCoordinatorResult.FromStatus(
                AppUpdateCoordinatorStatus.InstallFailed,
                ResolveInstallFailureMessage(launchResult));
    }

    private async Task<AppUpdateCoordinatorResult> HandleForceUpdateAsync(
        AppUpdateCheckResponse update,
        CancellationToken cancellationToken)
    {
        if (!installSafetyGuard.CanInstallUpdate(out var statusKey, out var statusArgs))
        {
            // 中文注释：交易未清空时不能进入全局阻断遮罩，否则用户无法完成或取消当前交易。
            state.ShowForceUpdatePendingInstall(
                update,
                string.Empty,
                ResumeForceUpdateAfterTransactionAsync,
                statusKey,
                statusArgs);
            return new AppUpdateCoordinatorResult(
                AppUpdateCoordinatorStatus.ForcePendingInstall,
                statusKey,
                statusArgs);
        }

        return await DownloadForceUpdateAsync(update, cancellationToken);

        async Task<ProcessLaunchResult> ResumeForceUpdateAfterTransactionAsync()
        {
            if (!installSafetyGuard.CanInstallUpdate(out var resumeStatusKey, out var resumeStatusArgs))
            {
                state.ShowForceUpdatePendingInstall(
                    update,
                    string.Empty,
                    ResumeForceUpdateAfterTransactionAsync,
                    resumeStatusKey,
                    resumeStatusArgs);
                return ProcessLaunchResult.Fail(null, resumeStatusKey, resumeStatusArgs);
            }

            var downloadResult = await DownloadForceUpdateAsync(update, CancellationToken.None);
            return ToProcessLaunchResult(downloadResult);
        }
    }

    private async Task<AppUpdateCoordinatorResult> DownloadForceUpdateAsync(
        AppUpdateCheckResponse update,
        CancellationToken cancellationToken)
    {
        // 中文注释：只有安装安全守卫放行后才进入阻断式下载，避免强更状态把活动交易锁死。
        state.ShowForceUpdateDownloading(update);
        var progress = new AppUpdateProgressSink(state.UpdateDownloadProgress);
        var download = await downloadService.DownloadAsync(update, progress, cancellationToken);
        if (!download.Success || string.IsNullOrWhiteSpace(download.FilePath))
        {
            state.ShowForceUpdateError(
                update,
                download.ErrorMessage ?? LocalizationResourceProvider.Instance["appUpdate.force.downloadFailedDefault"],
                () => CheckForUpdatesAsync(manual: true, CancellationToken.None),
                exitService.Exit);
            return AppUpdateCoordinatorResult.FromStatus(
                AppUpdateCoordinatorStatus.DownloadFailed,
                download.ErrorMessage ?? string.Empty);
        }

        async Task<ProcessLaunchResult> LaunchInstallerAsync()
        {
            var launchResult = await installerLauncher.LaunchAsync(download.FilePath, update, CancellationToken.None);
            if (ShouldDeferUntilTransactionCompletes(launchResult))
            {
                state.ShowForceUpdatePendingInstall(
                    update,
                    download.FilePath,
                    ResumeForceInstallAsync,
                    launchResult.StatusKey!,
                    launchResult.StatusArgs ?? []);
            }
            else if (!launchResult.Success)
            {
                // 中文注释：安装器已经下载完成，普通启动失败要切到可恢复错误态，避免强更遮罩只剩安装按钮。
                state.ShowForceUpdateError(
                    update,
                    launchResult.ErrorMessage ?? LocalizationResourceProvider.Instance["appUpdate.force.downloadFailedDefault"],
                    ResumeForceInstallAsync,
                    exitService.Exit);
            }

            return launchResult;
        }

        async Task<ProcessLaunchResult> ResumeForceInstallAsync()
        {
            if (!TryShowForceUpdateReady(update, download.FilePath, LaunchInstallerAsync, out var statusKey, out var statusArgs))
            {
                state.ShowForceUpdatePendingInstall(
                    update,
                    download.FilePath,
                    ResumeForceInstallAsync,
                    statusKey,
                    statusArgs);
                return ProcessLaunchResult.Fail(null, statusKey, statusArgs);
            }

            return await LaunchInstallerAsync();
        }

        if (!TryShowForceUpdateReady(update, download.FilePath, LaunchInstallerAsync, out var blockedStatusKey, out var blockedStatusArgs))
        {
            state.ShowForceUpdatePendingInstall(
                update,
                download.FilePath,
                ResumeForceInstallAsync,
                blockedStatusKey,
                blockedStatusArgs);
            return new AppUpdateCoordinatorResult(
                AppUpdateCoordinatorStatus.ForcePendingInstall,
                blockedStatusKey,
                blockedStatusArgs);
        }

        return AppUpdateCoordinatorResult.FromStatus(AppUpdateCoordinatorStatus.ForceReady);
    }

    private bool TryShowForceUpdateReady(
        AppUpdateCheckResponse update,
        string installerPath,
        Func<Task<ProcessLaunchResult>> installAsync,
        out string statusKey,
        out object[] statusArgs)
    {
        if (!installSafetyGuard.CanInstallUpdate(out statusKey, out statusArgs))
        {
            return false;
        }

        state.ShowForceUpdateReady(
            update,
            installerPath,
            installAsync,
            ResolveForceReadyStatusKey(update),
            ResolveForceReadyStatusArgs(update));
        return true;
    }

    private static bool ShouldDeferUntilTransactionCompletes(ProcessLaunchResult result)
    {
        return !result.Success &&
            string.Equals(result.StatusKey, ActiveTransactionStatusKey, StringComparison.Ordinal);
    }

    private static string ResolveInstallFailureMessage(ProcessLaunchResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            return result.ErrorMessage;
        }

        if (!string.IsNullOrWhiteSpace(result.StatusKey))
        {
            var template = LocalizationResourceProvider.Instance[result.StatusKey];
            return (result.StatusArgs?.Length ?? 0) == 0
                ? template
                : string.Format(
                    LocalizationResourceProvider.Instance.CurrentCulture,
                    template,
                    result.StatusArgs!);
        }

        return string.Empty;
    }

    private static ProcessLaunchResult ToProcessLaunchResult(AppUpdateCoordinatorResult result)
    {
        // 中文注释：pending 恢复流程必须保留下载/策略失败语义，不能把内部失败包装成安装成功。
        return result.Status is AppUpdateCoordinatorStatus.ForceReady or AppUpdateCoordinatorStatus.Installed
            ? ProcessLaunchResult.Succeeded()
            : ProcessLaunchResult.Fail(null, result.StatusKey, result.StatusArgs);
    }

    private static bool IsPolicyCheckFailure(AppUpdateCheckResponse update)
    {
        var errorCode = update.ErrorCode?.Trim();
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            return false;
        }

        return !errorCode.StartsWith("LOCAL_APP_UPDATE_", ErrorCodeComparison) &&
            !errorCode.StartsWith("APP_UPDATE_CENTER_", ErrorCodeComparison) &&
            !string.Equals(errorCode, "INVALID_UPDATE_CONTRACT", ErrorCodeComparison);
    }

    private static string ResolveForceReadyStatusKey(AppUpdateCheckResponse update)
    {
        if (update.IsRollback)
        {
            return "appUpdate.force.rollbackReady";
        }

        return string.IsNullOrWhiteSpace(update.MinimumSupportedVersion)
            ? "appUpdate.force.ready"
            : "appUpdate.force.minimumRequired";
    }

    private static object[] ResolveForceReadyStatusArgs(AppUpdateCheckResponse update)
    {
        if (update.IsRollback)
        {
            return [update.TargetVersion];
        }

        return string.IsNullOrWhiteSpace(update.MinimumSupportedVersion)
            ? []
            : [update.MinimumSupportedVersion];
    }

    private sealed class AppUpdateProgressSink(Action<AppUpdateDownloadProgress> report) : IProgress<AppUpdateDownloadProgress>
    {
        public void Report(AppUpdateDownloadProgress value)
        {
            report(value);
        }
    }
}
