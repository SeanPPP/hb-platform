using System.Reflection;
using System.Xml.Linq;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.AppUpdates;
using Microsoft.Extensions.Configuration;

namespace Hbpos.Client.Tests;

public sealed class AppUpdateCoordinatorTests
{
    [Fact]
    public void WpfProject_declares_release_version_source_for_app_update()
    {
        var repoRoot = FindRepoRoot();
        var projectPath = Path.Combine(
            repoRoot,
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "Hbpos.Client.Wpf.csproj");
        var document = XDocument.Load(projectPath);
        var properties = document.Descendants().ToList();

        Assert.Contains(
            properties,
            element => element.Name.LocalName == "HbposWpfAppVersion" &&
                element.Value.Contains("HBPOS_WPF_APP_VERSION", StringComparison.Ordinal));
        Assert.Contains(
            properties,
            element => element.Name.LocalName == "Version" &&
                element.Value.Contains("$(HbposWpfAppVersion)", StringComparison.Ordinal));
        Assert.Contains(
            properties,
            element => element.Name.LocalName == "InformationalVersion" &&
                element.Value.Contains("$(HbposWpfAppVersion)", StringComparison.Ordinal));
        Assert.DoesNotContain(
            properties,
            element => element.Name.LocalName == "Version" &&
                string.Equals(element.Value.Trim(), "1.0.0", StringComparison.Ordinal));
    }

    [Fact]
    public void AppVersionProvider_uses_environment_override_and_strips_build_metadata()
    {
        var previous = Environment.GetEnvironmentVariable(AppVersionProvider.VersionOverrideEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(AppVersionProvider.VersionOverrideEnvironmentVariable, " v2.3.4-preview+build.7 ");

            var provider = new AppVersionProvider();

            Assert.Equal("2.3.4", provider.CurrentVersion);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppVersionProvider.VersionOverrideEnvironmentVariable, previous);
        }
    }

    [Theory]
    [InlineData("v1.2.3+sha.abcdef", "1.2.3")]
    [InlineData("1.2.3.4-preview", "1.2.3.4")]
    [InlineData("internal-preview", "internal-preview")]
    public void AppVersionProvider_normalizes_semantic_versions_and_preserves_custom_values(
        string input,
        string expected)
    {
        Assert.Equal(expected, AppVersionProvider.NormalizeVersionText(input));
    }

    [Fact]
    public void AppUpdateChannelProvider_uses_env_then_config_then_default()
    {
        var previous = Environment.GetEnvironmentVariable(AppUpdateChannelProvider.ChannelEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(AppUpdateChannelProvider.ChannelEnvironmentVariable, null);
            var configured = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AppUpdate:Channel"] = " Preview "
                })
                .Build();
            Assert.Equal("preview", new AppUpdateChannelProvider(configured).CurrentChannel);

            Environment.SetEnvironmentVariable(AppUpdateChannelProvider.ChannelEnvironmentVariable, " hotfix ");
            Assert.Equal("hotfix", new AppUpdateChannelProvider(configured).CurrentChannel);

            Environment.SetEnvironmentVariable(AppUpdateChannelProvider.ChannelEnvironmentVariable, null);
            Assert.Equal("production", new AppUpdateChannelProvider(new ConfigurationBuilder().Build()).CurrentChannel);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppUpdateChannelProvider.ChannelEnvironmentVariable, previous);
        }
    }

    [Fact]
    public async Task CheckForUpdatesAsync_downloads_optional_update_after_prompt_confirmation()
    {
        var release = CreateRelease(force: false);
        var state = new AppUpdateState();
        var prompt = new CapturingPromptService(confirm: true);
        var installer = new CapturingInstallerLauncher();
        var download = new StaticDownloadService(AppUpdateDownloadResult.Succeeded(@"C:\Temp\hbpos.exe"));
        var coordinator = CreateCoordinator(release, download, installer, prompt, state);

        var result = await coordinator.CheckForUpdatesAsync(manual: false);

        Assert.Equal(AppUpdateCoordinatorStatus.Installed, result.Status);
        Assert.False(state.IsForceUpdateBlocking);
        Assert.False(state.IsOptionalUpdateReady);
        Assert.False(state.IsInstallerReady);
        Assert.False(state.InstallUpdateCommand.CanExecute(null));
        Assert.True(prompt.OptionalPromptShown);
        Assert.Equal(1, download.CallCount);
        Assert.Equal("1.1.0", prompt.Update?.TargetVersion);
        Assert.Equal(@"C:\Temp\hbpos.exe", installer.FilePath);
        Assert.Equal(1, installer.LaunchCallCount);
        Assert.False(installer.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_optional_decline_does_not_download()
    {
        var release = CreateRelease(force: false);
        var state = new AppUpdateState();
        var prompt = new CapturingPromptService(confirm: false);
        var download = new StaticDownloadService(AppUpdateDownloadResult.Succeeded(@"C:\Temp\hbpos.exe"));
        var coordinator = CreateCoordinator(release, download, new CapturingInstallerLauncher(), prompt, state);

        var result = await coordinator.CheckForUpdatesAsync(manual: true);

        Assert.Equal(AppUpdateCoordinatorStatus.OptionalDeclined, result.Status);
        Assert.Equal("settings.status.appUpdateOptionalDeclined", result.StatusKey);
        Assert.True(prompt.OptionalPromptShown);
        Assert.Equal(0, download.CallCount);
        Assert.False(state.IsOptionalUpdateReady);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_force_update_blocks_shell_until_installer_launch()
    {
        var release = CreateRelease(force: true);
        var state = new AppUpdateState();
        var installer = new CapturingInstallerLauncher();
        var coordinator = CreateCoordinator(
            release,
            new StaticDownloadService(AppUpdateDownloadResult.Succeeded(@"C:\Temp\hbpos.exe")),
            installer,
            new CapturingPromptService(),
            state);

        var result = await coordinator.CheckForUpdatesAsync(manual: false);

        Assert.Equal(AppUpdateCoordinatorStatus.ForceReady, result.Status);
        Assert.True(state.IsForceUpdateBlocking);
        Assert.True(state.IsInstallerReady);
        Assert.False(state.IsForceUpdateError);
        Assert.Equal("1.1.0", state.TargetVersion);

        await state.InstallUpdateCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\Temp\hbpos.exe", installer.FilePath);
        Assert.Equal("1.1.0", installer.Update?.TargetVersion);
        Assert.False(installer.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_force_update_with_active_transaction_stays_nonblocking_until_safe()
    {
        var release = CreateRelease(force: true);
        var state = new AppUpdateState();
        var installer = new CapturingInstallerLauncher();
        var download = new StaticDownloadService(AppUpdateDownloadResult.Succeeded(@"C:\Temp\hbpos.exe"));
        var guard = new ToggleInstallSafetyGuard(canInstall: false);
        var coordinator = CreateCoordinator(
            release,
            download,
            installer,
            new CapturingPromptService(),
            state,
            guard: guard);

        var result = await coordinator.CheckForUpdatesAsync(manual: false);

        Assert.Equal(AppUpdateCoordinatorStatus.ForcePendingInstall, result.Status);
        Assert.False(state.IsForceUpdateBlocking);
        Assert.True(state.IsForceUpdatePendingInstall);
        Assert.True(state.InstallUpdateCommand.CanExecute(null));
        Assert.Equal("appUpdate.install.activeTransaction", state.StatusKey);
        Assert.False(state.IsInstallerReady);
        Assert.Equal(0, download.CallCount);
        Assert.Equal(0, installer.LaunchCallCount);

        guard.CanInstall = true;
        await state.InstallUpdateCommand.ExecuteAsync(null);

        Assert.True(state.IsForceUpdateBlocking);
        Assert.False(state.IsForceUpdatePendingInstall);
        Assert.True(state.IsInstallerReady);
        Assert.Equal(1, download.CallCount);
        Assert.Equal(0, installer.LaunchCallCount);

        await state.InstallUpdateCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\Temp\hbpos.exe", installer.FilePath);
        Assert.Equal(1, installer.LaunchCallCount);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_force_update_pending_resume_reports_download_failure()
    {
        var release = CreateRelease(force: true);
        var state = new AppUpdateState();
        var download = new StaticDownloadService(AppUpdateDownloadResult.Fail(null, "network failed"));
        var guard = new ToggleInstallSafetyGuard(canInstall: false);
        var coordinator = CreateCoordinator(
            release,
            download,
            new CapturingInstallerLauncher(),
            new CapturingPromptService(),
            state,
            guard: guard);

        await coordinator.CheckForUpdatesAsync(manual: false);
        var resumeInstallAsync = GetPendingInstallAsync(state);
        guard.CanInstall = true;

        var result = await resumeInstallAsync();

        Assert.False(result.Success);
        Assert.Equal("settings.status.appUpdateDownloadFailed", result.StatusKey);
        Assert.Equal(["network failed"], result.StatusArgs);
        Assert.True(state.IsForceUpdateError);
        Assert.Equal("appUpdate.force.downloadFailed", state.StatusKey);
        Assert.Equal(1, download.CallCount);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_force_download_failure_allows_retry_and_exit_only()
    {
        var release = CreateRelease(force: true);
        var state = new AppUpdateState();
        var exitService = new CapturingApplicationExitService();
        var coordinator = CreateCoordinator(
            release,
            new StaticDownloadService(AppUpdateDownloadResult.Fail(null, "network failed")),
            new CapturingInstallerLauncher(),
            new CapturingPromptService(),
            state,
            exitService);

        var result = await coordinator.CheckForUpdatesAsync(manual: true);

        Assert.Equal(AppUpdateCoordinatorStatus.DownloadFailed, result.Status);
        Assert.True(state.IsForceUpdateBlocking);
        Assert.True(state.IsForceUpdateError);
        Assert.False(state.InstallUpdateCommand.CanExecute(null));
        Assert.True(state.RetryForceUpdateCommand.CanExecute(null));
        Assert.True(state.ExitApplicationCommand.CanExecute(null));

        state.ExitApplicationCommand.Execute(null);
        Assert.Equal(1, exitService.ExitCallCount);
    }

    [Fact]
    public async Task ShowStartupUpdateError_blocks_startup_with_retry_and_exit_actions()
    {
        var state = new AppUpdateState();
        var retryCallCount = 0;
        var exitCallCount = 0;

        state.ShowStartupUpdateError(
            "center unavailable",
            () =>
            {
                retryCallCount++;
                return Task.CompletedTask;
            },
            () => exitCallCount++);

        Assert.True(state.IsForceUpdateBlocking);
        Assert.True(state.IsForceUpdateError);
        Assert.False(state.IsDownloading);
        Assert.False(state.InstallUpdateCommand.CanExecute(null));
        Assert.True(state.RetryForceUpdateCommand.CanExecute(null));
        Assert.True(state.ExitApplicationCommand.CanExecute(null));
        Assert.Equal("appUpdate.startup.checkFailed", state.StatusKey);
        Assert.Equal(["center unavailable"], state.StatusArgs);

        await state.RetryForceUpdateCommand.ExecuteAsync(null);
        state.ExitApplicationCommand.Execute(null);

        Assert.Equal(1, retryCallCount);
        Assert.Equal(1, exitCallCount);

        state.ClearStartupUpdateError();

        Assert.False(state.IsForceUpdateBlocking);
        Assert.False(state.IsForceUpdateError);
        Assert.False(state.RetryForceUpdateCommand.CanExecute(null));
        Assert.False(state.ExitApplicationCommand.CanExecute(null));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_force_install_command_ignores_cancelled_check_token()
    {
        var release = CreateRelease(force: true);
        var state = new AppUpdateState();
        var installer = new CapturingInstallerLauncher();
        using var cts = new CancellationTokenSource();
        var coordinator = CreateCoordinator(
            release,
            new StaticDownloadService(AppUpdateDownloadResult.Succeeded(@"C:\Temp\hbpos.exe")),
            installer,
            new CapturingPromptService(),
            state);

        await coordinator.CheckForUpdatesAsync(manual: false, cts.Token);
        await cts.CancelAsync();
        await state.InstallUpdateCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\Temp\hbpos.exe", installer.FilePath);
        Assert.False(installer.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_force_install_failure_allows_retry_and_exit()
    {
        var release = CreateRelease(force: true);
        var state = new AppUpdateState();
        var exitService = new CapturingApplicationExitService();
        var installer = new SequenceInstallerLauncher(ProcessLaunchResult.Fail("launch failed"));
        var coordinator = CreateCoordinator(
            release,
            new StaticDownloadService(AppUpdateDownloadResult.Succeeded(@"C:\Temp\hbpos.exe")),
            installer,
            new CapturingPromptService(),
            state,
            exitService);

        await coordinator.CheckForUpdatesAsync(manual: false);
        await state.InstallUpdateCommand.ExecuteAsync(null);

        Assert.True(state.IsForceUpdateBlocking);
        Assert.True(state.IsForceUpdateError);
        Assert.False(state.InstallUpdateCommand.CanExecute(null));
        Assert.True(state.RetryForceUpdateCommand.CanExecute(null));
        Assert.True(state.ExitApplicationCommand.CanExecute(null));
        Assert.Equal("appUpdate.force.downloadFailed", state.StatusKey);
        Assert.Equal(1, installer.LaunchCallCount);

        state.ExitApplicationCommand.Execute(null);
        Assert.Equal(1, exitService.ExitCallCount);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_force_download_retry_runs_new_check_and_download()
    {
        var release = CreateRelease(force: true);
        var state = new AppUpdateState();
        var apiClient = new StaticUpdateApiClient(release);
        var download = new StaticDownloadService(AppUpdateDownloadResult.Fail(null, "network failed"));
        var coordinator = CreateCoordinator(
            apiClient,
            download,
            new CapturingInstallerLauncher(),
            new CapturingPromptService(),
            state);

        await coordinator.CheckForUpdatesAsync(manual: false);

        Assert.True(state.RetryForceUpdateCommand.CanExecute(null));
        Assert.Equal(1, apiClient.CallCount);
        Assert.Equal(1, download.CallCount);

        await state.RetryForceUpdateCommand.ExecuteAsync(null);

        Assert.True(state.IsForceUpdateError);
        Assert.Equal("appUpdate.force.downloadFailed", state.StatusKey);
        Assert.Equal(2, apiClient.CallCount);
        Assert.Equal(2, download.CallCount);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_force_update_updates_download_progress_state()
    {
        var release = CreateRelease(force: true);
        var state = new AppUpdateState();
        var download = new ProgressReportingDownloadService(
            AppUpdateDownloadResult.Succeeded(@"C:\Temp\hbpos.exe"),
            new AppUpdateDownloadProgress(12, 12, 100));
        var coordinator = CreateCoordinator(
            release,
            download,
            new CapturingInstallerLauncher(),
            new CapturingPromptService(),
            state);

        await coordinator.CheckForUpdatesAsync(manual: false);

        Assert.True(state.HasDownloadProgress);
        Assert.Equal(100, state.DownloadProgressPercent);
        Assert.Equal("12 / 12 bytes (100%)", state.DownloadProgressText);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_concurrent_manual_check_returns_already_running_without_second_download()
    {
        var release = CreateRelease(force: true);
        var download = new BlockingDownloadService();
        var coordinator = CreateCoordinator(
            release,
            download,
            new CapturingInstallerLauncher(),
            new CapturingPromptService(),
            new AppUpdateState());

        var first = coordinator.CheckForUpdatesAsync(manual: false);
        await download.WaitUntilStartedAsync();
        var second = await coordinator.CheckForUpdatesAsync(manual: true);
        download.Complete(AppUpdateDownloadResult.Succeeded(@"C:\Temp\hbpos.exe"));
        await first;

        Assert.Equal(AppUpdateCoordinatorStatus.AlreadyRunning, second.Status);
        Assert.Equal("settings.status.appUpdateAlreadyRunning", second.StatusKey);
        Assert.Equal(1, download.CallCount);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_manual_no_update_returns_localized_status()
    {
        var coordinator = CreateCoordinator(
            AppUpdateCheckResponse.NoUpdate("1.0.0"),
            new StaticDownloadService(AppUpdateDownloadResult.Succeeded(@"C:\Temp\hbpos.exe")),
            new CapturingInstallerLauncher(),
            new CapturingPromptService(),
            new AppUpdateState());

        var result = await coordinator.CheckForUpdatesAsync(manual: true);

        Assert.Equal(AppUpdateCoordinatorStatus.NoUpdate, result.Status);
        Assert.Equal("settings.status.appUpdateLatest", result.StatusKey);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_uses_configured_channel()
    {
        var apiClient = new StaticUpdateApiClient(AppUpdateCheckResponse.NoUpdate("1.0.0"));
        var coordinator = CreateCoordinator(
            apiClient,
            new StaticDownloadService(AppUpdateDownloadResult.Succeeded(@"C:\Temp\hbpos.exe")),
            new CapturingInstallerLauncher(),
            new CapturingPromptService(),
            new AppUpdateState(),
            channelProvider: new StaticChannelProvider("preview"));

        await coordinator.CheckForUpdatesAsync(manual: true);

        Assert.Equal("preview", apiClient.LastRequest?.Channel);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_policy_error_does_not_block_shell()
    {
        var response = AppUpdateCheckResponse.Failed(
            "1.0.0",
            "TARGET_RELEASE_NOT_FOUND",
            "Target release is disabled.");
        var state = new AppUpdateState();
        var coordinator = CreateCoordinator(
            response,
            new StaticDownloadService(AppUpdateDownloadResult.Succeeded(@"C:\Temp\hbpos.exe")),
            new CapturingInstallerLauncher(),
            new CapturingPromptService(),
            state);

        var result = await coordinator.CheckForUpdatesAsync(manual: true);

        Assert.Equal(AppUpdateCoordinatorStatus.PolicyFailed, result.Status);
        Assert.Equal("settings.status.appUpdatePolicyFailed", result.StatusKey);
        Assert.False(state.IsForceUpdateBlocking);
        Assert.False(state.IsInstallerReady);
        Assert.Equal("settings.status.appUpdatePolicyFailed", state.StatusKey);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_transport_check_failure_preserves_check_failed_status()
    {
        var response = AppUpdateCheckResponse.Failed(
            "1.0.0",
            "APP_UPDATE_CENTER_HTTP_ERROR",
            "center unavailable");
        var state = new AppUpdateState();
        var coordinator = CreateCoordinator(
            response,
            new StaticDownloadService(AppUpdateDownloadResult.Succeeded(@"C:\Temp\hbpos.exe")),
            new CapturingInstallerLauncher(),
            new CapturingPromptService(),
            state);

        var result = await coordinator.CheckForUpdatesAsync(manual: true);

        Assert.Equal(AppUpdateCoordinatorStatus.CheckFailed, result.Status);
        Assert.Equal("settings.status.appUpdateCheckFailed", result.StatusKey);
        Assert.False(state.IsForceUpdateBlocking);
        Assert.False(state.IsInstallerReady);
        Assert.Equal("settings.status.appUpdateCheckFailed", state.StatusKey);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_optional_install_retry_success_clears_ready_state()
    {
        var release = CreateRelease(force: false);
        var state = new AppUpdateState();
        var prompt = new CapturingPromptService(confirm: true);
        var installer = new SequenceInstallerLauncher(
            ProcessLaunchResult.Fail("launch failed"),
            ProcessLaunchResult.Succeeded());
        var coordinator = CreateCoordinator(
            release,
            new StaticDownloadService(AppUpdateDownloadResult.Succeeded(@"C:\Temp\hbpos.exe")),
            installer,
            prompt,
            state);

        var firstResult = await coordinator.CheckForUpdatesAsync(manual: false);

        Assert.Equal(AppUpdateCoordinatorStatus.InstallFailed, firstResult.Status);
        Assert.Equal("settings.status.appUpdateInstallFailed", firstResult.StatusKey);
        Assert.Equal("launch failed", Assert.Single(firstResult.StatusArgs));
        Assert.True(state.IsOptionalUpdateReady);
        Assert.True(state.IsInstallerReady);
        Assert.True(state.InstallUpdateCommand.CanExecute(null));
        Assert.Equal(1, installer.LaunchCallCount);

        await state.InstallUpdateCommand.ExecuteAsync(null);

        Assert.False(state.IsOptionalUpdateReady);
        Assert.False(state.IsInstallerReady);
        Assert.False(state.InstallUpdateCommand.CanExecute(null));
        Assert.Equal(2, installer.LaunchCallCount);
    }

    [Theory]
    [InlineData(true, null, "appUpdate.force.ready")]
    [InlineData(true, "1.0.0", "appUpdate.force.minimumRequired")]
    [InlineData(false, null, "appUpdate.force.rollbackReady")]
    public async Task CheckForUpdatesAsync_force_ready_uses_policy_specific_message(
        bool currentIsOlder,
        string? minimumVersion,
        string expectedStatusKey)
    {
        var release = CreateRelease(force: true) with
        {
            CurrentVersion = currentIsOlder ? "1.0.0" : "1.2.0",
            TargetVersion = "1.1.0",
            IsRollback = !currentIsOlder,
            MinimumSupportedVersion = minimumVersion
        };
        var state = new AppUpdateState();
        var coordinator = CreateCoordinator(
            release,
            new StaticDownloadService(AppUpdateDownloadResult.Succeeded(@"C:\Temp\hbpos.exe")),
            new CapturingInstallerLauncher(),
            new CapturingPromptService(),
            state);

        await coordinator.CheckForUpdatesAsync(manual: false);

        Assert.Equal(expectedStatusKey, state.StatusKey);
    }

    private static AppUpdateCoordinator CreateCoordinator(
        AppUpdateCheckResponse response,
        IAppUpdateDownloadService downloadService,
        IAppUpdateInstallerLauncher installerLauncher,
        IAppUpdatePromptService promptService,
        AppUpdateState state,
        IApplicationExitService? exitService = null,
        IAppUpdateChannelProvider? channelProvider = null,
        IAppUpdateInstallSafetyGuard? guard = null)
    {
        return CreateCoordinator(
            new StaticUpdateApiClient(response),
            downloadService,
            installerLauncher,
            promptService,
            state,
            exitService,
            channelProvider,
            guard);
    }

    private static AppUpdateCoordinator CreateCoordinator(
        IAppUpdateApiClient apiClient,
        IAppUpdateDownloadService downloadService,
        IAppUpdateInstallerLauncher installerLauncher,
        IAppUpdatePromptService promptService,
        AppUpdateState state,
        IApplicationExitService? exitService = null,
        IAppUpdateChannelProvider? channelProvider = null,
        IAppUpdateInstallSafetyGuard? guard = null)
    {
        return new AppUpdateCoordinator(
            new StaticVersionProvider("1.0.0"),
            apiClient,
            downloadService,
            installerLauncher,
            guard ?? AllowInstallSafetyGuard.Instance,
            promptService,
            state,
            exitService ?? new CapturingApplicationExitService(),
            channelProvider ?? new StaticChannelProvider("production"));
    }

    private static AppUpdateCheckResponse CreateRelease(bool force) => new()
    {
        UpdateAvailable = true,
        ForceUpdate = force,
        CurrentVersion = "1.0.0",
        TargetVersion = "1.1.0",
        DownloadUrl = "https://downloads.example/hbpos.exe",
        FileName = "hbpos.exe",
        FileSize = 12,
        Sha256 = new string('a', 64),
        InstallerType = "exe",
        ReleaseNotes = "更新说明"
    };

    private sealed class StaticVersionProvider(string version) : IAppVersionProvider
    {
        public string CurrentVersion => version;
    }

    private sealed class StaticUpdateApiClient(AppUpdateCheckResponse response) : IAppUpdateApiClient
    {
        public AppUpdateCheckRequest? LastRequest { get; private set; }

        public int CallCount { get; private set; }

        public Task<AppUpdateCheckResponse> CheckAsync(
            AppUpdateCheckRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(response with { CurrentVersion = response.CurrentVersion });
        }
    }

    private sealed class StaticChannelProvider(string channel) : IAppUpdateChannelProvider
    {
        public string CurrentChannel => channel;
    }

    private sealed class StaticDownloadService(AppUpdateDownloadResult result) : IAppUpdateDownloadService
    {
        public int CallCount { get; private set; }

        public Task<AppUpdateDownloadResult> DownloadAsync(
            AppUpdateCheckResponse update,
            IProgress<AppUpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class BlockingDownloadService : IAppUpdateDownloadService
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<AppUpdateDownloadResult> _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public Task WaitUntilStartedAsync() => _started.Task;

        public void Complete(AppUpdateDownloadResult result) => _completed.SetResult(result);

        public Task<AppUpdateDownloadResult> DownloadAsync(
            AppUpdateCheckResponse update,
            IProgress<AppUpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            _started.TrySetResult();
            return _completed.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ProgressReportingDownloadService(
        AppUpdateDownloadResult result,
        AppUpdateDownloadProgress value) : IAppUpdateDownloadService
    {
        public Task<AppUpdateDownloadResult> DownloadAsync(
            AppUpdateCheckResponse update,
            IProgress<AppUpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(value);
            return Task.FromResult(result);
        }
    }

    private sealed class CapturingInstallerLauncher : IAppUpdateInstallerLauncher
    {
        public string? FilePath { get; private set; }

        public AppUpdateCheckResponse? Update { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public int LaunchCallCount { get; private set; }

        public Task<ProcessLaunchResult> LaunchAsync(
            string installerPath,
            AppUpdateCheckResponse update,
            CancellationToken cancellationToken = default)
        {
            FilePath = installerPath;
            Update = update;
            CancellationToken = cancellationToken;
            LaunchCallCount++;
            return Task.FromResult(ProcessLaunchResult.Succeeded());
        }
    }

    private sealed class SequenceInstallerLauncher(params ProcessLaunchResult[] results) : IAppUpdateInstallerLauncher
    {
        private readonly Queue<ProcessLaunchResult> _results = new(results);

        public int LaunchCallCount { get; private set; }

        public Task<ProcessLaunchResult> LaunchAsync(
            string installerPath,
            AppUpdateCheckResponse update,
            CancellationToken cancellationToken = default)
        {
            LaunchCallCount++;
            return Task.FromResult(_results.Count > 0
                ? _results.Dequeue()
                : ProcessLaunchResult.Succeeded());
        }
    }

    private sealed class CapturingPromptService(bool confirm = false) : IAppUpdatePromptService
    {
        public bool OptionalPromptShown { get; private set; }

        public AppUpdateCheckResponse? Update { get; private set; }

        public Task<bool> ConfirmOptionalDownloadAndInstallAsync(
            AppUpdateCheckResponse update,
            CancellationToken cancellationToken = default)
        {
            OptionalPromptShown = true;
            Update = update;
            return Task.FromResult(confirm);
        }
    }

    private sealed class ToggleInstallSafetyGuard(bool canInstall) : IAppUpdateInstallSafetyGuard
    {
        public bool CanInstall { get; set; } = canInstall;

        public bool CanInstallUpdate(out string statusKey, out object[] args)
        {
            statusKey = CanInstall ? string.Empty : "appUpdate.install.activeTransaction";
            args = [];
            return CanInstall;
        }
    }

    private sealed class CapturingApplicationExitService : IApplicationExitService
    {
        public int ExitCallCount { get; private set; }

        public void Exit()
        {
            ExitCallCount++;
        }
    }

    private static Func<Task<ProcessLaunchResult>> GetPendingInstallAsync(AppUpdateState state)
    {
        var field = typeof(AppUpdateState).GetField("_installAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        var value = field?.GetValue(state);
        return Assert.IsType<Func<Task<ProcessLaunchResult>>>(value);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, "hb-platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to find repository root.");
    }
}
