using Hbpos.Api.Services;
using Hbpos.Contracts.Linkly;

namespace Hbpos.Api.Tests;

public sealed class LinklySettlementSyncServiceTests
{
    private static readonly DateTimeOffset RequestedAt =
        new(2026, 8, 1, 1, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset CompletedAt = RequestedAt.AddMinutes(1);

    [Fact]
    public async Task SyncAsync_accepts_new_snapshot_and_is_idempotent_for_the_same_revision()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);
        var request = CreateRequest(status: "Succeeded", revision: 1);

        var first = await service.SyncAsync(request, "S001", "POS-01", CancellationToken.None);
        var second = await service.SyncAsync(request, "S001", "POS-01", CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.False(first.AlreadySynced);
        Assert.True(second.AlreadySynced);
        Assert.Equal(1, second.AcceptedRevision);
        Assert.Single(repository.Records);
    }

    [Fact]
    public async Task SyncAsync_rejects_different_content_for_the_same_revision()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);
        var request = CreateRequest(status: "Succeeded", revision: 1);
        await service.SyncAsync(request, "S001", "POS-01", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<LinklySettlementConflictException>(() =>
            service.SyncAsync(
                request with { ResponseText = "DIFFERENT" },
                "S001",
                "POS-01",
                CancellationToken.None));

        Assert.Equal("REVISION_CONTENT_CONFLICT", exception.Code);
    }

    [Fact]
    public async Task SyncAsync_treats_an_older_revision_as_already_synced_without_overwrite()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);
        var current = CreateRequest(status: "Succeeded", revision: 2);
        await service.SyncAsync(current, "S001", "POS-01", CancellationToken.None);

        var response = await service.SyncAsync(
            current with { ClientRevision = 1, ResponseText = "STALE" },
            "S001",
            "POS-01",
            CancellationToken.None);

        Assert.True(response.AlreadySynced);
        Assert.Equal(2, response.AcceptedRevision);
        Assert.Equal("APPROVED", Assert.Single(repository.Records).ResponseText);
    }

    [Fact]
    public async Task SyncAsync_allows_unknown_to_final_then_print_audit_progression()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);
        var unknown = CreateRequest(status: "Unknown", revision: 1) with
        {
            ResponseCode = null,
            ResponseText = "Result unknown",
            SettlementData = null,
            ReceiptTexts = [],
            CompletedAt = null
        };
        await service.SyncAsync(unknown, "S001", "POS-01", CancellationToken.None);
        var final = unknown with
        {
            Status = "Succeeded",
            ResponseCode = "00",
            ResponseText = "APPROVED",
            SettlementData = "TOTAL=10.00",
            ReceiptTexts = ["SETTLEMENT RECEIPT"],
            CompletedAt = CompletedAt,
            ClientRevision = 2
        };
        await service.SyncAsync(final, "S001", "POS-01", CancellationToken.None);
        var printedAt = CompletedAt.AddMinutes(1);

        var response = await service.SyncAsync(
            final with
            {
                FirstPrintedAt = printedAt,
                LastPrintedAt = printedAt,
                PrintCount = 1,
                ClientRevision = 3
            },
            "S001",
            "POS-01",
            CancellationToken.None);

        Assert.False(response.AlreadySynced);
        var stored = Assert.Single(repository.Records);
        Assert.Equal("Succeeded", stored.Status);
        Assert.Equal(1, stored.PrintCount);
    }

    [Fact]
    public async Task SyncAsync_rejects_higher_revision_that_rewrites_final_bank_evidence()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);
        var final = CreateRequest(status: "Succeeded", revision: 1);
        await service.SyncAsync(final, "S001", "POS-01", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<LinklySettlementConflictException>(() =>
            service.SyncAsync(
                final with { ResponseText = "CHANGED", ClientRevision = 2 },
                "S001",
                "POS-01",
                CancellationToken.None));

        Assert.Equal("BANK_EVIDENCE_CONFLICT", exception.Code);
    }

    [Fact]
    public async Task SyncAsync_sanitizes_free_text_card_shapes_before_persistence()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);
        var request = CreateRequest(status: "Succeeded", revision: 1) with
        {
            ResponseText = "BATCH 123456789012",
            SettlementData = "MERCHANT 123456789012",
            ReceiptTexts = ["CARD 4111111111111111"]
        };

        await service.SyncAsync(request, "S001", "POS-01", CancellationToken.None);

        var stored = Assert.Single(repository.Records);
        Assert.Equal("BATCH 123456789012", stored.ResponseText);
        Assert.Equal("MERCHANT ****9012", stored.SettlementData);
        Assert.Contains("****1111", stored.ReceiptTextsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SyncAsync_allows_a_failed_unsubmitted_cloud_settlement_without_a_provider_session()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);
        var request = CreateRequest(status: "Failed", revision: 1) with
        {
            ConnectionMode = "CloudBackendAsync",
            ProviderSessionId = null,
            ProviderSubmissionState = ProviderSubmissionState.NotSubmitted
        };

        var response = await service.SyncAsync(request, "S001", "POS-01", CancellationToken.None);

        Assert.True(response.Accepted);
        Assert.Equal(0, repository.CloudBackendLookupCount);
        Assert.Equal("NotSubmitted", Assert.Single(repository.Records).ProviderSubmissionState);
    }

    [Fact]
    public async Task SyncAsync_inferrs_the_legacy_unsubmitted_cloud_failure_state()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);
        var request = CreateRequest(status: "Failed", revision: 1) with
        {
            ConnectionMode = "CloudBackendAsync",
            ProviderSessionId = null,
            ProviderSubmissionState = null
        };

        await service.SyncAsync(request, "S001", "POS-01", CancellationToken.None);

        Assert.Equal("NotSubmitted", Assert.Single(repository.Records).ProviderSubmissionState);
    }

    [Fact]
    public async Task SyncAsync_inferrs_legacy_local_success_as_submitted_without_provider_session()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);
        var request = CreateRequest(status: "Succeeded", revision: 1) with
        {
            ProviderSessionId = null,
            ProviderSubmissionState = null
        };

        await service.SyncAsync(request, "S001", "POS-01", CancellationToken.None);

        Assert.Equal("Submitted", Assert.Single(repository.Records).ProviderSubmissionState);
    }

    [Fact]
    public async Task SyncAsync_rejects_a_submitted_cloud_failure_without_a_provider_session()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);
        var request = CreateRequest(status: "Failed", revision: 1) with
        {
            ConnectionMode = "CloudBackendAsync",
            ProviderSessionId = null,
            ProviderSubmissionState = ProviderSubmissionState.Submitted
        };

        var exception = await Assert.ThrowsAsync<LinklySettlementValidationException>(() =>
            service.SyncAsync(request, "S001", "POS-01", CancellationToken.None));

        Assert.Equal("PROVIDER_SESSION_REQUIRED", exception.Code);
    }

    [Fact]
    public async Task SyncAsync_rejects_a_pending_cloud_settlement_with_a_final_submission_state()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);
        var request = CreateRequest(status: "Unknown", revision: 1) with
        {
            ConnectionMode = "CloudBackendAsync",
            ProviderSessionId = null,
            CompletedAt = null,
            ProviderSubmissionState = ProviderSubmissionState.Submitted
        };

        var exception = await Assert.ThrowsAsync<LinklySettlementValidationException>(() =>
            service.SyncAsync(request, "S001", "POS-01", CancellationToken.None));

        Assert.Equal("INVALID_PROVIDER_SUBMISSION_STATE", exception.Code);
    }

    [Theory]
    [InlineData("LocalIp", "Succeeded", ProviderSubmissionState.NotSubmitted)]
    [InlineData("CloudDirectSync", "Failed", ProviderSubmissionState.Unknown)]
    public async Task SyncAsync_rejects_invalid_final_submission_state_for_local_modes(
        string connectionMode,
        string status,
        ProviderSubmissionState providerSubmissionState)
    {
        var service = CreateService(new FakeRepository());
        var request = CreateRequest(status, revision: 1) with
        {
            ConnectionMode = connectionMode,
            ProviderSubmissionState = providerSubmissionState
        };

        var exception = await Assert.ThrowsAsync<LinklySettlementValidationException>(() =>
            service.SyncAsync(request, "S001", "POS-01", CancellationToken.None));

        Assert.Equal("INVALID_PROVIDER_SUBMISSION_STATE", exception.Code);
    }

    [Fact]
    public async Task SyncAsync_links_existing_cloud_backend_session_without_rewriting_the_client_snapshot()
    {
        var repository = new FakeRepository
        {
            CloudBackendFact = new LinklyCloudBackendSettlementFact
            {
                Id = 42,
                Status = "Completed",
                OperationSuccess = true,
                ResponseCode = "00",
                SettlementReceiptTexts = "[\"SETTLEMENT RECEIPT\"]"
            }
        };
        var service = CreateService(repository);
        var request = CreateRequest(status: "Succeeded", revision: 1) with
        {
            ConnectionMode = "CloudBackendAsync",
            ProviderSessionId = "backend-session-1",
            ResponseText = "CLIENT APPROVED"
        };

        var response = await service.SyncAsync(request, "S001", "POS-01", CancellationToken.None);

        Assert.True(response.Accepted);
        Assert.Equal(1, repository.CloudBackendLookupCount);
        var stored = Assert.Single(repository.Records);
        Assert.Equal(42, stored.CloudBackendSessionId);
        Assert.Equal("Succeeded", stored.Status);
        Assert.Equal("CLIENT APPROVED", stored.ResponseText);
        Assert.Equal(CompletedAt, stored.CompletedAtUtc);
    }

    [Fact]
    public async Task SyncAsync_accepts_failed_cloud_backend_session_without_receipt()
    {
        var repository = new FakeRepository
        {
            CloudBackendFact = new LinklyCloudBackendSettlementFact
            {
                Id = 42,
                Status = "Completed",
                OperationSuccess = false,
                ResponseCode = "05"
            }
        };
        var service = CreateService(repository);
        var request = CreateRequest(status: "Failed", revision: 1) with
        {
            ConnectionMode = "CloudBackendAsync",
            ProviderSessionId = "backend-session-1",
            ProviderSubmissionState = ProviderSubmissionState.Submitted
        };

        var response = await service.SyncAsync(request, "S001", "POS-01", CancellationToken.None);

        Assert.True(response.Accepted);
        Assert.Equal("Failed", Assert.Single(repository.Records).Status);
    }

    [Fact]
    public async Task SyncAsync_rejects_final_cloud_backend_snapshot_until_the_linked_session_is_final()
    {
        var repository = new FakeRepository
        {
            CloudBackendFact = new LinklyCloudBackendSettlementFact
            {
                Id = 42,
                Status = "Pending"
            }
        };
        var service = CreateService(repository);
        var request = CreateRequest(status: "Succeeded", revision: 1) with
        {
            ConnectionMode = "CloudBackendAsync",
            ProviderSessionId = "backend-session-1"
        };

        var exception = await Assert.ThrowsAsync<LinklySettlementConflictException>(() =>
            service.SyncAsync(request, "S001", "POS-01", CancellationToken.None));

        Assert.Equal("CLOUD_BACKEND_SESSION_NOT_FINAL", exception.Code);
        Assert.Empty(repository.Records);
    }

    [Fact]
    public async Task SyncAsync_rejects_cloud_backend_result_that_disagrees_with_the_linked_session()
    {
        var repository = new FakeRepository
        {
            CloudBackendFact = new LinklyCloudBackendSettlementFact
            {
                Id = 42,
                Status = "Completed",
                OperationSuccess = false,
                SettlementReceiptTexts = "[\"DECLINED RECEIPT\"]"
            }
        };
        var service = CreateService(repository);
        var request = CreateRequest(status: "Succeeded", revision: 1) with
        {
            ConnectionMode = "CloudBackendAsync",
            ProviderSessionId = "backend-session-1"
        };

        var exception = await Assert.ThrowsAsync<LinklySettlementConflictException>(() =>
            service.SyncAsync(request, "S001", "POS-01", CancellationToken.None));

        Assert.Equal("CLOUD_BACKEND_RESULT_CONFLICT", exception.Code);
        Assert.Empty(repository.Records);
    }

    [Fact]
    public async Task SyncAsync_rejects_cloud_backend_provider_session_outside_existing_facts()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);
        var request = CreateRequest(status: "Unknown", revision: 1) with
        {
            ConnectionMode = "CloudBackendAsync",
            ProviderSessionId = "missing-session",
            CompletedAt = null
        };

        var exception = await Assert.ThrowsAsync<LinklySettlementConflictException>(() =>
            service.SyncAsync(request, "S001", "POS-01", CancellationToken.None));

        Assert.Equal("CLOUD_BACKEND_SESSION_NOT_FOUND", exception.Code);
        Assert.Empty(repository.Records);
    }

    [Fact]
    public async Task SyncAsync_does_not_create_a_second_record_for_the_same_provider_session()
    {
        var repository = new FakeRepository
        {
            CloudBackendFact = new LinklyCloudBackendSettlementFact
            {
                Id = 42,
                Status = "Completed",
                OperationSuccess = true,
                ResponseCode = "00",
                SettlementReceiptTexts = "[\"SETTLEMENT RECEIPT\"]"
            }
        };
        var service = CreateService(repository);
        var first = CreateRequest(status: "Succeeded", revision: 1) with
        {
            ConnectionMode = "CloudBackendAsync",
            ProviderSessionId = "backend-session-1"
        };
        await service.SyncAsync(first, "S001", "POS-01", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<LinklySettlementConflictException>(() =>
            service.SyncAsync(
                first with { SettlementGuid = Guid.NewGuid() },
                "S001",
                "POS-01",
                CancellationToken.None));

        Assert.Equal("PROVIDER_SESSION_CONFLICT", exception.Code);
        Assert.Single(repository.Records);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Q1")]
    public async Task SyncAsync_treats_cloud_backend_success_flag_without_approved_code_as_failed(string? responseCode)
    {
        var repository = new FakeRepository
        {
            CloudBackendFact = new LinklyCloudBackendSettlementFact
            {
                Id = 42,
                Status = "Completed",
                OperationSuccess = true,
                ResponseCode = responseCode,
                SettlementReceiptTexts = "[\"SETTLEMENT RECEIPT\"]"
            }
        };
        var service = CreateService(repository);
        var request = CreateRequest(status: "Failed", revision: 1) with
        {
            ConnectionMode = "CloudBackendAsync",
            ProviderSessionId = "backend-session-1"
        };

        var response = await service.SyncAsync(request, "S001", "POS-01", CancellationToken.None);

        Assert.True(response.Accepted);
        Assert.Equal("Failed", Assert.Single(repository.Records).Status);
    }

    private static LinklySettlementSyncService CreateService(FakeRepository repository)
    {
        return new LinklySettlementSyncService(
            repository,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 1, 2, 0, 0, TimeSpan.Zero)));
    }

    private static LinklySettlementSyncRequest CreateRequest(string status, long revision)
    {
        return new LinklySettlementSyncRequest(
            1,
            Guid.NewGuid(),
            "S001",
            "POS-01",
            new DateOnly(2026, 8, 1),
            "LocalIp",
            "Production",
            "provider-session-1",
            status,
            "00",
            "APPROVED",
            "TOTAL=10.00",
            ["SETTLEMENT RECEIPT"],
            RequestedAt,
            status is "Succeeded" or "Failed" ? CompletedAt : null,
            null,
            null,
            0,
            null,
            revision);
    }

    private sealed class FakeRepository : ILinklySettlementRepository
    {
        public List<PosmLinklySettlementRecord> Records { get; } = [];

        public LinklyCloudBackendSettlementFact? CloudBackendFact { get; set; }

        public int CloudBackendLookupCount { get; private set; }

        public Task<PosmLinklySettlementRecord?> GetAsync(
            string storeCode,
            string deviceCode,
            Guid settlementGuid,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Records.FirstOrDefault(record =>
                record.StoreCode == storeCode &&
                record.DeviceCode == deviceCode &&
                record.SettlementGuid == settlementGuid));
        }

        public Task<PosmLinklySettlementRecord?> GetByProviderSessionAsync(
            string connectionMode,
            string environment,
            string storeCode,
            string deviceCode,
            string providerSessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Records.FirstOrDefault(record =>
                record.ConnectionMode == connectionMode &&
                record.Environment == environment &&
                record.StoreCode == storeCode &&
                record.DeviceCode == deviceCode &&
                record.ProviderSessionId == providerSessionId));
        }

        public Task<LinklyCloudBackendSettlementFact?> GetCloudBackendSettlementAsync(
            string environment,
            string storeCode,
            string deviceCode,
            string providerSessionId,
            CancellationToken cancellationToken)
        {
            CloudBackendLookupCount++;
            return Task.FromResult(CloudBackendFact);
        }

        public Task<bool> TryInsertAsync(
            PosmLinklySettlementRecord settlement,
            CancellationToken cancellationToken)
        {
            if (Records.Any(record =>
                    record.StoreCode == settlement.StoreCode &&
                    record.DeviceCode == settlement.DeviceCode &&
                    (record.SettlementGuid == settlement.SettlementGuid ||
                     record.ProviderSessionId is not null &&
                     record.ProviderSessionId == settlement.ProviderSessionId)))
            {
                return Task.FromResult(false);
            }

            settlement.Id = Records.Count + 1;
            Records.Add(settlement);
            return Task.FromResult(true);
        }

        public Task<bool> TryUpdateAsync(
            PosmLinklySettlementRecord settlement,
            long expectedRevision,
            CancellationToken cancellationToken)
        {
            var index = Records.FindIndex(record =>
                record.StoreCode == settlement.StoreCode &&
                record.DeviceCode == settlement.DeviceCode &&
                record.SettlementGuid == settlement.SettlementGuid &&
                record.ClientRevision == expectedRevision);
            if (index < 0)
            {
                return Task.FromResult(false);
            }

            Records[index] = settlement;
            return Task.FromResult(true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
