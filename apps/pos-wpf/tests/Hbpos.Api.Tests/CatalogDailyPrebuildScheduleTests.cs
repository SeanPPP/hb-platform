using Hbpos.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Tests;

public sealed class CatalogDailyPrebuildScheduleTests
{
    [Fact]
    public void GetNextRunUtc_AfterOneAm_SkipsDaytimeCatchUp()
    {
        var brisbane = TimeZoneInfo.FindSystemTimeZoneById("Australia/Brisbane");
        var now = new DateTimeOffset(2026, 7, 30, 2, 0, 0, TimeSpan.FromHours(10));

        var nextRun = CatalogDailyPrebuildSchedule.GetNextRunUtc(now, brisbane);

        Assert.Equal(
            new DateTimeOffset(2026, 7, 31, 1, 0, 0, TimeSpan.FromHours(10)).ToUniversalTime(),
            nextRun);
    }

    [Fact]
    public void IsWithinStartWindow_RejectsLateDaytimeWake()
    {
        var scheduled = new DateTimeOffset(2026, 7, 30, 1, 0, 0, TimeSpan.FromHours(10))
            .ToUniversalTime();

        Assert.True(CatalogDailyPrebuildSchedule.IsWithinStartWindow(
            scheduled.AddMinutes(4),
            scheduled));
        Assert.False(CatalogDailyPrebuildSchedule.IsWithinStartWindow(
            scheduled.AddHours(7),
            scheduled));
    }

    [Fact]
    public async Task RunDailyPrebuildAsync_WaitsForEveryQueuedStore()
    {
        var scheduler = new RecordingScheduler();
        var service = new CatalogDailyPrebuildService(
            new StaticStoreProvider(["S01", "S02"]),
            scheduler,
            Options.Create(new CatalogDailyPrebuildOptions { Enabled = true }),
            TimeProvider.System,
            NullLogger<CatalogDailyPrebuildService>.Instance);

        var run = service.RunDailyPrebuildAsync(CancellationToken.None);
        await scheduler.AllStoresQueued.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(run.IsCompleted);
        scheduler.Completion.TrySetResult();

        await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(["S01", "S02"], scheduler.QueuedStores.Order());
    }

    private sealed class StaticStoreProvider(IReadOnlyList<string> storeCodes) : IActiveCatalogStoreProvider
    {
        public Task<IReadOnlyList<string>> GetActiveStoreCodesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(storeCodes);
        }
    }

    private sealed class RecordingScheduler : ICatalogBackgroundRefreshScheduler
    {
        private int _queueCount;

        public TaskCompletionSource AllStoresQueued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> QueuedStores { get; } = [];

        public void QueueRefresh(string storeCode)
        {
            _ = QueueRefreshAsync(storeCode);
        }

        public Task QueueRefreshAsync(string storeCode)
        {
            QueuedStores.Add(storeCode);
            if (Interlocked.Increment(ref _queueCount) == 2)
            {
                AllStoresQueued.TrySetResult();
            }

            return Completion.Task;
        }
    }
}
