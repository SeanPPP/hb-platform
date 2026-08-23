using System.ComponentModel;
using System.Globalization;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Contracts.Cashiers;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Tests;

public sealed class CardRefundRecoveryPresenterTests
{
    private static readonly Guid AttemptGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid OperationGuid = Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa");

    [Fact]
    public async Task Resolve_refund_requires_supervisor_permission_without_duplicate_presenter_audit()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var authorization = new StubOperationAuthorizationService(CreateCashier(
            "SUPERVISOR",
            Permissions.PosTerminal.Returns.Confirm));
        var audit = new RecordingAuditLogger();
        var recovery = new StubRecoveryService
        {
            ResolveResult = new CardRefundSupervisorResolutionResult(
                true,
                "The return is ready to retry.",
                LockRetained: false,
                ResolutionPersisted: true,
                ResolutionApplied: true)
        };
        var presenter = CreatePresenter(recovery, authorization, audit, session);

        await presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false);
        var dialog = Assert.IsType<CardRecoveryResultDialogViewModel>(presenter.CardRecoveryResultDialog);
        dialog.RefundEvidence = "Bank portal has no matching refund";
        dialog.RefundSupervisorNote = "Settlement report checked";

        await presenter.ResolveCardRefundCommand.ExecuteAsync(CardRefundSupervisorDecision.ConfirmNotRefunded);

        Assert.Equal(Permissions.PosTerminal.Returns.Confirm, authorization.PermissionCode);
        Assert.Equal("card-recovery", authorization.Screen);
        Assert.Equal("resolve-refund/confirmnotrefunded", authorization.Action);
        Assert.NotNull(recovery.Resolution);
        Assert.Equal("Bank portal has no matching refund", recovery.Resolution.Evidence);
        Assert.Equal(CardRefundSupervisorDecision.ConfirmNotRefunded, recovery.Resolution.Decision);
        Assert.Empty(audit.Events);
        Assert.False(presenter.IsCardRecoveryResultDialogOpen);
    }

    [Fact]
    public async Task Resolve_refund_does_not_emit_duplicate_completion_audit_from_presenter()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var audit = new RecordingAuditLogger();
        var recovery = new StubRecoveryService
        {
            ResolveResult = new CardRefundSupervisorResolutionResult(
                true,
                "The return is ready to retry.",
                ResolutionPersisted: true,
                ResolutionApplied: true)
        };
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier(
                "SUPERVISOR",
                Permissions.PosTerminal.Returns.Confirm)),
            audit,
            session);

        await presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false);
        await presenter.ResolveCardRefundCommand.ExecuteAsync(CardRefundSupervisorDecision.ConfirmNotRefunded);

        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Resolve_refund_does_not_mutate_service_when_supervisor_authorization_is_denied()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var authorization = new StubOperationAuthorizationService(authorizer: null);
        var recovery = new StubRecoveryService();
        var presenter = CreatePresenter(recovery, authorization, new RecordingAuditLogger(), session);

        await presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false);
        var dialog = Assert.IsType<CardRecoveryResultDialogViewModel>(presenter.CardRecoveryResultDialog);
        dialog.RefundSupervisorNote = "Keep locked";

        await presenter.ResolveCardRefundCommand.ExecuteAsync(CardRefundSupervisorDecision.ContinueWaiting);

        Assert.Null(recovery.Resolution);
        Assert.True(presenter.IsCardRecoveryResultDialogOpen);
        Assert.False(string.IsNullOrWhiteSpace(dialog.RefundResolutionMessage));
    }

    [Fact]
    public async Task Resolve_refund_does_not_emit_completion_audit_when_finalization_is_pending()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var authorization = new StubOperationAuthorizationService(CreateCashier(
            "SUPERVISOR",
            Permissions.PosTerminal.Returns.Confirm));
        var audit = new RecordingAuditLogger();
        var recovery = new StubRecoveryService
        {
            ResolveResult = new CardRefundSupervisorResolutionResult(
                false,
                "The supervisor decision was saved, but finalization is still pending.",
                LockRetained: true,
                ResolutionPersisted: true,
                ResolutionApplied: true)
        };
        var presenter = CreatePresenter(recovery, authorization, audit, session);

        await presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false);
        await presenter.ResolveCardRefundCommand.ExecuteAsync(CardRefundSupervisorDecision.ContinueWaiting);

        Assert.Empty(audit.Events);
        Assert.True(presenter.IsCardRecoveryResultDialogOpen);
    }

    [Fact]
    public async Task Resolve_refund_applies_restored_draft_when_finalization_lock_is_retained()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var restoredTender = new PaymentTender(
            PaymentMethodKind.Card,
            -12.34m,
            $"CARD_ATTEMPT:{AttemptGuid:D}");
        var recovery = new StubRecoveryService
        {
            ResolveResult = new CardRefundSupervisorResolutionResult(
                true,
                "The refund draft was restored; save the order to finish recovery.",
                RecoveryResult: new CardPaymentRecoveryResult(
                    CardPaymentRecoveryOutcome.DraftRestored,
                    "The refund draft was restored; save the order to finish recovery.",
                    RestoredTenders: [restoredTender]),
                RetryAllowed: true,
                LockRetained: true,
                ResolutionPersisted: true,
                ResolutionApplied: true)
        };
        var navigated = false;
        IReadOnlyList<PaymentTender>? appliedTenders = null;
        string? appliedMessage = null;
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier(
                "SUPERVISOR",
                Permissions.PosTerminal.Returns.Confirm)),
            new RecordingAuditLogger(),
            session,
            navigateToPaymentOnDraft: () =>
            {
                navigated = true;
                return Task.CompletedTask;
            },
            onCardRecoveryDraftRestored: (tenders, message) =>
            {
                appliedTenders = tenders;
                appliedMessage = message;
            });

        await presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false);
        var dialog = Assert.IsType<CardRecoveryResultDialogViewModel>(presenter.CardRecoveryResultDialog);
        dialog.RefundReference = "BANK-REFUND-001";

        await presenter.ResolveCardRefundCommand.ExecuteAsync(CardRefundSupervisorDecision.ConfirmRefunded);

        Assert.True(navigated);
        Assert.Equal([restoredTender], appliedTenders);
        Assert.Equal(recovery.ResolveResult.Message, appliedMessage);
        Assert.True(presenter.IsCardRecoveryResultDialogOpen);
        Assert.Null(presenter.CardRecoveryResultDialog?.RefundDetails);
    }

    [Fact]
    public async Task Resolve_payment_uses_payment_confirm_permission_without_duplicate_presenter_audit()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var authorization = new StubOperationAuthorizationService(CreateCashier(
            "SUPERVISOR",
            Permissions.PosTerminal.Payment.Confirm));
        var audit = new RecordingAuditLogger();
        var recovery = new StubRecoveryService
        {
            RecoverResult = CreatePaymentSupervisorRecoveryResult(),
            PaymentResolveResult = new CardPaymentSupervisorResolutionResult(
                true,
                "Payment result saved.",
                ResolutionPersisted: true,
                ResolutionApplied: true)
        };
        var lockChanges = new List<(bool Blocked, string? Message)>();
        var presenter = CreatePresenter(
            recovery,
            authorization,
            audit,
            session,
            (blocked, message) => lockChanges.Add((blocked, message)));

        await presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false);
        var dialog = Assert.IsType<CardRecoveryResultDialogViewModel>(presenter.CardRecoveryResultDialog);
        dialog.RefundReference = "BANK-PAYMENT-001";
        dialog.RefundEvidence = "Bank portal shows approved";
        dialog.RefundSupervisorNote = string.Empty;

        await presenter.ResolveCardPaymentCommand.ExecuteAsync(CardPaymentSupervisorDecision.ConfirmPaid);

        Assert.Equal(Permissions.PosTerminal.Payment.Confirm, authorization.PermissionCode);
        Assert.Equal("card-recovery", authorization.Screen);
        Assert.Equal("resolve-payment/confirmpaid", authorization.Action);
        var resolution = Assert.IsType<CardPaymentSupervisorResolution>(recovery.PaymentResolution);
        Assert.Equal(CardPaymentSupervisorDecision.ConfirmPaid, resolution.Decision);
        Assert.Equal("SUPERVISOR", resolution.OperatorCashierId);
        Assert.Equal("USER-SUPERVISOR", resolution.OperatorUserGuid);
        Assert.Equal("SUPERVISOR", resolution.OperatorName);
        Assert.Equal("BANK-PAYMENT-001", resolution.PaymentReference);
        Assert.Equal("Bank portal shows approved", resolution.Evidence);
        Assert.Equal(string.Empty, resolution.Reason);
        Assert.Empty(audit.Events);
        Assert.False(presenter.IsCardRecoveryResultDialogOpen);
        Assert.Empty(lockChanges);
    }

    [Fact]
    public async Task Resolve_payment_does_not_emit_completion_audit_when_finalization_is_pending()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var authorization = new StubOperationAuthorizationService(CreateCashier(
            "SUPERVISOR",
            Permissions.PosTerminal.Payment.Confirm));
        var audit = new RecordingAuditLogger();
        var recovery = new StubRecoveryService
        {
            RecoverResult = CreatePaymentSupervisorRecoveryResult(),
            PaymentResolveResult = new CardPaymentSupervisorResolutionResult(
                false,
                "The supervisor decision was saved, but finalization is still pending.",
                LockRetained: true,
                ResolutionPersisted: true,
                ResolutionApplied: true)
        };
        var presenter = CreatePresenter(recovery, authorization, audit, session);

        await presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false);
        await presenter.ResolveCardPaymentCommand.ExecuteAsync(CardPaymentSupervisorDecision.ConfirmNotPaid);

        Assert.Empty(audit.Events);
        Assert.True(presenter.IsCardRecoveryResultDialogOpen);
    }

    [Fact]
    public async Task Resolve_payment_does_not_audit_requested_decision_when_another_resolution_won_cas()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var recovery = new StubRecoveryService
        {
            RecoverResult = CreatePaymentSupervisorRecoveryResult(),
            PaymentResolveResult = new CardPaymentSupervisorResolutionResult(
                true,
                "A newer supervisor decision was retained.",
                LockRetained: false,
                ResolutionPersisted: true,
                ResolutionApplied: false)
        };
        var audit = new RecordingAuditLogger();
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier(
                "SUPERVISOR",
                Permissions.PosTerminal.Payment.Confirm)),
            audit,
            session);

        await presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false);
        await presenter.ResolveCardPaymentCommand.ExecuteAsync(CardPaymentSupervisorDecision.ConfirmPaid);

        Assert.Empty(audit.Events);
        Assert.False(presenter.IsCardRecoveryResultDialogOpen);
    }

    [Fact]
    public async Task Resolve_refund_does_not_audit_requested_decision_when_another_resolution_won_cas()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var recovery = new StubRecoveryService
        {
            ResolveResult = new CardRefundSupervisorResolutionResult(
                true,
                "A newer supervisor decision was retained.",
                LockRetained: false,
                ResolutionPersisted: true,
                ResolutionApplied: false)
        };
        var audit = new RecordingAuditLogger();
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier(
                "SUPERVISOR",
                Permissions.PosTerminal.Returns.Confirm)),
            audit,
            session);

        await presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false);
        await presenter.ResolveCardRefundCommand.ExecuteAsync(CardRefundSupervisorDecision.ConfirmRefunded);

        Assert.Empty(audit.Events);
        Assert.False(presenter.IsCardRecoveryResultDialogOpen);
    }

    [Fact]
    public async Task Resolve_payment_closes_dialog_when_persisted_resolution_reached_terminal_without_draft()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var recovery = new StubRecoveryService
        {
            RecoverResult = CreatePaymentSupervisorRecoveryResult(),
            PaymentResolveResult = new CardPaymentSupervisorResolutionResult(
                false,
                "The unpaid result was finalized; continue the current order.",
                RecoveryResult: new CardPaymentRecoveryResult(
                    CardPaymentRecoveryOutcome.ActiveSessionNotPaid,
                    "The unpaid result was finalized; continue the current order."),
                LockRetained: false,
                ResolutionPersisted: true,
                ResolutionApplied: true)
        };
        var audit = new RecordingAuditLogger();
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier(
                "SUPERVISOR",
                Permissions.PosTerminal.Payment.Confirm)),
            audit,
            session);

        await presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false);
        await presenter.ResolveCardPaymentCommand.ExecuteAsync(CardPaymentSupervisorDecision.ConfirmNotPaid);

        Assert.False(presenter.IsCardRecoveryResultDialogOpen);
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Resolve_payment_post_commit_status_callback_failure_does_not_reopen_or_misreport_resolution()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var recovery = new StubRecoveryService
        {
            RecoverResult = CreatePaymentSupervisorRecoveryResult(),
            PaymentResolveResult = new CardPaymentSupervisorResolutionResult(
                true,
                "Payment result saved.",
                ResolutionPersisted: true,
                ResolutionApplied: true)
        };
        var audit = new RecordingAuditLogger();
        var failStatusCallback = false;
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier(
                "SUPERVISOR",
                Permissions.PosTerminal.Payment.Confirm)),
            audit,
            session,
            setStatusMessage: _ =>
            {
                if (failStatusCallback)
                {
                    throw new InvalidOperationException("subscriber failed");
                }
            });

        await presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false);
        failStatusCallback = true;
        Action<string> throwingLogSubscriber = line =>
        {
            if (line.Contains(
                    $"post-commit action failed context=payment resolution status attemptGuid={AttemptGuid:D}",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("diagnostic subscriber failed");
            }
        };
        ConsoleLog.LineWritten += throwingLogSubscriber;
        try
        {
            await presenter.ResolveCardPaymentCommand.ExecuteAsync(CardPaymentSupervisorDecision.ConfirmPaid);
        }
        finally
        {
            ConsoleLog.LineWritten -= throwingLogSubscriber;
        }

        Assert.False(presenter.IsCardRecoveryResultDialogOpen);
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Resolve_payment_does_not_depend_on_presenter_audit_logger()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var recovery = new StubRecoveryService
        {
            RecoverResult = CreatePaymentSupervisorRecoveryResult(),
            PaymentResolveResult = new CardPaymentSupervisorResolutionResult(
                true,
                "Payment result saved.",
                ResolutionPersisted: true,
                ResolutionApplied: true)
        };
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier(
                "SUPERVISOR",
                Permissions.PosTerminal.Payment.Confirm)),
            new ThrowingAuditLogger(new OutOfMemoryException("fatal audit failure")),
            session);

        await presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false);

        await presenter.ResolveCardPaymentCommand.ExecuteAsync(CardPaymentSupervisorDecision.ConfirmPaid);

        Assert.False(presenter.IsCardRecoveryResultDialogOpen);
    }

    [Fact]
    public async Task Resolve_payment_continue_waiting_keeps_dialog_without_global_payment_lock()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var authorization = new StubOperationAuthorizationService(CreateCashier(
            "SUPERVISOR",
            Permissions.PosTerminal.Payment.Confirm));
        var recovery = new StubRecoveryService
        {
            RecoverResult = CreatePaymentSupervisorRecoveryResult(),
            PaymentResolveResult = new CardPaymentSupervisorResolutionResult(
                true,
                "Continue waiting for the bank.",
                LockRetained: true,
                ResolutionPersisted: true,
                ResolutionApplied: true)
        };
        var lockChanges = new List<(bool Blocked, string? Message)>();
        var presenter = CreatePresenter(
            recovery,
            authorization,
            new RecordingAuditLogger(),
            session,
            (blocked, message) => lockChanges.Add((blocked, message)));

        await presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false);
        var dialog = Assert.IsType<CardRecoveryResultDialogViewModel>(presenter.CardRecoveryResultDialog);
        dialog.RefundSupervisorNote = "Settlement is not available yet";

        await presenter.ResolveCardPaymentCommand.ExecuteAsync(CardPaymentSupervisorDecision.ContinueWaiting);

        Assert.True(presenter.IsCardRecoveryResultDialogOpen);
        Assert.Equal("Continue waiting for the bank.", dialog.RefundResolutionMessage);
        Assert.Empty(lockChanges);
        Assert.Equal(CardPaymentSupervisorDecision.ContinueWaiting, recovery.PaymentResolution?.Decision);
    }

    [Fact]
    public async Task Resolve_payment_continue_waiting_does_not_emit_false_sale_completion_audit()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var audit = new RecordingAuditLogger();
        var recovery = new StubRecoveryService
        {
            RecoverResult = CreatePaymentSupervisorRecoveryResult(),
            PaymentResolveResult = new CardPaymentSupervisorResolutionResult(
                false,
                "The payment remains pending.",
                LockRetained: true,
                ResolutionPersisted: true,
                ResolutionApplied: true)
        };
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier(
                "SUPERVISOR",
                Permissions.PosTerminal.Payment.Confirm)),
            audit,
            session);

        await presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false);
        await presenter.ResolveCardPaymentCommand.ExecuteAsync(CardPaymentSupervisorDecision.ContinueWaiting);

        Assert.Empty(audit.Events);
        Assert.True(presenter.IsCardRecoveryResultDialogOpen);
    }

    [Fact]
    public async Task Active_session_unknown_exposes_supervisor_resolution_and_close_without_global_payment_lock()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var recovery = new StubRecoveryService
        {
            ActiveSessionResult = CreatePaymentSupervisorRecoveryResult()
        };
        var lockChanges = new List<(bool Blocked, string? Message)>();
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier(
                "SUPERVISOR",
            Permissions.PosTerminal.Payment.Confirm)),
            new RecordingAuditLogger(),
            session,
            (blocked, message) => lockChanges.Add((blocked, message)));

        var recoveryTask = presenter.RecoverActiveCardPaymentSessionFromPaymentAsync();
        await WaitUntilAsync(() => presenter.IsCardRecoveryResultDialogOpen);

        var dialog = Assert.IsType<CardRecoveryResultDialogViewModel>(presenter.CardRecoveryResultDialog);
        Assert.True(dialog.CanResolvePayment);
        Assert.NotNull(dialog.PaymentSupervisorDetails);

        presenter.CloseCardRecoveryResultDialogCommand.Execute(null);

        Assert.False(await recoveryTask);
        Assert.Empty(lockChanges);
    }

    [Fact]
    public async Task Active_session_continue_waiting_keeps_supervisor_dialog_without_global_payment_lock()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var recovery = new StubRecoveryService
        {
            ActiveSessionResult = CreatePaymentSupervisorRecoveryResult(),
            PaymentResolveResult = new CardPaymentSupervisorResolutionResult(
                true,
                "Continue waiting for the active session.",
                LockRetained: true,
                ResolutionPersisted: true,
                ResolutionApplied: true)
        };
        var lockChanges = new List<(bool Blocked, string? Message)>();
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier(
                "SUPERVISOR",
                Permissions.PosTerminal.Payment.Confirm)),
            new RecordingAuditLogger(),
            session,
            (blocked, message) => lockChanges.Add((blocked, message)));

        var recoveryTask = presenter.RecoverActiveCardPaymentSessionFromPaymentAsync();
        await WaitUntilAsync(() => presenter.CardRecoveryResultDialog?.CanResolvePayment == true);
        var dialog = Assert.IsType<CardRecoveryResultDialogViewModel>(presenter.CardRecoveryResultDialog);
        dialog.RefundSupervisorNote = "Bank settlement is still unavailable";

        await presenter.ResolveCardPaymentCommand.ExecuteAsync(CardPaymentSupervisorDecision.ContinueWaiting);

        Assert.True(presenter.IsCardRecoveryResultDialogOpen);
        Assert.False(recoveryTask.IsCompleted);
        Assert.Equal("Continue waiting for the active session.", dialog.RefundResolutionMessage);
        Assert.Empty(lockChanges);

        presenter.CloseCardRecoveryResultDialogCommand.Execute(null);
        Assert.False(await recoveryTask);
    }

    [Fact]
    public async Task Active_session_terminal_resolution_completes_waiter_when_close_subscriber_fails()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var recovery = new StubRecoveryService
        {
            ActiveSessionResult = CreatePaymentSupervisorRecoveryResult(),
            PaymentResolveResult = new CardPaymentSupervisorResolutionResult(
                true,
                "Payment result finalized.",
                LockRetained: false,
                ResolutionPersisted: true,
                ResolutionApplied: true)
        };
        var failNotification = false;
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier(
                "SUPERVISOR",
                Permissions.PosTerminal.Payment.Confirm)),
            new RecordingAuditLogger(),
            session,
            notifyPropertyChanged: _ =>
            {
                if (failNotification)
                {
                    throw new InvalidOperationException("property subscriber failed");
                }
            });

        var recoveryTask = presenter.RecoverActiveCardPaymentSessionFromPaymentAsync();
        await WaitUntilAsync(() => presenter.CardRecoveryResultDialog?.CanResolvePayment == true);
        failNotification = true;

        await presenter.ResolveCardPaymentCommand.ExecuteAsync(CardPaymentSupervisorDecision.ConfirmPaid);

        Assert.False(await recoveryTask.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.False(presenter.IsCardRecoveryResultDialogOpen);
    }

    [Fact]
    public async Task Active_session_retry_releases_waiter_when_close_subscriber_throws_fatal_exception()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var recovery = new StubRecoveryService
        {
            ActiveSessionResult = CreatePaymentSupervisorRecoveryResult()
        };
        var failNextNotification = false;
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier(
                "SUPERVISOR",
                Permissions.PosTerminal.Payment.Confirm)),
            new RecordingAuditLogger(),
            session,
            notifyPropertyChanged: _ =>
            {
                if (failNextNotification)
                {
                    failNextNotification = false;
                    throw new OutOfMemoryException("fatal property subscriber failure");
                }
            });

        var recoveryTask = presenter.RecoverActiveCardPaymentSessionFromPaymentAsync();
        await WaitUntilAsync(() => presenter.IsCardRecoveryResultDialogOpen);
        failNextNotification = true;

        Assert.Throws<OutOfMemoryException>(
            () => presenter.RetryActiveSessionRecoveryCommand.Execute(null));

        await WaitUntilAsync(() => recovery.ActiveSessionCallCount >= 2);
        await WaitUntilAsync(() => presenter.IsCardRecoveryResultDialogOpen);
        presenter.CloseCardRecoveryResultDialogCommand.Execute(null);

        Assert.False(await recoveryTask.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    private static CardRecoveryPresenter CreatePresenter(
        ICardPaymentRecoveryService recovery,
        IOperationAuthorizationService authorization,
        IOperationAuditLogger audit,
        PosSessionState session,
        Action<bool, string?>? setPaymentRecoveryBlocked = null,
        Action<string?>? setStatusMessage = null,
        Action<string>? notifyPropertyChanged = null,
        Func<Task>? navigateToPaymentOnDraft = null,
        Action<IReadOnlyList<PaymentTender>?, string?>? onCardRecoveryDraftRestored = null)
    {
        return new CardRecoveryPresenter(
            recovery,
            new CardRecoveryResultDialogService(),
            receiptQueryService: null!,
            receiptPrinterSettingsStore: null,
            receiptTextFormatter: null!,
            localization: new LocalizationService(),
            linklyFallbackPromptCoordinator: null,
            linklyBankReceiptPrinter: null,
            mainChildViewModelFactory: null!,
            cart: new PosCartService(),
            setStatusMessage: setStatusMessage,
            notifyPropertyChanged: notifyPropertyChanged,
            navigateToPaymentOnDraft: navigateToPaymentOnDraft,
            onCardRecoveryDraftRestored: onCardRecoveryDraftRestored,
            getSession: () => session,
            operationAuthorizationService: authorization,
            operationAuditLogger: audit,
            requirePermission: _ => false,
            setPaymentRecoveryBlocked: setPaymentRecoveryBlocked);
    }

    private static CardPaymentRecoveryResult CreatePaymentSupervisorRecoveryResult() =>
        new(
            CardPaymentRecoveryOutcome.Unknown,
            "Payment result requires supervisor reconciliation.",
            DialogDetails: new CardPaymentRecoveryDialogDetails(
                SessionId: "SESSION-001",
                TxnRef: "TXN-001",
                ResponseCode: null,
                ResponseText: null,
                Amount: 12.34m,
                Timestamp: DateTimeOffset.UtcNow),
            PaymentSupervisorDetails: new CardPaymentSupervisorDetails(
                AttemptGuid,
                CardProcessorKind.Linkly,
                "SESSION-001",
                OperationGuid,
                LocalCardPaymentAttemptStatus.RequiresReview,
                DateTimeOffset.UtcNow));

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow >= timeoutAt)
            {
                throw new TimeoutException("Timed out waiting for the presenter state.");
            }

            await Task.Delay(10);
        }
    }

    private static PosSessionState CreateSession(CashierSessionDto cashier) =>
        new(
            "HB POS",
            "S001",
            "Main Store",
            "POS-01",
            cashier.CashierId,
            cashier.CashierName,
            true,
            0,
            cashier);

    private static CashierSessionDto CreateCashier(string cashierId, params string[] permissions) =>
        new(
            cashierId,
            $"USER-{cashierId}",
            cashierId,
            "S001",
            "POS-01",
            [],
            permissions,
            ["S001"],
            IsSuperAdmin: false,
            IsOfflineCached: false,
            IsEmergencyOverride: false,
            AuthorizationToken: $"ticket-{cashierId}",
            AuthorizationExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1));

    private sealed class StubRecoveryService : ICardPaymentRecoveryService
    {
        public CardRefundSupervisorResolution? Resolution { get; private set; }

        public CardPaymentSupervisorResolution? PaymentResolution { get; private set; }

        public CardPaymentRecoveryResult? RecoverResult { get; init; }

        public CardPaymentRecoveryResult? ActiveSessionResult { get; init; }

        public int ActiveSessionCallCount { get; private set; }

        public CardRefundSupervisorResolutionResult ResolveResult { get; init; } =
            new(true, "Saved.", LockRetained: true);

        public CardPaymentSupervisorResolutionResult PaymentResolveResult { get; init; } =
            new(true, "Saved.");

        public Task<CardPaymentRecoveryResult> RecoverLatestAsync(
            PosCartService cart,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            if (RecoverResult is not null)
            {
                return Task.FromResult(RecoverResult);
            }

            return Task.FromResult(new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                "Refund requires supervisor reconciliation.",
                DialogDetails: new CardPaymentRecoveryDialogDetails(
                    SessionId: null,
                    TxnRef: "txn-refund-1",
                    ResponseCode: null,
                    ResponseText: null,
                    Amount: 12.34m,
                    Timestamp: DateTimeOffset.UtcNow),
                RefundDetails: new CardRefundRecoveryDetails(
                    AttemptGuid,
                    CardProcessorKind.Linkly,
                    OperationGuid,
                    12.34m,
                    "ANZ:SALE-1")));
        }

        public Task<CardRefundSupervisorResolutionResult> ResolveRefundAsync(
            CardRefundSupervisorResolution resolution,
            PosCartService cart,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            Resolution = resolution;
            return Task.FromResult(ResolveResult);
        }

        public Task<CardPaymentSupervisorResolutionResult> ResolvePaymentAsync(
            CardPaymentSupervisorResolution resolution,
            PosCartService cart,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            PaymentResolution = resolution;
            return Task.FromResult(PaymentResolveResult);
        }

        public Task<CardPaymentRecoveryResult> RecoverActiveSessionAsync(
            PosCartService cart,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            ActiveSessionCallCount++;
            return Task.FromResult(ActiveSessionResult ?? CardPaymentRecoveryResult.None);
        }

        public Task<CardPaymentRecoveryResult> ManuallyClearActiveSessionAsync(
            string sessionId,
            PosSessionState session,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CardPaymentRecoveryResult.None);
    }

    private sealed class StubOperationAuthorizationService(CashierSessionDto? authorizer)
        : IOperationAuthorizationService
    {
        public string ScannerPageId => "operation-authorization";

        public bool IsPromptOpen => false;

        public bool IsBusy => false;

        public string PromptMessage => string.Empty;

        public string StatusMessage => string.Empty;

        public string PermissionCode { get; private set; } = string.Empty;

        public string Screen { get; private set; } = string.Empty;

        public string Action { get; private set; } = string.Empty;

        public IRelayCommand CancelCommand { get; } = new RelayCommand(() => { });

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public event EventHandler? StatusChanged
        {
            add { }
            remove { }
        }

        public Task<OperationAuthorizationScope?> AuthorizeAsync(
            string permissionCode,
            string screen,
            string action,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            PermissionCode = permissionCode;
            Screen = screen;
            Action = action;
            if (authorizer is null || session.CashierSession is null)
            {
                return Task.FromResult<OperationAuthorizationScope?>(null);
            }

            var scope = new OperationAuthorizationScope(
                session.CashierSession,
                permissionCode,
                screen,
                action);
            scope.SetAuthorizingSession(authorizer);
            return Task.FromResult<OperationAuthorizationScope?>(scope);
        }

        public bool ProcessScannerBarcode(string barcode) => false;

        public void Cancel()
        {
        }

        public void RevokeAll()
        {
        }
    }

    private sealed class RecordingAuditLogger : IOperationAuditLogger
    {
        public List<OperationAuditEventDto> Events { get; } = [];

        public void Record(OperationAuditEventDto auditEvent) => Events.Add(auditEvent);
    }

    private sealed class ThrowingAuditLogger(Exception exception) : IOperationAuditLogger
    {
        public void Record(OperationAuditEventDto auditEvent) => throw exception;
    }
}
