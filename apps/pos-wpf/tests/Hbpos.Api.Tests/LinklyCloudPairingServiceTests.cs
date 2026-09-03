using System.Net;
using System.Net.Http;
using System.Text.Json;
using Hbpos.Api.Services;
using Hbpos.Contracts.Linkly;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Tests;

public sealed class LinklyCloudPairingServiceTests
{
    [Theory]
    [InlineData("Staging", "123456")]
    [InlineData("Sandbox", "12345")]
    [InlineData("Sandbox", "1234567")]
    [InlineData("Sandbox", "12345A")]
    public async Task PairAsync_rejects_invalid_environment_or_pair_code(
        string environment,
        string pairCode)
    {
        var credentials = new FakeCredentialRepository();
        var transport = new FakePairingTransport();
        var service = CreateService(credentials, new FakeTerminalCredentialRepository(), transport);

        var exception = await Assert.ThrowsAsync<LinklyCloudPairingValidationException>(() =>
            service.PairAsync("S01", "POS-01", new LinklyCloudBackendPairRequest(environment, pairCode), "device:POS-01", CancellationToken.None));

        Assert.True(
            exception.Message.Contains("environment", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("pairCode", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, credentials.GetCalls);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task PairAsync_rejects_missing_store_credential_before_upstream_call()
    {
        var credentials = new FakeCredentialRepository { Record = null };
        var transport = new FakePairingTransport();
        var terminal = new FakeTerminalCredentialRepository();
        var service = CreateService(credentials, terminal, transport);

        await Assert.ThrowsAsync<LinklyCloudPairingCredentialMissingException>(() =>
            service.PairAsync("S01", "POS-01", CreateRequest(), "device:POS-01", CancellationToken.None));

        Assert.Equal(1, credentials.GetCalls);
        Assert.Equal(0, transport.Calls);
        Assert.Equal(1, terminal.LegacyLeaseReleaseCalls);
    }

    [Fact]
    public async Task PairAsync_does_not_call_transport_when_multi_terminal_mode_is_active()
    {
        var terminal = new FakeTerminalCredentialRepository
        {
            LegacyLeaseAcquireException = new LinklyCloudLegacyModeDisabledException()
        };
        var transport = new FakePairingTransport();
        var service = CreateService(new FakeCredentialRepository(), terminal, transport);

        await Assert.ThrowsAsync<LinklyCloudLegacyModeDisabledException>(() =>
            service.PairAsync("S01", "POS-01", CreateRequest(), "device:POS-01", CancellationToken.None));

        Assert.Equal(1, terminal.LegacyLeaseAcquireCalls);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task PairAsync_does_not_call_transport_when_database_lease_is_busy()
    {
        var terminal = new FakeTerminalCredentialRepository
        {
            LegacyLeaseAcquireException = new LinklyCloudPairingInProgressException()
        };
        var transport = new FakePairingTransport();
        var service = CreateService(new FakeCredentialRepository(), terminal, transport);

        await Assert.ThrowsAsync<LinklyCloudPairingInProgressException>(() =>
            service.PairAsync("S01", "POS-01", CreateRequest(), "device:POS-01", CancellationToken.None));

        Assert.Equal(1, terminal.LegacyLeaseAcquireCalls);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task PairAsync_treats_lease_acquisition_cancellation_as_unknown_without_transport_replay()
    {
        var terminal = new FakeTerminalCredentialRepository
        {
            LegacyLeaseAcquireException = new OperationCanceledException("database operation cancelled")
        };
        var transport = new FakePairingTransport();
        var service = CreateService(new FakeCredentialRepository(), terminal, transport);

        await Assert.ThrowsAsync<LinklyCloudPairingTimeoutException>(() =>
            service.PairAsync("S01", "POS-01", CreateRequest(), "device:POS-01", CancellationToken.None));

        Assert.Equal(1, terminal.LegacyLeaseAcquireCalls);
        Assert.Equal(0, terminal.LegacyLeaseReleaseCalls);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task PairAsync_releases_legacy_lease_when_configuration_read_fails_before_transport()
    {
        var credentials = new FakeCredentialRepository
        {
            GetException = new InvalidOperationException("configuration unavailable")
        };
        var terminal = new FakeTerminalCredentialRepository();
        var transport = new FakePairingTransport();
        var service = CreateService(credentials, terminal, transport);

        await Assert.ThrowsAsync<LinklyCloudPairingPreparationException>(() =>
            service.PairAsync("S01", "POS-01", CreateRequest(), "device:POS-01", CancellationToken.None));

        Assert.Equal(1, terminal.LegacyLeaseAcquireCalls);
        Assert.Equal(1, terminal.LegacyLeaseReleaseCalls);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task PairAsync_generates_uuid_v4_for_first_pair_and_never_returns_secret()
    {
        var terminal = new FakeTerminalCredentialRepository(new LinklyCloudBackendTerminalCredentialRecord
        {
            Environment = "Sandbox",
            StoreCode = "S01",
            DeviceCode = "POS-01",
            Secret = null,
            PosId = "legacy-pos-id"
        });
        var service = CreateService(
            new FakeCredentialRepository(),
            terminal,
            new FakePairingTransport(new LinklyCloudPairingTransportResponse(HttpStatusCode.OK, "upstream-secret")));

        var response = await service.PairAsync(
            " S01 ",
            " POS-01 ",
            CreateRequest(),
            " device:POS-01 ",
            CancellationToken.None);

        Assert.True(IsUuidV4(terminal.SavedPosId));
        Assert.True(response.HasSecret);
        Assert.DoesNotContain("upstream-secret", JsonSerializer.Serialize(response), StringComparison.Ordinal);
        Assert.Equal("device:POS-01", terminal.SavedBy);
    }

    [Fact]
    public async Task PairAsync_reuses_existing_valid_uuid_v4_pos_id()
    {
        const string existingPosId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
        var terminal = new FakeTerminalCredentialRepository(new LinklyCloudBackendTerminalCredentialRecord
        {
            Environment = "Production",
            StoreCode = "S01",
            DeviceCode = "POS-01",
            Secret = "old-secret",
            PosId = existingPosId
        });
        var transport = new FakePairingTransport(new LinklyCloudPairingTransportResponse(HttpStatusCode.OK, "new-secret"));
        var service = CreateService(new FakeCredentialRepository(), terminal, transport);

        var response = await service.PairAsync(
            "S01",
            "POS-01",
            new LinklyCloudBackendPairRequest("Production", "654321"),
            "device:POS-01",
            CancellationToken.None);

        Assert.Equal(existingPosId, terminal.SavedPosId);
        Assert.Equal(existingPosId, response.PosId);
        Assert.Equal("https://auth.example/v1/", transport.LastAuthBaseUrl);
    }

    [Fact]
    public async Task PairAsync_fails_closed_when_persisted_readback_scope_is_inconsistent()
    {
        var terminal = new FakeTerminalCredentialRepository();
        terminal.SaveHandler = _ =>
        {
            terminal.Record = new LinklyCloudBackendTerminalCredentialRecord
            {
                Environment = "Production",
                StoreCode = "OTHER-STORE",
                DeviceCode = "OTHER-DEVICE",
                Secret = "upstream-secret",
                PosId = terminal.SavedPosId,
                UpdatedAt = DateTime.UtcNow
            };
            return Task.FromResult(terminal.Record);
        };
        var service = CreateService(
            new FakeCredentialRepository(),
            terminal,
            new FakePairingTransport(new LinklyCloudPairingTransportResponse(HttpStatusCode.OK, "upstream-secret")));

        await Assert.ThrowsAsync<LinklyCloudPairingPersistenceException>(() =>
            service.PairAsync(
                "S01",
                "POS-01",
                CreateRequest(),
                "device:POS-01",
                CancellationToken.None));
    }

    [Theory]
    [InlineData((int)HttpStatusCode.BadRequest)]
    [InlineData((int)HttpStatusCode.Unauthorized)]
    [InlineData((int)HttpStatusCode.UnprocessableEntity)]
    public async Task PairAsync_maps_definite_upstream_4xx_to_rejection_without_persisting(int statusCode)
    {
        var terminal = new FakeTerminalCredentialRepository();
        var service = CreateService(
            new FakeCredentialRepository(),
            terminal,
            new FakePairingTransport(new LinklyCloudPairingTransportResponse((HttpStatusCode)statusCode, null)));

        await Assert.ThrowsAsync<LinklyCloudPairingRejectedException>(() =>
            service.PairAsync("S01", "POS-01", CreateRequest(), "device:POS-01", CancellationToken.None));

        Assert.Equal(0, terminal.UpsertCalls);
        Assert.Equal(1, terminal.LegacyLeaseReleaseCalls);
    }

    [Fact]
    public async Task PairAsync_maps_upstream_408_to_uncertain_timeout_without_persisting()
    {
        var terminal = new FakeTerminalCredentialRepository();
        var service = CreateService(
            new FakeCredentialRepository(),
            terminal,
            new FakePairingTransport(new LinklyCloudPairingTransportResponse(HttpStatusCode.RequestTimeout, null)));

        await Assert.ThrowsAsync<LinklyCloudPairingTimeoutException>(() =>
            service.PairAsync("S01", "POS-01", CreateRequest(), "device:POS-01", CancellationToken.None));

        Assert.Equal(0, terminal.UpsertCalls);
        Assert.Equal(1, terminal.LegacyLeaseAcquireCalls);
        Assert.Equal(0, terminal.LegacyLeaseReleaseCalls);
    }

    [Theory]
    [InlineData((int)HttpStatusCode.BadGateway, "upstream-secret")]
    [InlineData((int)HttpStatusCode.OK, "")]
    public async Task PairAsync_maps_upstream_failure_or_missing_secret_to_upstream_failure(
        int statusCode,
        string secret)
    {
        var terminal = new FakeTerminalCredentialRepository();
        var service = CreateService(
            new FakeCredentialRepository(),
            terminal,
            new FakePairingTransport(new LinklyCloudPairingTransportResponse((HttpStatusCode)statusCode, secret)));

        await Assert.ThrowsAsync<LinklyCloudPairingUpstreamException>(() =>
            service.PairAsync("S01", "POS-01", CreateRequest(), "device:POS-01", CancellationToken.None));

        Assert.Equal(0, terminal.UpsertCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PairAsync_maps_upstream_timeout_or_cancellation_to_uncertain_result(bool taskCanceled)
    {
        var transport = new FakePairingTransport
        {
            Exception = taskCanceled
                ? new TaskCanceledException("upstream timeout")
                : new OperationCanceledException("request cancelled")
        };
        var service = CreateService(new FakeCredentialRepository(), new FakeTerminalCredentialRepository(), transport);

        await Assert.ThrowsAsync<LinklyCloudPairingTimeoutException>(() =>
            service.PairAsync("S01", "POS-01", CreateRequest(), "device:POS-01", CancellationToken.None));
    }

    [Fact]
    public async Task PairAsync_maps_save_failure_after_upstream_success()
    {
        var terminal = new FakeTerminalCredentialRepository
        {
            SaveException = new InvalidOperationException("database write failed")
        };
        var service = CreateService(
            new FakeCredentialRepository(),
            terminal,
            new FakePairingTransport(new LinklyCloudPairingTransportResponse(HttpStatusCode.OK, "upstream-secret")));

        await Assert.ThrowsAsync<LinklyCloudPairingPersistenceException>(() =>
            service.PairAsync("S01", "POS-01", CreateRequest(), "device:POS-01", CancellationToken.None));

        Assert.Equal(1, terminal.LegacyCompleteCalls);
    }

    [Fact]
    public async Task PairAsync_completes_legacy_pairing_with_lease_compare_and_swap()
    {
        var terminal = new FakeTerminalCredentialRepository();
        var service = CreateService(
            new FakeCredentialRepository(),
            terminal,
            new FakePairingTransport(new LinklyCloudPairingTransportResponse(HttpStatusCode.OK, "upstream-secret")));

        await service.PairAsync("S01", "POS-01", CreateRequest(), "device:POS-01", CancellationToken.None);

        Assert.Equal(1, terminal.LegacyLeaseAcquireCalls);
        Assert.Equal(1, terminal.LegacyCompleteCalls);
        Assert.Equal(0, terminal.UpsertCalls);
        Assert.NotEqual(Guid.Empty, terminal.CompletedAttemptId);
    }

    [Fact]
    public async Task PairAsync_treats_late_lease_completion_as_uncertain_persistence_failure()
    {
        var terminal = new FakeTerminalCredentialRepository
        {
            LegacyCompleteException = new LinklyCloudLegacyPairingLeaseConflictException()
        };
        var service = CreateService(
            new FakeCredentialRepository(),
            terminal,
            new FakePairingTransport(new LinklyCloudPairingTransportResponse(HttpStatusCode.OK, "upstream-secret")));

        await Assert.ThrowsAsync<LinklyCloudPairingPersistenceException>(() =>
            service.PairAsync("S01", "POS-01", CreateRequest(), "device:POS-01", CancellationToken.None));

        Assert.Equal(1, terminal.LegacyCompleteCalls);
        Assert.Equal(0, terminal.LegacyLeaseReleaseCalls);
    }

    [Fact]
    public async Task PairAsync_preserves_active_mode_exception_from_atomic_completion()
    {
        var terminal = new FakeTerminalCredentialRepository
        {
            LegacyCompleteException = new LinklyCloudLegacyModeDisabledException()
        };
        var service = CreateService(
            new FakeCredentialRepository(),
            terminal,
            new FakePairingTransport(new LinklyCloudPairingTransportResponse(HttpStatusCode.OK, "upstream-secret")));

        await Assert.ThrowsAsync<LinklyCloudLegacyModeDisabledException>(() =>
            service.PairAsync("S01", "POS-01", CreateRequest(), "device:POS-01", CancellationToken.None));

        Assert.Equal(1, terminal.LegacyCompleteCalls);
        Assert.Equal(0, terminal.LegacyLeaseReleaseCalls);
    }

    [Fact]
    public async Task PairAsync_requires_the_atomic_completion_result_to_match_upstream_success()
    {
        var terminal = new FakeTerminalCredentialRepository
        {
            // 原子完成 SQL 必须读回完整记录；返回缺少 secret 的结果不能向终端宣告成功。
            SaveHandler = _ => Task.FromResult(new LinklyCloudBackendTerminalCredentialRecord
            {
                Environment = "Sandbox",
                StoreCode = "S01",
                DeviceCode = "POS-01",
                Secret = null,
                PosId = "550e8400-e29b-41d4-a716-446655440000",
                UpdatedAt = DateTime.UtcNow
            })
        };
        var service = CreateService(
            new FakeCredentialRepository(),
            terminal,
            new FakePairingTransport(new LinklyCloudPairingTransportResponse(HttpStatusCode.OK, "upstream-secret")));

        await Assert.ThrowsAsync<LinklyCloudPairingPersistenceException>(() =>
            service.PairAsync("S01", "POS-01", CreateRequest(), "device:POS-01", CancellationToken.None));

        Assert.Equal(1, terminal.LegacyCompleteCalls);
    }

    [Fact]
    public async Task PairAsync_fails_fast_when_same_environment_store_and_device_are_in_progress()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new FakePairingTransport
        {
            Handler = async (_, _, _, pairCode) =>
            {
                if (pairCode == "111111")
                {
                    started.TrySetResult(true);
                    await release.Task;
                }

                return new LinklyCloudPairingTransportResponse(HttpStatusCode.OK, "upstream-secret");
            }
        };
        var service = CreateService(new FakeCredentialRepository(), new FakeTerminalCredentialRepository(), transport);

        var first = service.PairAsync(
            "S01",
            "POS-01",
            new LinklyCloudBackendPairRequest("Sandbox", "111111"),
            "device:POS-01",
            CancellationToken.None);
        await started.Task;

        var secondException = await Assert.ThrowsAsync<LinklyCloudPairingInProgressException>(() =>
            service.PairAsync(
                "s01",
                "pos-01",
                new LinklyCloudBackendPairRequest("sandbox", "222222"),
                "device:POS-01",
                CancellationToken.None));

        Assert.Contains("already", secondException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, transport.Calls);
        release.SetResult(true);
        await first;
    }

    [Fact]
    public async Task PairAsync_allows_a_different_device_while_first_scope_is_in_progress()
    {
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new FakePairingTransport
        {
            Handler = async (_, _, _, pairCode) =>
            {
                if (pairCode == "111111")
                {
                    firstStarted.TrySetResult(true);
                    await releaseFirst.Task;
                }

                return new LinklyCloudPairingTransportResponse(HttpStatusCode.OK, "upstream-secret");
            }
        };
        var service = CreateService(new FakeCredentialRepository(), new FakeTerminalCredentialRepository(), transport);

        var first = service.PairAsync(
            "S01",
            "POS-01",
            new LinklyCloudBackendPairRequest("Sandbox", "111111"),
            "device:POS-01",
            CancellationToken.None);
        await firstStarted.Task;

        var second = await service.PairAsync(
            "S01",
            "POS-02",
            new LinklyCloudBackendPairRequest("Sandbox", "222222"),
            "device:POS-02",
            CancellationToken.None);

        Assert.True(second.HasSecret);
        releaseFirst.SetResult(true);
        await first;
        Assert.Equal(2, transport.Calls);
    }

    [Fact]
    public async Task PairAsync_uses_internal_persistence_token_after_client_cancellation()
    {
        using var callerCancellation = new CancellationTokenSource();
        CancellationToken terminalSaveToken = default;
        var terminal = new FakeTerminalCredentialRepository();
        terminal.SaveHandler = token =>
        {
            terminalSaveToken = token;
            terminal.Record = terminal.BuildSavedRecord();
            return Task.FromResult(terminal.Record);
        };
        var transport = new FakePairingTransport
        {
            Handler = (_, _, _, _) =>
            {
                callerCancellation.Cancel();
                return Task.FromResult(new LinklyCloudPairingTransportResponse(HttpStatusCode.OK, "upstream-secret"));
            }
        };
        var service = CreateService(new FakeCredentialRepository(), terminal, transport);

        var response = await service.PairAsync(
            "S01",
            "POS-01",
            CreateRequest(),
            "device:POS-01",
            callerCancellation.Token);

        Assert.True(callerCancellation.IsCancellationRequested);
        Assert.NotEqual(callerCancellation.Token, terminalSaveToken);
        Assert.False(terminalSaveToken.IsCancellationRequested);
        Assert.True(response.HasSecret);
    }

    [Fact]
    public async Task PairAsync_logs_no_pair_code_or_credential_material()
    {
        const string username = "merchant-user-sensitive";
        const string password = "merchant-password-sensitive";
        const string pairCode = "123456";
        const string secret = "upstream-secret-sensitive";
        var logger = new RecordingLogger<LinklyCloudPairingService>();
        var transport = new FakePairingTransport
        {
            Exception = new HttpRequestException($"unexpected upstream response {secret} {password}")
        };
        var credentials = new FakeCredentialRepository(new LinklyCloudCredentialRecord
        {
            StoreCode = "S01",
            Environment = "Sandbox",
            Username = username,
            Password = password
        });
        var service = CreateService(credentials, new FakeTerminalCredentialRepository(), transport, logger);

        await Assert.ThrowsAsync<LinklyCloudPairingUpstreamException>(() =>
            service.PairAsync("S01", "POS-01", new LinklyCloudBackendPairRequest("Sandbox", pairCode), "device:POS-01", CancellationToken.None));

        var logText = string.Join(Environment.NewLine, logger.Lines);
        Assert.DoesNotContain(pairCode, logText, StringComparison.Ordinal);
        Assert.DoesNotContain(username, logText, StringComparison.Ordinal);
        Assert.DoesNotContain(password, logText, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, logText, StringComparison.Ordinal);
    }

    private static LinklyCloudBackendPairRequest CreateRequest() =>
        new("Sandbox", "123456");

    private static LinklyCloudPairingService CreateService(
        FakeCredentialRepository credentials,
        FakeTerminalCredentialRepository terminal,
        FakePairingTransport transport,
        ILogger<LinklyCloudPairingService>? logger = null) =>
        new(
            credentials,
            terminal,
            transport,
            Options.Create(new LinklyCloudBackendAsyncOptions
            {
                SandboxAuthBaseUrl = "https://auth.sandbox.example/v1/",
                ProductionAuthBaseUrl = "https://auth.example/v1/"
            }),
            logger);

    private static bool IsUuidV4(string? value) =>
        value is not null &&
        value.Length == 36 &&
        Guid.TryParse(value, out _) &&
        value[14] == '4' &&
        value[19] is '8' or '9' or 'a' or 'A' or 'b' or 'B';

    private sealed class FakeCredentialRepository(LinklyCloudCredentialRecord? record = null) : ILinklyCloudCredentialRepository
    {
        public LinklyCloudCredentialRecord? Record { get; set; } = record ?? new LinklyCloudCredentialRecord
        {
            StoreCode = "S01",
            Environment = "Sandbox",
            Username = "merchant-user",
            Password = "merchant-password"
        };

        public int GetCalls { get; private set; }

        public Exception? GetException { get; set; }

        public Task<LinklyCloudCredentialRecord?> GetByStoreCodeAsync(
            string storeCode,
            string environment,
            CancellationToken cancellationToken)
        {
            GetCalls++;
            if (GetException is not null)
            {
                throw GetException;
            }

            return Task.FromResult(Record);
        }

        public Task<LinklyCloudCredentialRecord> UpsertAsync(
            string storeCode,
            string environment,
            string username,
            string password,
            DateTime updatedAt,
            string? updatedBy,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTerminalCredentialRepository(LinklyCloudBackendTerminalCredentialRecord? record = null) : ILinklyCloudBackendTerminalCredentialRepository
    {
        public LinklyCloudBackendTerminalCredentialRecord? Record { get; set; } = record;

        public Exception? SaveException { get; set; }

        public Func<CancellationToken, Task<LinklyCloudBackendTerminalCredentialRecord>>? SaveHandler { get; set; }

        public int UpsertCalls { get; private set; }

        public int LegacyLeaseAcquireCalls { get; private set; }

        public int LegacyLeaseReleaseCalls { get; private set; }

        public int LegacyCompleteCalls { get; private set; }

        public Guid CompletedAttemptId { get; private set; }

        public Exception? LegacyLeaseAcquireException { get; set; }

        public Exception? LegacyCompleteException { get; set; }

        public string? SavedPosId { get; private set; }

        public string? SavedBy { get; private set; }

        public CancellationToken SavedToken { get; private set; }

        public Task<LinklyCloudBackendTerminalCredentialRecord?> GetByDeviceAsync(
            string environment,
            string storeCode,
            string deviceCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(Record);

        public async Task<LinklyCloudBackendTerminalCredentialRecord> UpsertAsync(
            string environment,
            string storeCode,
            string deviceCode,
            string secret,
            string posId,
            DateTime updatedAt,
            string? updatedBy,
            CancellationToken cancellationToken)
        {
            UpsertCalls++;
            SavedPosId = posId;
            SavedBy = updatedBy;
            SavedToken = cancellationToken;
            if (SaveException is not null)
            {
                throw SaveException;
            }

            if (SaveHandler is not null)
            {
                return await SaveHandler(cancellationToken);
            }

            Record = new LinklyCloudBackendTerminalCredentialRecord
            {
                Environment = environment,
                StoreCode = storeCode,
                DeviceCode = deviceCode,
                Secret = secret,
                PosId = posId,
                UpdatedAt = updatedAt,
                UpdatedBy = updatedBy
            };
            return Record;
        }

        public Task AcquireLegacyPairingLeaseAsync(
            string environment,
            string storeCode,
            Guid attemptId,
            DateTime leaseExpiresAt,
            DateTime now,
            CancellationToken cancellationToken)
        {
            LegacyLeaseAcquireCalls++;
            if (LegacyLeaseAcquireException is not null)
            {
                throw LegacyLeaseAcquireException;
            }

            return Task.CompletedTask;
        }

        public Task ReleaseLegacyPairingLeaseAsync(
            string environment,
            string storeCode,
            Guid attemptId,
            CancellationToken cancellationToken)
        {
            LegacyLeaseReleaseCalls++;
            return Task.CompletedTask;
        }

        public async Task<LinklyCloudBackendTerminalCredentialRecord> CompleteLegacyPairingAsync(
            string environment,
            string storeCode,
            string deviceCode,
            Guid attemptId,
            DateTime now,
            string secret,
            string posId,
            string? updatedBy,
            CancellationToken cancellationToken)
        {
            LegacyCompleteCalls++;
            CompletedAttemptId = attemptId;
            if (LegacyCompleteException is not null)
            {
                throw LegacyCompleteException;
            }

            SavedPosId = posId;
            SavedBy = updatedBy;
            SavedToken = cancellationToken;

            if (SaveException is not null)
            {
                throw SaveException;
            }

            if (SaveHandler is not null)
            {
                return await SaveHandler(cancellationToken);
            }

            Record = new LinklyCloudBackendTerminalCredentialRecord
            {
                Environment = environment,
                StoreCode = storeCode,
                DeviceCode = deviceCode,
                Secret = secret,
                PosId = posId,
                UpdatedAt = now,
                UpdatedBy = updatedBy
            };
            return Record;
        }

        public LinklyCloudBackendTerminalCredentialRecord BuildSavedRecord() =>
            new()
            {
                Environment = "Sandbox",
                StoreCode = "S01",
                DeviceCode = "POS-01",
                Secret = "upstream-secret",
                PosId = SavedPosId,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = SavedBy
            };
    }

    private sealed class FakePairingTransport(LinklyCloudPairingTransportResponse? response = null) : ILinklyCloudPairingTransport
    {
        public LinklyCloudPairingTransportResponse? Response { get; set; } = response ?? new(HttpStatusCode.OK, "upstream-secret");

        public Exception? Exception { get; set; }

        public Func<string, string, string, string, Task<LinklyCloudPairingTransportResponse>>? Handler { get; set; }

        public int Calls { get; private set; }

        public string? LastAuthBaseUrl { get; private set; }

        public Task<LinklyCloudPairingTransportResponse> PairAsync(
            string authBaseUrl,
            string username,
            string password,
            string pairCode,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastAuthBaseUrl = authBaseUrl;
            if (Exception is not null)
            {
                throw Exception;
            }

            return Handler is null
                ? Task.FromResult(Response!)
                : Handler(authBaseUrl, username, password, pairCode);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }
}
