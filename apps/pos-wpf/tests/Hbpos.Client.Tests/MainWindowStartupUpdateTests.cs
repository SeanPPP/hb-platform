using Hbpos.Client.Wpf;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class MainWindowStartupUpdateTests
{
    [Theory]
    [InlineData(AppUpdateCoordinatorStatus.CheckFailed)]
    [InlineData(AppUpdateCoordinatorStatus.PolicyFailed)]
    public async Task RunStartupAppUpdateCheckCoreAsync_allows_check_failures_without_retry(
        AppUpdateCoordinatorStatus status)
    {
        var checkCallCount = 0;
        var reportedResults = new List<AppUpdateCoordinatorResult>();
        var failure = status == AppUpdateCoordinatorStatus.CheckFailed
            ? AppUpdateCoordinatorResult.CheckFailed("CENTER_UNAVAILABLE", "center unavailable")
            : AppUpdateCoordinatorResult.FromStatus(status, "policy unavailable");

        var result = await MainWindow.RunStartupAppUpdateCheckCoreAsync(
            () =>
            {
                checkCallCount++;
                return Task.FromResult(failure);
            },
            ex => throw new InvalidOperationException("Unexpected startup update exception.", ex),
            reportedResults.Add);

        Assert.Same(failure, result);
        Assert.Equal(1, checkCallCount);
        Assert.Same(failure, Assert.Single(reportedResults));
        Assert.True(MainWindow.ShouldContinueStartupAfterAppUpdateCheck(result));
    }

    [Fact]
    public async Task RunStartupAppUpdateCheckCoreAsync_reports_exception_and_allows_check_failure()
    {
        var checkCallCount = 0;
        Exception? reportedException = null;

        var result = await MainWindow.RunStartupAppUpdateCheckCoreAsync(
            () =>
            {
                checkCallCount++;
                throw new HttpRequestException("center unavailable");
            },
            ex => reportedException = ex,
            _ => throw new InvalidOperationException("Unexpected result report."));

        Assert.Equal(1, checkCallCount);
        Assert.IsType<HttpRequestException>(reportedException);
        Assert.Equal(AppUpdateCoordinatorStatus.CheckFailed, result.Status);
        Assert.True(MainWindow.ShouldContinueStartupAfterAppUpdateCheck(result));
    }

    [Theory]
    [InlineData(AppUpdateCoordinatorStatus.NoUpdate, true)]
    [InlineData(AppUpdateCoordinatorStatus.OptionalDeclined, true)]
    [InlineData(AppUpdateCoordinatorStatus.CheckFailed, true)]
    [InlineData(AppUpdateCoordinatorStatus.PolicyFailed, true)]
    [InlineData(AppUpdateCoordinatorStatus.ForceReady, false)]
    [InlineData(AppUpdateCoordinatorStatus.ForcePendingInstall, false)]
    [InlineData(AppUpdateCoordinatorStatus.DownloadFailed, false)]
    [InlineData(AppUpdateCoordinatorStatus.Installed, false)]
    public void ShouldContinueStartupAfterAppUpdateCheck_blocks_until_startup_gate_is_clear(
        AppUpdateCoordinatorStatus status,
        bool expected)
    {
        Assert.Equal(
            expected,
            MainWindow.ShouldContinueStartupAfterAppUpdateCheck(AppUpdateCoordinatorResult.FromStatus(status)));
    }
}
