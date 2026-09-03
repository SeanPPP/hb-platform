using System.Net;
using Hbpos.Api.Services;
using Hbpos.Contracts.Linkly;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Tests;

public sealed class LinklyCloudTerminalServiceTests
{
    [Fact]
    public void Sql_repository_readiness_fences_require_current_credential_version()
    {
        Assert.Contains(
            "[CredentialProtectionVersion] = 1",
            SqlSugarLinklyCloudTerminalRepository.UpsertSelectionSql,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "[CredentialProtectionVersion] = 1",
            SqlSugarLinklyCloudTerminalRepository.TryBeginPairingSql,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "[CredentialProtectionVersion] = 1",
            SqlSugarLinklyCloudTerminalRepository.TryAcquireOperationLeaseSql,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sql_repository_selection_uses_session_terminal_selection_lock_order()
    {
        var sql = SqlSugarLinklyCloudTerminalRepository.UpsertSelectionSql;

        Assert.Contains("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[ClientAcknowledgedAt] IS NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[PairingState] = N'Ready'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("51002", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("51003", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("51004", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[TerminalId] = @TerminalId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[DeviceCode] <> @DeviceCode", sql, StringComparison.OrdinalIgnoreCase);
        var sessionIndex = sql.IndexOf("POSM_LinklyCloudBackendSession", StringComparison.OrdinalIgnoreCase);
        var terminalIndex = sql.IndexOf("POSM_LinklyCloudTerminal", StringComparison.OrdinalIgnoreCase);
        var selectionIndex = sql.IndexOf("POSM_LinklyCloudDeviceSelection", StringComparison.OrdinalIgnoreCase);
        Assert.True(sessionIndex >= 0 && terminalIndex > sessionIndex && selectionIndex > terminalIndex);
    }

    [Theory]
    [InlineData("SQL Server error 51004")]
    [InlineData("Violation of UNIQUE KEY INDEX 'UX_POSM_LinklyCloudDeviceSelection_Scope_Terminal'.")]
    public void Sql_repository_maps_terminal_assignment_guard_and_unique_index_to_same_domain_error(
        string databaseMessage)
    {
        Assert.True(SqlSugarLinklyCloudTerminalRepository.IsTerminalAssignmentViolation(
            new InvalidOperationException(databaseMessage)));
    }

    [Fact]
    public void Sql_repository_pairing_marker_uses_session_first_serializable_fence()
    {
        var sql = SqlSugarLinklyCloudTerminalRepository.TryBeginPairingSql;

        Assert.Contains("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("POSM_LinklyCloudBackendSession", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[ClientAcknowledgedAt] IS NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[PairingState] = N'Unknown'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[PairingAttemptId] = @PairingAttemptId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[PairingLeaseExpiresAt] = @PairingLeaseExpiresAt", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OUTPUT inserted.[TerminalId]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[UpdatedAt] = @ExpectedUpdatedAt", sql, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            sql.IndexOf("POSM_LinklyCloudBackendSession", StringComparison.OrdinalIgnoreCase)
            < sql.IndexOf("UPDATE [dbo].[POSM_LinklyCloudTerminal]", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sql_repository_operation_lease_uses_session_terminal_selection_mode_lock_order()
    {
        var sql = SqlSugarLinklyCloudTerminalRepository.TryAcquireOperationLeaseSql;

        Assert.Contains("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[ClientAcknowledgedAt] IS NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[UpdatedAt] = @ExpectedTerminalUpdatedAt", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[PairingAttemptId] IS NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[PairingLeaseExpiresAt] <= @Now", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[Revision] = @ExpectedSelectionRevision", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ISNULL(@Mode, N'Legacy') = N'Active'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[PairingAttemptId] = @OperationLeaseId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[PairingLeaseExpiresAt] = @OperationLeaseExpiresAt", sql, StringComparison.OrdinalIgnoreCase);
        var sessionIndex = sql.IndexOf("POSM_LinklyCloudBackendSession", StringComparison.OrdinalIgnoreCase);
        var terminalIndex = sql.IndexOf("POSM_LinklyCloudTerminal", StringComparison.OrdinalIgnoreCase);
        var selectionIndex = sql.IndexOf("POSM_LinklyCloudDeviceSelection", StringComparison.OrdinalIgnoreCase);
        var modeIndex = sql.IndexOf("POSM_LinklyCloudConfigurationMode", StringComparison.OrdinalIgnoreCase);
        Assert.True(sessionIndex >= 0 && terminalIndex > sessionIndex && selectionIndex > terminalIndex && modeIndex > selectionIndex);
    }

    [Fact]
    public void Sql_repository_health_snapshot_uses_configuration_version_cas_without_mutating_configuration()
    {
        var sql = SqlSugarLinklyCloudTerminalRepository.TryRecordHealthSql;

        Assert.Contains("SET [LastHealthStatus] = @HealthStatus", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[LastHealthAt] = @CheckedAt", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[UpdatedAt] = @ExpectedTerminalUpdatedAt", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[LastHealthAt] IS NULL OR [LastHealthAt] <= @CheckedAt", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SET [UpdatedAt]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[PairingState]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[Secret]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[PosId]", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecordHealthAsync_updates_only_the_matching_configuration_revision()
    {
        var terminalId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var original = CreateTerminal(terminalId, "S01", "Production", 1, "Front");
        var repository = new FakeTerminalRepository { Terminals = [original] };
        var service = CreateService(repository);
        var context = new LinklyCloudTerminalPaymentContext(
            original,
            new LinklyCloudDeviceSelectionRecord
            {
                Environment = "Production",
                StoreCode = "S01",
                DeviceCode = "POS-01",
                TerminalId = terminalId,
                Revision = 4
            });
        var checkedAt = new DateTime(2026, 9, 3, 1, 2, 3, DateTimeKind.Utc);

        Assert.True(await service.RecordHealthAsync(context, "Healthy", checkedAt, CancellationToken.None));
        var updated = Assert.Single(repository.Terminals);
        Assert.Equal("Healthy", updated.LastHealthStatus);
        Assert.Equal(checkedAt, updated.LastHealthAt);
        Assert.Equal(original.UpdatedAt, updated.UpdatedAt);
        Assert.Equal(original.PairingState, updated.PairingState);
        Assert.Equal(original.Secret, updated.Secret);
        Assert.Equal(original.PosId, updated.PosId);

        Assert.False(await service.RecordHealthAsync(
            context,
            "Unhealthy",
            checkedAt.AddTicks(-1),
            CancellationToken.None));
        var newer = Assert.Single(repository.Terminals);
        Assert.Equal("Healthy", newer.LastHealthStatus);
        Assert.Equal(checkedAt, newer.LastHealthAt);

        repository.Terminals[0] = newer with { UpdatedAt = checkedAt.AddTicks(1) };
        Assert.False(await service.RecordHealthAsync(context, "Unhealthy", checkedAt.AddMinutes(1), CancellationToken.None));
        var drifted = Assert.Single(repository.Terminals);
        Assert.Equal("Healthy", drifted.LastHealthStatus);
        Assert.Equal(checkedAt, drifted.LastHealthAt);
        Assert.Equal(checkedAt.AddTicks(1), drifted.UpdatedAt);
    }

    [Fact]
    public async Task GetTerminalsAsync_returns_claim_scoped_safe_list_and_current_selection()
    {
        var selectedId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var otherId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var repository = new FakeTerminalRepository
        {
            Terminals =
            [
                CreateTerminal(selectedId, "S01", "Production", 1, "Front"),
                CreateTerminal(otherId, "OTHER", "Production", 2, "Other Store")
            ],
            Selection = new LinklyCloudDeviceSelectionRecord
            {
                Environment = "Production",
                StoreCode = "S01",
                DeviceCode = "POS-01",
                TerminalId = selectedId,
                Revision = 4
            }
        };
        var service = CreateService(repository);

        var response = await service.GetTerminalsAsync("S01", "POS-01", "Production", CancellationToken.None);

        var terminal = Assert.Single(response.Terminals);
        Assert.Equal(selectedId, response.SelectedTerminalId);
        Assert.Equal(4, response.SelectionRevision);
        Assert.Equal("Legacy", response.Mode);
        Assert.Equal("Front", terminal.DisplayName);
        Assert.True(terminal.IsReady);
    }

    [Fact]
    public async Task GetTerminalsAsync_returns_scope_configuration_mode_even_when_list_is_empty()
    {
        var repository = new FakeTerminalRepository { Mode = "Active" };
        var service = CreateService(repository);

        var response = await service.GetTerminalsAsync(
            "S01", "POS-01", "Production", CancellationToken.None);

        Assert.Equal("Active", response.Mode);
        Assert.Empty(response.Terminals);
    }

    [Fact]
    public async Task GetConfigurationModeAsync_normalizes_scope_and_returns_repository_mode()
    {
        var repository = new FakeTerminalRepository { Mode = "Active" };
        var service = CreateService(repository);

        var mode = await service.GetConfigurationModeAsync(
            " sandbox ", " S01 ", CancellationToken.None);

        Assert.Equal("Active", mode);
        Assert.Equal("Sandbox", repository.LastModeEnvironment);
        Assert.Equal("S01", repository.LastModeStoreCode);
    }

    [Fact]
    public async Task SelectTerminalAsync_requires_matching_revision_and_persists_device_scope()
    {
        var terminalId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var repository = new FakeTerminalRepository
        {
            Terminals = [CreateTerminal(terminalId, "S01", "Production", 1, "Front")],
            Selection = new LinklyCloudDeviceSelectionRecord
            {
                Environment = "Production",
                StoreCode = "S01",
                DeviceCode = "POS-01",
                TerminalId = terminalId,
                Revision = 3
            }
        };
        var service = CreateService(repository);

        await Assert.ThrowsAsync<LinklyCloudTerminalSelectionConflictException>(() =>
            service.SelectTerminalAsync(
                "S01",
                "POS-01",
                new LinklyCloudTerminalSelectionRequest("Production", terminalId, 2),
                "device:POS-01",
                CancellationToken.None));

        var response = await service.SelectTerminalAsync(
            "S01",
            "POS-01",
            new LinklyCloudTerminalSelectionRequest("Production", terminalId, 3),
            "device:POS-01",
            CancellationToken.None);

        Assert.Equal(4, response.Revision);
        Assert.Equal("POS-01", repository.Selection!.DeviceCode);
    }

    [Fact]
    public async Task SelectTerminalAsync_rejects_legacy_credential_version_even_if_state_says_ready()
    {
        var terminalId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var repository = new FakeTerminalRepository
        {
            Terminals = [CreateTerminal(terminalId, "S01", "Production", 1, "Front") with
            {
                CredentialProtectionVersion =
                    BlazorApp.Shared.Security.LinklyCloudTerminalCredentialDataProtection.LegacyPlaintextVersion
            }]
        };
        var service = CreateService(repository);

        await Assert.ThrowsAsync<LinklyCloudTerminalNotReadyException>(() =>
            service.SelectTerminalAsync(
                "S01",
                "POS-01",
                new LinklyCloudTerminalSelectionRequest("Production", terminalId, 0),
                "device:POS-01",
                CancellationToken.None));

        Assert.Null(repository.Selection);
    }

    [Fact]
    public async Task SelectTerminalAsync_rejects_terminal_assigned_to_another_pos()
    {
        var terminalId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var terminalRepository = new FakeTerminalRepository
        {
            Terminals = [CreateTerminal(terminalId, "S01", "Production", 1, "Front")],
            OtherSelections =
            [
                new LinklyCloudDeviceSelectionRecord
                {
                    Environment = "Production",
                    StoreCode = "S01",
                    DeviceCode = "POS-02",
                    TerminalId = terminalId,
                    Revision = 1
                }
            ]
        };
        var service = CreateService(terminalRepository);

        var exception = await Assert.ThrowsAsync<LinklyCloudTerminalAssignedException>(() =>
            service.SelectTerminalAsync(
                "S01",
                "POS-01",
                new LinklyCloudTerminalSelectionRequest("Production", terminalId, 0),
                "device:POS-01",
                CancellationToken.None));

        Assert.Contains("another POS", exception.Message, StringComparison.Ordinal);
        Assert.Null(terminalRepository.Selection);
    }

    [Fact]
    public async Task PairTerminalAsync_uses_terminal_credentials_and_returns_no_secret()
    {
        var terminalId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var repository = new FakeTerminalRepository
        {
            Terminals = [CreateTerminal(terminalId, "S01", "Sandbox", 1, "Front") with
            {
                Username = "lane-user",
                Password = "lane-password"
            }]
        };
        var transport = new FakePairingTransport(
            new LinklyCloudPairingTransportResponse(HttpStatusCode.OK, "paired-secret"));
        var service = CreateService(repository, transport);

        var response = await service.PairTerminalAsync(
            "S01",
            "POS-01",
            terminalId,
            new LinklyCloudBackendPairRequest("Sandbox", "123456"),
            "device:POS-01",
            CancellationToken.None);

        Assert.Equal("lane-user", transport.Username);
        Assert.Equal("lane-password", transport.Password);
        Assert.Equal("Ready", response.PairingState);
        Assert.True(response.IsReady);
        Assert.Equal("paired-secret", repository.Terminals.Single().Secret);
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PairTerminalAsync_timeout_marks_terminal_unknown_without_retry()
    {
        var terminalId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var repository = new FakeTerminalRepository
        {
            Terminals = [CreateTerminal(terminalId, "S01", "Production", 1, "Front")]
        };
        var transport = new FakePairingTransport(
            new LinklyCloudPairingTransportResponse(HttpStatusCode.RequestTimeout, null));
        var service = CreateService(repository, transport);

        await Assert.ThrowsAsync<LinklyCloudPairingTimeoutException>(() =>
            service.PairTerminalAsync(
                "S01",
                "POS-01",
                terminalId,
                new LinklyCloudBackendPairRequest("Production", "123456"),
                "device:POS-01",
                CancellationToken.None));

        Assert.Equal(1, transport.CallCount);
        Assert.Equal("Unknown", repository.Terminals.Single().PairingState);
        Assert.NotNull(repository.Terminals.Single().PairingAttemptId);
        Assert.True(repository.Terminals.Single().PairingLeaseExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task PairTerminalAsync_database_lease_blocks_second_service_instance()
    {
        var terminalId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var repository = new FakeTerminalRepository
        {
            Terminals = [CreateTerminal(terminalId, "S01", "Production", 1, "Front")]
        };
        var firstTransport = new FakePairingTransport(
            new LinklyCloudPairingTransportResponse(HttpStatusCode.RequestTimeout, null));
        var secondTransport = new FakePairingTransport(
            new LinklyCloudPairingTransportResponse(HttpStatusCode.OK, "must-not-run"));
        var firstService = CreateService(repository, firstTransport);
        var secondService = CreateService(repository, secondTransport);

        await Assert.ThrowsAsync<LinklyCloudPairingTimeoutException>(() =>
            firstService.PairTerminalAsync(
                "S01", "POS-01", terminalId,
                new LinklyCloudBackendPairRequest("Production", "123456"),
                "device:POS-01", CancellationToken.None));
        await Assert.ThrowsAsync<LinklyCloudPairingInProgressException>(() =>
            secondService.PairTerminalAsync(
                "S01", "POS-02", terminalId,
                new LinklyCloudBackendPairRequest("Production", "654321"),
                "device:POS-02", CancellationToken.None));

        Assert.Equal(1, firstTransport.CallCount);
        Assert.Equal(0, secondTransport.CallCount);
    }

    [Fact]
    public async Task Terminal_operation_lease_and_pairing_are_mutually_exclusive()
    {
        var terminalId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var repository = new FakeTerminalRepository
        {
            Mode = "Active",
            Terminals = [CreateTerminal(terminalId, "S01", "Production", 1, "Front")],
            Selection = new LinklyCloudDeviceSelectionRecord
            {
                Environment = "Production",
                StoreCode = "S01",
                DeviceCode = "POS-01",
                TerminalId = terminalId,
                Revision = 1
            }
        };
        var transport = new FakePairingTransport(
            new LinklyCloudPairingTransportResponse(HttpStatusCode.OK, "must-not-run"));
        var service = CreateService(repository, transport);
        var context = await service.ResolvePaymentTerminalAsync(
            "Production", "S01", "POS-01", terminalId, 1, CancellationToken.None);
        Assert.NotNull(context);

        var lease = await service.AcquireOperationLeaseAsync(
            "Production", "S01", "POS-01", context!, CancellationToken.None);

        await Assert.ThrowsAsync<LinklyCloudPairingInProgressException>(() =>
            service.PairTerminalAsync(
                "S01", "POS-02", terminalId,
                new LinklyCloudBackendPairRequest("Production", "123456"),
                "device:POS-02", CancellationToken.None));

        Assert.Equal(0, transport.CallCount);
        Assert.Equal(lease.LeaseId, repository.Terminals.Single().PairingAttemptId);
    }

    [Fact]
    public async Task Pairing_lease_blocks_terminal_operation_lease()
    {
        var terminalId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var repository = new FakeTerminalRepository
        {
            Mode = "Active",
            Terminals = [CreateTerminal(terminalId, "S01", "Production", 1, "Front")],
            Selection = new LinklyCloudDeviceSelectionRecord
            {
                Environment = "Production",
                StoreCode = "S01",
                DeviceCode = "POS-01",
                TerminalId = terminalId,
                Revision = 1
            }
        };
        var service = CreateService(
            repository,
            new FakePairingTransport(new LinklyCloudPairingTransportResponse(HttpStatusCode.RequestTimeout, null)));
        var context = await service.ResolvePaymentTerminalAsync(
            "Production", "S01", "POS-01", terminalId, 1, CancellationToken.None);
        Assert.NotNull(context);
        await Assert.ThrowsAsync<LinklyCloudPairingTimeoutException>(() =>
            service.PairTerminalAsync(
                "S01", "POS-01", terminalId,
                new LinklyCloudBackendPairRequest("Production", "123456"),
                "device:POS-01", CancellationToken.None));

        await Assert.ThrowsAsync<LinklyCloudBackendActiveTransactionException>(() =>
            service.AcquireOperationLeaseAsync(
                "Production", "S01", "POS-01", context!, CancellationToken.None));
    }

    [Fact]
    public async Task Expired_operation_lease_can_be_reacquired_by_another_service_instance()
    {
        var terminalId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var repository = new FakeTerminalRepository
        {
            Mode = "Active",
            Terminals = [CreateTerminal(terminalId, "S01", "Production", 1, "Front")],
            Selection = new LinklyCloudDeviceSelectionRecord
            {
                Environment = "Production",
                StoreCode = "S01",
                DeviceCode = "POS-01",
                TerminalId = terminalId,
                Revision = 1
            }
        };
        var firstService = CreateService(repository);
        var secondService = CreateService(repository);
        var context = await firstService.ResolvePaymentTerminalAsync(
            "Production", "S01", "POS-01", terminalId, 1, CancellationToken.None);
        Assert.NotNull(context);
        var firstLease = await firstService.AcquireOperationLeaseAsync(
            "Production", "S01", "POS-01", context!, CancellationToken.None);
        repository.Terminals[0] = repository.Terminals[0] with
        {
            PairingLeaseExpiresAt = DateTime.UtcNow.AddTicks(-1)
        };

        var secondLease = await secondService.AcquireOperationLeaseAsync(
            "Production", "S01", "POS-01", context!, CancellationToken.None);

        Assert.NotEqual(firstLease.LeaseId, secondLease.LeaseId);
        Assert.Equal(secondLease.LeaseId, repository.Terminals.Single().PairingAttemptId);
    }

    [Fact]
    public async Task PairTerminalAsync_rejects_missing_terminal_credentials_before_upstream_call()
    {
        var terminalId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var repository = new FakeTerminalRepository
        {
            Terminals = [CreateTerminal(terminalId, "S01", "Production", 1, "Front") with
            {
                Password = " "
            }]
        };
        var transport = new FakePairingTransport(
            new LinklyCloudPairingTransportResponse(HttpStatusCode.OK, "paired-secret"));
        var service = CreateService(repository, transport);

        await Assert.ThrowsAsync<LinklyCloudPairingCredentialMissingException>(() =>
            service.PairTerminalAsync(
                "S01",
                "POS-01",
                terminalId,
                new LinklyCloudBackendPairRequest("Production", "123456"),
                "device:POS-01",
                CancellationToken.None));

        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task PairTerminalAsync_rejects_active_or_unacknowledged_session_before_upstream_call()
    {
        var terminalId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var repository = new FakeTerminalRepository
        {
            PairingBlocked = true,
            Terminals = [CreateTerminal(terminalId, "S01", "Production", 1, "Front")]
        };
        var sessions = new InMemoryLinklyCloudBackendAsyncRepository();
        await sessions.UpsertSessionAsync(new LinklyCloudBackendSessionRecord
        {
            Environment = "Production",
            StoreCode = "S01",
            DeviceCode = "POS-02",
            TerminalId = terminalId,
            SessionId = "completed-unacknowledged",
            Status = "Completed",
            IsActive = false,
            ClientAcknowledgedAt = null,
            UpdatedAt = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        var transport = new FakePairingTransport(
            new LinklyCloudPairingTransportResponse(HttpStatusCode.OK, "paired-secret"));
        var service = CreateService(repository, transport, sessions);

        await Assert.ThrowsAsync<LinklyCloudTerminalSessionActiveException>(() =>
            service.PairTerminalAsync(
                "S01",
                "POS-01",
                terminalId,
                new LinklyCloudBackendPairRequest("Production", "123456"),
                "device:POS-01",
                CancellationToken.None));

        Assert.Equal(0, transport.CallCount);
        Assert.Equal("Ready", repository.Terminals.Single().PairingState);
    }

    [Fact]
    public async Task PairTerminalAsync_does_not_overwrite_newer_web_edit_when_upstream_returns()
    {
        var terminalId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var repository = new FakeTerminalRepository
        {
            Terminals = [CreateTerminal(terminalId, "S01", "Production", 1, "Front")]
        };
        var transport = new FakePairingTransport(
            new LinklyCloudPairingTransportResponse(HttpStatusCode.OK, "stale-paired-secret"),
            () =>
            {
                var current = repository.Terminals.Single();
                repository.Terminals[0] = current with
                {
                    Password = "new-web-password",
                    Secret = null,
                    PosId = null,
                    PairingState = "NeedsRepair",
                    UpdatedAt = current.UpdatedAt!.Value.AddTicks(1)
                };
            });
        var service = CreateService(repository, transport);

        await Assert.ThrowsAsync<LinklyCloudPairingPersistenceException>(() =>
            service.PairTerminalAsync(
                "S01",
                "POS-01",
                terminalId,
                new LinklyCloudBackendPairRequest("Production", "123456"),
                "device:POS-01",
                CancellationToken.None));

        var saved = repository.Terminals.Single();
        Assert.Equal("new-web-password", saved.Password);
        Assert.Equal("NeedsRepair", saved.PairingState);
        Assert.Null(saved.Secret);
        Assert.Null(saved.PosId);
        Assert.NotNull(saved.PairingAttemptId);
        Assert.True(saved.PairingLeaseExpiresAt > DateTime.UtcNow);
        Assert.Equal(1, transport.CallCount);
    }

    [Fact]
    public async Task ResolvePaymentTerminalAsync_active_mode_fails_closed_on_stale_selection()
    {
        var terminalId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var repository = new FakeTerminalRepository
        {
            Mode = "Active",
            Terminals = [CreateTerminal(terminalId, "S01", "Production", 1, "Front")],
            Selection = new LinklyCloudDeviceSelectionRecord
            {
                Environment = "Production",
                StoreCode = "S01",
                DeviceCode = "POS-01",
                TerminalId = terminalId,
                Revision = 8
            }
        };
        var service = CreateService(repository);

        await Assert.ThrowsAsync<LinklyCloudTerminalSelectionConflictException>(() =>
            service.ResolvePaymentTerminalAsync(
                "Production", "S01", "POS-01", terminalId, 7, CancellationToken.None));

        var resolved = await service.ResolvePaymentTerminalAsync(
            "Production", "S01", "POS-01", terminalId, 8, CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal(terminalId, resolved.Terminal.TerminalId);
    }

    [Theory]
    [InlineData("Legacy")]
    [InlineData("Draft")]
    public async Task ResolvePaymentTerminalAsync_non_active_mode_preserves_legacy_path(string mode)
    {
        var repository = new FakeTerminalRepository { Mode = mode };
        var service = CreateService(repository);

        var resolved = await service.ResolvePaymentTerminalAsync(
            "Production", "S01", "POS-01", null, null, CancellationToken.None);

        Assert.Null(resolved);
    }

    private static LinklyCloudTerminalService CreateService(
        FakeTerminalRepository repository,
        ILinklyCloudPairingTransport? transport = null,
        ILinklyCloudBackendAsyncRepository? sessionRepository = null)
    {
        return new LinklyCloudTerminalService(
            repository,
            sessionRepository ?? new InMemoryLinklyCloudBackendAsyncRepository(),
            transport ?? new FakePairingTransport(
                new LinklyCloudPairingTransportResponse(HttpStatusCode.OK, "paired-secret")),
            Options.Create(new LinklyCloudBackendAsyncOptions
            {
                ProductionAuthBaseUrl = "https://auth.example/v1/",
                SandboxAuthBaseUrl = "https://auth.sandbox.example/v1/"
            }));
    }

    private static LinklyCloudTerminalRecord CreateTerminal(
        Guid terminalId,
        string storeCode,
        string environment,
        int laneNo,
        string displayName)
    {
        return new LinklyCloudTerminalRecord
        {
            TerminalId = terminalId,
            Environment = environment,
            StoreCode = storeCode,
            LaneNo = laneNo,
            DisplayName = displayName,
            Username = "user",
            Password = "password",
            Secret = "secret",
            CredentialProtectionVersion =
                BlazorApp.Shared.Security.LinklyCloudTerminalCredentialDataProtection.CurrentVersion,
            PosId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            PairingState = "Ready",
            UpdatedAt = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)
        };
    }

    private sealed class FakeTerminalRepository : ILinklyCloudTerminalRepository
    {
        private readonly object _gate = new();

        public List<LinklyCloudTerminalRecord> Terminals { get; set; } = [];

        public LinklyCloudDeviceSelectionRecord? Selection { get; set; }

        public List<LinklyCloudDeviceSelectionRecord> OtherSelections { get; set; } = [];

        public string Mode { get; set; } = "Legacy";

        public string? LastModeEnvironment { get; private set; }

        public string? LastModeStoreCode { get; private set; }

        public bool PairingBlocked { get; set; }

        public Task<IReadOnlyList<LinklyCloudTerminalRecord>> ListAsync(
            string environment, string storeCode, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LinklyCloudTerminalRecord>>(Terminals
                .Where(item => item.Environment == environment && item.StoreCode == storeCode)
                .ToArray());
        }

        public Task<LinklyCloudTerminalRecord?> GetAsync(
            string environment, string storeCode, Guid terminalId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Terminals.SingleOrDefault(item =>
                item.Environment == environment && item.StoreCode == storeCode && item.TerminalId == terminalId));
        }

        public Task<LinklyCloudDeviceSelectionRecord?> GetSelectionAsync(
            string environment, string storeCode, string deviceCode, CancellationToken cancellationToken)
        {
            return Task.FromResult(Selection is not null &&
                Selection.Environment == environment && Selection.StoreCode == storeCode && Selection.DeviceCode == deviceCode
                ? Selection
                : null);
        }

        public Task<LinklyCloudDeviceSelectionRecord> UpsertSelectionAsync(
            string environment,
            string storeCode,
            string deviceCode,
            Guid terminalId,
            long? expectedRevision,
            DateTime updatedAt,
            string? updatedBy,
            CancellationToken cancellationToken)
        {
            if (OtherSelections.Any(item =>
                    item.Environment == environment &&
                    item.StoreCode == storeCode &&
                    item.TerminalId == terminalId &&
                    item.DeviceCode != deviceCode))
            {
                throw new LinklyCloudTerminalAssignedException();
            }

            var current = Selection;
            if (current is not null && expectedRevision != current.Revision ||
                current is null && expectedRevision is not null and not 0)
            {
                throw new LinklyCloudTerminalSelectionConflictException();
            }

            Selection = new LinklyCloudDeviceSelectionRecord
            {
                Environment = environment,
                StoreCode = storeCode,
                DeviceCode = deviceCode,
                TerminalId = terminalId,
                Revision = (current?.Revision ?? 0) + 1,
                UpdatedAt = updatedAt,
                UpdatedBy = updatedBy
            };
            return Task.FromResult(Selection);
        }

        public Task<string> GetConfigurationModeAsync(
            string environment, string storeCode, CancellationToken cancellationToken)
        {
            LastModeEnvironment = environment;
            LastModeStoreCode = storeCode;
            return Task.FromResult(Mode);
        }

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
            lock (_gate)
            {
                if (PairingBlocked)
                {
                    return Task.FromResult<LinklyCloudTerminalRecord?>(null);
                }

                var index = Terminals.FindIndex(item =>
                    item.Environment == environment &&
                    item.StoreCode == storeCode &&
                    item.TerminalId == terminalId &&
                    item.UpdatedAt == expectedUpdatedAt &&
                    (item.PairingAttemptId is null ||
                        item.PairingLeaseExpiresAt is null ||
                        item.PairingLeaseExpiresAt <= updatedAt));
                if (index < 0)
                {
                    return Task.FromResult<LinklyCloudTerminalRecord?>(null);
                }

                var marker = Terminals[index] with
                {
                    PairingState = "Unknown",
                    PairingAttemptId = pairingAttemptId,
                    PairingLeaseExpiresAt = pairingLeaseExpiresAt,
                    UpdatedAt = updatedAt,
                    UpdatedBy = updatedBy
                };
                Terminals[index] = marker;
                return Task.FromResult<LinklyCloudTerminalRecord?>(marker);
            }
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
            var index = Terminals.FindIndex(item =>
                item.Environment == environment &&
                item.StoreCode == storeCode &&
                item.TerminalId == terminalId &&
                item.PairingAttemptId == expectedPairingAttemptId &&
                item.UpdatedAt == expectedUpdatedAt);
            if (index < 0)
            {
                throw new LinklyCloudTerminalPairingConflictException();
            }

            var updated = Terminals[index] with
            {
                PairingState = pairingState,
                Secret = secret,
                PosId = posId,
                PairingAttemptId = null,
                PairingLeaseExpiresAt = null,
                UpdatedAt = updatedAt,
                UpdatedBy = updatedBy
            };
            Terminals[index] = updated;
            return Task.FromResult(updated);
        }

        public Task<bool> TryAcquireOperationLeaseAsync(
            string environment,
            string storeCode,
            string deviceCode,
            Guid terminalId,
            long expectedSelectionRevision,
            DateTime expectedTerminalUpdatedAt,
            Guid operationLeaseId,
            DateTime operationLeaseExpiresAt,
            DateTime now,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var selectionMatches = Selection is not null &&
                    Selection.Environment == environment &&
                    Selection.StoreCode == storeCode &&
                    Selection.DeviceCode == deviceCode &&
                    Selection.TerminalId == terminalId &&
                    Selection.Revision == expectedSelectionRevision;
                var index = Terminals.FindIndex(item =>
                    item.Environment == environment &&
                    item.StoreCode == storeCode &&
                    item.TerminalId == terminalId &&
                    item.UpdatedAt == expectedTerminalUpdatedAt &&
                    item.PairingState == "Ready" &&
                    !string.IsNullOrWhiteSpace(item.Secret) &&
                    !string.IsNullOrWhiteSpace(item.PosId) &&
                    (item.PairingAttemptId is null ||
                        item.PairingLeaseExpiresAt is null ||
                        item.PairingLeaseExpiresAt <= now));
                if (PairingBlocked || !selectionMatches || Mode != "Active" || index < 0)
                {
                    return Task.FromResult(false);
                }

                Terminals[index] = Terminals[index] with
                {
                    PairingAttemptId = operationLeaseId,
                    PairingLeaseExpiresAt = operationLeaseExpiresAt
                };
                return Task.FromResult(true);
            }
        }

        public Task ReleasePairingLeaseAsync(
            string environment,
            string storeCode,
            Guid terminalId,
            Guid expectedPairingAttemptId,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var index = Terminals.FindIndex(item =>
                    item.Environment == environment &&
                    item.StoreCode == storeCode &&
                    item.TerminalId == terminalId &&
                    item.PairingAttemptId == expectedPairingAttemptId);
                if (index >= 0)
                {
                    Terminals[index] = Terminals[index] with
                    {
                        PairingAttemptId = null,
                        PairingLeaseExpiresAt = null
                    };
                }
            }

            return Task.CompletedTask;
        }

        public Task ReleaseOperationLeaseAsync(
            string environment,
            string storeCode,
            Guid terminalId,
            Guid expectedOperationLeaseId,
            CancellationToken cancellationToken)
        {
            return ReleasePairingLeaseAsync(
                environment,
                storeCode,
                terminalId,
                expectedOperationLeaseId,
                cancellationToken);
        }

        public Task<bool> TryRecordHealthAsync(
            string environment,
            string storeCode,
            Guid terminalId,
            DateTime expectedTerminalUpdatedAt,
            string healthStatus,
            DateTime checkedAt,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var index = Terminals.FindIndex(item =>
                    item.Environment == environment &&
                    item.StoreCode == storeCode &&
                    item.TerminalId == terminalId &&
                    item.UpdatedAt == expectedTerminalUpdatedAt &&
                    (item.LastHealthAt is null || item.LastHealthAt <= checkedAt));
                if (index < 0)
                {
                    return Task.FromResult(false);
                }

                Terminals[index] = Terminals[index] with
                {
                    LastHealthStatus = healthStatus,
                    LastHealthAt = checkedAt
                };
                return Task.FromResult(true);
            }
        }
    }

    private sealed class FakePairingTransport(
        LinklyCloudPairingTransportResponse response,
        Action? beforeReturn = null)
        : ILinklyCloudPairingTransport
    {
        public string? Username { get; private set; }

        public string? Password { get; private set; }

        public int CallCount { get; private set; }

        public Task<LinklyCloudPairingTransportResponse> PairAsync(
            string authBaseUrl,
            string username,
            string password,
            string pairCode,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Username = username;
            Password = password;
            beforeReturn?.Invoke();
            return Task.FromResult(response);
        }
    }
}
