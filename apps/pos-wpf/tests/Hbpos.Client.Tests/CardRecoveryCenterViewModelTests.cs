using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using BlazorApp.Shared.Constants;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Cashiers;

namespace Hbpos.Client.Tests;

public sealed class CardRecoveryCenterViewModelTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-21T10:00:00+10:00", CultureInfo.InvariantCulture);

    [Fact]
    public async Task LoadAsync_authorizes_payment_view_before_exposing_open_attempts()
    {
        var first = CreateQueueItem(
            CardProcessorKind.Linkly,
            Guid.Parse("41000000-0000-0000-0000-000000000001"),
            updatedAt: Now);
        var second = CreateQueueItem(
            CardProcessorKind.Square,
            Guid.Parse("41000000-0000-0000-0000-000000000002"),
            updatedAt: Now.AddMinutes(-1));
        var recovery = new RecordingRecoveryService { OpenItems = [first, second] };
        var authorization = new RecordingAuthorizationService(CreateCashier("SUPERVISOR"));
        var reportedCounts = new List<int>();
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            authorization,
            CreateLocalization(),
            openCountChanged: reportedCounts.Add);

        await viewModel.LoadAsync();

        var request = Assert.Single(authorization.Requests);
        Assert.Equal(Permissions.PosTerminal.Payment.View, request.PermissionCode);
        Assert.Equal("card-recovery-center", request.Screen);
        Assert.Equal("view", request.Action);
        Assert.Equal([first, second], viewModel.OpenAttempts);
        Assert.Same(first, viewModel.SelectedAttempt);
        Assert.Equal([first.Key, second.Key], viewModel.OpenAttemptRows.Select(row => row.Key).ToArray());
        Assert.Equal("Sale", viewModel.OpenAttemptRows[0].OperationTypeText);
        Assert.Equal("Linkly", viewModel.OpenAttemptRows[0].ChannelText);
        Assert.Equal("Requires review", viewModel.OpenAttemptRows[0].StatusText);
        Assert.Equal([2], reportedCounts);
        Assert.Equal("2 card transactions need attention", viewModel.OpenCountText);
    }

    [Fact]
    public async Task LoadAsync_is_idempotent_when_navigation_and_view_loaded_both_request_initial_load()
    {
        var recovery = new RecordingRecoveryService();
        var authorization = new RecordingAuthorizationService(CreateCashier("SUPERVISOR"));
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            authorization,
            CreateLocalization());

        await viewModel.LoadAsync();
        await viewModel.LoadAsync();

        Assert.Equal(1, recovery.ListOpenCallCount);
        Assert.Single(authorization.Requests);
    }

    [Fact]
    public async Task RecoverCommand_targets_selected_key_reuses_cart_and_refreshes_open_count()
    {
        var first = CreateQueueItem(
            CardProcessorKind.Linkly,
            Guid.Parse("42000000-0000-0000-0000-000000000001"),
            updatedAt: Now);
        var selected = CreateQueueItem(
            CardProcessorKind.Square,
            Guid.Parse("42000000-0000-0000-0000-000000000002"),
            updatedAt: Now.AddMinutes(-1));
        var recovery = new RecordingRecoveryService
        {
            OpenItems = [first, selected],
            RecoverResult = new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Checking,
                "Still checking")
        };
        var authorization = new RecordingAuthorizationService(CreateCashier("SUPERVISOR"));
        var cart = CreateNonEmptyCart();
        var reportedCounts = new List<int>();
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            cart,
            CreateSession(),
            authorization,
            CreateLocalization(),
            openCountChanged: reportedCounts.Add);
        await viewModel.LoadAsync();
        viewModel.SelectedAttempt = selected;
        recovery.OpenItems = [first];

        await viewModel.RecoverCommand.ExecuteAsync(null);

        Assert.Equal(selected.Key, recovery.RecoveredKey);
        Assert.Same(cart, recovery.RecoveredCart);
        Assert.False(recovery.CartWasEmptyWhenRecoverCalled);
        Assert.False(cart.IsEmpty);
        Assert.Equal([first], viewModel.OpenAttempts);
        Assert.Same(first, viewModel.SelectedAttempt);
        Assert.Equal([2, 1], reportedCounts);
        Assert.Equal("Still checking", viewModel.StatusMessage);
        Assert.Equal(
            ["view", "recover"],
            authorization.Requests.Select(request => request.Action).ToArray());
        Assert.All(
            authorization.Requests,
            request => Assert.Equal(Permissions.PosTerminal.Payment.View, request.PermissionCode));
    }

    [Fact]
    public async Task RecoverCommand_reports_completed_result_to_shell_after_queue_refresh()
    {
        var selected = CreateQueueItem(
            CardProcessorKind.Linkly,
            Guid.Parse("42000000-0000-0000-0000-000000000003"),
            updatedAt: Now);
        var completed = new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.OrderCompleted,
            "Recovered order completed");
        var recovery = new RecordingRecoveryService
        {
            OpenItems = [selected],
            RecoverResult = completed
        };
        var handled = new List<(CardPaymentRecoveryResult Result, int RemainingCount)>();
        var remainingCount = -1;
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            CreateLocalization(),
            openCountChanged: count => remainingCount = count,
            recoveryResultHandledAsync: result =>
            {
                handled.Add((result, remainingCount));
                return Task.CompletedTask;
            });
        await viewModel.LoadAsync();
        recovery.OpenItems = [];

        await viewModel.RecoverCommand.ExecuteAsync(null);

        var callback = Assert.Single(handled);
        Assert.Same(completed, callback.Result);
        Assert.Equal(0, callback.RemainingCount);
    }

    [Theory]
    [InlineData("Refund", CardRecoverySupervisorDecision.ConfirmProcessed, "resolve/confirm-paid", Permissions.PosTerminal.Returns.Confirm)]
    [InlineData("Refund", CardRecoverySupervisorDecision.ConfirmNotProcessed, "resolve/confirm-not-paid", Permissions.PosTerminal.Returns.Confirm)]
    [InlineData("Sale", CardRecoverySupervisorDecision.ContinueWaiting, "resolve/continue-waiting", Permissions.PosTerminal.Payment.Confirm)]
    [InlineData("ActiveSession", CardRecoverySupervisorDecision.ConfirmProcessed, "resolve/confirm-paid", Permissions.PosTerminal.Payment.Confirm)]
    public async Task Resolve_commands_use_transaction_permission_and_target_selected_key(
        string operationKind,
        CardRecoverySupervisorDecision decision,
        string expectedAction,
        string expectedPermission)
    {
        var selected = CreateQueueItem(
            CardProcessorKind.Linkly,
            Guid.Parse("43000000-0000-0000-0000-000000000001"),
            updatedAt: Now,
            operationKind: operationKind);
        var recovery = new RecordingRecoveryService
        {
            OpenItems = [selected],
            ResolveResult = new CardRecoveryResolutionResult(true, "Resolution saved")
        };
        var authorization = new RecordingAuthorizationService(CreateCashier("SUPERVISOR"));
        var counts = new List<int>();
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            authorization,
            CreateLocalization(),
            openCountChanged: counts.Add);
        await viewModel.LoadAsync();
        authorization.Requests.Clear();
        authorization.IssuedScopes.Clear();
        viewModel.ResolutionReason = "  settlement checked  ";
        viewModel.ResolutionEvidence = "  bank portal evidence  ";
        viewModel.ResolutionReference = "  BANK-REF-1  ";
        recovery.OpenItems = [];

        var command = decision switch
        {
            CardRecoverySupervisorDecision.ConfirmProcessed => viewModel.ConfirmPaidCommand,
            CardRecoverySupervisorDecision.ConfirmNotProcessed => viewModel.ConfirmNotPaidCommand,
            _ => viewModel.ContinueWaitingCommand
        };
        await command.ExecuteAsync(null);

        var request = Assert.Single(authorization.Requests);
        Assert.Equal(expectedPermission, request.PermissionCode);
        Assert.Equal("card-recovery-center", request.Screen);
        Assert.Equal(expectedAction, request.Action);
        Assert.Equal(selected.Key, recovery.ResolvedKey);
        Assert.Equal(decision, recovery.ResolvedDecision);
        Assert.Equal("settlement checked", recovery.ResolvedReason);
        Assert.Equal("bank portal evidence", recovery.ResolvedEvidence);
        Assert.Equal(
            decision == CardRecoverySupervisorDecision.ContinueWaiting ? null : "BANK-REF-1",
            recovery.ResolvedReference);
        Assert.Equal("SUPERVISOR", recovery.AuthorizingCashierIdDuringResolve);
        Assert.False(Assert.Single(authorization.IssuedScopes).IsActive);
        Assert.Empty(viewModel.OpenAttempts);
        Assert.Null(viewModel.SelectedAttempt);
        Assert.Equal([1, 0], counts);
        Assert.Equal("Resolution saved", viewModel.StatusMessage);
    }

    [Theory]
    [InlineData(CardProcessorKind.Linkly, "Refund", "Pending", false)]
    [InlineData(CardProcessorKind.Linkly, "Refund", "RequiresReview", true)]
    [InlineData(CardProcessorKind.Square, "Refund", "CheckoutCompleted", false)]
    [InlineData(CardProcessorKind.Square, "Refund", "Unknown", true)]
    [InlineData(CardProcessorKind.Linkly, "Sale", "Pending", true)]
    [InlineData(CardProcessorKind.Square, "Sale", "CheckoutCompleted", true)]
    [InlineData(CardProcessorKind.Square, "Sale", CardRecoveryPhases.FinalizePending, false)]
    public async Task Supervisor_commands_only_enable_for_states_accepted_by_provider_cas(
        CardProcessorKind processor,
        string operationKind,
        string status,
        bool expectedCanResolve)
    {
        var selected = CreateQueueItem(
            processor,
            Guid.NewGuid(),
            updatedAt: Now,
            operationKind: operationKind) with
        {
            Status = status
        };
        var recovery = new RecordingRecoveryService { OpenItems = [selected] };
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            CreateLocalization());

        await viewModel.LoadAsync();

        Assert.True(viewModel.RecoverCommand.CanExecute(null));
        Assert.Equal(expectedCanResolve, viewModel.ConfirmPaidCommand.CanExecute(null));
        Assert.Equal(expectedCanResolve, viewModel.ConfirmNotPaidCommand.CanExecute(null));
        Assert.Equal(expectedCanResolve, viewModel.ContinueWaitingCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("PAYMENT-1", null)]
    [InlineData(null, "PENDING")]
    public async Task Square_refund_with_provider_evidence_keeps_recover_and_hides_supervisor_controls(
        string? paymentId,
        string? paymentStatus)
    {
        var localization = CreateLocalization();
        var selected = CreateQueueItem(
            CardProcessorKind.Square,
            Guid.Parse("43000000-0000-0000-0000-000000000099"),
            updatedAt: Now,
            operationKind: "Refund") with
        {
            Status = "Recovering",
            PaymentId = paymentId,
            PaymentStatus = paymentStatus
        };
        var recovery = new RecordingRecoveryService { OpenItems = [selected] };
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            localization);

        await viewModel.LoadAsync();

        Assert.True(viewModel.RecoverCommand.CanExecute(null));
        Assert.False(viewModel.ConfirmPaidCommand.CanExecute(null));
        Assert.False(viewModel.ConfirmNotPaidCommand.CanExecute(null));
        Assert.False(viewModel.ContinueWaitingCommand.CanExecute(null));
        Assert.True(viewModel.IsSquareRefundProcessing);
        Assert.False(viewModel.CanShowSupervisorResolution);
        Assert.Equal(
            "Square refund is already processing. Use Recover to check the latest status. Do not submit another refund.",
            viewModel.SquareRefundProcessingMessage);

        localization.SetCulture("zh-CN");

        Assert.Equal(
            "Square 退款已在处理中。请使用“查询所选交易”获取最新状态，不要再次提交退款。",
            viewModel.SquareRefundProcessingMessage);
    }

    [Fact]
    public async Task RefreshCommand_uses_payment_view_and_preserves_selection_by_key()
    {
        var first = CreateQueueItem(
            CardProcessorKind.Linkly,
            Guid.Parse("44000000-0000-0000-0000-000000000001"),
            updatedAt: Now);
        var selected = CreateQueueItem(
            CardProcessorKind.Square,
            Guid.Parse("44000000-0000-0000-0000-000000000002"),
            updatedAt: Now.AddMinutes(-1));
        var recovery = new RecordingRecoveryService { OpenItems = [first, selected] };
        var authorization = new RecordingAuthorizationService(CreateCashier("SUPERVISOR"));
        var counts = new List<int>();
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            authorization,
            CreateLocalization(),
            openCountChanged: counts.Add);
        await viewModel.LoadAsync();
        viewModel.SelectedAttempt = selected;
        var refreshedSelected = selected with
        {
            Status = "Pending",
            UpdatedAt = Now.AddMinutes(1)
        };
        recovery.OpenItems = [refreshedSelected, first];
        authorization.Requests.Clear();

        await viewModel.RefreshCommand.ExecuteAsync(null);

        var request = Assert.Single(authorization.Requests);
        Assert.Equal(Permissions.PosTerminal.Payment.View, request.PermissionCode);
        Assert.Equal("refresh", request.Action);
        Assert.Same(refreshedSelected, viewModel.SelectedAttempt);
        Assert.Equal([2, 2], counts);
        Assert.Equal("Loaded 2 open card transactions.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Selection_inputs_survive_same_key_refresh_and_clear_when_provider_attempt_key_changes()
    {
        var first = CreateQueueItem(
            CardProcessorKind.Linkly,
            Guid.Parse("44000000-0000-0000-0000-000000000010"),
            updatedAt: Now);
        var selected = CreateQueueItem(
            CardProcessorKind.Square,
            Guid.Parse("44000000-0000-0000-0000-000000000011"),
            updatedAt: Now.AddMinutes(-1),
            operationKind: "Refund");
        var recovery = new RecordingRecoveryService { OpenItems = [first, selected] };
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            CreateLocalization());
        await viewModel.LoadAsync();
        viewModel.SelectedAttempt = selected;
        viewModel.ResolutionReason = "Keep waiting";
        viewModel.ResolutionEvidence = "Bank portal checked";
        viewModel.ResolutionReference = "REF-KEEP";
        var refreshedSelected = selected with
        {
            Status = "Pending",
            UpdatedAt = Now.AddMinutes(1)
        };
        recovery.OpenItems = [first, refreshedSelected];

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Same(refreshedSelected, viewModel.SelectedAttempt);
        Assert.Equal("Keep waiting", viewModel.ResolutionReason);
        Assert.Equal("Bank portal checked", viewModel.ResolutionEvidence);
        Assert.Equal("REF-KEEP", viewModel.ResolutionReference);

        viewModel.SelectedAttempt = first;

        Assert.Equal(string.Empty, viewModel.ResolutionReason);
        Assert.Equal(string.Empty, viewModel.ResolutionEvidence);
        Assert.Equal(string.Empty, viewModel.ResolutionReference);
    }

    [Fact]
    public async Task Culture_refresh_ignores_transient_null_and_resynchronizes_provider_scoped_selection()
    {
        var sharedAttemptGuid = Guid.Parse("44000000-0000-0000-0000-000000000099");
        var linkly = CreateQueueItem(CardProcessorKind.Linkly, sharedAttemptGuid, Now);
        var square = CreateQueueItem(
            CardProcessorKind.Square,
            sharedAttemptGuid,
            Now.AddMinutes(-1),
            operationKind: "Refund");
        var localization = CreateLocalization();
        var recovery = new RecordingRecoveryService { OpenItems = [linkly, square] };
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            localization);

        await viewModel.LoadAsync();
        viewModel.SelectedAttempt = square;
        viewModel.OpenAttemptRows.CollectionChanged += (_, _) =>
        {
            if (viewModel.OpenAttemptRows.Count == 0)
            {
                // 模拟 ListBox 在 ItemsSource 清空期间由 TwoWay 绑定回写的临时 null。
                viewModel.SelectedRow = null;
            }
        };

        localization.SetCulture("zh-CN");

        Assert.Same(square, viewModel.SelectedAttempt);
        Assert.Same(square, viewModel.SelectedRow?.Source);
        Assert.Equal(square.Key, viewModel.SelectedRow?.Key);
        Assert.Equal("退款", viewModel.SelectedTypeText);
    }

    [Theory]
    [InlineData(
        CardProcessorKind.Linkly,
        "Sale",
        "Supervisor payment reconciliation",
        "Check the bank result before unlocking this payment. Confirming paid requires a reference or evidence; confirming not paid requires evidence. A supervisor note is optional.",
        "Supervisor note (optional)",
        "Bank evidence (required when confirming not paid)",
        "Payment reference (when available)")]
    [InlineData(
        CardProcessorKind.Linkly,
        "ActiveSession",
        "Supervisor payment reconciliation",
        "Check the bank result before unlocking this payment. Confirming paid requires a reference or evidence; confirming not paid requires evidence. A supervisor note is optional.",
        "Supervisor note (optional)",
        "Bank evidence (required when confirming not paid)",
        "Payment reference (when available)")]
    [InlineData(
        CardProcessorKind.Linkly,
        "Refund",
        "Supervisor refund reconciliation",
        "Check the bank or terminal record before choosing one outcome. The refund remains locked until a supervisor decision is saved.",
        "Supervisor note (required when waiting; reference or note required when refunded)",
        "Bank evidence (required when no refund was processed)",
        "Refund reference (when available)")]
    [InlineData(
        CardProcessorKind.Square,
        "Refund",
        "Supervisor refund reconciliation",
        "Check the Square refund record before choosing an outcome. Confirm refunded requires a real Square refund reference; confirm not refunded requires bank evidence; continue waiting requires a supervisor note.",
        "Supervisor note (required when continuing to wait)",
        "Bank evidence (required when no refund was processed)",
        "Square refund reference (required when confirming refunded)")]
    [InlineData(
        CardProcessorKind.Linkly,
        "Unexpected",
        "Supervisor resolution",
        "Confirm the bank or terminal evidence for this selected transaction. Each manual decision requires one-time supervisor authorization.",
        "Supervisor reason or note",
        "Bank or terminal evidence",
        "Payment or settlement reference")]
    public async Task Resolution_guidance_matches_selected_operation(
        CardProcessorKind processor,
        string operationKind,
        string expectedTitle,
        string expectedInstructions,
        string expectedReasonLabel,
        string expectedEvidenceLabel,
        string expectedReferenceLabel)
    {
        var selected = CreateQueueItem(
            processor,
            Guid.NewGuid(),
            updatedAt: Now,
            operationKind: operationKind);
        var recovery = new RecordingRecoveryService { OpenItems = [selected] };
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            CreateLocalization());

        await viewModel.LoadAsync();

        Assert.Equal(expectedTitle, GetStringProperty(viewModel, "ResolutionSectionTitleText"));
        Assert.Equal(expectedInstructions, GetStringProperty(viewModel, "ResolutionInstructionsText"));
        Assert.Equal(expectedReasonLabel, GetStringProperty(viewModel, "ResolutionReasonLabelText"));
        Assert.Equal(expectedEvidenceLabel, GetStringProperty(viewModel, "ResolutionEvidenceLabelText"));
        Assert.Equal(expectedReferenceLabel, GetStringProperty(viewModel, "ResolutionReferenceLabelText"));
    }

    [Fact]
    public async Task Resolution_guidance_notifies_all_labels_when_culture_changes()
    {
        var localization = new ResolutionLocalizationService();
        var selected = CreateQueueItem(
            CardProcessorKind.Square,
            Guid.NewGuid(),
            updatedAt: Now,
            operationKind: "Refund");
        var recovery = new RecordingRecoveryService { OpenItems = [selected] };
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            localization);
        await viewModel.LoadAsync();
        var changedProperties = new HashSet<string>(StringComparer.Ordinal);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                changedProperties.Add(args.PropertyName);
            }
        };

        localization.SetCulture("zh-CN");

        Assert.Equal("主管退款核对", GetStringProperty(viewModel, "ResolutionSectionTitleText"));
        Assert.Equal(
            "选择结果前，请先核对 Square 退款记录。确认已退款必须填写真实的 Square 退款参考号；确认未退款必须填写银行证据；继续等待必须填写主管备注。",
            GetStringProperty(viewModel, "ResolutionInstructionsText"));
        Assert.Equal(
            "主管备注（继续等待时必填）",
            GetStringProperty(viewModel, "ResolutionReasonLabelText"));
        Assert.Equal(
            "银行证据（确认未退款时必填）",
            GetStringProperty(viewModel, "ResolutionEvidenceLabelText"));
        Assert.Equal(
            "Square 退款参考号（确认已退款时必填）",
            GetStringProperty(viewModel, "ResolutionReferenceLabelText"));
        Assert.All(
            new[]
            {
                "ResolutionSectionTitleText",
                "ResolutionInstructionsText",
                "ResolutionReasonLabelText",
                "ResolutionEvidenceLabelText",
                "ResolutionReferenceLabelText"
            },
            propertyName => Assert.Contains(propertyName, changedProperties));
    }

    [Fact]
    public void BackCommand_invokes_callback_without_authorization_or_recovery_calls()
    {
        var recovery = new RecordingRecoveryService();
        var authorization = new RecordingAuthorizationService(CreateCashier("SUPERVISOR"));
        var callbackCount = 0;
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            authorization,
            CreateLocalization(),
            back: () => callbackCount++);

        viewModel.BackCommand.Execute(null);

        Assert.Equal(1, callbackCount);
        Assert.Empty(authorization.Requests);
        Assert.Null(recovery.RecoveredKey);
        Assert.Null(recovery.ResolvedKey);
    }

    [Fact]
    public async Task Selected_attempt_exposes_required_details_and_original_product_snapshot()
    {
        var originalLine = new PosCartLineSnapshot(
            "STORE-1",
            "SKU-ORIGINAL",
            null,
            "Original product",
            "9300002",
            "ITEM-2",
            null,
            2m,
            6.17m,
            0m,
            null,
            PriceSourceKind.StoreRetailPrice,
            "Store price");
        var draft = new CardPaymentOrderDraft(
            Guid.Parse("45000000-0000-0000-0000-000000000001"),
            CreateSession(),
            new PosCartSnapshot([originalLine]),
            [],
            12.34m,
            12.34m,
            "Sale",
            null,
            Now.AddMinutes(-1));
        var selected = CreateQueueItem(
            CardProcessorKind.Square,
            Guid.Parse("45000000-0000-0000-0000-000000000002"),
            updatedAt: Now,
            operationKind: "Sale",
            orderDraftJson: JsonSerializer.Serialize(
                draft,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))) with
        {
            SessionId = null,
            CheckoutId = "CHECKOUT-1",
            TxnRef = null,
            PaymentId = "PAYMENT-1"
        };
        var recovery = new RecordingRecoveryService { OpenItems = [selected] };
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            CreateLocalization());

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasSelection);
        Assert.Equal("Sale", viewModel.SelectedTypeText);
        Assert.Equal("Square", viewModel.SelectedChannelText);
        Assert.Equal("$12.34", viewModel.SelectedAmountText);
        Assert.Equal("CASHIER-1", viewModel.SelectedCashierText);
        Assert.Equal(Now.ToString("g", CultureInfo.GetCultureInfo("en-US")), viewModel.SelectedTimeText);
        Assert.Equal("CHECKOUT-1", viewModel.SelectedSessionText);
        Assert.Equal("PAYMENT-1", viewModel.SelectedTxnText);
        Assert.Equal("00", viewModel.SelectedResponseCodeText);
        Assert.Equal("APPROVED", viewModel.SelectedResponseText);
        Assert.True(viewModel.HasProductSnapshot);
        var product = Assert.Single(viewModel.SelectedProductLines);
        Assert.Equal("Original product", product.DisplayName);
        Assert.Equal("SKU-ORIGINAL", product.ProductCode);

        viewModel.SelectedAttempt = selected with { OrderDraftJson = "not-json" };

        Assert.False(viewModel.HasProductSnapshot);
        Assert.Empty(viewModel.SelectedProductLines);

        var semanticFailure = Record.Exception(() =>
            viewModel.SelectedAttempt = selected with { OrderDraftJson = "{}" });

        Assert.Null(semanticFailure);
        Assert.False(viewModel.HasProductSnapshot);
        Assert.Empty(viewModel.SelectedProductLines);
    }

    [Fact]
    public async Task Queue_rows_and_details_map_finalize_pending_without_exposing_raw_status_and_refresh_on_culture_change()
    {
        var localization = CreateLocalization();
        var selected = CreateQueueItem(
            CardProcessorKind.Square,
            Guid.Parse("45000000-0000-0000-0000-000000000003"),
            updatedAt: Now,
            operationKind: "ActiveSession") with
        {
            Status = CardRecoveryPhases.FinalizePending
        };
        var recovery = new RecordingRecoveryService { OpenItems = [selected] };
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            localization);

        await viewModel.LoadAsync();

        Assert.Equal("Active session", viewModel.OpenAttemptRows[0].OperationTypeText);
        Assert.Equal("Square", viewModel.OpenAttemptRows[0].ChannelText);
        Assert.Equal("Finalization pending", viewModel.OpenAttemptRows[0].StatusText);
        Assert.Equal("Finalization pending", viewModel.SelectedStatusText);
        Assert.DoesNotContain(CardRecoveryPhases.FinalizePending, viewModel.OpenAttemptRows[0].StatusText, StringComparison.Ordinal);

        localization.SetCulture("zh-CN");

        Assert.Equal("活动会话", viewModel.OpenAttemptRows[0].OperationTypeText);
        Assert.Equal("Square", viewModel.OpenAttemptRows[0].ChannelText);
        Assert.Equal("等待完成", viewModel.OpenAttemptRows[0].StatusText);
        Assert.Equal("等待完成", viewModel.SelectedStatusText);
    }

    [Fact]
    public void View_is_touch_first_master_detail_and_binds_all_targeted_actions()
    {
        var viewPath = Path.Combine(
            FindRepoRoot(),
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "Views",
            "CardRecoveryCenterView.xaml");
        Assert.True(File.Exists(viewPath), $"Missing view: {viewPath}");

        var document = XDocument.Load(viewPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var root = Assert.IsType<XElement>(document.Root);
        Assert.Equal("Hbpos.Client.Wpf.Views.CardRecoveryCenterView", root.Attribute(x + "Class")?.Value);

        var layout = Assert.Single(document.Descendants(presentation + "Grid")
            .Where(element => element.Attribute(x + "Name")?.Value == "RecoveryMasterDetailGrid"));
        var columns = layout.Element(presentation + "Grid.ColumnDefinitions")?
            .Elements(presentation + "ColumnDefinition")
            .ToArray() ?? [];
        Assert.Equal(3, columns.Length);
        Assert.Contains(columns, column => column.Attribute("Width")?.Value.Contains('*') == true);

        var list = Assert.Single(document.Descendants(presentation + "ListBox")
            .Where(element => element.Attribute(x + "Name")?.Value == "OpenAttemptsList"));
        Assert.Equal("{Binding OpenAttemptRows}", list.Attribute("ItemsSource")?.Value);
        Assert.Equal("{Binding SelectedRow, Mode=TwoWay}", list.Attribute("SelectedItem")?.Value);
        var rowHeight = document.Descendants(presentation + "Setter")
            .Where(setter => setter.Attribute("Property")?.Value == "MinHeight")
            .Select(setter => setter.Attribute("Value")?.Value)
            .Select(value => double.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0)
            .DefaultIfEmpty()
            .Max();
        Assert.True(rowHeight >= 72, $"Expected a touch list row of at least 72, found {rowHeight}.");

        var detailScroller = Assert.Single(document.Descendants(presentation + "ScrollViewer")
            .Where(element => element.Attribute(x + "Name")?.Value == "DetailScrollViewer"));
        Assert.Equal("VerticalOnly", detailScroller.Attribute("PanningMode")?.Value);

        var attributeValues = root.DescendantsAndSelf()
            .Attributes()
            .Select(attribute => attribute.Value)
            .ToArray();
        foreach (var binding in new[]
                 {
                     "SelectedTypeText",
                     "SelectedChannelText",
                     "SelectedAmountText",
                     "SelectedCashierText",
                     "SelectedProductLines",
                     "SelectedTimeText",
                     "SelectedSessionText",
                     "SelectedTxnText",
                     "SelectedResponseCodeText",
                     "SelectedResponseText"
                 })
        {
            Assert.Contains(attributeValues, value => value.Contains($"Binding {binding}", StringComparison.Ordinal));
        }

        foreach (var command in new[]
                 {
                     "BackCommand",
                     "RefreshCommand",
                     "RecoverCommand",
                     "ConfirmPaidCommand",
                     "ConfirmNotPaidCommand",
                     "ContinueWaitingCommand"
                 })
        {
            var button = Assert.Single(document.Descendants(presentation + "Button")
                .Where(element => element.Attribute("Command")?.Value.Contains(command, StringComparison.Ordinal) == true));
            Assert.True(
                double.TryParse(button.Attribute("MinHeight")?.Value, CultureInfo.InvariantCulture, out var minHeight) &&
                minHeight >= 48,
                $"{command} must have a touch target of at least 48.");
            var label = Assert.Single(button.Descendants(presentation + "TextBlock"));
            Assert.StartsWith("{loc:Loc ", label.Attribute("Text")?.Value);
        }
    }

    [Fact]
    public void Supervisor_resolution_inputs_render_persistent_labels()
    {
        var viewPath = Path.Combine(
            FindRepoRoot(),
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "Views",
            "CardRecoveryCenterView.xaml");
        var document = XDocument.Load(viewPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        foreach (var (inputProperty, labelProperty) in new[]
                 {
                     ("ResolutionReason", "ResolutionReasonLabelText"),
                     ("ResolutionEvidence", "ResolutionEvidenceLabelText"),
                     ("ResolutionReference", "ResolutionReferenceLabelText")
                 })
        {
            var input = Assert.Single(document.Descendants(presentation + "TextBox")
                .Where(element =>
                    element.Attribute("Text")?.Value.Contains(
                        $"Binding {inputProperty}",
                        StringComparison.Ordinal) == true));
            Assert.Equal(
                $"{{Binding {labelProperty}}}",
                input.Attribute("AutomationProperties.Name")?.Value);
            Assert.DoesNotContain(
                input.Attributes(),
                attribute => attribute.Name.LocalName == "HintAssist.Hint");

            var container = Assert.IsType<XElement>(input.Parent);
            var label = Assert.Single(container.Elements(presentation + "TextBlock")
                .Where(element =>
                    element.Attribute("Text")?.Value == $"{{Binding {labelProperty}}}"));
            var children = container.Elements().ToArray();
            Assert.True(
                Array.IndexOf(children, label) < Array.IndexOf(children, input),
                $"{inputProperty} must keep its visible label above the input.");
        }

        Assert.Contains(
            document.Descendants(presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value == "{Binding ResolutionSectionTitleText}");
        Assert.Contains(
            document.Descendants(presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value == "{Binding ResolutionInstructionsText}");
    }

    [Fact]
    public void Center_resources_are_bilingual_complete_and_keep_parallel_wiring_keys_stable()
    {
        var resourceRoot = Path.Combine(
            FindRepoRoot(),
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "Resources");
        var english = LoadResources(Path.Combine(resourceRoot, "Strings.resx"));
        var chinese = LoadResources(Path.Combine(resourceRoot, "Strings.zh-CN.resx"));

        var requiredKeys = new[]
        {
            "shell.page.cardRecovery",
            "cardRecovery.center.title",
            "cardRecovery.center.subtitle",
            "cardRecovery.center.openCount",
            "cardRecovery.center.list.title",
            "cardRecovery.center.list.empty",
            "cardRecovery.center.detail.title",
            "cardRecovery.center.detail.empty",
            "cardRecovery.center.field.type",
            "cardRecovery.center.field.channel",
            "cardRecovery.center.field.amount",
            "cardRecovery.center.field.cashier",
            "cardRecovery.center.field.products",
            "cardRecovery.center.field.time",
            "cardRecovery.center.field.session",
            "cardRecovery.center.field.txn",
            "cardRecovery.center.field.responseCode",
            "cardRecovery.center.field.responseText",
            "cardRecovery.center.field.status",
            "cardRecovery.center.field.attempt",
            "cardRecovery.center.field.environment",
            "cardRecovery.center.field.reference",
            "cardRecovery.center.product.name",
            "cardRecovery.center.product.code",
            "cardRecovery.center.product.quantity",
            "cardRecovery.center.product.unitPrice",
            "cardRecovery.center.product.unavailable",
            "cardRecovery.center.resolution.title",
            "cardRecovery.center.resolution.instructions",
            "cardRecovery.center.input.reason",
            "cardRecovery.center.input.evidence",
            "cardRecovery.center.input.reference",
            "cardRecovery.center.action.back",
            "cardRecovery.center.action.refresh",
            "cardRecovery.center.action.recover",
            "cardRecovery.center.action.confirmPaid",
            "cardRecovery.center.action.confirmNotPaid",
            "cardRecovery.center.action.continueWaiting",
            "cardRecovery.center.status.ready",
            "cardRecovery.center.status.loaded",
            "cardRecovery.center.status.empty",
            "cardRecovery.center.status.authorizationRequired",
            "cardRecovery.center.status.selectionChanged",
            "cardRecovery.center.status.recoverNoResult",
            "cardRecovery.center.status.resolveNoResult",
            "cardRecovery.center.status.refreshFailed",
            "cardRecovery.center.status.recoverFailed",
            "cardRecovery.center.status.resolveFailed",
            "cardRecovery.center.status.unexpected",
            "cardRecovery.center.value.none",
            "cardRecovery.center.squareRefund.processing",
            "cardRecovery.refund.section.squareInstructions",
            "cardRecovery.refund.field.squareRefundReference",
            "cardRecovery.refund.field.squareNote",
            "payment.card.squareRefundMissingReference",
            "payment.card.squareRefundPending",
            "cardRecovery.refund.squarePending",
            "cardRecovery.refund.resolveFailed"
        };

        requiredKeys = [
            .. requiredKeys,
            "cardRecovery.operation.sale",
            "cardRecovery.operation.refund",
            "cardRecovery.operation.activeSession",
            "cardRecovery.operation.unknown",
            "cardRecovery.channel.linkly",
            "cardRecovery.channel.square",
            "cardRecovery.channel.unknown",
            "cardRecovery.status.none",
            "cardRecovery.status.pending",
            "cardRecovery.status.sessionStarted",
            "cardRecovery.status.recovering",
            "cardRecovery.status.approved",
            "cardRecovery.status.requiresReview",
            "cardRecovery.status.declined",
            "cardRecovery.status.timedOut",
            "cardRecovery.status.cancelled",
            "cardRecovery.status.failed",
            "cardRecovery.status.orderCompleted",
            "cardRecovery.status.abandoned",
            "cardRecovery.status.checkoutCreated",
            "cardRecovery.status.checkoutCompleted",
            "cardRecovery.status.paymentVerified",
            "cardRecovery.status.canceled",
            "cardRecovery.status.unknown",
            "cardRecovery.status.finalizePending",
            "cardRecovery.linkly.supervisorWaiting",
            "cardRecovery.linkly.paymentAlreadyFinalized",
            "cardRecovery.square.supervisorWaiting",
            "cardRecovery.square.notPaidCartNotEmpty",
            "cardRecovery.square.notPaidDraftInvalid",
            "cardRecovery.square.notPaidRestoreFailed",
            "cardRecovery.square.notPaidTerminalizeFailed",
            "cardRecovery.square.notPaidRetryAllowed"
        ];

        foreach (var key in requiredKeys)
        {
            Assert.True(english.TryGetValue(key, out var englishValue), $"Missing English resource: {key}");
            Assert.False(string.IsNullOrWhiteSpace(englishValue));
            Assert.True(chinese.TryGetValue(key, out var chineseValue), $"Missing Chinese resource: {key}");
            Assert.False(string.IsNullOrWhiteSpace(chineseValue));
        }

        Assert.Equal("{0} card transactions need attention", english["cardRecovery.center.openCount"]);
        Assert.Equal("{0} 笔卡交易待处理", chinese["cardRecovery.center.openCount"]);
        Assert.True(english.ContainsKey("cardRecovery.center.title"));
        Assert.True(chinese.ContainsKey("cardRecovery.center.title"));
        Assert.True(english.ContainsKey("shell.page.cardRecovery"));
        Assert.True(chinese.ContainsKey("shell.page.cardRecovery"));
    }

    [Fact]
    public void Alternative_refund_resources_are_bilingual_and_actionable()
    {
        var resourceRoot = Path.Combine(
            FindRepoRoot(),
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "Resources");
        var english = LoadResources(Path.Combine(resourceRoot, "Strings.resx"));
        var chinese = LoadResources(Path.Combine(resourceRoot, "Strings.zh-CN.resx"));

        Assert.Equal(
            "Square confirmed that no refund was processed. The return was restored, but card refund is blocked for this recovery. Use cash or voucher.",
            english["cardRecovery.refund.squareAlternativeMethodRequired"]);
        Assert.Equal(
            "This recovered Square refund must be completed with cash or voucher. Card refund is unavailable.",
            english["payment.refund.status.alternativeMethodRequired"]);
        Assert.Equal(
            "Square 已确认未发生退款。原退货已恢复，但本次恢复禁止再次使用银行卡退款，请改用现金或代金券。",
            chinese["cardRecovery.refund.squareAlternativeMethodRequired"]);
        Assert.Equal(
            "此 Square 恢复退款必须使用现金或代金券完成，银行卡退款不可用。",
            chinese["payment.refund.status.alternativeMethodRequired"]);
    }

    [Fact]
    public async Task RefreshCommand_contains_service_failure_and_keeps_current_list_available()
    {
        var item = CreateQueueItem(
            CardProcessorKind.Linkly,
            Guid.Parse("46000000-0000-0000-0000-000000000001"),
            updatedAt: Now);
        var recovery = new RecordingRecoveryService { OpenItems = [item] };
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            CreateLocalization());
        await viewModel.LoadAsync();
        recovery.ListException = new InvalidOperationException("terminal offline");

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsBusy);
        Assert.Equal([item], viewModel.OpenAttempts);
        Assert.Same(item, viewModel.SelectedAttempt);
        Assert.Equal("Could not refresh card transactions. terminal offline", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ResolveCommand_preserves_persisted_result_when_post_commit_refresh_fails()
    {
        var item = CreateQueueItem(
            CardProcessorKind.Linkly,
            Guid.Parse("46000000-0000-0000-0000-000000000002"),
            updatedAt: Now);
        var recovery = new RecordingRecoveryService
        {
            OpenItems = [item],
            ResolveResult = new CardRecoveryResolutionResult(
                false,
                "Decision saved; recovery is still pending.",
                LockRetained: true,
                ResolutionPersisted: true)
        };
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            CreateLocalization());
        await viewModel.LoadAsync();
        recovery.ListException = new InvalidOperationException("refresh failed after commit");

        Action<string> throwingLogSubscriber = line =>
        {
            if (line.Contains(
                    "post-commit action failed context=targeted resolution refresh processor=Linkly attempt=46000000-0000-0000-0000-000000000002",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("diagnostic subscriber failed");
            }
        };
        ConsoleLog.LineWritten += throwingLogSubscriber;
        try
        {
            await viewModel.ContinueWaitingCommand.ExecuteAsync(null);
        }
        finally
        {
            ConsoleLog.LineWritten -= throwingLogSubscriber;
        }

        Assert.False(viewModel.IsBusy);
        Assert.Equal("Decision saved; recovery is still pending.", viewModel.StatusMessage);
        Assert.Equal([item], viewModel.OpenAttempts);
    }

    [Fact]
    public async Task RecoverCommand_draft_handoff_failure_reports_fail_closed_status_instead_of_success()
    {
        var item = CreateQueueItem(
            CardProcessorKind.Square,
            Guid.Parse("46000000-0000-0000-0000-000000000004"),
            updatedAt: Now);
        var recovery = new RecordingRecoveryService
        {
            OpenItems = [item],
            RecoverResult = new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.DraftRestored,
                "The refund draft is ready.")
        };
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            CreateLocalization(),
            recoveryResultHandledAsync: _ =>
                throw new InvalidOperationException("payment projection failed"));
        await viewModel.LoadAsync();

        await viewModel.RecoverCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsBusy);
        Assert.Equal(
            "The recovery result was saved, but the payment draft could not be handed off safely. Run recovery again before taking another payment or refund. payment projection failed",
            viewModel.StatusMessage);
        Assert.DoesNotContain("The refund draft is ready.", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecoverCommand_committed_draft_handoff_warning_does_not_request_financial_retry()
    {
        var item = CreateQueueItem(
            CardProcessorKind.Square,
            Guid.Parse("46000000-0000-0000-0000-000000000007"),
            updatedAt: Now);
        var recovery = new RecordingRecoveryService
        {
            OpenItems = [item],
            RecoverResult = new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.DraftRestored,
                "The refund draft is ready.")
        };
        const string warning =
            "Recovery was committed, but the payment page remains locked. Review the recovery center before continuing.";
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            CreateLocalization(),
            recoveryResultHandledAsync: _ =>
                throw new CardRecoveryDraftHandoffPostCommitException(warning));
        await viewModel.LoadAsync();

        await viewModel.RecoverCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsBusy);
        Assert.Equal(warning, viewModel.StatusMessage);
        Assert.DoesNotContain("Run recovery again", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveCommand_draft_handoff_failure_reports_fail_closed_status_instead_of_success()
    {
        var item = CreateQueueItem(
            CardProcessorKind.Linkly,
            Guid.Parse("46000000-0000-0000-0000-000000000005"),
            operationKind: "Refund",
            updatedAt: Now);
        var recoveredDraft = new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.DraftRestored,
            "The supervisor recovery draft is ready.");
        var recovery = new RecordingRecoveryService
        {
            OpenItems = [item],
            ResolveResult = new CardRecoveryResolutionResult(
                true,
                "The supervisor decision was saved.",
                recoveredDraft,
                RetryAllowed: true,
                LockRetained: true,
                ResolutionPersisted: true,
                ResolutionApplied: true)
        };
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            CreateLocalization(),
            recoveryResultHandledAsync: _ =>
                throw new InvalidOperationException("payment command refresh failed"));
        await viewModel.LoadAsync();
        viewModel.ResolutionReason = "Verified with the processor.";

        await viewModel.ConfirmNotPaidCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsBusy);
        Assert.Equal(
            "The recovery result was saved, but the payment draft could not be handed off safely. Run recovery again before taking another payment or refund. payment command refresh failed",
            viewModel.StatusMessage);
        Assert.DoesNotContain("The supervisor decision was saved.", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecoverCommand_draft_handoff_propagates_fatal_callback_exception(bool outOfMemory)
    {
        var item = CreateQueueItem(
            CardProcessorKind.Square,
            Guid.Parse("46000000-0000-0000-0000-000000000006"),
            updatedAt: Now);
        Exception fatal = outOfMemory
            ? new OutOfMemoryException("fatal projection failure")
            : new StackOverflowException("fatal projection failure");
        var recovery = new RecordingRecoveryService
        {
            OpenItems = [item],
            RecoverResult = new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.DraftRestored,
                "The refund draft is ready.")
        };
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            CreateLocalization(),
            recoveryResultHandledAsync: _ => throw fatal);
        await viewModel.LoadAsync();

        var thrown = await Assert.ThrowsAnyAsync<Exception>(
            () => viewModel.RecoverCommand.ExecuteAsync(null));

        Assert.Same(fatal, thrown);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task ResolveCommand_does_not_swallow_fatal_service_exception()
    {
        var item = CreateQueueItem(
            CardProcessorKind.Linkly,
            Guid.Parse("46000000-0000-0000-0000-000000000003"),
            updatedAt: Now);
        var recovery = new RecordingRecoveryService
        {
            OpenItems = [item],
            ResolveException = new OutOfMemoryException("fatal resolution failure")
        };
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            CreateLocalization());
        await viewModel.LoadAsync();

        await Assert.ThrowsAsync<OutOfMemoryException>(
            () => viewModel.ContinueWaitingCommand.ExecuteAsync(null));
        Assert.False(viewModel.IsBusy);
    }

    private static IReadOnlyDictionary<string, string> LoadResources(string path) =>
        XDocument.Load(path)
            .Root!
            .Elements("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

    private static string FindRepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                    Directory.Exists(Path.Combine(current.FullName, "apps", "pos-wpf")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string GetStringProperty(CardRecoveryCenterViewModel viewModel, string propertyName)
    {
        var property = typeof(CardRecoveryCenterViewModel).GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<string>(property.GetValue(viewModel));
    }

    private static CardRecoveryQueueItem CreateQueueItem(
        CardProcessorKind processor,
        Guid attemptGuid,
        DateTimeOffset updatedAt,
        string operationKind = "Sale",
        string? orderDraftJson = null) =>
        new(
            processor,
            attemptGuid,
            operationKind,
            12.34m,
            "STORE-1",
            "POS-1",
            "CASHIER-1",
            "Sandbox",
            "RequiresReview",
            updatedAt.AddMinutes(-1),
            updatedAt,
            OrderDraftJson: orderDraftJson,
            SessionId: "SESSION-1",
            TxnRef: "TXN-1",
            ResponseCode: "00",
            ResponseText: "APPROVED");

    private static PosSessionState CreateSession() =>
        new(
            "HB POS",
            "STORE-1",
            "Store 1",
            "POS-1",
            "CASHIER-1",
            "Cashier 1",
            true,
            0,
            CreateCashier("CASHIER-1", Permissions.PosTerminal.Payment.View));

    private static PosCartService CreateNonEmptyCart()
    {
        var cart = new PosCartService();
        cart.RestoreSnapshot(new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "STORE-1",
                "SKU-1",
                null,
                "Current item",
                "9300001",
                "ITEM-1",
                null,
                1m,
                5m,
                0m,
                null,
                PriceSourceKind.StoreRetailPrice,
                "Store price")
        ]));
        return cart;
    }

    private static CashierSessionDto CreateCashier(string cashierId, params string[] permissions) =>
        new(
            cashierId,
            $"USER-{cashierId}",
            cashierId,
            "STORE-1",
            "POS-1",
            [],
            permissions,
            ["STORE-1"],
            IsSuperAdmin: false,
            IsOfflineCached: false,
            IsEmergencyOverride: false,
            AuthorizationToken: $"ticket-{cashierId}",
            AuthorizationExpiresAtUtc: Now.AddYears(1));

    private static DictionaryLocalizationService CreateLocalization() =>
        new(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cardRecovery.center.resolution.title"] = "Supervisor resolution",
            ["cardRecovery.center.resolution.instructions"] = "Confirm the bank or terminal evidence for this selected transaction. Each manual decision requires one-time supervisor authorization.",
            ["cardRecovery.center.input.reason"] = "Supervisor reason or note",
            ["cardRecovery.center.input.evidence"] = "Bank or terminal evidence",
            ["cardRecovery.center.input.reference"] = "Payment or settlement reference",
            ["cardRecovery.payment.section.title"] = "Supervisor payment reconciliation",
            ["cardRecovery.payment.section.instructions"] = "Check the bank result before unlocking this payment. Confirming paid requires a reference or evidence; confirming not paid requires evidence. A supervisor note is optional.",
            ["cardRecovery.payment.field.paymentReference"] = "Payment reference (when available)",
            ["cardRecovery.payment.field.evidence"] = "Bank evidence (required when confirming not paid)",
            ["cardRecovery.payment.field.note"] = "Supervisor note (optional)",
            ["cardRecovery.refund.section.title"] = "Supervisor refund reconciliation",
            ["cardRecovery.refund.section.instructions"] = "Check the bank or terminal record before choosing one outcome. The refund remains locked until a supervisor decision is saved.",
            ["cardRecovery.refund.field.refundReference"] = "Refund reference (when available)",
            ["cardRecovery.refund.field.evidence"] = "Bank evidence (required when no refund was processed)",
            ["cardRecovery.refund.field.note"] = "Supervisor note (required when waiting; reference or note required when refunded)",
            ["cardRecovery.refund.section.squareInstructions"] = "Check the Square refund record before choosing an outcome. Confirm refunded requires a real Square refund reference; confirm not refunded requires bank evidence; continue waiting requires a supervisor note.",
            ["cardRecovery.refund.field.squareRefundReference"] = "Square refund reference (required when confirming refunded)",
            ["cardRecovery.refund.field.squareNote"] = "Supervisor note (required when continuing to wait)",
            ["cardRecovery.center.openCount"] = "{0} card transactions need attention",
            ["cardRecovery.center.status.ready"] = "Review an open card transaction.",
            ["cardRecovery.center.status.loaded"] = "Loaded {0} open card transactions.",
            ["cardRecovery.center.status.empty"] = "No card transactions need attention.",
            ["cardRecovery.center.status.authorizationRequired"] = "Authorization is required.",
            ["cardRecovery.center.status.selectionChanged"] = "The selected transaction changed. Select it again before continuing.",
            ["cardRecovery.center.status.recoverNoResult"] = "The selected transaction is no longer open.",
            ["cardRecovery.center.status.refreshFailed"] = "Could not refresh card transactions. {0}",
            ["cardRecovery.center.status.recoverFailed"] = "Could not check the selected transaction. {0}",
            ["cardRecovery.center.status.resolveFailed"] = "Could not save the supervisor decision. {0}",
            ["cardRecovery.center.squareRefund.processing"] = "Square refund is already processing. Use Recover to check the latest status. Do not submit another refund.",
            ["cardRecovery.center.value.none"] = "-"
        }, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cardRecovery.center.resolution.title"] = "主管结案",
            ["cardRecovery.center.resolution.instructions"] = "请核对当前所选交易的银行或终端证据。每次人工决定均需要一次性主管授权。",
            ["cardRecovery.center.input.reason"] = "主管原因或备注",
            ["cardRecovery.center.input.evidence"] = "银行或终端证据",
            ["cardRecovery.center.input.reference"] = "付款或结算参考号",
            ["cardRecovery.payment.section.title"] = "主管付款核对",
            ["cardRecovery.payment.section.instructions"] = "解锁前请先核对银行结果。确认已付款需参考号或证据，确认未付款需银行证据；主管备注可选。",
            ["cardRecovery.payment.field.paymentReference"] = "付款参考号（如有）",
            ["cardRecovery.payment.field.evidence"] = "银行证据（确认未付款时必填）",
            ["cardRecovery.payment.field.note"] = "主管备注（选填）",
            ["cardRecovery.refund.section.title"] = "主管退款核对",
            ["cardRecovery.refund.section.instructions"] = "请先核对银行或终端记录，再选择一个结果。主管决定保存前，该退款会继续锁定。",
            ["cardRecovery.refund.field.refundReference"] = "退款参考号（如有）",
            ["cardRecovery.refund.field.evidence"] = "银行证据（确认未退款时必填）",
            ["cardRecovery.refund.field.note"] = "主管备注（继续等待时必填；确认已退款需参考号或备注）",
            ["cardRecovery.refund.section.squareInstructions"] = "选择结果前，请先核对 Square 退款记录。确认已退款必须填写真实的 Square 退款参考号；确认未退款必须填写银行证据；继续等待必须填写主管备注。",
            ["cardRecovery.refund.field.squareRefundReference"] = "Square 退款参考号（确认已退款时必填）",
            ["cardRecovery.refund.field.squareNote"] = "主管备注（继续等待时必填）",
            ["cardRecovery.center.openCount"] = "{0} 笔卡交易待处理",
            ["cardRecovery.center.status.ready"] = "请检查待处理的卡交易。",
            ["cardRecovery.center.status.loaded"] = "已加载 {0} 笔待处理卡交易。",
            ["cardRecovery.center.status.empty"] = "没有待处理的卡交易。",
            ["cardRecovery.center.status.authorizationRequired"] = "需要授权。",
            ["cardRecovery.center.status.selectionChanged"] = "所选交易已变化，请重新选择。",
            ["cardRecovery.center.status.recoverNoResult"] = "所选交易已不再待处理。",
            ["cardRecovery.center.status.refreshFailed"] = "无法刷新卡交易。{0}",
            ["cardRecovery.center.status.recoverFailed"] = "无法检查所选交易。{0}",
            ["cardRecovery.center.status.resolveFailed"] = "无法保存主管决定。{0}",
            ["cardRecovery.center.squareRefund.processing"] = "Square 退款已在处理中。请使用“查询所选交易”获取最新状态，不要再次提交退款。",
            ["cardRecovery.center.value.none"] = "-",
            ["cardRecovery.operation.sale"] = "销售",
            ["cardRecovery.operation.refund"] = "退款",
            ["cardRecovery.operation.activeSession"] = "活动会话",
            ["cardRecovery.operation.unknown"] = "未知操作",
            ["cardRecovery.channel.linkly"] = "Linkly",
            ["cardRecovery.channel.square"] = "Square",
            ["cardRecovery.channel.unknown"] = "未知渠道",
            ["cardRecovery.status.finalizePending"] = "等待完成",
            ["cardRecovery.status.requiresReview"] = "需要复核"
        });

    private sealed class RecordingRecoveryService : ICardPaymentRecoveryService
    {
        public IReadOnlyList<CardRecoveryQueueItem> OpenItems { get; set; } = [];
        public Exception? ListException { get; set; }
        public CardPaymentRecoveryResult RecoverResult { get; set; } = CardPaymentRecoveryResult.None;
        public CardRecoveryAttemptKey? RecoveredKey { get; private set; }
        public PosCartService? RecoveredCart { get; private set; }
        public bool CartWasEmptyWhenRecoverCalled { get; private set; }
        public CardRecoveryResolutionResult ResolveResult { get; set; } =
            new(false, "Resolution failed", LockRetained: true);
        public Exception? ResolveException { get; set; }
        public CardRecoveryAttemptKey? ResolvedKey { get; private set; }
        public CardRecoverySupervisorDecision? ResolvedDecision { get; private set; }
        public string? ResolvedReason { get; private set; }
        public string? ResolvedEvidence { get; private set; }
        public string? ResolvedReference { get; private set; }
        public string? AuthorizingCashierIdDuringResolve { get; private set; }
        public int ListOpenCallCount { get; private set; }

        public Task<IReadOnlyList<CardRecoveryQueueItem>> ListOpenAsync(
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            ListOpenCallCount++;
            return ListException is null
                ? Task.FromResult(OpenItems)
                : Task.FromException<IReadOnlyList<CardRecoveryQueueItem>>(ListException);
        }

        public Task<CardPaymentRecoveryResult> RecoverLatestAsync(
            PosCartService cart,
            PosSessionState session,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CardPaymentRecoveryResult.None);

        public Task<CardPaymentRecoveryResult> RecoverActiveSessionAsync(
            PosCartService cart,
            PosSessionState session,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CardPaymentRecoveryResult.None);

        public Task<CardPaymentRecoveryResult> ManuallyClearActiveSessionAsync(
            string sessionId,
            PosSessionState session,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CardPaymentRecoveryResult.None);

        public Task<CardPaymentRecoveryResult> RecoverAsync(
            CardRecoveryAttemptKey key,
            PosCartService cart,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            RecoveredKey = key;
            RecoveredCart = cart;
            CartWasEmptyWhenRecoverCalled = cart.IsEmpty;
            return Task.FromResult(RecoverResult);
        }

        public Task<CardRecoveryResolutionResult> ResolveAsync(
            CardRecoveryAttemptKey key,
            CardRecoverySupervisorDecision decision,
            string reason,
            string? evidence,
            string? reference,
            PosCartService cart,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            ResolvedKey = key;
            ResolvedDecision = decision;
            ResolvedReason = reason;
            ResolvedEvidence = evidence;
            ResolvedReference = reference;
            AuthorizingCashierIdDuringResolve =
                OperationAuthorizationScope.CurrentAuthorizationContext?.AuthorizingSession.CashierId;
            return ResolveException is null
                ? Task.FromResult(ResolveResult)
                : Task.FromException<CardRecoveryResolutionResult>(ResolveException);
        }
    }

    private sealed class RecordingAuthorizationService(CashierSessionDto authorizer)
        : IOperationAuthorizationService
    {
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

        public List<AuthorizationRequest> Requests { get; } = [];
        public List<OperationAuthorizationScope> IssuedScopes { get; } = [];
        public bool Allow { get; set; } = true;
        public Action? BeforeReturn { get; set; }
        public string ScannerPageId => "card-recovery-center-test";
        public bool IsPromptOpen => false;
        public bool IsBusy => false;
        public string PromptMessage => string.Empty;
        public string StatusMessage => string.Empty;
        public string PermissionCode => string.Empty;
        public string Screen => string.Empty;
        public string Action => string.Empty;
        public IRelayCommand CancelCommand { get; } = new RelayCommand(() => { });

        public Task<OperationAuthorizationScope?> AuthorizeAsync(
            string permissionCode,
            string screen,
            string action,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(new AuthorizationRequest(permissionCode, screen, action));
            BeforeReturn?.Invoke();
            if (!Allow || session.CashierSession is null)
            {
                return Task.FromResult<OperationAuthorizationScope?>(null);
            }

            var scope = new OperationAuthorizationScope(
                session.CashierSession,
                permissionCode,
                screen,
                action);
            scope.SetAuthorizingSession(authorizer);
            IssuedScopes.Add(scope);
            return Task.FromResult<OperationAuthorizationScope?>(scope);
        }

        public bool ProcessScannerBarcode(string barcode) => false;
        public void Cancel() { }
        public void RevokeAll() { }
    }

    private sealed record AuthorizationRequest(string PermissionCode, string Screen, string Action);

    private sealed class ResolutionLocalizationService : ILocalizationService
    {
        private static readonly IReadOnlyDictionary<string, string> English =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cardRecovery.center.openCount"] = "{0} card transactions need attention",
                ["cardRecovery.refund.section.title"] = "Supervisor refund reconciliation",
                ["cardRecovery.refund.section.squareInstructions"] = "Check the Square refund record before choosing an outcome. Confirm refunded requires a real Square refund reference; confirm not refunded requires bank evidence; continue waiting requires a supervisor note.",
                ["cardRecovery.refund.field.squareNote"] = "Supervisor note (required when continuing to wait)",
                ["cardRecovery.refund.field.evidence"] = "Bank evidence (required when no refund was processed)",
                ["cardRecovery.refund.field.squareRefundReference"] = "Square refund reference (required when confirming refunded)"
            };

        private static readonly IReadOnlyDictionary<string, string> Chinese =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cardRecovery.center.openCount"] = "{0} 笔卡交易待处理",
                ["cardRecovery.refund.section.title"] = "主管退款核对",
                ["cardRecovery.refund.section.squareInstructions"] = "选择结果前，请先核对 Square 退款记录。确认已退款必须填写真实的 Square 退款参考号；确认未退款必须填写银行证据；继续等待必须填写主管备注。",
                ["cardRecovery.refund.field.squareNote"] = "主管备注（继续等待时必填）",
                ["cardRecovery.refund.field.evidence"] = "银行证据（确认未退款时必填）",
                ["cardRecovery.refund.field.squareRefundReference"] = "Square 退款参考号（确认已退款时必填）"
            };

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public event EventHandler? CultureChanged;

        public IReadOnlyList<CultureInfo> AvailableCultures { get; } =
            [CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("zh-CN")];

        public CultureInfo CurrentCulture { get; private set; } =
            CultureInfo.GetCultureInfo("en-US");

        public void SetCulture(string cultureName) => SetCulture(CultureInfo.GetCultureInfo(cultureName));

        public void SetCulture(CultureInfo culture)
        {
            CurrentCulture = culture;
            CultureChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task SetCultureAsync(string cultureName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetCulture(cultureName);
            return Task.CompletedTask;
        }

        public string T(string key)
        {
            var values = CurrentCulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? Chinese
                : English;
            return values.TryGetValue(key, out var value) ? value : $"[[{key}]]";
        }
    }

    private sealed class DictionaryLocalizationService(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, string>? chineseValues = null)
        : ILocalizationService
    {
        private readonly IReadOnlyDictionary<string, string> _values = values;
        private readonly IReadOnlyDictionary<string, string> _chineseValues = chineseValues ?? values;

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public event EventHandler? CultureChanged;

        public IReadOnlyList<CultureInfo> AvailableCultures { get; } =
            [CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("zh-CN")];

        public CultureInfo CurrentCulture { get; private set; } =
            CultureInfo.GetCultureInfo("en-US");

        public void SetCulture(string cultureName) => SetCulture(CultureInfo.GetCultureInfo(cultureName));

        public void SetCulture(CultureInfo culture)
        {
            CurrentCulture = culture;
            CultureChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task SetCultureAsync(string cultureName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetCulture(cultureName);
            return Task.CompletedTask;
        }

        public string T(string key)
        {
            var values = CurrentCulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? _chineseValues
                : _values;
            return values.TryGetValue(key, out var value) ? value : $"[[{key}]]";
        }
    }
}
