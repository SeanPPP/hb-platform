using System.Windows;
using Hbpos.Client.Wpf;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class MainWindowStateTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData(MainWindow.FullscreenWindowModeValue)]
    public void ResolveWindowState_defaults_to_fullscreen(string? savedMode)
    {
        Assert.Equal(WindowState.Maximized, MainWindow.ResolveWindowState(savedMode));
    }

    [Theory]
    [InlineData(MainWindow.NormalCenteredWindowModeValue)]
    [InlineData("normalcentered")]
    public void ResolveWindowState_restores_normal_centered_mode(string savedMode)
    {
        Assert.Equal(WindowState.Normal, MainWindow.ResolveWindowState(savedMode));
    }

    [Theory]
    [InlineData(WindowState.Maximized, MainWindow.FullscreenWindowModeValue)]
    [InlineData(WindowState.Normal, MainWindow.NormalCenteredWindowModeValue)]
    [InlineData(WindowState.Minimized, null)]
    public void GetPersistedWindowMode_ignores_minimized(
        WindowState state,
        string? expectedMode)
    {
        Assert.Equal(expectedMode, MainWindow.GetPersistedWindowMode(state));
    }

    [Fact]
    public async Task LoadWindowStateAsync_falls_back_to_fullscreen_when_local_settings_fail()
    {
        var repository = new RecordingSettingsRepository { ExceptionToThrow = new InvalidOperationException("broken") };
        Exception? reportedException = null;

        var state = await MainWindow.LoadWindowStateAsync(repository, ex => reportedException = ex);

        Assert.Equal(WindowState.Maximized, state);
        Assert.Same(repository.ExceptionToThrow, reportedException);
    }

    [Fact]
    public async Task PersistWindowModeAfterAsync_waits_for_previous_save_and_swallows_write_failure()
    {
        var previousSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new RecordingSettingsRepository { ExceptionToThrow = new InvalidOperationException("broken") };
        Exception? reportedException = null;

        var saveTask = MainWindow.PersistWindowModeAfterAsync(
            previousSave.Task,
            repository,
            MainWindow.NormalCenteredWindowModeValue,
            ex => reportedException = ex);

        Assert.Equal(0, repository.SetCallCount);

        previousSave.SetResult();
        await saveTask;

        Assert.Equal(1, repository.SetCallCount);
        Assert.Same(repository.ExceptionToThrow, reportedException);
    }

    private sealed class RecordingSettingsRepository : ILocalAppSettingsRepository
    {
        public Exception? ExceptionToThrow { get; init; }

        public int SetCallCount { get; private set; }

        public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
        {
            return ExceptionToThrow is null
                ? Task.FromResult<string?>(null)
                : Task.FromException<string?>(ExceptionToThrow);
        }

        public Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            SetCallCount++;
            return ExceptionToThrow is null
                ? Task.CompletedTask
                : Task.FromException(ExceptionToThrow);
        }

        public Task DeleteValueAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
