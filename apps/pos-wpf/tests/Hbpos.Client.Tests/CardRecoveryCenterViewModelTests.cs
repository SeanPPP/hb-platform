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

[Collection(ConsoleLogGlobalStateTestCollection.Name)]
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
        Assert.Equal([first.Key, second.Key], viewModel.OpenAttempts.Select(item => item.Key));
        Assert.Equal("Card sale", viewModel.OpenAttempts[0].OperationKind);
        Assert.Equal("Needs supervisor review", viewModel.OpenAttempts[0].Status);
        Assert.Equal(first.Key, viewModel.SelectedAttempt?.Key);
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
        Assert.Equal([first.Key], viewModel.OpenAttempts.Select(item => item.Key));
        Assert.Equal(first.Key, viewModel.SelectedAttempt?.Key);
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
    public async Task RecoverCommand_reports_operation_start_key_after_queue_refresh_clears_selection()
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
        var handled = new List<(CardRecoveryAttemptKey Key, CardPaymentRecoveryResult Result, int RemainingCount)>();
        var remainingCount = -1;
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            CreateLocalization(),
            openCountChanged: count => remainingCount = count,
            recoveryResultHandledAsync: (key, result) =>
            {
                handled.Add((key, result, remainingCount));
                return Task.CompletedTask;
            });
        await viewModel.LoadAsync();
        recovery.OpenItems = [];

        await viewModel.RecoverCommand.ExecuteAsync(null);

        var callback = Assert.Single(handled);
        Assert.Equal(selected.Key, callback.Key);
        Assert.Same(completed, callback.Result);
        Assert.Equal(0, callback.RemainingCount);
        Assert.Null(viewModel.SelectedAttempt);
    }

    [Fact]
    public async Task RecoverCommand_reports_result_when_post_operation_refresh_fails()
    {
        var selected = CreateQueueItem(
            CardProcessorKind.Linkly,
            Guid.Parse("42000000-0000-0000-0000-000000000006"),
            updatedAt: Now);
        var completed = new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.OrderCompleted,
            "Recovered order completed");
        var recovery = new RecordingRecoveryService
        {
            OpenItems = [selected],
            RecoverResult = completed
        };
        var handled = new List<(CardRecoveryAttemptKey Key, CardPaymentRecoveryResult Result)>();
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            CreateLocalization(),
            recoveryResultHandledAsync: (key, result) =>
            {
                handled.Add((key, result));
                return Task.CompletedTask;
            });
        await viewModel.LoadAsync();
        recovery.ListException = new InvalidOperationException("queue unavailable");

        await viewModel.RecoverCommand.ExecuteAsync(null);

        var callback = Assert.Single(handled);
        Assert.Equal(selected.Key, callback.Key);
        Assert.Same(completed, callback.Result);
        Assert.Equal("Could not refresh card transactions. queue unavailable", viewModel.StatusMessage);
        Assert.Equal(selected.Key, viewModel.SelectedAttempt?.Key);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task RecoverCommand_reports_result_when_refresh_failure_logging_subscriber_throws()
    {
        var selected = CreateQueueItem(
            CardProcessorKind.Linkly,
            Guid.Parse("42000000-0000-0000-0000-000000000008"),
            updatedAt: Now);
        var completed = new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.OrderCompleted,
            "Recovered order completed");
        var recovery = new RecordingRecoveryService
        {
            OpenItems = [selected],
            RecoverResult = completed
        };
        var handled = new List<(CardRecoveryAttemptKey Key, CardPaymentRecoveryResult Result)>();
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            CreateLocalization(),
            recoveryResultHandledAsync: (key, result) =>
            {
                handled.Add((key, result));
                return Task.CompletedTask;
            });
        await viewModel.LoadAsync();
        recovery.ListException = new InvalidOperationException("queue unavailable");
        void ThrowFromLog(string _) => throw new InvalidOperationException("log subscriber failed");

        ConsoleLog.LineWritten += ThrowFromLog;
        try
        {
            await viewModel.RecoverCommand.ExecuteAsync(null);
        }
        finally
        {
            ConsoleLog.LineWritten -= ThrowFromLog;
        }

        var callback = Assert.Single(handled);
        Assert.Equal(selected.Key, callback.Key);
        Assert.Same(completed, callback.Result);
        Assert.Equal("Could not refresh card transactions. queue unavailable", viewModel.StatusMessage);
        Assert.Equal(selected.Key, viewModel.SelectedAttempt?.Key);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task ResolveCommand_reports_operation_start_key_when_queue_refresh_changes_selection()
    {
        var selected = CreateQueueItem(
            CardProcessorKind.Linkly,
            Guid.Parse("42000000-0000-0000-0000-000000000004"),
            updatedAt: Now);
        var replacement = CreateQueueItem(
            CardProcessorKind.Square,
            Guid.Parse("42000000-0000-0000-0000-000000000005"),
            updatedAt: Now.AddMinutes(1));
        var completed = new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.OrderCompleted,
            "Confirmed payment completed");
        var recovery = new RecordingRecoveryService
        {
            OpenItems = [selected],
            ResolveResult = new CardRecoveryResolutionResult(
                true,
                "Resolution saved",
                completed)
        };
        var handled = new List<(
            CardRecoveryAttemptKey Key,
            CardPaymentRecoveryResult Result,
            CardRecoveryAttemptKey? SelectedKeyAtCallback)>();
        CardRecoveryCenterViewModel? callbackOwner = null;
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            CreateLocalization(),
            recoveryResultHandledAsync: (key, result) =>
            {
                handled.Add((key, result, callbackOwner?.SelectedAttempt?.Key));
                return Task.CompletedTask;
            });
        callbackOwner = viewModel;
        await viewModel.LoadAsync();
        viewModel.ResolutionReason = "Settlement checked";
        recovery.OpenItems = [replacement];

        await viewModel.ConfirmPaidCommand.ExecuteAsync(null);

        var callback = Assert.Single(handled);
        Assert.Equal(selected.Key, callback.Key);
        Assert.Same(completed, callback.Result);
        Assert.Equal(replacement.Key, callback.SelectedKeyAtCallback);
        Assert.Equal(replacement.Key, viewModel.SelectedAttempt?.Key);
    }

    [Fact]
    public async Task ResolveCommand_reports_result_when_post_operation_refresh_fails()
    {
        var selected = CreateQueueItem(
            CardProcessorKind.Linkly,
            Guid.Parse("42000000-0000-0000-0000-000000000007"),
            updatedAt: Now);
        var completed = new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.OrderCompleted,
            "Confirmed payment completed");
        var recovery = new RecordingRecoveryService
        {
            OpenItems = [selected],
            ResolveResult = new CardRecoveryResolutionResult(
                true,
                "Resolution saved",
                completed)
        };
        var handled = new List<(CardRecoveryAttemptKey Key, CardPaymentRecoveryResult Result)>();
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            CreateLocalization(),
            recoveryResultHandledAsync: (key, result) =>
            {
                handled.Add((key, result));
                return Task.CompletedTask;
            });
        await viewModel.LoadAsync();
        viewModel.ResolutionReason = "Settlement checked";
        recovery.ListException = new InvalidOperationException("queue unavailable");

        await viewModel.ConfirmPaidCommand.ExecuteAsync(null);

        var callback = Assert.Single(handled);
        Assert.Equal(selected.Key, callback.Key);
        Assert.Same(completed, callback.Result);
        Assert.Equal("Could not refresh card transactions. queue unavailable", viewModel.StatusMessage);
        Assert.Equal(selected.Key, viewModel.SelectedAttempt?.Key);
        Assert.False(viewModel.IsBusy);
    }

    [Theory]
    [InlineData(CardRecoverySupervisorDecision.ConfirmProcessed, "resolve/confirm-paid")]
    [InlineData(CardRecoverySupervisorDecision.ConfirmNotProcessed, "resolve/confirm-not-paid")]
    [InlineData(CardRecoverySupervisorDecision.ContinueWaiting, "resolve/continue-waiting")]
    public async Task Resolve_commands_use_one_shot_audit_authorization_and_target_selected_key(
        CardRecoverySupervisorDecision decision,
        string expectedAction)
    {
        var selected = CreateQueueItem(
            CardProcessorKind.Linkly,
            Guid.Parse("43000000-0000-0000-0000-000000000001"),
            updatedAt: Now,
            operationKind: "Refund");
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
        Assert.Equal(Permissions.PosTerminal.Audit.View, request.PermissionCode);
        Assert.Equal("card-recovery-center", request.Screen);
        Assert.Equal(expectedAction, request.Action);
        Assert.Equal(selected.Key, recovery.ResolvedKey);
        Assert.Equal(decision, recovery.ResolvedDecision);
        Assert.Equal("settlement checked", recovery.ResolvedReason);
        Assert.Equal("bank portal evidence", recovery.ResolvedEvidence);
        Assert.Equal("BANK-REF-1", recovery.ResolvedReference);
        Assert.Equal("SUPERVISOR", recovery.AuthorizingCashierIdDuringResolve);
        Assert.False(Assert.Single(authorization.IssuedScopes).IsActive);
        Assert.Empty(viewModel.OpenAttempts);
        Assert.Null(viewModel.SelectedAttempt);
        Assert.Equal([1, 0], counts);
        Assert.Equal("Resolution saved", viewModel.StatusMessage);
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
        Assert.Equal(refreshedSelected.Key, viewModel.SelectedAttempt?.Key);
        Assert.Equal("Pending", viewModel.SelectedAttempt?.Status);
        Assert.Equal([2, 2], counts);
        Assert.Equal("Loaded 2 open card transactions.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Culture_change_reprojects_open_attempts_and_preserves_selection_without_service_call()
    {
        var sale = CreateQueueItem(
            CardProcessorKind.Linkly,
            Guid.Parse("44000000-0000-0000-0000-000000000011"),
            updatedAt: Now,
            operationKind: "Sale",
            status: "RequiresReview");
        var refund = CreateQueueItem(
            CardProcessorKind.Square,
            Guid.Parse("44000000-0000-0000-0000-000000000012"),
            updatedAt: Now.AddMinutes(-1),
            operationKind: "Refund",
            status: "Unknown");
        var activeSession = CreateQueueItem(
            CardProcessorKind.Linkly,
            Guid.Parse("44000000-0000-0000-0000-000000000013"),
            updatedAt: Now.AddMinutes(-2),
            operationKind: "ActiveSession",
            status: "Recovering");
        var recovery = new RecordingRecoveryService { OpenItems = [sale, refund, activeSession] };
        var localization = CreateLocalization();
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            localization);
        await viewModel.LoadAsync();
        viewModel.SelectedAttempt = viewModel.OpenAttempts[1];
        var selectedKey = viewModel.SelectedAttempt.Key;

        localization.SetCulture("zh-CN");

        Assert.Equal(1, recovery.ListOpenCallCount);
        Assert.Equal(selectedKey, viewModel.SelectedAttempt?.Key);
        Assert.Equal(["卡收款", "卡退款", "活动终端会话"], viewModel.OpenAttempts.Select(item => item.OperationKind));
        Assert.Equal(["需要主管核对", "结果未知", "正在核对结果"], viewModel.OpenAttempts.Select(item => item.Status));
        Assert.Equal("卡退款", viewModel.SelectedTypeText);
        Assert.Equal("Square", viewModel.SelectedChannelText);
        Assert.Equal("结果未知", viewModel.SelectedStatusText);
        Assert.Equal("3 笔卡交易待处理", viewModel.OpenCountText);
    }

    [Fact]
    public async Task Unknown_recovery_values_use_safe_localized_fallbacks()
    {
        var unknown = CreateQueueItem(
            (CardProcessorKind)999,
            Guid.Parse("44000000-0000-0000-0000-000000000014"),
            updatedAt: Now,
            operationKind: "FutureOperation",
            status: "FutureStatus");
        var recovery = new RecordingRecoveryService { OpenItems = [unknown] };
        var localization = CreateLocalization();
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            localization);

        await viewModel.LoadAsync();

        Assert.Equal("Unknown", viewModel.OpenAttempts[0].OperationKind);
        Assert.Equal("Unknown", viewModel.OpenAttempts[0].Status);
        Assert.Equal("Unknown", viewModel.SelectedTypeText);
        Assert.Equal("Unknown", viewModel.SelectedChannelText);
        Assert.Equal("Unknown", viewModel.SelectedStatusText);

        localization.SetCulture("zh-CN");

        Assert.Equal(1, recovery.ListOpenCallCount);
        Assert.Equal("未知", viewModel.OpenAttempts[0].OperationKind);
        Assert.Equal("未知", viewModel.OpenAttempts[0].Status);
        Assert.Equal("未知", viewModel.SelectedTypeText);
        Assert.Equal("未知", viewModel.SelectedChannelText);
        Assert.Equal("未知", viewModel.SelectedStatusText);
    }

    [Theory]
    [InlineData("Declined", "Declined", "已拒绝")]
    [InlineData("TimedOut", "Timed out", "已超时")]
    [InlineData("Cancelled", "Cancelled", "已取消")]
    [InlineData("Canceled", "Cancelled", "已取消")]
    [InlineData("Failed", "Failed", "失败")]
    [InlineData("Abandoned", "Abandoned", "已放弃")]
    public async Task Known_open_recovery_status_is_bilingual(
        string rawStatus,
        string englishText,
        string chineseText)
    {
        var item = CreateQueueItem(
            CardProcessorKind.Linkly,
            Guid.NewGuid(),
            updatedAt: Now,
            operationKind: "ActiveSession",
            status: rawStatus);
        var recovery = new RecordingRecoveryService { OpenItems = [item] };
        var localization = CreateLocalization();
        using var viewModel = new CardRecoveryCenterViewModel(
            recovery,
            new PosCartService(),
            CreateSession(),
            new RecordingAuthorizationService(CreateCashier("SUPERVISOR")),
            localization);

        await viewModel.LoadAsync();

        Assert.Equal(englishText, viewModel.OpenAttempts[0].Status);
        Assert.Equal(englishText, viewModel.SelectedStatusText);

        localization.SetCulture("zh-CN");

        Assert.Equal(1, recovery.ListOpenCallCount);
        Assert.Equal(chineseText, viewModel.OpenAttempts[0].Status);
        Assert.Equal(chineseText, viewModel.SelectedStatusText);
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
        Assert.Equal("Card sale", viewModel.SelectedTypeText);
        Assert.Equal("Square", viewModel.SelectedChannelText);
        Assert.Equal("Needs supervisor review", viewModel.SelectedStatusText);
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
        Assert.Equal("{Binding OpenAttempts}", list.Attribute("ItemsSource")?.Value);
        Assert.Equal("{Binding SelectedAttempt, Mode=TwoWay}", list.Attribute("SelectedItem")?.Value);
        var listBindingValues = list.DescendantsAndSelf()
            .Attributes()
            .Select(attribute => attribute.Value)
            .ToArray();
        Assert.Contains("{Binding OperationKind}", listBindingValues);
        Assert.Contains("{Binding Processor}", listBindingValues);
        Assert.Contains("{Binding Status}", listBindingValues);
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
            "cardRecovery.center.value.unknown",
            "cardRecovery.center.type.sale",
            "cardRecovery.center.type.refund",
            "cardRecovery.center.type.activeSession",
            "cardRecovery.center.channel.linkly",
            "cardRecovery.center.channel.square",
            "cardRecovery.center.transactionStatus.pending",
            "cardRecovery.center.transactionStatus.sessionStarted",
            "cardRecovery.center.transactionStatus.recovering",
            "cardRecovery.center.transactionStatus.approved",
            "cardRecovery.center.transactionStatus.requiresReview",
            "cardRecovery.center.transactionStatus.orderCompleted",
            "cardRecovery.center.transactionStatus.checkoutCreated",
            "cardRecovery.center.transactionStatus.checkoutCompleted",
            "cardRecovery.center.transactionStatus.paymentVerified",
            "cardRecovery.center.transactionStatus.unknown",
            "cardRecovery.center.transactionStatus.declined",
            "cardRecovery.center.transactionStatus.timedOut",
            "cardRecovery.center.transactionStatus.cancelled",
            "cardRecovery.center.transactionStatus.failed",
            "cardRecovery.center.transactionStatus.abandoned",
            "cardRecovery.square.supervisorWaiting",
            "cardRecovery.square.notPaidCartNotEmpty",
            "cardRecovery.square.notPaidDraftInvalid",
            "cardRecovery.square.notPaidRestoreFailed",
            "cardRecovery.square.notPaidTerminalizeFailed",
            "cardRecovery.square.notPaidRetryAllowed",
            "payment.card.error.overlay.activeSession.openRecoveryCenter"
        };

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
        Assert.Equal(
            english.Keys.Order(StringComparer.Ordinal),
            chinese.Keys.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(english, pair => string.IsNullOrWhiteSpace(pair.Value));
        Assert.DoesNotContain(chinese, pair => string.IsNullOrWhiteSpace(pair.Value));
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
        Assert.Equal([item.Key], viewModel.OpenAttempts.Select(openItem => openItem.Key));
        Assert.Equal(item.Key, viewModel.SelectedAttempt?.Key);
        Assert.Equal("Could not refresh card transactions. terminal offline", viewModel.StatusMessage);
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

    private static CardRecoveryQueueItem CreateQueueItem(
        CardProcessorKind processor,
        Guid attemptGuid,
        DateTimeOffset updatedAt,
        string operationKind = "Sale",
        string status = "RequiresReview",
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
            status,
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
        new(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
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
                ["cardRecovery.center.value.none"] = "-",
                ["cardRecovery.center.value.unknown"] = "Unknown",
                ["cardRecovery.center.type.sale"] = "Card sale",
                ["cardRecovery.center.type.refund"] = "Card refund",
                ["cardRecovery.center.type.activeSession"] = "Active terminal session",
                ["cardRecovery.center.channel.linkly"] = "Linkly",
                ["cardRecovery.center.channel.square"] = "Square",
                ["cardRecovery.center.transactionStatus.pending"] = "Pending",
                ["cardRecovery.center.transactionStatus.sessionStarted"] = "Session started",
                ["cardRecovery.center.transactionStatus.recovering"] = "Checking result",
                ["cardRecovery.center.transactionStatus.approved"] = "Payment approved",
                ["cardRecovery.center.transactionStatus.requiresReview"] = "Needs supervisor review",
                ["cardRecovery.center.transactionStatus.orderCompleted"] = "Order completed",
                ["cardRecovery.center.transactionStatus.checkoutCreated"] = "Checkout created",
                ["cardRecovery.center.transactionStatus.checkoutCompleted"] = "Checkout completed",
                ["cardRecovery.center.transactionStatus.paymentVerified"] = "Payment verified",
                ["cardRecovery.center.transactionStatus.unknown"] = "Result unknown",
                ["cardRecovery.center.transactionStatus.declined"] = "Declined",
                ["cardRecovery.center.transactionStatus.timedOut"] = "Timed out",
                ["cardRecovery.center.transactionStatus.cancelled"] = "Cancelled",
                ["cardRecovery.center.transactionStatus.failed"] = "Failed",
                ["cardRecovery.center.transactionStatus.abandoned"] = "Abandoned"
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cardRecovery.center.openCount"] = "{0} 笔卡交易待处理",
                ["cardRecovery.center.value.none"] = "-",
                ["cardRecovery.center.value.unknown"] = "未知",
                ["cardRecovery.center.type.sale"] = "卡收款",
                ["cardRecovery.center.type.refund"] = "卡退款",
                ["cardRecovery.center.type.activeSession"] = "活动终端会话",
                ["cardRecovery.center.channel.linkly"] = "Linkly",
                ["cardRecovery.center.channel.square"] = "Square",
                ["cardRecovery.center.transactionStatus.pending"] = "等待处理",
                ["cardRecovery.center.transactionStatus.sessionStarted"] = "会话已开始",
                ["cardRecovery.center.transactionStatus.recovering"] = "正在核对结果",
                ["cardRecovery.center.transactionStatus.approved"] = "付款已批准",
                ["cardRecovery.center.transactionStatus.requiresReview"] = "需要主管核对",
                ["cardRecovery.center.transactionStatus.orderCompleted"] = "订单已完成",
                ["cardRecovery.center.transactionStatus.checkoutCreated"] = "结账已创建",
                ["cardRecovery.center.transactionStatus.checkoutCompleted"] = "结账已完成",
                ["cardRecovery.center.transactionStatus.paymentVerified"] = "付款已核实",
                ["cardRecovery.center.transactionStatus.unknown"] = "结果未知",
                ["cardRecovery.center.transactionStatus.declined"] = "已拒绝",
                ["cardRecovery.center.transactionStatus.timedOut"] = "已超时",
                ["cardRecovery.center.transactionStatus.cancelled"] = "已取消",
                ["cardRecovery.center.transactionStatus.failed"] = "失败",
                ["cardRecovery.center.transactionStatus.abandoned"] = "已放弃"
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
            return Task.FromResult(ResolveResult);
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

    private sealed class DictionaryLocalizationService(
        IReadOnlyDictionary<string, string> englishValues,
        IReadOnlyDictionary<string, string> chineseValues)
        : ILocalizationService
    {
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
            var values = CurrentCulture.Name == "zh-CN" ? chineseValues : englishValues;
            return values.TryGetValue(key, out var value) ? value : $"[[{key}]]";
        }
    }
}
