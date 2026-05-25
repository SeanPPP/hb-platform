using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class UiPriorityCoordinatorTests
{
    [Fact]
    public async Task WaitForUiIdleAsync_WaitsUntilRecentInputBecomesIdle()
    {
        var coordinator = new UiPriorityCoordinator(
            idleDelay: TimeSpan.FromMilliseconds(80),
            pollDelay: TimeSpan.FromMilliseconds(5));

        coordinator.NotifyUserInput();

        var waitTask = coordinator.WaitForUiIdleAsync();
        await Task.Delay(20);

        Assert.False(waitTask.IsCompleted);
        await waitTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(coordinator.IsUiActive);
    }

    [Fact]
    public async Task BeginUiOperation_KeepsCoordinatorActiveUntilScopeDisposes()
    {
        var coordinator = new UiPriorityCoordinator(
            idleDelay: TimeSpan.FromMilliseconds(20),
            pollDelay: TimeSpan.FromMilliseconds(5));

        using var operation = coordinator.BeginUiOperation("scan");
        var waitTask = coordinator.WaitForUiIdleAsync();
        await Task.Delay(30);

        Assert.True(coordinator.IsUiActive);
        Assert.False(waitTask.IsCompleted);

        operation.Dispose();
        await waitTask.WaitAsync(TimeSpan.FromSeconds(1));
    }
}
