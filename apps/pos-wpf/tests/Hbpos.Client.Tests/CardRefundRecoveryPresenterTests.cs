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

namespace Hbpos.Client.Tests;

public sealed class CardRefundRecoveryPresenterTests
{
    private static readonly Guid AttemptGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid OperationGuid = Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa");

    [Fact]
    public async Task Resolve_refund_requires_supervisor_permission_and_records_authorized_evidence_audit()
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
                "The refund remains locked.",
                LockRetained: true)
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
        var savedAudit = Assert.Single(audit.Events);
        Assert.Equal("RETURN_REFUND_COMPLETE", savedAudit.OperationType);
        Assert.Equal("ConfirmNotRefunded", savedAudit.Outcome);
        Assert.Equal(CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded, savedAudit.ReasonCode);
        Assert.Contains("Bank portal has no matching refund", savedAudit.SafeMessage, StringComparison.Ordinal);
        Assert.Equal("REQUESTER", savedAudit.Properties?["requestingCashierId"]);
        Assert.Equal("SUPERVISOR", savedAudit.Properties?["authorizingCashierId"]);
        Assert.Equal(Permissions.PosTerminal.Returns.Confirm, savedAudit.Properties?["permissionCode"]);
        Assert.False(presenter.IsCardRecoveryResultDialogOpen);
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
    public async Task Resolve_payment_uses_audit_permission_and_authorizing_supervisor_identity()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var authorization = new StubOperationAuthorizationService(CreateCashier(
            "SUPERVISOR",
            Permissions.PosTerminal.Audit.View));
        var recovery = new StubRecoveryService
        {
            RecoverResult = CreatePaymentSupervisorRecoveryResult(),
            PaymentResolveResult = new CardPaymentSupervisorResolutionResult(
                true,
                "Payment result saved.")
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
        dialog.RefundReference = "BANK-PAYMENT-001";
        dialog.RefundEvidence = "Bank portal shows approved";
        dialog.RefundSupervisorNote = string.Empty;

        await presenter.ResolveCardPaymentCommand.ExecuteAsync(CardPaymentSupervisorDecision.ConfirmPaid);

        Assert.Equal(Permissions.PosTerminal.Audit.View, authorization.PermissionCode);
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
        Assert.False(presenter.IsCardRecoveryResultDialogOpen);
        Assert.Contains(lockChanges, change => !change.Blocked);
    }

    [Fact]
    public async Task Resolve_payment_continue_waiting_keeps_dialog_and_payment_locked()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var authorization = new StubOperationAuthorizationService(CreateCashier(
            "SUPERVISOR",
            Permissions.PosTerminal.Audit.View));
        var recovery = new StubRecoveryService
        {
            RecoverResult = CreatePaymentSupervisorRecoveryResult(),
            PaymentResolveResult = new CardPaymentSupervisorResolutionResult(
                true,
                "Continue waiting for the bank.",
                LockRetained: true)
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
        Assert.Contains(lockChanges, change => change.Blocked);
        Assert.Equal(CardPaymentSupervisorDecision.ContinueWaiting, recovery.PaymentResolution?.Decision);
    }

    [Fact]
    public async Task Active_session_unknown_exposes_supervisor_resolution_and_close_keeps_payment_locked()
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
                Permissions.PosTerminal.Audit.View)),
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
        Assert.Contains(lockChanges, change => change.Blocked);
    }

    [Fact]
    public async Task Active_session_continue_waiting_keeps_supervisor_dialog_and_payment_locked()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var recovery = new StubRecoveryService
        {
            ActiveSessionResult = CreatePaymentSupervisorRecoveryResult(),
            PaymentResolveResult = new CardPaymentSupervisorResolutionResult(
                true,
                "Continue waiting for the active session.",
                LockRetained: true)
        };
        var lockChanges = new List<(bool Blocked, string? Message)>();
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier(
                "SUPERVISOR",
                Permissions.PosTerminal.Audit.View)),
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
        Assert.Contains(lockChanges, change => change.Blocked);

        presenter.CloseCardRecoveryResultDialogCommand.Execute(null);
        Assert.False(await recoveryTask);
    }

    private static CardRecoveryPresenter CreatePresenter(
        ICardPaymentRecoveryService recovery,
        IOperationAuthorizationService authorization,
        IOperationAuditLogger audit,
        PosSessionState session,
        Action<bool, string?>? setPaymentRecoveryBlocked = null)
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
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ActiveSessionResult ?? CardPaymentRecoveryResult.None);

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
}
