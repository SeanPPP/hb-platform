using System.Net;
using BlazorApp.Shared.Security;
using Hbpos.Api.Security;
using Hbpos.Api.Services;
using Hbpos.Contracts.Linkly;
using Microsoft.Extensions.Options;
using CredentialProtectionContract = BlazorApp.Shared.Security.LinklyCloudTerminalCredentialDataProtection;
using PosCredentialDataProtection = Hbpos.Api.Security.LinklyCloudTerminalCredentialDataProtection;

namespace Hbpos.Api.Tests;

public sealed class LinklyCloudTerminalCredentialBoundaryTests
{
    [Fact]
    public void Response_display_name_query_never_reads_terminal_credentials()
    {
        var sql = SqlSugarLinklyCloudTerminalRepository.GetDisplayNameSql;

        Assert.Contains("[DisplayName]", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("[Username]", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("[Password]", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("[Secret]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Pairing_completion_materialization_returns_only_non_sensitive_state()
    {
        var stored = CreateStoredTerminal(
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            "protected-password") with
        {
            Username = "sensitive-user",
            Secret = "protected-secret",
            PairingState = "Ready",
            PosId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            CredentialProtectionVersion = CredentialProtectionContract.CurrentVersion
        };

        var completion = SqlSugarLinklyCloudTerminalRepository.MaterializePairingCompletion(stored);

        Assert.Empty(completion.Username);
        Assert.Empty(completion.Password);
        Assert.Null(completion.Secret);
        Assert.True(completion.HasUsableSecret);
        Assert.Equal("Ready", completion.PairingState);
        Assert.Equal(stored.TerminalId, completion.TerminalId);
        Assert.Equal(stored.DisplayName, completion.DisplayName);
    }

    [Fact]
    public async Task Pairing_receives_plaintext_password_and_persists_protected_secret()
    {
        var protector = CreateProtector();
        var terminalId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var repository = new ProtectingTerminalRepository(
            CreateStoredTerminal(terminalId, protector.ProtectPassword("lane-password")),
            protector);
        var transport = new RecordingPairingTransport("upstream-secret");
        var service = CreateService(repository, transport);

        var response = await service.PairTerminalAsync(
            "S01",
            "POS-01",
            terminalId,
            new LinklyCloudBackendPairRequest("Sandbox", "123456"),
            "device:POS-01",
            CancellationToken.None);

        Assert.Equal("lane-password", transport.Password);
        Assert.Equal(1, transport.Calls);
        Assert.Equal("Ready", response.PairingState);
        Assert.NotNull(repository.Stored.Secret);
        Assert.NotEqual("upstream-secret", repository.Stored.Secret);
        Assert.Equal("upstream-secret", protector.UnprotectSecret(repository.Stored.Secret!));
        Assert.Equal(
            CredentialProtectionContract.CurrentVersion,
            repository.Stored.CredentialProtectionVersion);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Legacy_or_corrupted_password_is_rejected_before_pairing_upstream(bool legacy)
    {
        var protector = CreateProtector();
        var terminalId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var stored = CreateStoredTerminal(
            terminalId,
            legacy ? "legacy-plaintext" : "corrupted-ciphertext") with
        {
            CredentialProtectionVersion = legacy
                ? CredentialProtectionContract.LegacyPlaintextVersion
                : CredentialProtectionContract.CurrentVersion
        };
        var repository = new ProtectingTerminalRepository(stored, protector);
        var transport = new RecordingPairingTransport("unused");
        var service = CreateService(repository, transport);

        var exception = await Record.ExceptionAsync(() => service.PairTerminalAsync(
            "S01",
            "POS-01",
            terminalId,
            new LinklyCloudBackendPairRequest("Sandbox", "123456"),
            "device:POS-01",
            CancellationToken.None));

        if (legacy)
        {
            Assert.IsType<LinklyCloudTerminalCredentialReentryRequiredException>(exception);
        }
        else
        {
            Assert.IsType<LinklyCloudTerminalCredentialUnavailableException>(exception);
        }
        Assert.Equal(0, transport.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void List_materialization_marks_legacy_or_corrupted_credentials_for_repair(bool legacy)
    {
        var protector = CreateProtector();
        var stored = CreateStoredTerminal(
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            legacy ? "legacy-plaintext" : protector.ProtectPassword("lane-password")) with
        {
            Secret = legacy ? "legacy-secret" : "corrupted-secret",
            PairingState = "Ready",
            PosId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            CredentialProtectionVersion = legacy
                ? CredentialProtectionContract.LegacyPlaintextVersion
                : CredentialProtectionContract.CurrentVersion
        };

        var materialized = SqlSugarLinklyCloudTerminalRepository.MaterializeListTerminal(
            stored,
            protector);

        Assert.Equal("NeedsRepair", materialized.PairingState);
        Assert.Empty(materialized.Password);
        Assert.Null(materialized.Secret);
        Assert.False(materialized.HasUsableSecret);
    }

    [Fact]
    public void List_materialization_validates_readiness_without_returning_credentials()
    {
        var protector = CreateProtector();
        var stored = CreateStoredTerminal(
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            protector.ProtectPassword("lane-password")) with
        {
            Secret = protector.ProtectSecret("terminal-secret"),
            PairingState = "Ready",
            PosId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
        };

        var materialized = SqlSugarLinklyCloudTerminalRepository.MaterializeListTerminal(
            stored,
            protector);

        Assert.Equal("Ready", materialized.PairingState);
        Assert.Empty(materialized.Password);
        Assert.Null(materialized.Secret);
        Assert.True(materialized.HasUsableSecret);
    }

    [Fact]
    public async Task Corrupted_secret_is_rejected_before_token_http_request()
    {
        var protector = CreateProtector();
        var terminalId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var repository = new ProtectingTerminalRepository(
            CreateStoredTerminal(terminalId, protector.ProtectPassword("lane-password")) with
            {
                Secret = "corrupted-secret",
                PairingState = "Ready",
                PosId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
            },
            protector)
        {
            Mode = "Active"
        };
        var handler = new CountingHttpMessageHandler();
        var provider = new HttpLinklyCloudBackendTokenProvider(
            new UnusedCredentialRepository(),
            new UnusedTerminalCredentialRepository(),
            new HttpClient(handler),
            Options.Create(new LinklyCloudBackendAsyncOptions()),
            logger: null,
            repository);

        var exception = await Assert.ThrowsAsync<LinklyCloudTerminalCredentialUnavailableException>(() =>
            provider.GetTokenAsync(
                "Sandbox",
                "S01",
                "POS-01",
                terminalId,
                CancellationToken.None));

        Assert.Equal(
            "Linkly Cloud terminal credentials are unavailable. Re-enter them in the management portal.",
            exception.Message);
        Assert.Equal(0, handler.Calls);
    }

    private static ILinklyCloudTerminalCredentialProtector CreateProtector()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "hbpos-linkly-boundary-tests",
            Guid.NewGuid().ToString("N"));
        return PosCredentialDataProtection.CreateProtector(
            PosCredentialDataProtection.CreateProvider(path));
    }

    private static LinklyCloudTerminalService CreateService(
        ILinklyCloudTerminalRepository repository,
        ILinklyCloudPairingTransport transport) => new(
            repository,
            new InMemoryLinklyCloudBackendAsyncRepository(),
            transport,
            Options.Create(new LinklyCloudBackendAsyncOptions
            {
                SandboxAuthBaseUrl = "https://auth.sandbox.example/v1/"
            }));

    private static LinklyCloudTerminalRecord CreateStoredTerminal(
        Guid terminalId,
        string protectedPassword) => new()
        {
            TerminalId = terminalId,
            Environment = "Sandbox",
            StoreCode = "S01",
            LaneNo = 1,
            DisplayName = "Front",
            Username = "lane-user",
            Password = protectedPassword,
            PairingState = "Unpaired",
            CredentialProtectionVersion = CredentialProtectionContract.CurrentVersion,
            UpdatedAt = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc)
        };

    private sealed class RecordingPairingTransport(string secret) : ILinklyCloudPairingTransport
    {
        public int Calls { get; private set; }
        public string? Password { get; private set; }

        public Task<LinklyCloudPairingTransportResponse> PairAsync(
            string authBaseUrl,
            string username,
            string password,
            string pairCode,
            CancellationToken cancellationToken)
        {
            Calls++;
            Password = password;
            return Task.FromResult(new LinklyCloudPairingTransportResponse(HttpStatusCode.OK, secret));
        }
    }

    private sealed class ProtectingTerminalRepository(
        LinklyCloudTerminalRecord stored,
        ILinklyCloudTerminalCredentialProtector protector) : ILinklyCloudTerminalRepository
    {
        public LinklyCloudTerminalRecord Stored { get; private set; } = stored;
        public string Mode { get; init; } = "Legacy";

        public Task<IReadOnlyList<LinklyCloudTerminalRecord>> ListAsync(
            string environment, string storeCode, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LinklyCloudTerminalRecord>>([
                SqlSugarLinklyCloudTerminalRepository.MaterializeListTerminal(Stored, protector)]);

        public Task<LinklyCloudTerminalRecord?> GetAsync(
            string environment, string storeCode, Guid terminalId, CancellationToken cancellationToken) =>
            Task.FromResult<LinklyCloudTerminalRecord?>(
                SqlSugarLinklyCloudTerminalRepository.MaterializeRuntimeTerminal(Stored, protector));

        public Task<string> GetConfigurationModeAsync(
            string environment, string storeCode, CancellationToken cancellationToken) =>
            Task.FromResult(Mode);

        public Task<LinklyCloudTerminalRecord?> TryBeginPairingAsync(
            string environment,
            string storeCode,
            Guid terminalId,
            Guid pairingAttemptId,
            DateTime pairingLeaseExpiresAt,
            DateTime expectedUpdatedAt,
            DateTime updatedAt,
            string? updatedBy,
            CancellationToken cancellationToken)
        {
            Stored = Stored with
            {
                PairingState = "Unknown",
                PairingAttemptId = pairingAttemptId,
                PairingLeaseExpiresAt = pairingLeaseExpiresAt,
                UpdatedAt = updatedAt,
                UpdatedBy = updatedBy
            };
            return Task.FromResult<LinklyCloudTerminalRecord?>(
                SqlSugarLinklyCloudTerminalRepository.MaterializeRuntimeTerminal(Stored, protector));
        }

        public Task<LinklyCloudTerminalRecord> UpdatePairingAsync(
            string environment,
            string storeCode,
            Guid terminalId,
            Guid expectedPairingAttemptId,
            DateTime expectedUpdatedAt,
            string pairingState,
            string? secret,
            string? posId,
            DateTime updatedAt,
            string? updatedBy,
            CancellationToken cancellationToken)
        {
            Stored = Stored with
            {
                PairingState = pairingState,
                Secret = SqlSugarLinklyCloudTerminalRepository.ProtectSecretForStorage(secret, protector),
                PosId = posId,
                PairingAttemptId = null,
                PairingLeaseExpiresAt = null,
                CredentialProtectionVersion = CredentialProtectionContract.CurrentVersion,
                UpdatedAt = updatedAt,
                UpdatedBy = updatedBy
            };
            return Task.FromResult(
                SqlSugarLinklyCloudTerminalRepository.MaterializeRuntimeTerminal(Stored, protector));
        }

        public Task<LinklyCloudDeviceSelectionRecord?> GetSelectionAsync(
            string environment, string storeCode, string deviceCode, CancellationToken cancellationToken) =>
            Task.FromResult<LinklyCloudDeviceSelectionRecord?>(null);

        public Task<LinklyCloudDeviceSelectionRecord> UpsertSelectionAsync(
            string environment, string storeCode, string deviceCode, Guid terminalId,
            long? expectedRevision, DateTime updatedAt, string? updatedBy,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ReleasePairingLeaseAsync(
            string environment, string storeCode, Guid terminalId, Guid expectedPairingAttemptId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> TryAcquireOperationLeaseAsync(
            string environment, string storeCode, string deviceCode, Guid terminalId,
            long expectedSelectionRevision, DateTime expectedTerminalUpdatedAt,
            Guid operationLeaseId, DateTime operationLeaseExpiresAt, DateTime now,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task ReleaseOperationLeaseAsync(
            string environment, string storeCode, Guid terminalId, Guid expectedOperationLeaseId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> TryRecordHealthAsync(
            string environment, string storeCode, Guid terminalId, DateTime expectedTerminalUpdatedAt,
            string healthStatus, DateTime checkedAt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedCredentialRepository : ILinklyCloudCredentialRepository
    {
        public Task<LinklyCloudCredentialRecord?> GetByStoreCodeAsync(
            string storeCode, string environment, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Legacy credential lookup must not run.");

        public Task<LinklyCloudCredentialRecord> UpsertAsync(
            string storeCode, string environment, string username, string password,
            DateTime updatedAt, string? updatedBy, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedTerminalCredentialRepository : ILinklyCloudBackendTerminalCredentialRepository
    {
        public Task<LinklyCloudBackendTerminalCredentialRecord?> GetByDeviceAsync(
            string environment, string storeCode, string deviceCode,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Legacy terminal lookup must not run.");

        public Task<LinklyCloudBackendTerminalCredentialRecord> UpsertAsync(
            string environment, string storeCode, string deviceCode, string secret, string posId,
            DateTime updatedAt, string? updatedBy, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AcquireLegacyPairingLeaseAsync(
            string environment, string storeCode, Guid attemptId, DateTime leaseExpiresAt,
            DateTime now, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Legacy pairing lease must not run.");

        public Task ReleaseLegacyPairingLeaseAsync(
            string environment, string storeCode, Guid attemptId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Legacy pairing lease must not run.");

        public Task<LinklyCloudBackendTerminalCredentialRecord> CompleteLegacyPairingAsync(
            string environment, string storeCode, string deviceCode, Guid attemptId, DateTime now,
            string secret, string posId, string? updatedBy, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Legacy pairing completion must not run.");
    }

    private sealed class CountingHttpMessageHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
