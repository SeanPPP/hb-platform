using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.AppUpdates;

namespace Hbpos.Client.Tests;

public sealed class AppUpdateInstallerLauncherTests
{
    [Fact]
    public async Task LaunchAsync_starts_exe_with_configured_arguments_and_exits_app()
    {
        var processLauncher = new CapturingProcessLauncher();
        var exitService = new CapturingExitService();
        var launcher = new AppUpdateInstallerLauncher(processLauncher, exitService, AllowInstallSafetyGuard.Instance);

        var result = await launcher.LaunchAsync(
            @"C:\Temp\hbpos.exe",
            new AppUpdateCheckResponse
            {
                InstallerType = "exe",
                InstallerArguments = "/quiet /norestart"
            });

        Assert.True(result.Success);
        Assert.Equal(@"C:\Temp\hbpos.exe", processLauncher.FileName);
        Assert.Equal("/quiet /norestart", processLauncher.Arguments);
        Assert.True(exitService.Exited);
    }

    [Fact]
    public async Task LaunchAsync_starts_msi_through_msiexec_and_exits_app()
    {
        var processLauncher = new CapturingProcessLauncher();
        var exitService = new CapturingExitService();
        var launcher = new AppUpdateInstallerLauncher(processLauncher, exitService, AllowInstallSafetyGuard.Instance);

        var result = await launcher.LaunchAsync(
            @"C:\Temp\hbpos.msi",
            new AppUpdateCheckResponse
            {
                InstallerType = "msi",
                InstallerArguments = "/qn"
            });

        Assert.True(result.Success);
        Assert.Equal("msiexec.exe", processLauncher.FileName);
        Assert.Equal(@"/i ""C:\Temp\hbpos.msi"" /qn", processLauncher.Arguments);
        Assert.True(exitService.Exited);
    }

    [Fact]
    public async Task LaunchAsync_does_not_start_installer_or_exit_when_safety_guard_blocks()
    {
        var processLauncher = new CapturingProcessLauncher();
        var exitService = new CapturingExitService();
        var guard = new BlockingInstallSafetyGuard("appUpdate.install.activeTransaction");
        var launcher = new AppUpdateInstallerLauncher(processLauncher, exitService, guard);

        var result = await launcher.LaunchAsync(
            @"C:\Temp\hbpos.exe",
            new AppUpdateCheckResponse { InstallerType = "exe" });

        Assert.False(result.Success);
        Assert.Equal("appUpdate.install.activeTransaction", result.StatusKey);
        Assert.Null(processLauncher.FileName);
        Assert.False(exitService.Exited);
    }

    [Fact]
    public async Task LaunchAsync_does_not_exit_when_process_launch_fails()
    {
        var processLauncher = new CapturingProcessLauncher
        {
            Result = ProcessLaunchResult.Fail("Process.Start returned null.")
        };
        var exitService = new CapturingExitService();
        var launcher = new AppUpdateInstallerLauncher(processLauncher, exitService, AllowInstallSafetyGuard.Instance);

        var result = await launcher.LaunchAsync(
            @"C:\Temp\hbpos.exe",
            new AppUpdateCheckResponse { InstallerType = "exe" });

        Assert.False(result.Success);
        Assert.Contains("null", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(exitService.Exited);
    }

    [Fact]
    public async Task LaunchAsync_rejects_declared_type_that_does_not_match_downloaded_extension()
    {
        var processLauncher = new CapturingProcessLauncher();
        var exitService = new CapturingExitService();
        var launcher = new AppUpdateInstallerLauncher(processLauncher, exitService, AllowInstallSafetyGuard.Instance);

        var result = await launcher.LaunchAsync(
            @"C:\Temp\hbpos.msi",
            new AppUpdateCheckResponse { InstallerType = "exe" });

        Assert.False(result.Success);
        Assert.Equal("appUpdate.install.failed", result.StatusKey);
        Assert.Null(processLauncher.FileName);
        Assert.False(exitService.Exited);
    }

    private sealed class CapturingProcessLauncher : IProcessLauncher
    {
        public string? FileName { get; private set; }

        public string? Arguments { get; private set; }

        public ProcessLaunchResult Result { get; init; } = ProcessLaunchResult.Succeeded();

        public Task<ProcessLaunchResult> StartAsync(string fileName, string arguments)
        {
            FileName = fileName;
            Arguments = arguments;
            return Task.FromResult(Result);
        }
    }

    private sealed class BlockingInstallSafetyGuard(string statusKey) : IAppUpdateInstallSafetyGuard
    {
        public bool CanInstallUpdate(out string blockedStatusKey, out object[] args)
        {
            blockedStatusKey = statusKey;
            args = [];
            return false;
        }
    }

    private sealed class CapturingExitService : IApplicationExitService
    {
        public bool Exited { get; private set; }

        public void Exit()
        {
            Exited = true;
        }
    }
}
