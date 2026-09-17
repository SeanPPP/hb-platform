using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class PaymentMethodSettingsServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("broken")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"UseManualCard\":\"true\"}")]
    public async Task Missing_or_invalid_settings_keep_both_switches_off(string? value)
    {
        var service = new PaymentMethodSettingsService(new SettingsRepository { Value = value });
        Assert.Equal(new PaymentMethodSettings(), service.Current);
        Assert.Equal(new PaymentMethodSettings(), await service.LoadAsync());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Saved_settings_round_trip_in_a_new_service(bool manual, bool voucher)
    {
        var repository = new SettingsRepository();
        var service = new PaymentMethodSettingsService(repository);
        var notifications = 0;
        service.Changed += (_, _) => notifications++;
        var settings = new PaymentMethodSettings(manual, voucher);
        await service.SaveAsync(settings);
        await service.SaveAsync(settings);

        Assert.Equal(settings, service.Current);
        Assert.Equal(1, notifications);
        Assert.Equal(settings, await new PaymentMethodSettingsService(repository).LoadAsync());
        Assert.Equal(PaymentMethodSettingsService.SettingsKey, repository.LastWrittenKey);
    }

    [Fact]
    public async Task Failed_save_keeps_last_applied_settings_and_does_not_notify()
    {
        var repository = new SettingsRepository();
        var service = new PaymentMethodSettingsService(repository);
        await service.SaveAsync(new(true, false));
        var savedValue = repository.Value;
        var notifications = 0;
        service.Changed += (_, _) => notifications++;
        repository.FailSave = true;

        await Assert.ThrowsAsync<IOException>(() => service.SaveAsync(new(false, true)));

        Assert.Equal(new PaymentMethodSettings(true, false), service.Current);
        Assert.Equal(savedValue, repository.Value);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task Read_failure_propagates_without_replacing_applied_settings()
    {
        var repository = new SettingsRepository();
        var service = new PaymentMethodSettingsService(repository);
        await service.SaveAsync(new(true, true));
        repository.FailRead = true;
        await Assert.ThrowsAsync<IOException>(() => service.LoadAsync());
        Assert.Equal(new PaymentMethodSettings(true, true), service.Current);
    }

    [Fact]
    public async Task Concurrent_save_waits_for_load_and_remains_current()
    {
        var repository = new SettingsRepository { BlockRead = true };
        var service = new PaymentMethodSettingsService(repository);
        var load = service.LoadAsync();
        await repository.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var save = service.SaveAsync(new(true, true));
        Assert.False(save.IsCompleted);
        repository.ReadRelease.SetResult();
        await load;
        await save;
        Assert.Equal(new PaymentMethodSettings(true, true), service.Current);
    }

    private sealed class SettingsRepository : ILocalAppSettingsRepository
    {
        public string? Value { get; set; }
        public string? LastWrittenKey { get; private set; }
        public bool FailSave { get; set; }
        public bool FailRead { get; set; }
        public bool BlockRead { get; init; }
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
        {
            if (FailRead) throw new IOException("read failed");
            var snapshot = Value;
            ReadStarted.TrySetResult();
            if (BlockRead) await ReadRelease.Task.WaitAsync(cancellationToken);
            return snapshot;
        }
        public Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailSave) throw new IOException("save failed");
            LastWrittenKey = key;
            Value = value;
            return Task.CompletedTask;
        }
        public Task SetValuesAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task DeleteValueAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
