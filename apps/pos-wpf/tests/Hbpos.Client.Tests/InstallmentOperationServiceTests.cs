using System.Text.Json;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Installments;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Tests;

public sealed class InstallmentOperationServiceTests
{
    [Fact]
    public async Task Create_is_blocked_when_terminal_has_another_recoverable_create()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(path);
            var existingRequest = CreateInstallmentRequest(Guid.NewGuid());
            var now = DateTimeOffset.UtcNow;
            await repository.CreateOrGetAsync(new LocalInstallmentOperation(
                existingRequest.DownPayment.PaymentGuid,
                LocalInstallmentOperationKind.Create,
                existingRequest.InstallmentGuid,
                existingRequest.DownPayment.PaymentGuid,
                existingRequest.StoreCode,
                existingRequest.DeviceCode,
                existingRequest.CashierId,
                existingRequest.DownPayment.IdempotencyKey!,
                JsonSerializer.Serialize(existingRequest),
                LocalInstallmentOperationState.ResultUnknown,
                null,
                null,
                null,
                "API result unknown",
                now,
                now));
            var terminal = new CountingTerminal(approve: true);
            var service = CreateService(repository, new RecordingInstallmentApi(), terminal);
            var nextRequest = CreateInstallmentRequest(Guid.NewGuid());

            var result = await service.ExecuteCreateAsync(Session, nextRequest, authorizeCard: true);

            Assert.False(result.Succeeded);
            Assert.True(result.RequiresReview);
            Assert.Equal(0, terminal.AuthorizeCalls);
            Assert.Single(await repository.GetRecoverableAsync(Session.StoreCode));
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Repayment_api_cancellation_recovers_by_replaying_only_the_original_api_request()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(path);
            var request = CreateRepaymentRequest();
            var firstTerminal = new CountingTerminal(approve: true);
            var firstApi = new RecordingInstallmentApi { AppendException = new OperationCanceledException() };
            var firstService = CreateService(repository, firstApi, firstTerminal);

            var initial = await firstService.ExecuteRepaymentAsync(Session, request, authorizeCard: true);

            Assert.False(initial.Succeeded);
            Assert.True(initial.RequiresReview);
            Assert.Equal(1, firstTerminal.AuthorizeCalls);
            var operation = Assert.Single(await repository.GetRecoverableAsync(Session.StoreCode));
            Assert.Equal(LocalInstallmentOperationState.ResultUnknown, operation.State);

            var restartedTerminal = new CountingTerminal(approve: true);
            var restartedApi = new RecordingInstallmentApi { AppendResponse = CreateAppendResponse(request) };
            var restartedService = CreateService(repository, restartedApi, restartedTerminal);

            var recovered = await restartedService.RecoverAsync(Session);

            Assert.True(Assert.Single(recovered).ReplayedApi);
            Assert.Equal(0, restartedTerminal.AuthorizeCalls);
            Assert.Equal(1, restartedApi.AppendCalls);
            Assert.Equal(request.PaymentGuid, restartedApi.LastAppendRequest!.PaymentGuid);
            Assert.Equal(request.IdempotencyKey, restartedApi.LastAppendRequest.IdempotencyKey);
            Assert.Equal(LocalInstallmentOperationState.Completed, (await repository.GetAsync(request.PaymentGuid))!.State);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Concurrent_recovery_uses_one_api_claim_and_does_not_repeat_the_terminal()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(path);
            var request = CreateRepaymentRequest();
            var operation = CreateApprovedRepaymentOperation(request);
            await repository.CreateOrGetAsync(operation);
            var api = new RecordingInstallmentApi { AppendResponse = CreateAppendResponse(request), AppendGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
            var terminal = new CountingTerminal(approve: true);
            var service = CreateService(repository, api, terminal);

            var firstRecovery = service.RecoverAsync(Session);
            await api.AppendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var secondRecovery = service.RecoverAsync(Session);
            api.AppendGate.SetResult();
            await Task.WhenAll(firstRecovery, secondRecovery);

            Assert.Equal(1, api.AppendCalls);
            Assert.Equal(0, terminal.AuthorizeCalls);
            Assert.Equal(LocalInstallmentOperationState.Completed, (await repository.GetAsync(operation.OperationGuid))!.State);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Restarted_service_immediately_claims_recent_api_submitting_with_a_new_process_token()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(path);
            var request = CreateRepaymentRequest();
            var operation = CreateApprovedRepaymentOperation(request);
            await repository.CreateOrGetAsync(operation);
            Assert.True(await repository.TryClaimApiAsync(operation.OperationGuid, "previous-process", false, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)));

            var terminal = new CountingTerminal(approve: true);
            var api = new RecordingInstallmentApi { AppendResponse = CreateAppendResponse(request) };
            var restartedService = CreateService(repository, api, terminal);

            var recovered = await restartedService.RecoverAsync(Session);

            Assert.True(Assert.Single(recovered).ReplayedApi);
            Assert.Equal(1, api.AppendCalls);
            Assert.Equal(0, terminal.AuthorizeCalls);
            Assert.Equal(LocalInstallmentOperationState.Completed, (await repository.GetAsync(operation.OperationGuid))!.State);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Persisted_linkly_approved_attempt_with_wrong_amount_does_not_synthesize_recovery_or_call_api()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalInstallmentOperationRepository(store);
            var attemptRepository = new LocalCardPaymentAttemptRepository(store);
            var request = CreateRepaymentRequest();
            var attemptGuid = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            await attemptRepository.CreateAsync(new LocalCardPaymentAttempt(
                attemptGuid, null, "LINKLY-APPROVED", "Linkly", "Production", "LocalIp", "P", request.Amount + 1m,
                LocalCardPaymentAttemptStatus.Approved, "{}", request.StoreCode, request.DeviceCode, request.CashierId,
                "00", "APPROVED", "LINKLY-APPROVED", now, now, now, null, "Repayment", request.PaymentGuid));
            var operation = CreateApprovedRepaymentOperation(request) with
            {
                State = LocalInstallmentOperationState.ResultUnknown,
                TerminalAttemptGuid = attemptGuid.ToString("D"),
                RequestJson = JsonSerializer.Serialize(request)
            };
            await repository.CreateOrGetAsync(operation);
            var terminal = new CountingTerminal(approve: true);
            var api = new RecordingInstallmentApi { AppendResponse = CreateAppendResponse(request) };
            var service = new InstallmentOperationService(repository, api, terminal, new NoopVoucherTenderClient(), cardPaymentAttemptRepository: attemptRepository);

            var recovered = await service.RecoverAsync(Session);

            Assert.False(Assert.Single(recovered).ReplayedApi);
            Assert.Equal(0, api.AppendCalls);
            Assert.Equal(0, terminal.AuthorizeCalls);
            Assert.Equal(LocalInstallmentOperationState.ResultUnknown, (await repository.GetAsync(request.PaymentGuid))!.State);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Unknown_linkly_attempt_queries_remote_approval_then_replays_only_api()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalInstallmentOperationRepository(store);
            var attemptRepository = new LocalCardPaymentAttemptRepository(store);
            var request = CreateRepaymentRequest();
            var attemptGuid = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            await attemptRepository.CreateAsync(new LocalCardPaymentAttempt(
                attemptGuid, null, "LINKLY-UNKNOWN", "Linkly", "Production", "LocalIp", "P", request.Amount,
                LocalCardPaymentAttemptStatus.Recovering, "{}", request.StoreCode, request.DeviceCode, request.CashierId,
                null, null, null, now, now, null, null, "Repayment", request.PaymentGuid));
            await repository.CreateOrGetAsync(CreateApprovedRepaymentOperation(request) with
            {
                State = LocalInstallmentOperationState.ResultUnknown,
                TerminalAttemptGuid = attemptGuid.ToString("D"),
                RequestJson = JsonSerializer.Serialize(request)
            });

            var terminal = new RemoteApprovalTerminal(request.Amount);
            var api = new RecordingInstallmentApi { AppendResponse = CreateAppendResponse(request) };
            var service = new InstallmentOperationService(repository, api, terminal, new NoopVoucherTenderClient(), cardPaymentAttemptRepository: attemptRepository);

            var recovered = await service.RecoverAsync(Session);

            Assert.True(Assert.Single(recovered).ReplayedApi);
            Assert.Equal(1, terminal.RecoveryCalls);
            Assert.Equal(0, terminal.AuthorizeCalls);
            Assert.Equal(1, api.AppendCalls);
            Assert.Equal("ANZ:LINKLY-REMOTE", api.LastAppendRequest!.Reference);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Unknown_square_attempt_queries_remote_approval_with_payment_reference_shape_then_replays_only_api()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalInstallmentOperationRepository(store);
            var attempts = new LocalSquarePaymentAttemptRepository(store);
            var request = CreateRepaymentRequest();
            var attemptGuid = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            await attempts.CreateAsync(new LocalSquarePaymentAttempt(
                attemptGuid, "checkout-001", "persisted-square-key", "device-001", "location-001", "Production", request.Amount, 4000, "AUD",
                LocalSquarePaymentAttemptStatus.Unknown, "IN_PROGRESS", null, "{}", request.StoreCode, request.DeviceCode, request.CashierId,
                null, null, null, null, now, now, null, null, null, "Repayment", request.PaymentGuid));
            await repository.CreateOrGetAsync(CreateApprovedRepaymentOperation(request) with
            {
                State = LocalInstallmentOperationState.ResultUnknown,
                TerminalAttemptGuid = attemptGuid.ToString("D"),
                RequestJson = JsonSerializer.Serialize(request)
            });

            var terminal = new RemoteSquareApprovalTerminal(request.Amount);
            var api = new RecordingInstallmentApi { AppendResponse = CreateAppendResponse(request) };
            var service = new InstallmentOperationService(repository, api, terminal, new NoopVoucherTenderClient(), squarePaymentAttemptRepository: attempts);

            var recovered = await service.RecoverAsync(Session);

            Assert.True(Assert.Single(recovered).ReplayedApi);
            Assert.Equal(1, terminal.RecoveryCalls);
            Assert.Equal(0, terminal.AuthorizeCalls);
            Assert.Equal(1, api.AppendCalls);
            Assert.Equal("SQ:payment-001", api.LastAppendRequest!.Reference);
            Assert.Equal("payment-001", Assert.Single(api.LastAppendRequest.CardTransactions!).TxnRef);
            Assert.Equal(request.PaymentGuid, api.LastAppendRequest.PaymentGuid);
            Assert.Equal(request.IdempotencyKey, api.LastAppendRequest.IdempotencyKey);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Supervisor_confirmed_linkly_non_refund_persists_new_txn_ref_before_retry()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(path);
            var order = CreateLocalOrder();
            var terminal = new SequencedLinklyRefundTerminal(repository);
            var api = new RecordingInstallmentApi { CancelResponse = CreateCancelResponse(order) };
            var service = new InstallmentOperationService(
                repository,
                api,
                terminal,
                new NoopVoucherTenderClient(),
                cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CardTerminalSettings.FromEnvironment() with
                {
                    Processor = CardProcessorKind.Linkly,
                    LinklyConnectionMode = LinklyConnectionMode.LocalIp
                }));

            var initial = await service.ExecuteCancelAsync(order, Session, "客户取消");
            Assert.True(initial.RequiresReview);
            var operation = Assert.Single(await repository.GetRecoverableAsync(Session.StoreCode));
            var step = Assert.Single((await repository.GetRefundStepsAsync(operation.OperationGuid)).Where(item => item.Method == PaymentMethodKind.Card));
            Assert.Equal(LocalInstallmentRefundStepState.ResultUnknown, step.State);

            Assert.True(await repository.ResolveRefundStepAsync(
                step.RefundStepGuid,
                new InstallmentRefundSupervisorResolution(
                    InstallmentRefundSupervisorDecision.ConfirmNotRefunded,
                    "manager-1",
                    "银行明确确认未退款",
                    Evidence: "bank-case-001"),
                DateTimeOffset.UtcNow));

            var firstRetry = await service.ResumeCancelAfterSupervisorAsync(operation.OperationGuid, order.InstallmentNumber, Session);
            Assert.True(firstRetry.RequiresReview);

            var secondUnknownStep = Assert.Single((await repository.GetRefundStepsAsync(operation.OperationGuid)).Where(item => item.Method == PaymentMethodKind.Card));
            Assert.Equal(LocalInstallmentRefundStepState.ResultUnknown, secondUnknownStep.State);
            Assert.True(await repository.ResolveRefundStepAsync(
                secondUnknownStep.RefundStepGuid,
                new InstallmentRefundSupervisorResolution(
                    InstallmentRefundSupervisorDecision.ConfirmNotRefunded,
                    "manager-1",
                    "银行再次确认未退款",
                    Evidence: "bank-case-002"),
                DateTimeOffset.UtcNow));

            var resumed = await service.ResumeCancelAfterSupervisorAsync(operation.OperationGuid, order.InstallmentNumber, Session);

            Assert.True(resumed.Succeeded, resumed.Message);
            Assert.Equal(3, terminal.RefundCalls);
            Assert.Equal(3, terminal.SubmissionReferences.Count);
            Assert.All(terminal.SubmissionReferences, reference => Assert.NotEqual("CARD-1", reference));
            Assert.Equal(3, terminal.PersistedReferencesAtSubmission.Count);
            Assert.Equal(terminal.SubmissionReferences, terminal.PersistedReferencesAtSubmission);
            Assert.NotEqual(terminal.SubmissionReferences[0], terminal.SubmissionReferences[1]);
            Assert.NotEqual(terminal.SubmissionReferences[1], terminal.SubmissionReferences[2]);
            Assert.Equal(2, terminal.RetryReferences.Count);
            Assert.All(terminal.RetryReferences, reference => Assert.NotEqual("CARD-1", reference));
            Assert.NotEqual(terminal.RetryReferences[0], terminal.RetryReferences[1]);
            Assert.Equal(terminal.RetryReferences, terminal.PersistedReferencesAtRetry);
            Assert.Equal(1, api.CancelCalls);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Linkly_session_binding_survives_the_cancelled_ui_token_after_terminal_accepts()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalInstallmentOperationRepository(store);
            var attempts = new LocalCardPaymentAttemptRepository(store);
            var context = new LinklyPaymentAttemptContextAccessor();
            var terminal = new CancelledTokenSessionBindingTerminal(context);
            var request = CreateRepaymentRequest();
            var settings = CardTerminalSettings.FromEnvironment() with
            {
                Processor = CardProcessorKind.Linkly,
                LinklyConnectionMode = LinklyConnectionMode.LocalIp
            };
            var service = new InstallmentOperationService(
                repository,
                new RecordingInstallmentApi(),
                terminal,
                new NoopVoucherTenderClient(),
                cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(settings),
                cardPaymentAttemptRepository: attempts,
                linklyPaymentAttemptContextAccessor: context);

            var result = await service.ExecuteRepaymentAsync(Session, request, authorizeCard: true);

            Assert.True(result.RequiresReview);
            Assert.True(terminal.BindCalled);
            var operation = Assert.Single(await repository.GetRecoverableAsync(Session.StoreCode));
            var attempt = await attempts.GetAttemptAsync(Guid.Parse(operation.TerminalAttemptGuid!));
            Assert.NotNull(attempt);
            Assert.Equal("LINKLY-SESSION-001", attempt.SessionId);
            Assert.Equal("LINKLY-TXN-001", attempt.TxnRef);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Repayment_approval_without_authorized_amount_remains_locked_and_does_not_call_api()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(path);
            var request = CreateRepaymentRequest();
            var terminal = new MissingAuthorizedAmountTerminal();
            var api = new RecordingInstallmentApi { AppendResponse = CreateAppendResponse(request) };
            var service = new InstallmentOperationService(repository, api, terminal, new NoopVoucherTenderClient());

            var result = await service.ExecuteRepaymentAsync(Session, request, authorizeCard: true);

            Assert.False(result.Succeeded);
            Assert.True(result.RequiresReview);
            Assert.Equal(1, terminal.AuthorizeCalls);
            Assert.Equal(0, api.AppendCalls);
            Assert.Equal(LocalInstallmentOperationState.ResultUnknown, (await repository.GetAsync(request.PaymentGuid))!.State);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Refund_approval_without_authorized_amount_remains_locked_and_does_not_call_cancel_api()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(path);
            var order = CreateLocalOrder();
            var terminal = new MissingAuthorizedAmountTerminal();
            var api = new RecordingInstallmentApi { CancelResponse = CreateCancelResponse(order) };
            var service = new InstallmentOperationService(repository, api, terminal, new NoopVoucherTenderClient());

            var result = await service.ExecuteCancelAsync(order, Session, "客户取消");

            Assert.False(result.Succeeded);
            Assert.True(result.RequiresReview);
            Assert.Equal(1, terminal.RefundCalls);
            Assert.Equal(0, api.CancelCalls);
            var operation = Assert.Single(await repository.GetRecoverableAsync(Session.StoreCode));
            Assert.Equal(LocalInstallmentOperationState.ResultUnknown, operation.State);
            var cardStep = Assert.Single((await repository.GetRefundStepsAsync(operation.OperationGuid)).Where(step => step.Method == PaymentMethodKind.Card));
            Assert.Equal(LocalInstallmentRefundStepState.ResultUnknown, cardStep.State);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Cancel_api_cancellation_does_not_repeat_completed_refunds_after_restart()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(path);
            var order = CreateLocalOrder();
            var firstTerminal = new CountingTerminal(approve: true);
            var firstApi = new RecordingInstallmentApi { CancelException = new OperationCanceledException() };
            var firstService = CreateService(repository, firstApi, firstTerminal);

            var initial = await firstService.ExecuteCancelAsync(order, Session, "客户取消");

            Assert.False(initial.Succeeded);
            Assert.True(initial.RequiresReview);
            Assert.Equal(1, firstTerminal.RefundCalls);
            var operation = Assert.Single(await repository.GetRecoverableAsync(Session.StoreCode));
            Assert.Equal(LocalInstallmentOperationState.ResultUnknown, operation.State);
            Assert.All(await repository.GetRefundStepsAsync(operation.OperationGuid), step => Assert.Equal(LocalInstallmentRefundStepState.Approved, step.State));

            var restartedTerminal = new CountingTerminal(approve: true);
            var restartedApi = new RecordingInstallmentApi { CancelResponse = CreateCancelResponse(order) };
            var restartedService = CreateService(repository, restartedApi, restartedTerminal);

            var recovered = await restartedService.RecoverAsync(Session);

            Assert.True(Assert.Single(recovered).ReplayedApi);
            Assert.Equal(0, restartedTerminal.RefundCalls);
            Assert.Equal(1, restartedApi.CancelCalls);
            Assert.All(await repository.GetRefundStepsAsync(operation.OperationGuid), step => Assert.Equal(LocalInstallmentRefundStepState.Completed, step.State));
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Explicit_refund_decline_returns_step_to_prepared_and_next_cancel_retries_without_supervisor_review()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(path);
            var order = CreateLocalOrder();
            var terminal = new DeclineThenApproveRefundTerminal();
            var api = new RecordingInstallmentApi { CancelResponse = CreateCancelResponse(order) };
            var service = new InstallmentOperationService(repository, api, terminal, new NoopVoucherTenderClient());

            var first = await service.ExecuteCancelAsync(order, Session, "客户取消");

            Assert.False(first.Succeeded);
            Assert.False(first.RequiresReview);
            var operation = Assert.Single(await repository.GetRecoverableAsync(Session.StoreCode));
            Assert.Equal(LocalInstallmentOperationState.Prepared, operation.State);
            Assert.Equal(
                LocalInstallmentRefundStepState.Prepared,
                Assert.Single((await repository.GetRefundStepsAsync(operation.OperationGuid)).Where(step => step.Method == PaymentMethodKind.Card)).State);

            var second = await service.ExecuteCancelAsync(order, Session, "客户取消");

            Assert.True(second.Succeeded, second.Message);
            Assert.Equal(2, terminal.RefundCalls);
            Assert.Equal(1, api.CancelCalls);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Restarted_cancel_marks_submitting_refund_step_unknown_and_keeps_it_locked()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(path);
            var order = CreateLocalOrder();
            var operation = CreateCancelOperation(order, LocalInstallmentOperationState.TerminalSubmitting);
            var now = DateTimeOffset.UtcNow;
            var step = new LocalInstallmentRefundStep(
                Guid.NewGuid(), operation.OperationGuid, order.Payments[1].PaymentGuid, PaymentMethodKind.Card, 40m,
                "CARD-1", "refund-idempotency", LocalInstallmentRefundStepState.TerminalSubmitting,
                null, JsonSerializer.Serialize<IReadOnlyList<CardTransactionDto>>([CreateCardTransaction(40m)]), null,
                null, null, null, null, null, now, now);
            await repository.CreateCancelOrGetAsync(operation, [step]);
            var api = new RecordingInstallmentApi { CancelResponse = CreateCancelResponse(order) };
            var service = new InstallmentOperationService(repository, api, new CountingTerminal(approve: true), new NoopVoucherTenderClient());

            var recovered = await service.RecoverAsync(Session);

            Assert.False(Assert.Single(recovered).ReplayedApi);
            Assert.Equal(0, api.CancelCalls);
            Assert.Equal(LocalInstallmentOperationState.ResultUnknown, (await repository.GetAsync(operation.OperationGuid))!.State);
            Assert.Equal(LocalInstallmentRefundStepState.ResultUnknown, Assert.Single(await repository.GetRefundStepsAsync(operation.OperationGuid)).State);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Restarted_cancel_with_all_refunds_approved_submits_only_cancel_api()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(path);
            var order = CreateLocalOrder();
            var operation = CreateCancelOperation(order, LocalInstallmentOperationState.TerminalSubmitting);
            var now = DateTimeOffset.UtcNow;
            var step = new LocalInstallmentRefundStep(
                Guid.NewGuid(), operation.OperationGuid, order.Payments[1].PaymentGuid, PaymentMethodKind.Card, 40m,
                "CARD-1", "refund-idempotency", LocalInstallmentRefundStepState.Approved,
                "REFUND-1", JsonSerializer.Serialize<IReadOnlyList<CardTransactionDto>>([CreateCardTransaction(40m)]), null,
                null, null, null, null, null, now, now);
            await repository.CreateCancelOrGetAsync(operation, [step]);
            var api = new RecordingInstallmentApi { CancelResponse = CreateCancelResponse(order) };
            var terminal = new CountingTerminal(approve: true);
            var service = new InstallmentOperationService(repository, api, terminal, new NoopVoucherTenderClient());

            var recovered = await service.RecoverAsync(Session);

            Assert.True(Assert.Single(recovered).ReplayedApi);
            Assert.Equal(1, api.CancelCalls);
            Assert.Equal(0, terminal.RefundCalls);
            Assert.Equal(LocalInstallmentOperationState.Completed, (await repository.GetAsync(operation.OperationGuid))!.State);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    private static readonly PosSessionState Session = new("HB POS", "S001", "Main", "POS-01", "C001", "Alice", true, 0);

    private static async Task<LocalInstallmentOperationRepository> CreateRepositoryAsync(string path)
    {
        var store = new LocalSqliteStore(path);
        await new LocalSchemaService(store).InitializeAsync();
        return new LocalInstallmentOperationRepository(store);
    }

    private static InstallmentOperationService CreateService(
        LocalInstallmentOperationRepository repository,
        RecordingInstallmentApi api,
        CountingTerminal terminal) =>
        new(repository, api, terminal, new NoopVoucherTenderClient());

    private static InstallmentAppendPaymentRequest CreateRepaymentRequest() => new(
        Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Guid.Parse("12345678-9999-aaaa-bbbb-cccccccccccc"),
        "S001", "POS-01", "C001", "Alice", 40m, PaymentMethodKind.Card, null, null, null, "repayment-idempotency");

    private static InstallmentCreateRequest CreateInstallmentRequest(Guid paymentGuid) => new(
        paymentGuid,
        "S001",
        "POS-01",
        "C001",
        "Alice",
        DateTimeOffset.UtcNow,
        120m,
        30m,
        [new InstallmentLineDto(Guid.NewGuid(), "SKU-001", null, "Rice Cooker", "690001", 1m, 120m, 0m, 120m)],
        new InstallmentPaymentCommandDto(
            paymentGuid,
            PaymentMethodKind.Card,
            30m,
            null,
            IdempotencyKey: $"{paymentGuid:D}:create"),
        "Alice",
        "0400111222");

    private static LocalInstallmentOperation CreateApprovedRepaymentOperation(InstallmentAppendPaymentRequest request)
    {
        var transaction = CreateCardTransaction(request.Amount);
        var snapshot = request with { Reference = transaction.TxnRef, CardTransactions = [transaction] };
        var now = DateTimeOffset.UtcNow;
        return new LocalInstallmentOperation(request.PaymentGuid, LocalInstallmentOperationKind.Repayment, request.InstallmentGuid, request.PaymentGuid, request.StoreCode, request.DeviceCode, request.CashierId, request.IdempotencyKey!, JsonSerializer.Serialize(snapshot), LocalInstallmentOperationState.TerminalApproved, "attempt", "Linkly", null, null, now, now);
    }

    private static LocalInstallmentOperation CreateCancelOperation(LocalInstallmentOrder order, LocalInstallmentOperationState state)
    {
        var now = DateTimeOffset.UtcNow;
        var request = new InstallmentCancelRequest(
            order.InstallmentGuid,
            Session.StoreCode,
            Session.DeviceCode,
            Session.CashierId,
            Session.CashierName,
            now,
            [],
            "客户取消",
            $"{order.InstallmentGuid:D}:cancel");
        return new LocalInstallmentOperation(
            Guid.NewGuid(),
            LocalInstallmentOperationKind.Cancel,
            order.InstallmentGuid,
            null,
            Session.StoreCode,
            Session.DeviceCode,
            Session.CashierId,
            request.IdempotencyKey!,
            JsonSerializer.Serialize(request),
            state,
            null,
            null,
            null,
            null,
            now,
            now);
    }

    private static InstallmentAppendPaymentResponse CreateAppendResponse(InstallmentAppendPaymentRequest request)
    {
        var details = CreateDetails(request.InstallmentGuid, InstallmentStatus.Active) with
        {
            PaidAmount = 70m,
            BalanceAmount = 50m,
            Payments =
            [
                new InstallmentPaymentDto(Guid.Parse("12345678-1111-2222-3333-444444444444"), PaymentMethodKind.Cash, 30m, null, InstallmentPaymentStatus.Recorded, DateTimeOffset.UtcNow, "C001", "POS-01"),
                new InstallmentPaymentDto(request.PaymentGuid, request.Method, request.Amount, request.Reference, InstallmentPaymentStatus.Recorded, DateTimeOffset.UtcNow, "C001", "POS-01", request.CardTransactions, request.IdempotencyKey)
            ]
        };
        return new InstallmentAppendPaymentResponse(request.InstallmentGuid, request.PaymentGuid, details.PaidAmount, details.BalanceAmount, details.Status, details, false, "补款完成");
    }

    private static LocalInstallmentOrder CreateLocalOrder()
    {
        var details = CreateDetails(Guid.Parse("11111111-2222-3333-4444-555555555555"), InstallmentStatus.Active) with
        {
            Payments =
            [
                new InstallmentPaymentDto(Guid.Parse("12345678-1111-2222-3333-444444444444"), PaymentMethodKind.Cash, 30m, "CASH-1", InstallmentPaymentStatus.Recorded, DateTimeOffset.UtcNow, "C001", "POS-01"),
                new InstallmentPaymentDto(Guid.Parse("12345678-2222-3333-4444-555555555555"), PaymentMethodKind.Card, 40m, "CARD-1", InstallmentPaymentStatus.Recorded, DateTimeOffset.UtcNow, "C001", "POS-01", [CreateCardTransaction(40m)])
            ],
            PaidAmount = 70m,
            BalanceAmount = 50m
        };
        return new LocalInstallmentOrder(details.InstallmentGuid, details.InstallmentGuid, details.InstallmentNumber, details.StoreCode, details.DeviceCode, details.CashierId, details.CashierName, details.CustomerName, details.CustomerPhone, details.CreatedAt, DateTimeOffset.UtcNow, details.TotalAmount, details.MinimumDownPayment, details.DownPaymentAmount, details.PaidAmount, details.BalanceAmount, details.Status, details.Lines, details.Payments, details.PickupInfo, details.Note, details.CancellationInfo);
    }

    private static InstallmentCancelResponse CreateCancelResponse(LocalInstallmentOrder order)
    {
        var details = CreateDetails(order.InstallmentGuid, InstallmentStatus.Cancelled) with
        {
            Payments = order.Payments,
            PaidAmount = order.PaidAmount,
            BalanceAmount = order.BalanceAmount,
            CancellationInfo = new InstallmentCancellationInfoDto(InstallmentCancellationKind.RefundCancel, DateTimeOffset.UtcNow, "Alice", "客户取消")
        };
        return new InstallmentCancelResponse(order.InstallmentGuid, InstallmentStatus.Cancelled, details, false, "已取消");
    }

    private static InstallmentDetailsDto CreateDetails(Guid installmentGuid, InstallmentStatus status) => new(
        installmentGuid, "IO-001", "S001", "POS-01", "C001", "Alice", "张三", "0400111222", DateTimeOffset.UtcNow,
        120m, 20m, 30m, 30m, 90m, status,
        [new InstallmentLineDto(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), "SKU-001", null, "Rice Cooker", "690001", 1m, 120m, 0m, 120m)],
        [new InstallmentPaymentDto(Guid.Parse("12345678-1111-2222-3333-444444444444"), PaymentMethodKind.Cash, 30m, null, InstallmentPaymentStatus.Recorded, DateTimeOffset.UtcNow, "C001", "POS-01")],
        null, null, null);

    private static CardTransactionDto CreateCardTransaction(decimal amount) => new("Linkly", "CARD-1", "AUTH-1", "VISA", 4, "1234", "MID-1", "00", "APPROVED", "RRN-1", DateTimeOffset.UtcNow, amount, "receipt");
    private static string CreateTempDatabasePath() => Path.Combine(Path.GetTempPath(), $"hbpos-installment-service-{Guid.NewGuid():N}.db");

    private static void DeleteTempDatabase(string path)
    {
        foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private sealed class CountingTerminal(bool approve) : ICardTerminalClient
    {
        public int AuthorizeCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public Task<PaymentAuthorizationResult> AuthorizeAsync(decimal amount, PosSessionState session, CancellationToken cancellationToken = default)
        {
            AuthorizeCalls++;
            return Task.FromResult(new PaymentAuthorizationResult(approve, "CARD-1", AuthorizedAmount: amount, CardTransactions: approve ? [CreateCardTransaction(amount)] : null, Processor: "Linkly"));
        }

        public Task<PaymentAuthorizationResult> RefundAsync(decimal amount, PosSessionState session, string? originalReference, CancellationToken cancellationToken = default)
        {
            RefundCalls++;
            return Task.FromResult(new PaymentAuthorizationResult(approve, $"REFUND:{originalReference}", AuthorizedAmount: amount, CardTransactions: approve ? [CreateCardTransaction(amount)] : null, Processor: "Linkly"));
        }
    }

    private sealed class MissingAuthorizedAmountTerminal : ICardTerminalClient
    {
        public int AuthorizeCalls { get; private set; }
        public int RefundCalls { get; private set; }

        public Task<PaymentAuthorizationResult> AuthorizeAsync(decimal amount, PosSessionState session, CancellationToken cancellationToken = default)
        {
            AuthorizeCalls++;
            return Task.FromResult(new PaymentAuthorizationResult(true, "CARD-1", CardTransactions: [CreateCardTransaction(amount)], Processor: "Linkly"));
        }

        public Task<PaymentAuthorizationResult> RefundAsync(decimal amount, PosSessionState session, string? originalReference, CancellationToken cancellationToken = default)
        {
            RefundCalls++;
            return Task.FromResult(new PaymentAuthorizationResult(true, $"REFUND:{originalReference}", CardTransactions: [CreateCardTransaction(amount)], Processor: "Linkly"));
        }
    }

    private sealed class DeclineThenApproveRefundTerminal : ICardTerminalClient
    {
        public int RefundCalls { get; private set; }

        public Task<PaymentAuthorizationResult> AuthorizeAsync(decimal amount, PosSessionState session, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentAuthorizationResult(false));

        public Task<PaymentAuthorizationResult> RefundAsync(decimal amount, PosSessionState session, string? originalReference, CancellationToken cancellationToken = default)
        {
            RefundCalls++;
            return Task.FromResult(RefundCalls == 1
                ? new PaymentAuthorizationResult(false, null, "DECLINED", Processor: "Linkly", ResponseCode: "05", ResponseText: "DECLINED")
                : new PaymentAuthorizationResult(true, "REFUND-1", AuthorizedAmount: amount, CardTransactions: [CreateCardTransaction(amount)], Processor: "Linkly"));
        }
    }

    private sealed class RemoteApprovalTerminal(decimal amount) : ICardTerminalClient, IInstallmentTerminalRecoveryClient
    {
        public int AuthorizeCalls { get; private set; }
        public int RecoveryCalls { get; private set; }

        public Task<PaymentAuthorizationResult> AuthorizeAsync(decimal amount, PosSessionState session, CancellationToken cancellationToken = default)
        {
            AuthorizeCalls++;
            return Task.FromResult(new PaymentAuthorizationResult(false));
        }

        public Task<PaymentAuthorizationResult> RefundAsync(decimal amount, PosSessionState session, string? originalReference, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentAuthorizationResult(false));

        public Task<PaymentAuthorizationResult> RecoverLinklyAsync(LocalCardPaymentAttempt attempt, PosSessionState session, CancellationToken cancellationToken = default)
        {
            RecoveryCalls++;
            return Task.FromResult(new PaymentAuthorizationResult(
                true,
                Reference: "ANZ:LINKLY-REMOTE",
                AuthorizedAmount: amount,
                CardTransactions: [new CardTransactionDto("ANZ", "LINKLY-REMOTE", null, null, null, null, null, "00", "APPROVED", null, DateTimeOffset.UtcNow, amount, null)],
                Processor: "ANZ"));
        }

        public Task<PaymentAuthorizationResult> RecoverSquareAsync(LocalSquarePaymentAttempt attempt, PosSessionState session, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentAuthorizationResult(false, ResultUnknown: true));
    }

    private sealed class RemoteSquareApprovalTerminal(decimal amount) : ICardTerminalClient, IInstallmentTerminalRecoveryClient
    {
        public int AuthorizeCalls { get; private set; }
        public int RecoveryCalls { get; private set; }

        public Task<PaymentAuthorizationResult> AuthorizeAsync(decimal amount, PosSessionState session, CancellationToken cancellationToken = default)
        {
            AuthorizeCalls++;
            return Task.FromResult(new PaymentAuthorizationResult(false));
        }

        public Task<PaymentAuthorizationResult> RefundAsync(decimal amount, PosSessionState session, string? originalReference, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentAuthorizationResult(false));

        public Task<PaymentAuthorizationResult> RecoverLinklyAsync(LocalCardPaymentAttempt attempt, PosSessionState session, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentAuthorizationResult(false, ResultUnknown: true));

        public Task<PaymentAuthorizationResult> RecoverSquareAsync(LocalSquarePaymentAttempt attempt, PosSessionState session, CancellationToken cancellationToken = default)
        {
            RecoveryCalls++;
            return Task.FromResult(new PaymentAuthorizationResult(
                true,
                Reference: "SQ:payment-001",
                AuthorizedAmount: amount,
                CardTransactions: [new CardTransactionDto("Square", "payment-001", null, null, null, null, null, null, "COMPLETED", null, DateTimeOffset.UtcNow, amount, null)],
                Processor: "Square"));
        }
    }

    private sealed class SequencedLinklyRefundTerminal(LocalInstallmentOperationRepository repository) : ICardTerminalClient, IIdempotentCardRefundClient
    {
        public int RefundCalls { get; private set; }
        public List<string?> RetryReferences { get; } = [];
        public List<string?> PersistedReferencesAtRetry { get; } = [];
        public List<string?> SubmissionReferences { get; } = [];
        public List<string?> PersistedReferencesAtSubmission { get; } = [];

        public Task<PaymentAuthorizationResult> AuthorizeAsync(decimal amount, PosSessionState session, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentAuthorizationResult(false));

        public Task<PaymentAuthorizationResult> RefundAsync(decimal amount, PosSessionState session, string? originalReference, CancellationToken cancellationToken = default) =>
            RefundAsync(amount, session, originalReference, "legacy", cancellationToken);

        public async Task<PaymentAuthorizationResult> RefundAsync(decimal amount, PosSessionState session, string? originalReference, string? idempotencyKey, CancellationToken cancellationToken = default)
        {
            RefundCalls++;
            SubmissionReferences.Add(idempotencyKey);
            PersistedReferencesAtSubmission.Add(await ReadPersistedReferenceAsync(session, cancellationToken));
            if (RefundCalls <= 2)
            {
                if (RefundCalls == 2)
                {
                    RetryReferences.Add(idempotencyKey);
                    PersistedReferencesAtRetry.Add(await ReadPersistedReferenceAsync(session, cancellationToken));
                }
                return new PaymentAuthorizationResult(false, null, "timeout", ResultUnknown: true);
            }

            RetryReferences.Add(idempotencyKey);
            PersistedReferencesAtRetry.Add(await ReadPersistedReferenceAsync(session, cancellationToken));
            return new PaymentAuthorizationResult(true, $"REFUND:{idempotencyKey}", AuthorizedAmount: amount, CardTransactions: [CreateCardTransaction(amount)], Processor: "Linkly");
        }

        private async Task<string?> ReadPersistedReferenceAsync(PosSessionState session, CancellationToken cancellationToken)
        {
            var operation = Assert.Single(await repository.GetRecoverableAsync(session.StoreCode, cancellationToken));
            return Assert.Single((await repository.GetRefundStepsAsync(operation.OperationGuid, cancellationToken))
                .Where(step => step.Method == PaymentMethodKind.Card)).RefundReference;
        }
    }

    private sealed class CancelledTokenSessionBindingTerminal(ILinklyPaymentAttemptContextAccessor context) : ICardTerminalClient
    {
        public bool BindCalled { get; private set; }

        public async Task<PaymentAuthorizationResult> AuthorizeAsync(decimal amount, PosSessionState session, CancellationToken cancellationToken = default)
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var current = context.Current ?? throw new InvalidOperationException("Linkly attempt context was not available.");
            await current.BindSessionAsync("LINKLY-SESSION-001", "LINKLY-TXN-001", DateTimeOffset.UtcNow, cancelled.Token);
            BindCalled = true;
            return new PaymentAuthorizationResult(false, null, "connection lost", ResultUnknown: true);
        }

        public Task<PaymentAuthorizationResult> RefundAsync(decimal amount, PosSessionState session, string? originalReference, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentAuthorizationResult(false));
    }

    private sealed class NoopVoucherTenderClient : IVoucherTenderClient
    {
        public Task<PaymentAuthorizationResult> RedeemAsync(decimal amount, PosSessionState session, string? voucherCode, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentAuthorizationResult(false));
        public Task<PaymentAuthorizationResult> IssueRefundAsync(decimal amount, PosSessionState session, string orderReference, string idempotencyKey, string? reason = null, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentAuthorizationResult(false));
        public Task<bool> ReleaseAsync(PosSessionState session, string voucherCode, string reservationToken, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class RecordingInstallmentApi : IInstallmentApiClient
    {
        public Exception? AppendException { get; init; }
        public InstallmentAppendPaymentResponse? AppendResponse { get; init; }
        public Exception? CancelException { get; init; }
        public InstallmentCancelResponse? CancelResponse { get; init; }
        public TaskCompletionSource? AppendGate { get; init; }
        public TaskCompletionSource AppendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int AppendCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public InstallmentAppendPaymentRequest? LastAppendRequest { get; private set; }

        public Task<InstallmentCreateResponse> CreateAsync(InstallmentCreateRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async Task<InstallmentAppendPaymentResponse> AppendPaymentAsync(InstallmentAppendPaymentRequest request, CancellationToken cancellationToken = default)
        {
            AppendCalls++;
            LastAppendRequest = request;
            AppendStarted.TrySetResult();
            if (AppendGate is not null) await AppendGate.Task;
            if (AppendException is not null) throw AppendException;
            return AppendResponse ?? throw new NotSupportedException();
        }

        public Task<InstallmentConfirmPickupResponse> ConfirmPickupAsync(InstallmentConfirmPickupRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<InstallmentCancelResponse> CancelAsync(InstallmentCancelRequest request, CancellationToken cancellationToken = default)
        {
            CancelCalls++;
            if (CancelException is not null) return Task.FromException<InstallmentCancelResponse>(CancelException);
            return Task.FromResult(CancelResponse ?? throw new NotSupportedException());
        }

        public Task<InstallmentVoidResponse> VoidAsync(InstallmentVoidRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
