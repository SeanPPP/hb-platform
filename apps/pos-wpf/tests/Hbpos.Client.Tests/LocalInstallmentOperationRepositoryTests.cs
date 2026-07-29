using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Installments;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Tests;

public sealed class LocalInstallmentOperationRepositoryTests
{
    [Fact]
    public async Task Schema_creates_restartable_operation_and_refund_step_tables()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();

            await using var connection = await store.OpenConnectionAsync();
            Assert.Equal(1L, await ReadCountAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'LocalInstallmentOperations';"));
            Assert.Equal(1L, await ReadCountAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'LocalInstallmentRefundSteps';"));
            Assert.Equal(1L, await ReadCountAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('LocalCardPaymentAttempts') WHERE name = 'OperationKind';"));
            Assert.Equal(1L, await ReadCountAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('LocalSquarePaymentAttempts') WHERE name = 'OperationGuid';"));
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Operation_state_compare_and_swap_allows_only_one_terminal_claim()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalInstallmentOperationRepository(store);
            var operation = CreateOperation();
            await repository.CreateOrGetAsync(operation);

            var claims = await Task.WhenAll(
                repository.TryTransitionAsync(operation.OperationGuid, [LocalInstallmentOperationState.Prepared], LocalInstallmentOperationState.TerminalSubmitting, DateTimeOffset.UtcNow),
                repository.TryTransitionAsync(operation.OperationGuid, [LocalInstallmentOperationState.Prepared], LocalInstallmentOperationState.TerminalSubmitting, DateTimeOffset.UtcNow));

            Assert.Equal(1, claims.Count(claim => claim));
            Assert.Equal(LocalInstallmentOperationState.TerminalSubmitting, (await repository.GetAsync(operation.OperationGuid))!.State);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Supervisor_not_refunded_requires_evidence_and_only_then_releases_step_for_retry()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalInstallmentOperationRepository(store);
            var operation = CreateOperation(LocalInstallmentOperationKind.Cancel) with { State = LocalInstallmentOperationState.ResultUnknown };
            var step = CreateStep(operation.OperationGuid, LocalInstallmentRefundStepState.ResultUnknown);
            await repository.CreateCancelOrGetAsync(operation, [step]);

            await Assert.ThrowsAsync<ArgumentException>(() => repository.ResolveRefundStepAsync(
                step.RefundStepGuid,
                new InstallmentRefundSupervisorResolution(InstallmentRefundSupervisorDecision.ConfirmNotRefunded, "manager-1", "银行确认未退款"),
                DateTimeOffset.UtcNow));

            var resolved = await repository.ResolveRefundStepAsync(
                step.RefundStepGuid,
                new InstallmentRefundSupervisorResolution(InstallmentRefundSupervisorDecision.ConfirmNotRefunded, "manager-1", "银行确认未退款", "bank-case-123"),
                DateTimeOffset.UtcNow);

            Assert.True(resolved);
            var saved = Assert.Single(await repository.GetRefundStepsAsync(operation.OperationGuid));
            Assert.Equal(LocalInstallmentRefundStepState.Prepared, saved.State);
            Assert.Equal("manager-1", saved.SupervisorUserId);
            Assert.Equal("bank-case-123", saved.SupervisorEvidence);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Approved_refund_step_is_not_claimable_again()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalInstallmentOperationRepository(store);
            var operation = CreateOperation(LocalInstallmentOperationKind.Cancel) with { State = LocalInstallmentOperationState.ResultUnknown };
            var step = CreateStep(operation.OperationGuid, LocalInstallmentRefundStepState.Approved);
            await repository.CreateCancelOrGetAsync(operation, [step]);

            var replay = await repository.TryTransitionRefundStepAsync(
                step.RefundStepGuid,
                [LocalInstallmentRefundStepState.Prepared],
                LocalInstallmentRefundStepState.TerminalSubmitting,
                DateTimeOffset.UtcNow);

            Assert.False(replay);
            Assert.Equal(LocalInstallmentRefundStepState.Approved, (await repository.GetRefundStepsAsync(operation.OperationGuid)).Single().State);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Supervisor_cannot_reopen_an_approved_refund_step()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalInstallmentOperationRepository(store);
            var operation = CreateOperation(LocalInstallmentOperationKind.Cancel);
            var step = CreateStep(operation.OperationGuid, LocalInstallmentRefundStepState.Approved);
            await repository.CreateCancelOrGetAsync(operation, [step]);

            var resolved = await repository.ResolveRefundStepAsync(
                step.RefundStepGuid,
                new InstallmentRefundSupervisorResolution(InstallmentRefundSupervisorDecision.ConfirmNotRefunded, "manager-1", "银行确认未退款", "bank-case-123"),
                DateTimeOffset.UtcNow);

            Assert.False(resolved);
            Assert.Equal(LocalInstallmentRefundStepState.Approved, (await repository.GetRefundStepsAsync(operation.OperationGuid)).Single().State);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Supervisor_cannot_resolve_a_refund_step_while_terminal_is_submitting()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalInstallmentOperationRepository(store);
            var operation = CreateOperation(LocalInstallmentOperationKind.Cancel) with { State = LocalInstallmentOperationState.TerminalSubmitting };
            var step = CreateStep(operation.OperationGuid, LocalInstallmentRefundStepState.TerminalSubmitting);
            await repository.CreateCancelOrGetAsync(operation, [step]);

            var resolved = await repository.ResolveRefundStepAsync(
                step.RefundStepGuid,
                new InstallmentRefundSupervisorResolution(InstallmentRefundSupervisorDecision.ConfirmNotRefunded, "manager-1", "银行确认未退款", "bank-case-123"),
                DateTimeOffset.UtcNow);

            Assert.False(resolved);
            Assert.Equal(LocalInstallmentRefundStepState.TerminalSubmitting, (await repository.GetRefundStepsAsync(operation.OperationGuid)).Single().State);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Restart_recovery_marks_terminal_submitting_unknown_without_calling_terminal_again()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalInstallmentOperationRepository(store);
            var operation = CreateOperation() with { State = LocalInstallmentOperationState.TerminalSubmitting };
            await repository.CreateOrGetAsync(operation);
            var terminal = new CountingTerminal();
            var service = new InstallmentOperationService(
                repository,
                new NoopInstallmentApiClient(),
                terminal,
                new NoopVoucherTenderClient());

            var results = await service.RecoverAsync(new PosSessionState("HB POS", "S001", "Main", "POS-01", "C001", "Alice", true, 0));

            Assert.Equal(0, terminal.AuthorizeCalls);
            Assert.Equal(0, terminal.RefundCalls);
            Assert.Equal(LocalInstallmentOperationState.ResultUnknown, Assert.Single(results).State);
            Assert.Equal(LocalInstallmentOperationState.ResultUnknown, (await repository.GetAsync(operation.OperationGuid))!.State);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Standard_attempt_recovery_ignores_installment_attempts_and_reads_operation_columns()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var now = DateTimeOffset.UtcNow;
            var cardRepository = new LocalCardPaymentAttemptRepository(store);
            var saleAttempt = new LocalCardPaymentAttempt(Guid.NewGuid(), null, "SALE-1", "Linkly", "Production", "LocalIp", "P", 10m, LocalCardPaymentAttemptStatus.Pending, "{}", "S001", "POS-01", "C001", null, null, null, now, now, null, null);
            var installmentAttempt = saleAttempt with { AttemptGuid = Guid.NewGuid(), TxnRef = "INSTALLMENT-1", UpdatedAt = now.AddMinutes(1), OperationKind = "Repayment", OperationGuid = Guid.NewGuid() };
            await cardRepository.CreateAsync(saleAttempt);
            await cardRepository.CreateAsync(installmentAttempt);

            var standardCardAttempt = await cardRepository.GetLatestOpenAttemptAsync("S001", "POS-01", "C001", "Production");

            Assert.NotNull(standardCardAttempt);
            Assert.Equal(saleAttempt.AttemptGuid, standardCardAttempt!.AttemptGuid);
            Assert.Equal("Sale", standardCardAttempt.OperationKind);

            var squareRepository = new LocalSquarePaymentAttemptRepository(store);
            var squareSale = new LocalSquarePaymentAttempt(Guid.NewGuid(), null, "sale-key", "device", "location", "Production", 10m, 1000, "AUD", LocalSquarePaymentAttemptStatus.Pending, null, null, "{}", "S001", "POS-01", "C001", null, null, null, null, now, now, null, null, null);
            var squareInstallment = squareSale with { AttemptGuid = Guid.NewGuid(), IdempotencyKey = "installment-key", UpdatedAt = now.AddMinutes(1), OperationKind = "Repayment", OperationGuid = Guid.NewGuid() };
            await squareRepository.CreateAsync(squareSale);
            await squareRepository.CreateAsync(squareInstallment);

            var standardSquareAttempt = await squareRepository.GetLatestOpenAttemptAsync("S001", "POS-01", "C001", "Production");

            Assert.NotNull(standardSquareAttempt);
            Assert.Equal(squareSale.AttemptGuid, standardSquareAttempt!.AttemptGuid);
            Assert.Equal("Sale", standardSquareAttempt.OperationKind);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    private static LocalInstallmentOperation CreateOperation(LocalInstallmentOperationKind kind = LocalInstallmentOperationKind.Repayment)
    {
        var now = DateTimeOffset.UtcNow;
        return new LocalInstallmentOperation(Guid.NewGuid(), kind, Guid.NewGuid(), Guid.NewGuid(), "S001", "POS-01", "C001", "installment-key", "{}", LocalInstallmentOperationState.Prepared, null, null, null, null, now, now);
    }

    private static LocalInstallmentRefundStep CreateStep(Guid operationGuid, LocalInstallmentRefundStepState state)
    {
        var now = DateTimeOffset.UtcNow;
        return new LocalInstallmentRefundStep(Guid.NewGuid(), operationGuid, Guid.NewGuid(), PaymentMethodKind.Card, 12m, "TXN-1", "refund-key", state, null, null, null, null, null, null, null, null, now, now);
    }

    private static string CreateTempDatabasePath() => Path.Combine(Path.GetTempPath(), $"hbpos-installment-operation-{Guid.NewGuid():N}.db");

    private static async Task<long> ReadCountAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static void DeleteTempDatabase(string path)
    {
        foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }

    private sealed class CountingTerminal : ICardTerminalClient
    {
        public int AuthorizeCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public Task<PaymentAuthorizationResult> AuthorizeAsync(decimal amount, PosSessionState session, CancellationToken cancellationToken = default)
        {
            AuthorizeCalls++;
            return Task.FromResult(new PaymentAuthorizationResult(false));
        }

        public Task<PaymentAuthorizationResult> RefundAsync(decimal amount, PosSessionState session, string? originalReference, CancellationToken cancellationToken = default)
        {
            RefundCalls++;
            return Task.FromResult(new PaymentAuthorizationResult(false));
        }
    }

    private sealed class NoopVoucherTenderClient : IVoucherTenderClient
    {
        public Task<PaymentAuthorizationResult> RedeemAsync(decimal amount, PosSessionState session, string? voucherCode, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentAuthorizationResult(false));
        public Task<PaymentAuthorizationResult> IssueRefundAsync(decimal amount, PosSessionState session, string orderReference, string idempotencyKey, string? reason = null, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentAuthorizationResult(false));
        public Task<bool> ReleaseAsync(PosSessionState session, string voucherCode, string reservationToken, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class NoopInstallmentApiClient : IInstallmentApiClient
    {
        public Task<InstallmentCreateResponse> CreateAsync(InstallmentCreateRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstallmentAppendPaymentResponse> AppendPaymentAsync(InstallmentAppendPaymentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstallmentConfirmPickupResponse> ConfirmPickupAsync(InstallmentConfirmPickupRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstallmentCancelResponse> CancelAsync(InstallmentCancelRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstallmentVoidResponse> VoidAsync(InstallmentVoidRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
