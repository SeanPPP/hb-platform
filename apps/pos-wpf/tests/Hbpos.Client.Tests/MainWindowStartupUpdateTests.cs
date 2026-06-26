using Hbpos.Client.Wpf;
using Hbpos.Client.Wpf.Services;
using System.Reflection;

namespace Hbpos.Client.Tests;

public sealed class MainWindowStartupUpdateTests
{
    [Fact]
    public async Task RunStartupAppUpdateCheckCoreAsync_retries_once_after_check_failure()
    {
        var checkCallCount = 0;
        var delays = new List<TimeSpan>();

        var result = await MainWindow.RunStartupAppUpdateCheckCoreAsync(
            () =>
            {
                checkCallCount++;
                return Task.FromResult(checkCallCount == 1
                    ? AppUpdateCoordinatorResult.FromStatus(AppUpdateCoordinatorStatus.CheckFailed)
                    : AppUpdateCoordinatorResult.NoUpdate());
            },
            delay =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            ex => throw new InvalidOperationException("Unexpected startup update exception.", ex));

        Assert.Equal(AppUpdateCoordinatorStatus.NoUpdate, result.Status);
        Assert.Equal(2, checkCallCount);
        var delay = Assert.Single(delays);
        Assert.Equal(TimeSpan.FromSeconds(5), delay);
    }

    [Fact]
    public async Task RunStartupAppUpdateCheckCoreAsync_retries_once_after_policy_failure()
    {
        var checkCallCount = 0;
        var delays = new List<TimeSpan>();

        var result = await MainWindow.RunStartupAppUpdateCheckCoreAsync(
            () =>
            {
                checkCallCount++;
                return Task.FromResult(checkCallCount == 1
                    ? AppUpdateCoordinatorResult.FromStatus(AppUpdateCoordinatorStatus.PolicyFailed)
                    : AppUpdateCoordinatorResult.NoUpdate());
            },
            delay =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            ex => throw new InvalidOperationException("Unexpected startup update exception.", ex));

        Assert.Equal(AppUpdateCoordinatorStatus.NoUpdate, result.Status);
        Assert.Equal(2, checkCallCount);
        var delay = Assert.Single(delays);
        Assert.Equal(TimeSpan.FromSeconds(5), delay);
    }

    [Fact]
    public async Task RunStartupAppUpdateCheckCoreAsync_returns_policy_failure_when_retry_also_fails()
    {
        var checkCallCount = 0;
        var delays = new List<TimeSpan>();
        var reportedResults = new List<AppUpdateCoordinatorResult>();
        var method = typeof(MainWindow).GetMethod(
            nameof(MainWindow.RunStartupAppUpdateCheckCoreAsync),
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        var invocationResult = method!.Invoke(
            null,
            [
                (Func<Task<AppUpdateCoordinatorResult>>)(() =>
                {
                    checkCallCount++;
                    return Task.FromResult(AppUpdateCoordinatorResult.FromStatus(AppUpdateCoordinatorStatus.PolicyFailed));
                }),
                (Func<TimeSpan, Task>)(delay =>
                {
                    delays.Add(delay);
                    return Task.CompletedTask;
                }),
                (Action<Exception>)(ex => throw new InvalidOperationException("Unexpected startup update exception.", ex)),
                (Action<AppUpdateCoordinatorResult>)(result => reportedResults.Add(result)),
            ]);
        var task = Assert.IsAssignableFrom<Task<AppUpdateCoordinatorResult>>(invocationResult);

        var returnedResult = await task;

        Assert.Equal(2, checkCallCount);
        Assert.Single(delays);
        var result = Assert.Single(reportedResults);
        Assert.Equal(AppUpdateCoordinatorStatus.PolicyFailed, result.Status);
        Assert.Equal("settings.status.appUpdatePolicyFailed", result.StatusKey);
        Assert.Equal(AppUpdateCoordinatorStatus.PolicyFailed, returnedResult.Status);
    }

    [Fact]
    public async Task RunStartupAppUpdateCheckCoreAsync_reports_exception_and_returns_check_failed()
    {
        var checkCallCount = 0;
        var delays = new List<TimeSpan>();
        Exception? reportedException = null;

        var result = await MainWindow.RunStartupAppUpdateCheckCoreAsync(
            () =>
            {
                checkCallCount++;
                throw new HttpRequestException("center unavailable");
            },
            delay =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            ex => reportedException = ex);

        Assert.Equal(1, checkCallCount);
        Assert.Empty(delays);
        Assert.IsType<HttpRequestException>(reportedException);
        Assert.Equal(AppUpdateCoordinatorStatus.CheckFailed, result.Status);
    }

    [Theory]
    [InlineData(AppUpdateCoordinatorStatus.NoUpdate, true)]
    [InlineData(AppUpdateCoordinatorStatus.OptionalDeclined, true)]
    [InlineData(AppUpdateCoordinatorStatus.CheckFailed, false)]
    [InlineData(AppUpdateCoordinatorStatus.PolicyFailed, false)]
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
