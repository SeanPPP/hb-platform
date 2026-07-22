using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Contracts.AppUpdates;

namespace Hbpos.Client.Wpf.Services;

public sealed partial class AppUpdateState : ObservableObject
{
    private Func<Task<ProcessLaunchResult>>? _installAsync;
    private Func<Task>? _retryAsync;
    private Action? _exitApplication;

    public AppUpdateState()
    {
        InstallUpdateCommand = new AsyncRelayCommand(InstallAsync, CanInstall);
        DismissOptionalUpdateCommand = new RelayCommand(ClearOptionalUpdate);
        RetryForceUpdateCommand = new AsyncRelayCommand(RetryForceUpdateAsync, CanRetryForceUpdate);
        ExitApplicationCommand = new RelayCommand(ExitApplication, CanExitApplication);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsForceUpdateBlocking))]
    private bool _isForceUpdateRequired;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsForceUpdateBlocking))]
    private bool _isForceUpdatePendingInstall;

    [ObservableProperty]
    private bool _isForceUpdateError;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstallerReady))]
    private string? _installerPath;

    private string _currentVersion = string.Empty;

    private bool _hasDifferentTargetVersion;

    private bool _isRollbackTarget;

    [ObservableProperty]
    private string? _targetVersion;

    [ObservableProperty]
    private string? _releaseNotes;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isOptionalUpdateReady;

    [ObservableProperty]
    private int _downloadProgressPercent;

    [ObservableProperty]
    private string _downloadProgressText = string.Empty;

    [ObservableProperty]
    private bool _hasDownloadProgress;

    public string StatusKey { get; private set; } = string.Empty;

    public object[] StatusArgs { get; private set; } = [];

    public bool IsForceUpdateBlocking => IsForceUpdateRequired && !IsForceUpdatePendingInstall;

    public bool IsInstallerReady => !string.IsNullOrWhiteSpace(InstallerPath);

    public string CurrentVersion
    {
        get => _currentVersion;
        private set => SetProperty(ref _currentVersion, value);
    }

    public bool HasDifferentTargetVersion
    {
        get => _hasDifferentTargetVersion;
        private set => SetProperty(ref _hasDifferentTargetVersion, value);
    }

    public bool IsRollbackTarget
    {
        get => _isRollbackTarget;
        private set => SetProperty(ref _isRollbackTarget, value);
    }

    public IAsyncRelayCommand InstallUpdateCommand { get; }

    public IRelayCommand DismissOptionalUpdateCommand { get; }

    public IAsyncRelayCommand RetryForceUpdateCommand { get; }

    public IRelayCommand ExitApplicationCommand { get; }

    public void InitializeCurrentVersion(string currentVersion)
    {
        CurrentVersion = AppVersionProvider.NormalizeVersionText(currentVersion);
        ClearVersionCheckResult();
    }

    public void ApplyVersionCheck(AppUpdateCheckResponse update)
    {
        var targetVersion = AppVersionProvider.NormalizeVersionText(update.TargetVersion);
        TargetVersion = targetVersion;
        HasDifferentTargetVersion = update.UpdateAvailable &&
            !string.IsNullOrWhiteSpace(targetVersion) &&
            !string.Equals(CurrentVersion, targetVersion, StringComparison.OrdinalIgnoreCase);
        IsRollbackTarget = HasDifferentTargetVersion && update.IsRollback;
    }

    public void ClearVersionCheckResult()
    {
        HasDifferentTargetVersion = false;
        IsRollbackTarget = false;
    }

    public void SetStatus(string statusKey, params object[] args)
    {
        StatusKey = statusKey;
        StatusArgs = args;
        StatusMessage = FormatStatus(statusKey, args);
    }

    public void ShowForceUpdateDownloading(AppUpdateCheckResponse update)
    {
        // 中文注释：强更下载期间不阻断收银流程，安装包就绪后再切到强更遮罩。
        IsForceUpdateRequired = false;
        IsForceUpdatePendingInstall = false;
        IsForceUpdateError = false;
        IsOptionalUpdateReady = false;
        IsDownloading = true;
        InstallerPath = null;
        TargetVersion = update.TargetVersion;
        ReleaseNotes = update.ReleaseNotes;
        _installAsync = null;
        _retryAsync = null;
        _exitApplication = null;
        ClearDownloadProgress();
        SetStatus("appUpdate.force.downloading");
        NotifyCommandStates();
    }

    public void ShowForceUpdateReady(
        AppUpdateCheckResponse update,
        string installerPath,
        Func<Task<ProcessLaunchResult>> installAsync,
        string statusKey,
        params object[] statusArgs)
    {
        IsForceUpdateRequired = true;
        IsForceUpdatePendingInstall = false;
        IsForceUpdateError = false;
        IsOptionalUpdateReady = false;
        IsDownloading = false;
        InstallerPath = installerPath;
        TargetVersion = update.TargetVersion;
        ReleaseNotes = update.ReleaseNotes;
        _installAsync = installAsync;
        _retryAsync = null;
        _exitApplication = null;
        SetStatus(statusKey, statusArgs);
        NotifyCommandStates();
    }

    public void ShowForceUpdatePendingInstall(
        AppUpdateCheckResponse update,
        string installerPath,
        Func<Task<ProcessLaunchResult>> installAsync,
        string statusKey,
        params object[] statusArgs)
    {
        IsForceUpdateRequired = true;
        IsForceUpdatePendingInstall = true;
        IsForceUpdateError = false;
        IsOptionalUpdateReady = false;
        IsDownloading = false;
        InstallerPath = installerPath;
        TargetVersion = update.TargetVersion;
        ReleaseNotes = update.ReleaseNotes;
        _installAsync = installAsync;
        _retryAsync = null;
        _exitApplication = null;
        ClearDownloadProgress();
        SetStatus(statusKey, statusArgs);
        NotifyCommandStates();
    }

    public void ShowForceUpdateError(
        AppUpdateCheckResponse update,
        string message,
        Func<Task> retryAsync,
        Action exitApplication)
    {
        IsForceUpdateRequired = true;
        IsForceUpdatePendingInstall = false;
        IsForceUpdateError = true;
        IsOptionalUpdateReady = false;
        IsDownloading = false;
        InstallerPath = null;
        TargetVersion = update.TargetVersion;
        ReleaseNotes = update.ReleaseNotes;
        _installAsync = null;
        _retryAsync = retryAsync;
        _exitApplication = exitApplication;
        ClearDownloadProgress();
        SetStatus("appUpdate.force.downloadFailed", message);
        NotifyCommandStates();
    }

    public void ShowStartupUpdateError(
        string message,
        Func<Task> retryAsync,
        Action exitApplication)
    {
        IsForceUpdateRequired = true;
        IsForceUpdatePendingInstall = false;
        IsForceUpdateError = true;
        IsOptionalUpdateReady = false;
        IsDownloading = false;
        InstallerPath = null;
        TargetVersion = null;
        ReleaseNotes = null;
        _installAsync = null;
        _retryAsync = retryAsync;
        _exitApplication = exitApplication;
        ClearDownloadProgress();
        // 启动阶段检查失败必须复用全局阻断遮罩，但文案不能误导成安装包下载失败。
        SetStatus("appUpdate.startup.checkFailed", message);
        NotifyCommandStates();
    }

    public void ClearStartupUpdateError()
    {
        if (!string.Equals(StatusKey, "appUpdate.startup.checkFailed", StringComparison.Ordinal))
        {
            return;
        }

        IsForceUpdateRequired = false;
        IsForceUpdatePendingInstall = false;
        IsForceUpdateError = false;
        IsOptionalUpdateReady = false;
        IsDownloading = false;
        InstallerPath = null;
        TargetVersion = null;
        ReleaseNotes = null;
        _installAsync = null;
        _retryAsync = null;
        _exitApplication = null;
        ClearDownloadProgress();
        StatusKey = string.Empty;
        StatusArgs = [];
        StatusMessage = string.Empty;
        NotifyCommandStates();
    }

    public void ShowOptionalUpdateReady(
        AppUpdateCheckResponse update,
        string installerPath,
        Func<Task<ProcessLaunchResult>> installAsync)
    {
        IsForceUpdateRequired = false;
        IsForceUpdatePendingInstall = false;
        IsForceUpdateError = false;
        IsOptionalUpdateReady = true;
        IsDownloading = false;
        InstallerPath = installerPath;
        TargetVersion = update.TargetVersion;
        ReleaseNotes = update.ReleaseNotes;
        _installAsync = installAsync;
        _retryAsync = null;
        _exitApplication = null;
        SetStatus("appUpdate.optional.ready");
        NotifyCommandStates();
    }

    public void UpdateDownloadProgress(AppUpdateDownloadProgress progress)
    {
        HasDownloadProgress = true;
        DownloadProgressPercent = progress.Percent;
        DownloadProgressText = $"{progress.DownloadedBytes} / {progress.TotalBytes} bytes ({progress.Percent}%)";
    }

    public void ApplyInstallFailure(ProcessLaunchResult result)
    {
        if (result.Success)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.StatusKey))
        {
            SetStatus(result.StatusKey, result.StatusArgs ?? []);
        }
        else
        {
            SetStatus("appUpdate.install.failed", result.ErrorMessage ?? string.Empty);
        }

        NotifyCommandStates();
    }

    public void ClearOptionalUpdateAfterSuccessfulInstall()
    {
        if (IsForceUpdateRequired)
        {
            return;
        }

        // 可选更新安装器已成功交给系统后，清掉重试入口，避免用户再次点击重复拉起安装器。
        IsOptionalUpdateReady = false;
        InstallerPath = null;
        _installAsync = null;
        ClearDownloadProgress();
        NotifyCommandStates();
    }

    private async Task InstallAsync()
    {
        if (_installAsync is not null)
        {
            var result = await _installAsync();
            // 中文注释：可选更新首次自动拉起失败后会保留重试入口；一旦用户重试成功，需要在这里统一清掉入口状态。
            if (result.Success && !IsForceUpdateRequired)
            {
                ClearOptionalUpdateAfterSuccessfulInstall();
                return;
            }

            if (IsForceUpdateError)
            {
                // 中文注释：强更安装器启动失败时，coordinator 已切到可恢复错误态；这里不能再用通用安装失败状态覆盖 retry/exit 文案。
                return;
            }

            ApplyInstallFailure(result);
        }
    }

    private bool CanInstall()
    {
        return _installAsync is not null && !IsDownloading;
    }

    private void ClearOptionalUpdate()
    {
        if (IsForceUpdateRequired)
        {
            return;
        }

        IsOptionalUpdateReady = false;
        InstallerPath = null;
        _installAsync = null;
        ClearDownloadProgress();
        NotifyCommandStates();
    }

    private async Task RetryForceUpdateAsync()
    {
        if (_retryAsync is not null)
        {
            await _retryAsync();
        }
    }

    private bool CanRetryForceUpdate()
    {
        return IsForceUpdateError && !IsDownloading && _retryAsync is not null;
    }

    private void ExitApplication()
    {
        _exitApplication?.Invoke();
    }

    private bool CanExitApplication()
    {
        return IsForceUpdateError && !IsDownloading && _exitApplication is not null;
    }

    private void ClearDownloadProgress()
    {
        HasDownloadProgress = false;
        DownloadProgressPercent = 0;
        DownloadProgressText = string.Empty;
    }

    private void NotifyCommandStates()
    {
        InstallUpdateCommand.NotifyCanExecuteChanged();
        RetryForceUpdateCommand.NotifyCanExecuteChanged();
        ExitApplicationCommand.NotifyCanExecuteChanged();
    }

    private static string FormatStatus(string statusKey, params object[] args)
    {
        var template = LocalizationResourceProvider.Instance[statusKey];
        return args.Length == 0
            ? template
            : string.Format(LocalizationResourceProvider.Instance.CurrentCulture, template, args);
    }
}
