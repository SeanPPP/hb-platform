using System.Net;
using System.Net.Http.Json;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Health;

namespace Hbpos.Client.Tests;

public sealed class ApiServerSwitchCoordinatorTests
{
    [Fact]
    public async Task Switch_same_address_does_not_clear_session_or_run_health_check()
    {
        var runtime = new FakeRuntime();
        var (coordinator, healthCalls, _) = CreateCoordinator(runtime);

        var result = await coordinator.SwitchAsync(" HTTPS://CURRENT.EXAMPLE.COM/pos-api/ ");

        Assert.Equal(ApiServerSwitchStatus.SameAddress, result.Status);
        Assert.Equal(0, healthCalls());
        Assert.Empty(runtime.Events);
    }

    [Theory]
    [InlineData(1, false, false, 0, 0, 0, 0)]
    [InlineData(0, true, false, 0, 0, 0, 0)]
    [InlineData(0, false, true, 0, 0, 0, 0)]
    [InlineData(0, false, false, 1, 0, 0, 0)]
    [InlineData(0, false, false, 0, 1, 0, 0)]
    [InlineData(0, false, false, 0, 0, 1, 0)]
    [InlineData(0, false, false, 0, 0, 0, 1)]
    public async Task Switch_blocks_for_each_unsafe_runtime_state(
        int cartCount,
        bool cardPayment,
        bool paymentInteraction,
        int pending,
        int failed,
        int syncing,
        int pendingAudits)
    {
        var runtime = new FakeRuntime
        {
            Snapshot = new ApiServerSwitchSafetySnapshot(
                cartCount,
                cardPayment,
                paymentInteraction,
                PaymentTenderCount: 0,
                pending,
                failed,
                syncing,
                pendingAudits)
        };
        var (coordinator, healthCalls, _) = CreateCoordinator(runtime);

        var result = await coordinator.SwitchAsync("https://new.example.com/pos-api/");

        Assert.Equal(ApiServerSwitchStatus.Blocked, result.Status);
        Assert.NotNull(result.BlockReason);
        Assert.Equal(0, healthCalls());
        Assert.Empty(runtime.Events);
    }

    [Fact]
    public async Task Switch_commits_database_endpoint_and_shell_reset_after_health_check()
    {
        var runtime = new FakeRuntime();
        var (coordinator, _, saved) = CreateCoordinator(runtime);

        var result = await coordinator.SwitchAsync("https://new.example.com/pos-api/");

        Assert.Equal(ApiServerSwitchStatus.Success, result.Status);
        Assert.Equal(["prepare", "begin", "final-safety", "commit", "post-commit"], runtime.Events);
        Assert.Equal("https://new.example.com/pos-api/", saved());
    }

    [Fact]
    public async Task Switch_reports_pre_commit_failure_without_changing_runtime()
    {
        var runtime = new FakeRuntime { ThrowOnPrepare = true };
        var (coordinator, _, saved) = CreateCoordinator(runtime);

        var result = await coordinator.SwitchAsync("https://new.example.com/pos-api/");

        Assert.Equal(ApiServerSwitchStatus.PreCommitFailed, result.Status);
        Assert.Equal(["prepare"], runtime.Events);
        Assert.Null(saved());
    }

    [Fact]
    public async Task Switch_restores_saved_address_when_commit_fails_before_boundary_is_committed()
    {
        var runtime = new FakeRuntime { ThrowOnCommit = true };
        var (coordinator, _, saved) = CreateCoordinator(runtime);

        var result = await coordinator.SwitchAsync("https://new.example.com/pos-api/");

        Assert.Equal(ApiServerSwitchStatus.PreCommitFailed, result.Status);
        Assert.Equal(["prepare", "begin", "final-safety", "commit", "abort"], runtime.Events);
        Assert.Equal("https://current.example.com/pos-api/", saved());
    }

    [Fact]
    public async Task Switch_keeps_new_boundary_and_fails_closed_on_post_commit_failure()
    {
        var runtime = new FakeRuntime { ThrowOnPostCommit = true };
        var (coordinator, _, saved) = CreateCoordinator(runtime);

        var result = await coordinator.SwitchAsync("https://new.example.com/pos-api/");

        Assert.Equal(ApiServerSwitchStatus.PostCommitFailed, result.Status);
        Assert.Equal(["prepare", "begin", "final-safety", "commit", "post-commit"], runtime.Events);
        Assert.Equal("https://new.example.com/pos-api/", saved());
    }

    [Fact]
    public async Task Switch_keeps_new_boundary_when_post_commit_reports_cancellation()
    {
        var runtime = new FakeRuntime { CancelOnPostCommit = true };
        var (coordinator, _, saved) = CreateCoordinator(runtime);

        var result = await coordinator.SwitchAsync("https://new.example.com/pos-api/");

        Assert.Equal(ApiServerSwitchStatus.PostCommitFailed, result.Status);
        Assert.Equal(["prepare", "begin", "final-safety", "commit", "post-commit"], runtime.Events);
        Assert.Equal("https://new.example.com/pos-api/", saved());
    }

    [Fact]
    public async Task Switch_aborts_transition_when_final_safety_check_becomes_unsafe()
    {
        var runtime = new FakeRuntime
        {
            FinalSnapshot = ApiServerSwitchSafetySnapshot.Safe with { PendingSyncCount = 1 }
        };
        var (coordinator, _, saved) = CreateCoordinator(runtime);

        var result = await coordinator.SwitchAsync("https://new.example.com/pos-api/");

        Assert.Equal(ApiServerSwitchStatus.Blocked, result.Status);
        Assert.Equal(["prepare", "begin", "final-safety", "abort"], runtime.Events);
        Assert.Null(saved());
    }

    [Fact]
    public async Task Switch_aborts_transition_and_restores_address_when_audit_revision_changes_at_commit()
    {
        var runtime = new FakeRuntime { RejectCommit = true };
        var (coordinator, _, saved) = CreateCoordinator(runtime);

        var result = await coordinator.SwitchAsync("https://new.example.com/pos-api/");

        Assert.Equal(ApiServerSwitchStatus.Blocked, result.Status);
        Assert.Equal("settings.serverAddress.blocked.audit", result.BlockReason);
        Assert.Equal(["prepare", "begin", "final-safety", "commit", "abort"], runtime.Events);
        Assert.Equal("https://current.example.com/pos-api/", saved());
    }

    private static (ApiServerSwitchCoordinator Coordinator, Func<int> HealthCalls, Func<string?> Saved) CreateCoordinator(
        FakeRuntime runtime)
    {
        var healthCalls = 0;
        string? saved = null;
        var service = new ApiServerSettingsService(
            new HttpClient(new StubHandler(_ =>
            {
                healthCalls++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(ApiResult<HealthCheckResponse>.Ok(
                        new HealthCheckResponse(true, DateTimeOffset.UnixEpoch, "ok")))
                };
            })),
            () => "https://current.example.com/pos-api/",
            address => saved = address);
        var state = new ApiRuntimeEndpointState("https://current.example.com/pos-api/");
        return (new ApiServerSwitchCoordinator(service, state, runtime), () => healthCalls, () => saved);
    }

    private sealed class FakeRuntime : IApiServerSwitchRuntime
    {
        public ApiServerSwitchSafetySnapshot Snapshot { get; set; } = ApiServerSwitchSafetySnapshot.Safe;
        public ApiServerSwitchSafetySnapshot FinalSnapshot { get; set; } = ApiServerSwitchSafetySnapshot.Safe;
        public List<string> Events { get; } = [];
        public bool ThrowOnPrepare { get; set; }
        public bool ThrowOnCommit { get; set; }
        public bool RejectCommit { get; set; }
        public bool ThrowOnPostCommit { get; set; }
        public bool CancelOnPostCommit { get; set; }

        public Task<ApiServerSwitchSafetySnapshot> GetSafetySnapshotAsync(CancellationToken cancellationToken)
            => Task.FromResult(Snapshot);

        public Task<object> PrepareAsync(string targetAddress, CancellationToken cancellationToken)
        {
            Events.Add("prepare");
            if (ThrowOnPrepare)
            {
                throw new IOException("prepare failed");
            }

            return Task.FromResult<object>(new object());
        }

        public Task<object> BeginTransitionAsync(
            string targetAddress,
            object preparedSwitch,
            CancellationToken cancellationToken)
        {
            Events.Add("begin");
            return Task.FromResult<object>(new object());
        }

        public Task<ApiServerSwitchSafetySnapshot> GetFinalSafetySnapshotAsync(
            object transition,
            CancellationToken cancellationToken)
        {
            Events.Add("final-safety");
            return Task.FromResult(FinalSnapshot);
        }

        public bool Commit(object preparedSwitch)
        {
            Events.Add("commit");
            if (ThrowOnCommit)
            {
                throw new IOException("commit failed");
            }

            return !RejectCommit;
        }

        public void Abort(object transition) => Events.Add("abort");

        public Task PostCommitAsync(CancellationToken cancellationToken)
        {
            Events.Add("post-commit");
            if (CancelOnPostCommit)
            {
                return Task.FromCanceled(new CancellationToken(canceled: true));
            }

            return ThrowOnPostCommit
                ? Task.FromException(new InvalidOperationException("reset failed"))
                : Task.CompletedTask;
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
