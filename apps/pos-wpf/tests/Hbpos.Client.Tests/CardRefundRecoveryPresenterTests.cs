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

    [Theory]
    [InlineData("projection")]
    [InlineData("command")]
    [InlineData("finalize")]
    public async Task Recover_draft_handoff_failure_rolls_back_owner_locks_page_and_can_retry(string failurePoint)
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var restoredTender = new PaymentTender(
            PaymentMethodKind.Card,
            -12.34m,
            $"CARD_ATTEMPT:{AttemptGuid:D}");
        var recovery = new StubRecoveryService
        {
            RecoverResult = new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.DraftRestored,
                "The draft is ready to continue.",
                RestoredTenders: [restoredTender])
            {
                DraftHandoffKey = new CardRecoveryAttemptKey(CardProcessorKind.Linkly, AttemptGuid)
            }
        };
        var cart = new PosCartService();
        PublishRecoveryOwner(cart);
        var lockChanges = new List<(bool Blocked, string? Message)>();
        var statusMessages = new List<string?>();
        var failFirstHandoff = true;
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier("SUPERVISOR")),
            new RecordingAuditLogger(),
            session,
            setPaymentRecoveryBlocked: (blocked, message) => lockChanges.Add((blocked, message)),
            setStatusMessage: message => statusMessages.Add(message),
            tryApplyCardRecoveryDraft: (_, _, _) =>
            {
                if (failurePoint == "projection" && failFirstHandoff)
                {
                    throw new InvalidOperationException("projection notification failed");
                }

                return true;
            },
            notifyShowCashPaymentCanExecuteChanged: () =>
            {
                if (failurePoint == "command" && failFirstHandoff)
                {
                    throw new InvalidOperationException("command notification failed");
                }
            },
            completeRecoveredDraftHandoffAsync: (attemptKey, _) =>
            {
                if (failurePoint == "finalize" && failFirstHandoff)
                {
                    return Task.FromResult(false);
                }

                return Task.FromResult(cart.CompleteRecoveryPublication(attemptKey));
            },
            cart: cart);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false));

        Assert.Null(cart.RecoveryOwnerAttemptGuid);
        Assert.False(presenter.IsCardRecoveryResultDialogOpen);
        Assert.Contains(lockChanges, change => change.Blocked);
        Assert.DoesNotContain("The draft is ready to continue.", statusMessages);

        failFirstHandoff = false;
        // 真实恢复服务会在重试时从本地 FinalizePending 重新发布精确 owner。
        PublishRecoveryOwner(cart);
        Assert.True(await presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false));

        Assert.Equal(2, recovery.RecoverLatestCallCount);
        Assert.True(presenter.IsCardRecoveryResultDialogOpen);
        Assert.False(lockChanges[^1].Blocked);
    }

    [Theory]
    [InlineData("projection")]
    [InlineData("command")]
    [InlineData("finalize")]
    public async Task Resolve_refund_draft_handoff_failure_rolls_back_owner_and_keeps_supervisor_dialog(string failurePoint)
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
                "The refund draft was restored.",
                RecoveryResult: new CardPaymentRecoveryResult(
                    CardPaymentRecoveryOutcome.DraftRestored,
                    "The refund draft was restored.",
                    RestoredTenders: [restoredTender])
                {
                    DraftHandoffKey = new CardRecoveryAttemptKey(CardProcessorKind.Linkly, AttemptGuid)
                },
                RetryAllowed: true,
                LockRetained: true,
                ResolutionPersisted: true,
                ResolutionApplied: true)
        };
        var cart = new PosCartService();
        PublishRecoveryOwner(cart);
        var lockChanges = new List<(bool Blocked, string? Message)>();
        var failHandoff = true;
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier("SUPERVISOR")),
            new RecordingAuditLogger(),
            session,
            setPaymentRecoveryBlocked: (blocked, message) => lockChanges.Add((blocked, message)),
            tryApplyCardRecoveryDraft: (_, _, _) =>
            {
                if (failurePoint == "projection" && failHandoff)
                {
                    throw new InvalidOperationException("projection notification failed");
                }

                return true;
            },
            notifyShowCashPaymentCanExecuteChanged: () =>
            {
                if (failurePoint == "command" && failHandoff)
                {
                    throw new InvalidOperationException("command notification failed");
                }
            },
            completeRecoveredDraftHandoffAsync: (attemptKey, _) =>
            {
                if (failurePoint == "finalize" && failHandoff)
                {
                    return Task.FromResult(false);
                }

                return Task.FromResult(cart.CompleteRecoveryPublication(attemptKey));
            },
            cart: cart);

        await presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false);
        var dialog = Assert.IsType<CardRecoveryResultDialogViewModel>(presenter.CardRecoveryResultDialog);
        dialog.RefundReference = "BANK-REF-001";
        dialog.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CardRecoveryResultDialogViewModel.RefundResolutionMessage) &&
                dialog.RefundResolutionMessage == "The refund draft was restored.")
            {
                throw new InvalidOperationException("resolution message subscriber failed");
            }
        };

        await presenter.ResolveCardRefundCommand.ExecuteAsync(CardRefundSupervisorDecision.ConfirmRefunded);

        Assert.Null(cart.RecoveryOwnerAttemptGuid);
        Assert.True(presenter.IsCardRecoveryResultDialogOpen);
        Assert.Same(dialog, presenter.CardRecoveryResultDialog);
        Assert.Contains(lockChanges, change => change.Blocked);
        Assert.DoesNotContain("The refund draft was restored.", dialog.RefundResolutionMessage);

        failHandoff = false;
        // 模拟主管路径重试时由持久化恢复状态重新发布同一草稿。
        PublishRecoveryOwner(cart);
        await presenter.ResolveCardRefundCommand.ExecuteAsync(CardRefundSupervisorDecision.ConfirmRefunded);

        Assert.True(presenter.IsCardRecoveryResultDialogOpen);
        Assert.NotSame(dialog, presenter.CardRecoveryResultDialog);
        Assert.Null(presenter.CardRecoveryResultDialog?.RefundDetails);
        Assert.False(lockChanges[^1].Blocked);
    }

    [Fact]
    public async Task Recover_draft_restored_passes_alternative_refund_policy_before_tender_handoff()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var restoredTender = new PaymentTender(
            PaymentMethodKind.Cash,
            -12.34m,
            "RECOVERED-CASH-REFUND");
        var recovery = new StubRecoveryService
        {
            RecoverResult = new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.DraftRestored,
                "Use another refund method.",
                RestoredTenders: [restoredTender])
            {
                RequiresAlternativeRefundMethod = true
            }
        };
        var handoffEvents = new List<string>();
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier("SUPERVISOR")),
            new RecordingAuditLogger(),
            session,
            onCardRecoveryDraftRestored: (tenders, message) =>
            {
                Assert.Equal([restoredTender], tenders);
                Assert.Equal("Use another refund method.", message);
                handoffEvents.Add("draft");
            },
            setAlternativeRefundMethodRequired: required => handoffEvents.Add($"policy:{required}"));

        await presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false);

        Assert.Equal(["policy:True", "draft"], handoffEvents);
    }

    [Fact]
    public async Task Recover_draft_handoff_commits_owner_after_projection_and_command_before_unlock()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var recovery = new StubRecoveryService
        {
            RecoverResult = new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.DraftRestored,
                "The retry draft is ready.")
            {
                DraftHandoffKey = new CardRecoveryAttemptKey(CardProcessorKind.Linkly, AttemptGuid)
            }
        };
        var cart = new PosCartService();
        PublishRecoveryOwner(cart);
        var events = new List<string>();
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier("SUPERVISOR")),
            new RecordingAuditLogger(),
            session,
            setPaymentRecoveryBlocked: (blocked, _) =>
            {
                if (!blocked)
                {
                    events.Add("unlock");
                }
            },
            tryApplyCardRecoveryDraft: (_, _, _) =>
            {
                events.Add("projection");
                return true;
            },
            notifyShowCashPaymentCanExecuteChanged: () => events.Add("command"),
            completeRecoveredDraftHandoffAsync: (attemptKey, _) =>
            {
                events.Add("finalize");
                return Task.FromResult(cart.CompleteRecoveryPublication(attemptKey));
            },
            cart: cart);

        Assert.True(await presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false));

        Assert.Equal(["projection", "command", "finalize", "unlock"], events);
        Assert.Null(cart.RecoveryOwnerAttemptGuid);
    }

    [Fact]
    public async Task Recover_draft_handoff_callback_success_without_owner_release_fails_closed()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var recovery = new StubRecoveryService
        {
            RecoverResult = new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.DraftRestored,
                "The retry draft is ready.")
            {
                DraftHandoffKey = new CardRecoveryAttemptKey(CardProcessorKind.Linkly, AttemptGuid)
            }
        };
        var cart = new PosCartService();
        PublishRecoveryOwner(cart);
        var lockChanges = new List<bool>();
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier("SUPERVISOR")),
            new RecordingAuditLogger(),
            session,
            setPaymentRecoveryBlocked: (blocked, _) => lockChanges.Add(blocked),
            tryApplyCardRecoveryDraft: (_, _, _) => true,
            completeRecoveredDraftHandoffAsync: (_, _) => Task.FromResult(true),
            cart: cart);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false));

        Assert.Null(cart.RecoveryOwnerAttemptKey);
        Assert.Contains(true, lockChanges);
        Assert.DoesNotContain(false, lockChanges);
        Assert.False(presenter.IsCardRecoveryResultDialogOpen);
    }

    [Fact]
    public async Task Recover_draft_unlock_failure_after_durable_finalize_keeps_terminal_owner_released_and_page_locked()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var recovery = new StubRecoveryService
        {
            RecoverResult = new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.DraftRestored,
                "The retry draft is ready.")
            {
                DraftHandoffKey = new CardRecoveryAttemptKey(CardProcessorKind.Linkly, AttemptGuid)
            }
        };
        var cart = new PosCartService();
        PublishRecoveryOwner(cart);
        var lockChanges = new List<(bool Blocked, string? Message)>();
        var statusMessages = new List<string?>();
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier("SUPERVISOR")),
            new RecordingAuditLogger(),
            session,
            setPaymentRecoveryBlocked: (blocked, message) =>
            {
                if (!blocked)
                {
                    throw new InvalidOperationException("unlock notification failed");
                }

                lockChanges.Add((blocked, message));
            },
            setStatusMessage: message => statusMessages.Add(message),
            tryApplyCardRecoveryDraft: (_, _, _) => true,
            completeRecoveredDraftHandoffAsync: (attemptKey, _) =>
                Task.FromResult(cart.CompleteRecoveryPublication(attemptKey)),
            cart: cart);

        Assert.True(await presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false));

        Assert.Null(cart.RecoveryOwnerAttemptGuid);
        Assert.False(presenter.IsCardRecoveryResultDialogOpen);
        Assert.NotEmpty(lockChanges);
        Assert.True(lockChanges[^1].Blocked);
        Assert.Contains(
            statusMessages,
            message => message?.Contains(
                "The recovery was committed, but the payment page could not be unlocked safely.",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Recover_draft_unlock_failure_before_owner_release_rolls_back_exact_publication()
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        var recovery = new StubRecoveryService
        {
            RecoverResult = new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.DraftRestored,
                "Complete the alternative refund.")
        };
        var cart = new PosCartService();
        PublishRecoveryOwner(cart);
        var lockChanges = new List<bool>();
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier("SUPERVISOR")),
            new RecordingAuditLogger(),
            session,
            setPaymentRecoveryBlocked: (blocked, _) =>
            {
                if (!blocked)
                {
                    throw new InvalidOperationException("unlock notification failed");
                }

                lockChanges.Add(blocked);
            },
            tryApplyCardRecoveryDraft: (_, _, _) => true,
            // FAILED/REJECTED 替代退款的 owner 必须留给订单落库，不在 UI handoff 中释放。
            completeRecoveredDraftHandoffAsync: (_, _) => Task.FromResult(true),
            cart: cart);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false));

        Assert.Null(cart.RecoveryOwnerAttemptGuid);
        Assert.NotEmpty(lockChanges);
        Assert.True(lockChanges[^1]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Recover_draft_unlock_fatal_exception_propagates_after_durable_finalize(bool outOfMemory)
    {
        var session = CreateSession(CreateCashier("REQUESTER"));
        Exception fatal = outOfMemory
            ? new OutOfMemoryException("fatal unlock failure")
            : new StackOverflowException("fatal unlock failure");
        var recovery = new StubRecoveryService
        {
            RecoverResult = new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.DraftRestored,
                "The retry draft is ready.")
            {
                DraftHandoffKey = new CardRecoveryAttemptKey(CardProcessorKind.Linkly, AttemptGuid)
            }
        };
        var cart = new PosCartService();
        PublishRecoveryOwner(cart);
        var presenter = CreatePresenter(
            recovery,
            new StubOperationAuthorizationService(CreateCashier("SUPERVISOR")),
            new RecordingAuditLogger(),
            session,
            setPaymentRecoveryBlocked: (blocked, _) =>
            {
                if (!blocked)
                {
                    throw fatal;
                }
            },
            tryApplyCardRecoveryDraft: (_, _, _) => true,
            completeRecoveredDraftHandoffAsync: (attemptKey, _) =>
                Task.FromResult(cart.CompleteRecoveryPublication(attemptKey)),
            cart: cart);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(
            () => presenter.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false));

        Assert.Same(fatal, thrown);
        Assert.Null(cart.RecoveryOwnerAttemptGuid);
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
        Assert.Contains(lockChanges, change => change.Blocked);
        Assert.False(lockChanges[^1].Blocked);
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
    public async Task Resolve_payment_continue_waiting_keeps_dialog_and_payment_page_locked()
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
        Assert.NotEmpty(lockChanges);
        Assert.True(lockChanges[^1].Blocked);
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
    public async Task Active_session_unknown_exposes_supervisor_resolution_and_keeps_payment_page_locked()
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
        Assert.NotEmpty(lockChanges);
        Assert.True(lockChanges[^1].Blocked);
    }

    [Fact]
    public async Task Active_session_continue_waiting_keeps_supervisor_dialog_and_payment_page_locked()
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
        Assert.NotEmpty(lockChanges);
        Assert.True(lockChanges[^1].Blocked);

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
        Action<IReadOnlyList<PaymentTender>?, string?>? onCardRecoveryDraftRestored = null,
        Action<bool>? setAlternativeRefundMethodRequired = null,
        Func<bool, IReadOnlyList<PaymentTender>?, string?, bool>? tryApplyCardRecoveryDraft = null,
        Action? notifyShowCashPaymentCanExecuteChanged = null,
        Func<CardRecoveryAttemptKey, CancellationToken, Task<bool>>? completeRecoveredDraftHandoffAsync = null,
        PosCartService? cart = null)
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
            cart: cart ?? new PosCartService(),
            setStatusMessage: setStatusMessage,
            notifyPropertyChanged: notifyPropertyChanged,
            navigateToPaymentOnDraft: navigateToPaymentOnDraft,
            onCardRecoveryDraftRestored: onCardRecoveryDraftRestored,
            tryApplyCardRecoveryDraft: tryApplyCardRecoveryDraft,
            completeRecoveredDraftHandoffAsync: completeRecoveredDraftHandoffAsync,
            getSession: () => session,
            operationAuthorizationService: authorization,
            operationAuditLogger: audit,
            requirePermission: _ => false,
            setPaymentRecoveryBlocked: setPaymentRecoveryBlocked,
            notifyShowCashPaymentCanExecuteChanged: notifyShowCashPaymentCanExecuteChanged,
            setAlternativeRefundMethodRequired: setAlternativeRefundMethodRequired);
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

    private static void PublishRecoveryOwner(PosCartService cart)
    {
        var publication = cart.TryPublishRecoverySnapshot(
            new CardRecoveryAttemptKey(CardProcessorKind.Linkly, AttemptGuid),
            cart.Revision,
            new PosCartService().CreateSnapshot());
        Assert.True(publication.Succeeded);
        Assert.Equal(AttemptGuid, cart.RecoveryOwnerAttemptGuid);
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

        public int RecoverLatestCallCount { get; private set; }

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
            RecoverLatestCallCount++;
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
