using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.RemoteMaintenance.Setup;

namespace Hbpos.Client.Tests;

[Collection(ShutdownTimingTestCollection.Name)]
public sealed class RemoteMaintenanceServiceTests
{
    [Fact]
    public async Task Fresh_install_reads_id_written_by_elevated_helper_before_commit()
    {
        using var fixture = new Fixture();
        fixture.Launcher.InstallWritesId = true;

        var result = await fixture.Service.InstallAsync(fixture.Session);

        Assert.True(result.Succeeded);
        Assert.Equal("rustdesk-123", fixture.Api.CommittedRustdeskIds.Single());
        Assert.Equal(new[] { "install", "configure" }, fixture.Launcher.Stages);
    }

    [Fact]
    public async Task Prepared_retry_reuses_operation_and_password_and_does_not_reinstall_twice()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        var password = "same-password-for-retry";
        await fixture.WriteStateAsync(fixture.State(operationId, RemoteMaintenanceOperationState.Prepared,
            rustdeskId: string.Empty, protectedMonitorToken: null));
        fixture.Launcher.InstallWritesId = true;

        var first = await fixture.Service.InstallAsync(fixture.Session);

        Assert.True(first.Succeeded);
        Assert.Equal(operationId, fixture.Api.PreparedOperations.Single());
        Assert.Equal(password, fixture.Api.CommittedPasswords.Single());
        Assert.Equal(1, fixture.Launcher.Stages.Count(x => x == "install"));
        Assert.Equal(operationId, fixture.Api.CommittedOperations.Single());
    }

    [Fact]
    public async Task Installed_pending_commit_retries_commit_without_reinstall()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        await fixture.WriteStateAsync(fixture.State(operationId, RemoteMaintenanceOperationState.InstalledPendingCommit,
            rustdeskId: "rustdesk-123", protectedMonitorToken: null));

        var result = await fixture.Service.InstallAsync(fixture.Session);

        Assert.True(result.Succeeded);
        Assert.Empty(fixture.Launcher.Stages.Where(x => x == "install"));
        Assert.Equal(new[] { "configure" }, fixture.Launcher.Stages);
        Assert.Equal(operationId, fixture.Api.CommittedOperations.Single());
    }

    [Fact]
    public async Task Committed_recovery_reconfigures_same_operation_and_token()
    {
        using var fixture = new Fixture();
        var operationId = Guid.NewGuid();
        await fixture.WriteStateAsync(fixture.State(operationId, RemoteMaintenanceOperationState.Committed,
            rustdeskId: "rustdesk-123", protectedMonitorToken: fixture.Protect("monitor-token")));

        var result = await fixture.Service.InstallAsync(fixture.Session);

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "configure" }, fixture.Launcher.Stages);
        Assert.Equal(operationId, fixture.Launcher.OperationIds.Single());
    }

    [Fact]
    public async Task Uac_cancellation_requests_fail_closed_cleanup()
    {
        using var fixture = new Fixture(cancelInstall: true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Service.InstallAsync(fixture.Session));

        Assert.Contains("fail-closed", fixture.Launcher.Stages);
    }

    [Fact]
    public async Task Helper_rejection_does_not_request_cleanup_for_preexisting_services()
    {
        using var fixture = new Fixture();
        fixture.Launcher.InstallExitCode = 4;

        var result = await fixture.Service.InstallAsync(fixture.Session);

        Assert.False(result.Succeeded);
        var cleanup = Assert.Single(fixture.Launcher.FailClosedRequests);
        Assert.False(cleanup.RustDeskInstalled);
        Assert.False(cleanup.StatusAgentInstalled);
    }

    [Fact]
    public async Task Partial_install_failure_keeps_cleanup_for_components_already_touched()
    {
        using var fixture = new Fixture();
        fixture.Launcher.InstallExitCode = 4;
        fixture.Launcher.InstallRustDeskInstalled = true;

        var result = await fixture.Service.InstallAsync(fixture.Session);

        Assert.False(result.Succeeded);
        var cleanup = Assert.Single(fixture.Launcher.FailClosedRequests);
        Assert.True(cleanup.RustDeskInstalled);
        Assert.False(cleanup.StatusAgentInstalled);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "hbpos-remote-test-" + Guid.NewGuid().ToString("N"));
        private readonly PrefixProtector _protector = new();
        private readonly RemoteMaintenanceJournal _journal;
        public Fixture(bool cancelInstall = false)
        {
            Directory.CreateDirectory(_root);
            _journal = new RemoteMaintenanceJournal(Path.Combine(_root, "journal.json"), _protector);
            Api = new FakeApi();
            Downloader = new FakeDownloader();
            Launcher = new FakeLauncher(_journal);
            Launcher.CancelInstall = cancelInstall;
            Installer = new FakeInstaller();
            Service = new RemoteMaintenanceService(Api, Downloader, Installer, Launcher, _journal, _protector);
        }

        public FakeApi Api { get; }
        public FakeDownloader Downloader { get; }
        public FakeLauncher Launcher { get; }
        public FakeInstaller Installer { get; }
        public RemoteMaintenanceService Service { get; }
        public PosSessionState Session => new("HB POS", "S001", "Main", "POS-01", "C001", "Alice", true, 0);

        public string Protect(string value) => _protector.Protect(value);

        public RemoteMaintenanceJournalState State(Guid operationId, RemoteMaintenanceOperationState state,
            string rustdeskId, string? protectedMonitorToken)
        {
            string? rustDeskPath = null;
            string? agentPath = null;
            string? dataDirectory = null;
            if (state == RemoteMaintenanceOperationState.InstalledPendingCommit)
            {
                dataDirectory = Path.Combine(_root, "data");
                Directory.CreateDirectory(dataDirectory);
                rustDeskPath = Path.Combine(dataDirectory, "rustdesk.exe");
                agentPath = Path.Combine(dataDirectory, "agent.exe");
                File.WriteAllBytes(rustDeskPath, [1]);
                File.WriteAllBytes(agentPath, [1]);
            }

            return new(
                operationId, Guid.NewGuid(), state, rustdeskId, "1.4.9", _protector.Protect("same-password-for-retry"),
                protectedMonitorToken, state == RemoteMaintenanceOperationState.Committed ? "https://example.test/heartbeat" : null,
                DateTimeOffset.UtcNow, Manifest.Config, rustDeskPath, agentPath, dataDirectory, Manifest.Artifacts);
        }

        public Task WriteStateAsync(RemoteMaintenanceJournalState state) => _journal.WriteAsync(state);

        public void Dispose() => Directory.Delete(_root, recursive: true);

        private static class Manifest
        {
            public static RemoteMaintenanceConfig Config => new("https://id.example.test", "https://relay.example.test", "public-key");
            public static RemoteMaintenanceArtifactManifest Artifacts => new(
                new("1.4.9", "rustdesk.exe", "/rustdesk", "00".PadLeft(64, '0'), 1),
                new("1.0.0", "agent.exe", "/agent", "11".PadLeft(64, '0'), 1));
        }

        private sealed class PrefixProtector : IRemoteMaintenanceSecretProtector
        {
            public string Protect(string plaintext) => "protected:" + plaintext;
            public string? Unprotect(string protectedValue) => protectedValue.StartsWith("protected:", StringComparison.Ordinal)
                ? protectedValue[10..] : null;
        }

        public sealed class FakeApi : IRemoteMaintenanceApiClient
        {
            public List<Guid> PreparedOperations { get; } = [];
            public List<Guid> CommittedOperations { get; } = [];
            public List<string> CommittedPasswords { get; } = [];
            public List<string> CommittedRustdeskIds { get; } = [];
            public Task<RemoteMaintenancePrepareResponse> PrepareAsync(RemoteMaintenancePrepareRequest request, CancellationToken cancellationToken = default)
            {
                PreparedOperations.Add(request.OperationId);
                return Task.FromResult(new RemoteMaintenancePrepareResponse(request.OperationId, Guid.NewGuid(), Manifest.Config, Manifest.Artifacts));
            }
            public Task<Stream> DownloadArtifactAsync(string downloadUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<RemoteMaintenanceCommitResponse> CommitAsync(RemoteMaintenanceCommitRequest request, CancellationToken cancellationToken = default)
            {
                CommittedOperations.Add(request.OperationId);
                CommittedPasswords.Add(request.Password);
                CommittedRustdeskIds.Add(request.RustdeskId);
                return Task.FromResult(new RemoteMaintenanceCommitResponse(Guid.NewGuid(), "monitor-token", "https://example.test/heartbeat"));
            }
        }

        public sealed class FakeDownloader : IRemoteMaintenanceArtifactDownloader
        {
            public Task<string> DownloadAndVerifyAsync(RemoteMaintenanceArtifact artifact, string destinationDirectory, CancellationToken cancellationToken = default)
            {
                Directory.CreateDirectory(destinationDirectory);
                var path = Path.Combine(destinationDirectory, artifact.FileName);
                File.WriteAllBytes(path, [1]);
                return Task.FromResult(path);
            }
        }

        public sealed class FakeInstaller : IRemoteMaintenanceInstaller
        {
            public Task<RemoteMaintenanceInstallationResult> InstallAsync(RemoteMaintenanceInstallationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task FailClosedAsync(RemoteMaintenanceInstallationResult installation, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task ConfigureStatusAgentAsync(RemoteMaintenancePrepareResponse prepare, RemoteMaintenanceCommitResponse commit, string rustdeskId, string clientVersion, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<string?> GetRustdeskIdAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("unused");
            public Task<RemoteMaintenanceStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(new RemoteMaintenanceStatus(false, "", "", "stopped"));
        }

        public sealed class FakeLauncher(RemoteMaintenanceJournal journal) : IRemoteMaintenanceUacHelperLauncher
        {
            public List<string> Stages { get; } = [];
            public List<Guid> OperationIds { get; } = [];
            public List<RemoteMaintenanceInstallationResult> FailClosedRequests { get; } = [];
            public bool InstallWritesId { get; set; }
            public bool CancelInstall { get; set; }
            public int InstallExitCode { get; set; }
            public bool InstallRustDeskInstalled { get; set; }
            public bool InstallStatusAgentInstalled { get; set; }
            public async Task<int> RunAsync(string helperPath, string journalPath, Guid operationId, string stage, CancellationToken cancellationToken = default)
            {
                Stages.Add(stage);
                OperationIds.Add(operationId);
                if (stage == "install" && CancelInstall) throw new OperationCanceledException(cancellationToken);
                if (stage == "fail-closed")
                {
                    var state = await journal.ReadAsync(CancellationToken.None) ?? throw new InvalidOperationException();
                    FailClosedRequests.Add(new RemoteMaintenanceInstallationResult(
                        state.RustDeskInstalled,
                        state.StatusAgentInstalled,
                        state.RustdeskId,
                        state.ClientVersion,
                        state.DataDirectory ?? string.Empty));
                    return 0;
                }
                if (stage == "install" && InstallWritesId)
                {
                    var state = await journal.ReadAsync(cancellationToken) ?? throw new InvalidOperationException();
                    await journal.WriteAsync(state with
                    {
                        RustdeskId = "rustdesk-123",
                        RustDeskInstalled = InstallRustDeskInstalled,
                        StatusAgentInstalled = InstallStatusAgentInstalled
                    }, cancellationToken);
                }
                if (stage == "install" && (InstallRustDeskInstalled || InstallStatusAgentInstalled))
                {
                    var state = await journal.ReadAsync(cancellationToken) ?? throw new InvalidOperationException();
                    await journal.WriteAsync(state with
                    {
                        RustDeskInstalled = InstallRustDeskInstalled,
                        StatusAgentInstalled = InstallStatusAgentInstalled
                    }, cancellationToken);
                }
                return stage == "install" ? InstallExitCode : 0;
            }
        }
    }
}
