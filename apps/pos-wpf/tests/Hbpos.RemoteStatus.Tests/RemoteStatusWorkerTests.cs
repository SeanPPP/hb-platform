using Microsoft.Extensions.Logging.Abstractions;

namespace Hbpos.RemoteStatus.Tests;

public sealed class RemoteStatusWorkerTests
{
    [Fact]
    public async Task SendOne_persists_sequence_before_sending_payload()
    {
        var order = new List<string>();
        var sender = new RecordingSender(order);
        var store = new RecordingSequenceStore(order);
        var worker = CreateWorker(new FixedProbe(RemoteRustDeskServiceStatus.Running), store, sender);

        await worker.SendOneAsync(CancellationToken.None);

        Assert.Equal(new[] { "allocate", "send" }, order);
        Assert.Equal(1, sender.Payloads.Single().Sequence);
        Assert.Equal("running", sender.Payloads.Single().ServiceStatus);
    }

    [Fact]
    public async Task SendOne_maps_probe_failure_to_checkFailed_without_throwing()
    {
        var sender = new RecordingSender([]);
        var worker = CreateWorker(new ThrowingProbe(), new RecordingSequenceStore([]), sender);

        await worker.SendOneAsync(CancellationToken.None);

        Assert.Equal("checkFailed", sender.Payloads.Single().ServiceStatus);
    }

    [Fact]
    public async Task Unauthorized_heartbeat_stops_follow_up_attempts()
    {
        var sender = new RecordingSender([], HeartbeatSendResult.Unauthorized(401));
        var worker = CreateWorker(new FixedProbe(RemoteRustDeskServiceStatus.Running), new RecordingSequenceStore([]), sender);

        await worker.SendOneAsync(CancellationToken.None);
        await worker.SendOneAsync(CancellationToken.None);

        Assert.Single(sender.Payloads);
        Assert.True(sender.LastWorkerWasStoppedForUnauthorized);
    }

    [Fact]
    public async Task SendOne_enforces_ten_second_minimum_even_when_status_is_unchanged()
    {
        var clock = new ManualTimeProvider();
        var sender = new RecordingSender([]);
        var worker = CreateWorker(new FixedProbe(RemoteRustDeskServiceStatus.Running), new RecordingSequenceStore([]), sender, clock);

        await worker.SendOneAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(2));
        await worker.SendOneAsync(CancellationToken.None);
        Assert.Single(sender.Payloads);

        clock.Advance(TimeSpan.FromSeconds(8));
        await worker.SendOneAsync(CancellationToken.None);
        Assert.Equal(2, sender.Payloads.Count);
    }

    [Fact]
    public async Task SendOne_recovers_from_network_failure_without_exiting_worker()
    {
        var clock = new ManualTimeProvider();
        var sender = new RecordingSender([]);
        sender.Errors.Enqueue(new HttpRequestException("offline"));
        sender.Results.Enqueue(HeartbeatSendResult.Success());
        var worker = CreateWorker(new FixedProbe(RemoteRustDeskServiceStatus.Running), new RecordingSequenceStore([]), sender, clock);

        await worker.SendOneAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(2));
        await worker.SendOneAsync(CancellationToken.None);
        Assert.Single(sender.Payloads);

        clock.Advance(TimeSpan.FromSeconds(8));
        await worker.SendOneAsync(CancellationToken.None);
        Assert.Equal(2, sender.Payloads.Count);
    }

    private static RemoteStatusWorker CreateWorker(
        IRemoteStatusProbe probe,
        IRemoteStatusSequenceStore store,
        RecordingSender sender,
        TimeProvider? timeProvider = null) =>
        new(
            new RemoteStatusAgentOptions("1.0", "123", "1.4.9", "https://example.test/heartbeat", "secret", "", TimeSpan.FromSeconds(15)),
            probe,
            store,
            sender,
            NullLogger<RemoteStatusWorker>.Instance,
            timeProvider);

    private sealed class FixedProbe(RemoteRustDeskServiceStatus status) : IRemoteStatusProbe
    {
        public Task<RustDeskProbeResult> ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new RustDeskProbeResult(status, "123", "1.4.9"));
    }

    private sealed class ThrowingProbe : IRemoteStatusProbe
    {
        public Task<RustDeskProbeResult> ProbeAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("probe failed");
    }

    private sealed class RecordingSequenceStore(List<string> order) : IRemoteStatusSequenceStore
    {
        public Task<long> AllocateNextAsync(CancellationToken cancellationToken)
        {
            order.Add("allocate");
            return Task.FromResult(1L);
        }
    }

    private sealed class RecordingSender(
        List<string> order,
        HeartbeatSendResult? result = null) : IRemoteStatusHeartbeatSender
    {
        public List<RemoteHeartbeatPayload> Payloads { get; } = [];
        public Queue<HeartbeatSendResult> Results { get; } = [];
        public Queue<Exception> Errors { get; } = [];
        public bool LastWorkerWasStoppedForUnauthorized { get; private set; }

        public Task<HeartbeatSendResult> SendAsync(
            RemoteHeartbeatPayload payload,
            string heartbeatUrl,
            string monitorToken,
            CancellationToken cancellationToken)
        {
            order.Add("send");
            Payloads.Add(payload);
            if (Errors.Count > 0) throw Errors.Dequeue();
            if (result?.StopRetrying == true) LastWorkerWasStoppedForUnauthorized = true;
            return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : result ?? HeartbeatSendResult.Success());
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
