using Hbpos.Client.Wpf.Services;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Tests;

public sealed class CardRefundAttemptConcurrencyTests
{
    [Fact]
    public async Task Refund_schema_uses_partial_unique_business_keys_and_submission_tokens()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();

            await using var connection = await store.OpenConnectionAsync();
            Assert.Equal(1, await ReadScalarIntAsync(
                connection,
                "SELECT COUNT(*) FROM pragma_table_info('LocalCardPaymentAttempts') WHERE name = 'RefundBusinessKey';"));
            Assert.Equal(1, await ReadScalarIntAsync(
                connection,
                "SELECT COUNT(*) FROM pragma_table_info('LocalCardPaymentAttempts') WHERE name = 'SubmissionToken';"));
            Assert.Equal(1, await ReadScalarIntAsync(
                connection,
                "SELECT COUNT(*) FROM pragma_table_info('LocalSquarePaymentAttempts') WHERE name = 'RefundBusinessKey';"));
            Assert.Equal(1, await ReadScalarIntAsync(
                connection,
                "SELECT COUNT(*) FROM pragma_table_info('LocalSquarePaymentAttempts') WHERE name = 'SubmissionToken';"));

            var linklySql = await ReadScalarStringAsync(
                connection,
                "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'UX_LocalCardPaymentAttempts_OpenRefundBusinessKey';");
            var squareSql = await ReadScalarStringAsync(
                connection,
                "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'UX_LocalSquarePaymentAttempts_OpenRefundBusinessKey';");

            Assert.Contains("UNIQUE INDEX", linklySql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("RefundBusinessKey", linklySql, StringComparison.Ordinal);
            Assert.Contains("OperationKind = 'Refund'", linklySql, StringComparison.Ordinal);
            Assert.Contains("Status NOT IN", linklySql, StringComparison.Ordinal);
            Assert.Contains("UNIQUE INDEX", squareSql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("RefundBusinessKey", squareSql, StringComparison.Ordinal);
            Assert.Contains("OperationKind = 'Refund'", squareSql, StringComparison.Ordinal);
            Assert.Contains("Status NOT IN", squareSql, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Linkly_concurrent_create_or_get_returns_one_open_attempt_and_terminal_row_releases_key()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository1 = new LocalCardPaymentAttemptRepository(store);
            var repository2 = new LocalCardPaymentAttemptRepository(store);
            var first = CreateLinklyRefundAttempt(
                Guid.Parse("10000000-0000-0000-0000-000000000001"),
                "txn-first",
                "refund-business-key");
            var second = CreateLinklyRefundAttempt(
                Guid.Parse("10000000-0000-0000-0000-000000000002"),
                "txn-second",
                "refund-business-key");

            var persisted = await Task.WhenAll(
                repository1.CreateOrGetOpenRefundAsync(first),
                repository2.CreateOrGetOpenRefundAsync(second));

            Assert.Equal(persisted[0].AttemptGuid, persisted[1].AttemptGuid);
            Assert.Equal(persisted[0].TxnRef, persisted[1].TxnRef);
            Assert.Single(await repository1.GetOpenRefundAttemptsAsync("S001", "POS-01", "Production"));

            await repository1.UpdateOutcomeAsync(
                persisted[0].AttemptGuid,
                LocalCardPaymentAttemptStatus.Failed,
                "DECLINED",
                "Refund did not proceed.",
                null,
                persisted[0].UpdatedAt.AddMinutes(1));
            var replacement = await repository1.CreateOrGetOpenRefundAsync(
                CreateLinklyRefundAttempt(
                    Guid.Parse("10000000-0000-0000-0000-000000000003"),
                    "txn-replacement",
                    "refund-business-key",
                    persisted[0].UpdatedAt.AddMinutes(2)));

            Assert.NotEqual(persisted[0].AttemptGuid, replacement.AttemptGuid);
            Assert.Equal("txn-replacement", replacement.TxnRef);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Square_concurrent_create_or_get_returns_one_open_attempt_and_terminal_row_releases_key()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository1 = new LocalSquarePaymentAttemptRepository(store);
            var repository2 = new LocalSquarePaymentAttemptRepository(store);
            var first = CreateSquareRefundAttempt(
                Guid.Parse("20000000-0000-0000-0000-000000000001"),
                "idem-first",
                "refund-business-key");
            var second = CreateSquareRefundAttempt(
                Guid.Parse("20000000-0000-0000-0000-000000000002"),
                "idem-second",
                "refund-business-key");

            var persisted = await Task.WhenAll(
                repository1.CreateOrGetOpenRefundAsync(first),
                repository2.CreateOrGetOpenRefundAsync(second));

            Assert.Equal(persisted[0].AttemptGuid, persisted[1].AttemptGuid);
            Assert.Equal(persisted[0].IdempotencyKey, persisted[1].IdempotencyKey);
            Assert.Single(await repository1.GetOpenRefundAttemptsAsync("S001", "POS-01", "Production"));

            await repository1.MarkFailedAsync(
                persisted[0].AttemptGuid,
                LocalSquarePaymentAttemptStatus.Failed,
                checkoutStatus: null,
                paymentStatus: "FAILED",
                responseCode: "DECLINED",
                responseText: "Refund did not proceed.",
                persisted[0].UpdatedAt.AddMinutes(1));
            var replacement = await repository1.CreateOrGetOpenRefundAsync(
                CreateSquareRefundAttempt(
                    Guid.Parse("20000000-0000-0000-0000-000000000003"),
                    "idem-replacement",
                    "refund-business-key",
                    persisted[0].UpdatedAt.AddMinutes(2)));

            Assert.NotEqual(persisted[0].AttemptGuid, replacement.AttemptGuid);
            Assert.Equal("idem-replacement", replacement.IdempotencyKey);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Linkly_refund_dispatch_claim_has_one_winner()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository1 = new LocalCardPaymentAttemptRepository(store);
            var repository2 = new LocalCardPaymentAttemptRepository(store);
            var attempt = CreateLinklyRefundAttempt(
                Guid.Parse("30000000-0000-0000-0000-000000000001"),
                "txn-claim",
                "claim-key");
            await repository1.CreateAsync(attempt);

            var results = await Task.WhenAll(
                repository1.TryBeginRefundSubmissionAsync(
                    attempt.AttemptGuid,
                    attempt.UpdatedAt,
                    "token-one",
                    attempt.UpdatedAt.AddTicks(1)),
                repository2.TryBeginRefundSubmissionAsync(
                    attempt.AttemptGuid,
                    attempt.UpdatedAt,
                    "token-two",
                    attempt.UpdatedAt.AddTicks(2)));
            var saved = await repository1.GetAttemptAsync(attempt.AttemptGuid);

            Assert.Equal(1, results.Count(result => result));
            Assert.NotNull(saved);
            Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, saved.Status);
            Assert.Contains(saved.SubmissionToken, new[] { "token-one", "token-two" });
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Square_refund_dispatch_claim_has_one_winner()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository1 = new LocalSquarePaymentAttemptRepository(store);
            var repository2 = new LocalSquarePaymentAttemptRepository(store);
            var attempt = CreateSquareRefundAttempt(
                Guid.Parse("40000000-0000-0000-0000-000000000001"),
                "idem-claim",
                "claim-key");
            await repository1.CreateAsync(attempt);

            var results = await Task.WhenAll(
                repository1.TryBeginRefundSubmissionAsync(
                    attempt.AttemptGuid,
                    attempt.UpdatedAt,
                    "token-one",
                    attempt.UpdatedAt.AddTicks(1)),
                repository2.TryBeginRefundSubmissionAsync(
                    attempt.AttemptGuid,
                    attempt.UpdatedAt,
                    "token-two",
                    attempt.UpdatedAt.AddTicks(2)));
            var saved = await repository1.GetAttemptAsync(attempt.AttemptGuid);

            Assert.Equal(1, results.Count(result => result));
            Assert.NotNull(saved);
            Assert.Equal(LocalSquarePaymentAttemptStatus.Recovering, saved.Status);
            Assert.Contains(saved.SubmissionToken, new[] { "token-one", "token-two" });
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Theory]
    [InlineData(CardRefundSupervisorDecision.ConfirmRefunded)]
    [InlineData(CardRefundSupervisorDecision.ConfirmNotRefunded)]
    [InlineData(CardRefundSupervisorDecision.ContinueWaiting)]
    public async Task Linkly_retry_boundary_clears_old_resolution_and_restart_can_apply_all_decisions(
        CardRefundSupervisorDecision decision)
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalCardPaymentAttemptRepository(store);
            var attempt = CreateLinklyRefundAttempt(
                Guid.Parse("50000000-0000-0000-0000-000000000001"),
                "txn-old",
                "retry-key") with
            {
                Status = LocalCardPaymentAttemptStatus.Recovering,
                SubmissionToken = "old-token"
            };
            await repository.CreateAsync(attempt);
            Assert.True(await repository.ResolveRefundAsync(CreateResolution(
                attempt.AttemptGuid,
                CardRefundSupervisorDecision.ConfirmNotRefunded,
                attempt.UpdatedAt.AddMinutes(1),
                retryTxnRef: "txn-retry")));
            var retry = Assert.IsType<LocalCardPaymentAttempt>(await repository.GetAttemptAsync(attempt.AttemptGuid));

            Assert.True(await repository.TryBeginRefundSubmissionAsync(
                retry.AttemptGuid,
                retry.UpdatedAt,
                "retry-token",
                retry.UpdatedAt.AddTicks(1)));
            var afterBoundary = Assert.IsType<LocalCardPaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));

            Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, afterBoundary.Status);
            Assert.Null(afterBoundary.ResponseCode);
            Assert.Null(afterBoundary.ResponseText);
            Assert.Equal("retry-token", afterBoundary.SubmissionToken);
            Assert.True(await repository.ResolveRefundAsync(CreateResolution(
                attempt.AttemptGuid,
                decision,
                afterBoundary.UpdatedAt.AddMinutes(1),
                refundReference: decision == CardRefundSupervisorDecision.ConfirmRefunded ? "refund-linkly" : null,
                retryTxnRef: decision == CardRefundSupervisorDecision.ConfirmNotRefunded ? "txn-next" : null)));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Theory]
    [InlineData(CardRefundSupervisorDecision.ConfirmRefunded)]
    [InlineData(CardRefundSupervisorDecision.ConfirmNotRefunded)]
    [InlineData(CardRefundSupervisorDecision.ContinueWaiting)]
    public async Task Square_retry_boundary_clears_old_resolution_and_restart_can_apply_all_decisions(
        CardRefundSupervisorDecision decision)
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalSquarePaymentAttemptRepository(store);
            var attempt = CreateSquareRefundAttempt(
                Guid.Parse("60000000-0000-0000-0000-000000000001"),
                "idem-retry",
                "retry-key") with
            {
                Status = LocalSquarePaymentAttemptStatus.Recovering,
                SubmissionToken = "old-token"
            };
            await repository.CreateAsync(attempt);
            Assert.True(await repository.ResolveRefundAsync(CreateResolution(
                attempt.AttemptGuid,
                CardRefundSupervisorDecision.ConfirmNotRefunded,
                attempt.UpdatedAt.AddMinutes(1))));
            var retry = Assert.IsType<LocalSquarePaymentAttempt>(await repository.GetAttemptAsync(attempt.AttemptGuid));

            Assert.True(await repository.TryBeginRefundSubmissionAsync(
                retry.AttemptGuid,
                retry.UpdatedAt,
                "retry-token",
                retry.UpdatedAt.AddTicks(1)));
            var afterBoundary = Assert.IsType<LocalSquarePaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));

            Assert.Equal(LocalSquarePaymentAttemptStatus.Recovering, afterBoundary.Status);
            Assert.Null(afterBoundary.ResponseCode);
            Assert.Null(afterBoundary.ResponseText);
            Assert.Equal("retry-token", afterBoundary.SubmissionToken);
            Assert.True(await repository.ResolveRefundAsync(CreateResolution(
                attempt.AttemptGuid,
                decision,
                afterBoundary.UpdatedAt.AddMinutes(1),
                refundReference: decision == CardRefundSupervisorDecision.ConfirmRefunded ? "refund-square" : null)));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Linkly_old_worker_cannot_write_after_retry_claim_or_supervisor_resolution()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalCardPaymentAttemptRepository(store);
            var attempt = CreateLinklyRefundAttempt(
                Guid.Parse("70000000-0000-0000-0000-000000000001"),
                "txn-old",
                "stale-key") with
            {
                Status = LocalCardPaymentAttemptStatus.Recovering,
                SubmissionToken = "old-token"
            };
            await repository.CreateAsync(attempt);
            Assert.True(await repository.ResolveRefundAsync(CreateResolution(
                attempt.AttemptGuid,
                CardRefundSupervisorDecision.ConfirmNotRefunded,
                attempt.UpdatedAt.AddMinutes(1),
                retryTxnRef: "txn-new")));
            var retry = Assert.IsType<LocalCardPaymentAttempt>(await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.True(await repository.TryBeginRefundSubmissionAsync(
                attempt.AttemptGuid,
                retry.UpdatedAt,
                "new-token",
                retry.UpdatedAt.AddTicks(1)));

            Assert.False(await repository.TryUpdateRefundSessionAsync(
                attempt.AttemptGuid,
                "old-token",
                "old-session",
                "txn-old",
                retry.UpdatedAt.AddMinutes(2)));
            Assert.False(await repository.TryUpdateRefundOutcomeAsync(
                attempt.AttemptGuid,
                "old-token",
                LocalCardPaymentAttemptStatus.Approved,
                "00",
                "Late old approval",
                "old-refund",
                retry.UpdatedAt.AddMinutes(3)));
            Assert.False(await repository.TryMarkRefundRecoveringAsync(
                attempt.AttemptGuid,
                "old-token",
                retry.UpdatedAt.AddMinutes(4)));

            var active = Assert.IsType<LocalCardPaymentAttempt>(await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.True(await repository.ResolveRefundAsync(CreateResolution(
                attempt.AttemptGuid,
                CardRefundSupervisorDecision.ConfirmRefunded,
                active.UpdatedAt.AddMinutes(1),
                refundReference: "confirmed-refund")));
            Assert.False(await repository.TryUpdateRefundOutcomeAsync(
                attempt.AttemptGuid,
                "new-token",
                LocalCardPaymentAttemptStatus.Approved,
                "00",
                "Late active approval",
                "late-refund",
                active.UpdatedAt.AddMinutes(2)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateOutcomeAsync(
                attempt.AttemptGuid,
                LocalCardPaymentAttemptStatus.Failed,
                "ERROR",
                "Generic stale writer",
                null,
                active.UpdatedAt.AddMinutes(3)));

            var saved = Assert.IsType<LocalCardPaymentAttempt>(await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(CardRefundSupervisorResolutionCodes.ConfirmedRefunded, saved.ResponseCode);
            Assert.Equal("confirmed-refund", saved.PaymentReference);
            Assert.Null(saved.SubmissionToken);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Square_old_worker_cannot_write_after_retry_claim_or_supervisor_resolution()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalSquarePaymentAttemptRepository(store);
            var attempt = CreateSquareRefundAttempt(
                Guid.Parse("80000000-0000-0000-0000-000000000001"),
                "idem-stable",
                "stale-key") with
            {
                Status = LocalSquarePaymentAttemptStatus.Recovering,
                SubmissionToken = "old-token"
            };
            await repository.CreateAsync(attempt);
            Assert.True(await repository.ResolveRefundAsync(CreateResolution(
                attempt.AttemptGuid,
                CardRefundSupervisorDecision.ConfirmNotRefunded,
                attempt.UpdatedAt.AddMinutes(1))));
            var retry = Assert.IsType<LocalSquarePaymentAttempt>(await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.True(await repository.TryBeginRefundSubmissionAsync(
                attempt.AttemptGuid,
                retry.UpdatedAt,
                "new-token",
                retry.UpdatedAt.AddTicks(1)));

            Assert.False(await repository.TryRecordRefundResponseAsync(
                attempt.AttemptGuid,
                "old-token",
                "old-refund",
                "PENDING",
                retry.UpdatedAt.AddMinutes(1)));
            Assert.True(await repository.TryRecordRefundResponseAsync(
                attempt.AttemptGuid,
                "new-token",
                "refund-current",
                "PENDING",
                retry.UpdatedAt.AddMinutes(1)));
            Assert.False(await repository.TryMarkRefundCheckoutCreatedAsync(
                attempt.AttemptGuid,
                "old-token",
                "old-checkout",
                "PENDING",
                retry.UpdatedAt.AddMinutes(2)));
            Assert.False(await repository.TryMarkRefundPaymentVerifiedAsync(
                attempt.AttemptGuid,
                "old-token",
                "old-payment",
                "COMPLETED",
                null,
                "Late old verification",
                retry.UpdatedAt.AddMinutes(3)));
            Assert.False(await repository.TryMarkRefundFailedAsync(
                attempt.AttemptGuid,
                "old-token",
                LocalSquarePaymentAttemptStatus.Failed,
                null,
                "FAILED",
                "ERROR",
                "Late old failure",
                retry.UpdatedAt.AddMinutes(4)));

            var active = Assert.IsType<LocalSquarePaymentAttempt>(await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal("refund-current", active.PaymentId);
            Assert.Equal("PENDING", active.PaymentStatus);
            Assert.Equal(LocalSquarePaymentAttemptStatus.Recovering, active.Status);
            Assert.True(await repository.ResolveRefundAsync(CreateResolution(
                attempt.AttemptGuid,
                CardRefundSupervisorDecision.ConfirmRefunded,
                active.UpdatedAt.AddMinutes(1),
                refundReference: "confirmed-square-refund")));
            Assert.False(await repository.TryMarkRefundFailedAsync(
                attempt.AttemptGuid,
                "new-token",
                LocalSquarePaymentAttemptStatus.Failed,
                null,
                "FAILED",
                "ERROR",
                "Late active failure",
                active.UpdatedAt.AddMinutes(2)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.MarkFailedAsync(
                attempt.AttemptGuid,
                LocalSquarePaymentAttemptStatus.Failed,
                null,
                "FAILED",
                "ERROR",
                "Generic stale writer",
                active.UpdatedAt.AddMinutes(3)));

            var saved = Assert.IsType<LocalSquarePaymentAttempt>(await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(CardRefundSupervisorResolutionCodes.ConfirmedRefunded, saved.ResponseCode);
            Assert.Equal("confirmed-square-refund", saved.PaymentId);
            Assert.Null(saved.SubmissionToken);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Square_sale_checkout_callback_rejects_stale_submission_token()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalSquarePaymentAttemptRepository(store);
            var attempt = CreateSquareRefundAttempt(
                Guid.Parse("80000000-0000-0000-0000-000000000002"),
                "sale-idempotency",
                "unused-refund-key") with
            {
                OperationKind = "Sale",
                RefundBusinessKey = null,
                SubmissionToken = "current-sale-token"
            };
            await repository.CreateAsync(attempt);

            Assert.False(await repository.TryMarkCheckoutCreatedAsync(
                attempt.AttemptGuid,
                "stale-sale-token",
                "stale-checkout",
                "PENDING",
                attempt.UpdatedAt.AddMinutes(1)));
            Assert.True(await repository.TryMarkCheckoutCreatedAsync(
                attempt.AttemptGuid,
                "current-sale-token",
                "current-checkout",
                "PENDING",
                attempt.UpdatedAt.AddMinutes(2)));

            var saved = Assert.IsType<LocalSquarePaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal("current-checkout", saved.CheckoutId);
            Assert.Equal("current-sale-token", saved.SubmissionToken);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Supervisor_resolution_cannot_overwrite_verified_refund_outcomes()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var linklyRepository = new LocalCardPaymentAttemptRepository(store);
            var squareRepository = new LocalSquarePaymentAttemptRepository(store);
            var linkly = CreateLinklyRefundAttempt(
                Guid.Parse("90000000-0000-0000-0000-000000000001"),
                "txn-approved",
                "approved-linkly-key") with
            {
                Status = LocalCardPaymentAttemptStatus.Approved
            };
            var square = CreateSquareRefundAttempt(
                Guid.Parse("90000000-0000-0000-0000-000000000002"),
                "idem-approved",
                "approved-square-key") with
            {
                Status = LocalSquarePaymentAttemptStatus.PaymentVerified,
                PaymentId = "verified-payment"
            };
            await linklyRepository.CreateAsync(linkly);
            await squareRepository.CreateAsync(square);

            Assert.False(await linklyRepository.ResolveRefundAsync(CreateResolution(
                linkly.AttemptGuid,
                CardRefundSupervisorDecision.ConfirmNotRefunded,
                linkly.UpdatedAt.AddMinutes(1),
                retryTxnRef: "must-not-apply")));
            Assert.False(await squareRepository.ResolveRefundAsync(CreateResolution(
                square.AttemptGuid,
                CardRefundSupervisorDecision.ConfirmNotRefunded,
                square.UpdatedAt.AddMinutes(1))));

            Assert.Equal(
                LocalCardPaymentAttemptStatus.Approved,
                (await linklyRepository.GetAttemptAsync(linkly.AttemptGuid))?.Status);
            Assert.Equal(
                LocalSquarePaymentAttemptStatus.PaymentVerified,
                (await squareRepository.GetAttemptAsync(square.AttemptGuid))?.Status);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    private static LocalCardPaymentAttempt CreateLinklyRefundAttempt(
        Guid attemptGuid,
        string txnRef,
        string refundBusinessKey,
        DateTimeOffset? updatedAt = null)
    {
        var timestamp = updatedAt ?? DateTimeOffset.Parse("2026-07-28T09:00:00+10:00");
        return new LocalCardPaymentAttempt(
            attemptGuid,
            SessionId: null,
            txnRef,
            Processor: "Linkly",
            Environment: "Production",
            ConnectionMode: "Cloud",
            TxnType: "R",
            Amount: 12.34m,
            Status: LocalCardPaymentAttemptStatus.Pending,
            OrderDraftJson: "{}",
            StoreCode: "S001",
            DeviceCode: "POS-01",
            CashierId: "C001",
            ResponseCode: null,
            ResponseText: null,
            PaymentReference: null,
            CreatedAt: timestamp.AddMinutes(-1),
            UpdatedAt: timestamp,
            CompletedAt: null,
            AcknowledgedAt: null,
            OperationKind: "Refund",
            OperationGuid: Guid.NewGuid(),
            SubmissionToken: null,
            RefundBusinessKey: refundBusinessKey);
    }

    private static LocalSquarePaymentAttempt CreateSquareRefundAttempt(
        Guid attemptGuid,
        string idempotencyKey,
        string refundBusinessKey,
        DateTimeOffset? updatedAt = null)
    {
        var timestamp = updatedAt ?? DateTimeOffset.Parse("2026-07-28T09:00:00+10:00");
        return new LocalSquarePaymentAttempt(
            attemptGuid,
            CheckoutId: null,
            idempotencyKey,
            DeviceId: "SQ-DEVICE",
            LocationId: "SQ-LOCATION",
            Environment: "Production",
            Amount: 12.34m,
            AmountCents: 1234,
            Currency: "AUD",
            Status: LocalSquarePaymentAttemptStatus.Pending,
            CheckoutStatus: null,
            CancelReason: null,
            OrderDraftJson: "{}",
            StoreCode: "S001",
            DeviceCode: "POS-01",
            CashierId: "C001",
            PaymentId: null,
            PaymentStatus: null,
            ResponseCode: null,
            ResponseText: null,
            CreatedAt: timestamp.AddMinutes(-1),
            UpdatedAt: timestamp,
            CompletedAt: null,
            OrderCompletedAt: null,
            ResolvedAt: null,
            OperationKind: "Refund",
            OperationGuid: Guid.NewGuid(),
            SubmissionToken: null,
            RefundBusinessKey: refundBusinessKey);
    }

    private static CardRefundAttemptResolution CreateResolution(
        Guid attemptGuid,
        CardRefundSupervisorDecision decision,
        DateTimeOffset resolvedAt,
        string? refundReference = null,
        string? retryTxnRef = null)
    {
        return new CardRefundAttemptResolution(
            attemptGuid,
            decision,
            "Supervisor reconciled the bank evidence.",
            "Bank portal evidence",
            refundReference,
            retryTxnRef,
            resolvedAt);
    }

    private static string CreateTempDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"hbpos-refund-cas-{Guid.NewGuid():N}.db");

    private static void DeleteTempDatabase(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static async Task<int> ReadScalarIntAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ReadScalarStringAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }
}
