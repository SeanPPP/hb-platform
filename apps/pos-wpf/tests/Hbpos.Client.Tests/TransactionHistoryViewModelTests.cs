using BlazorApp.Shared.DTOs;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Contracts.Installments;
using Hbpos.Contracts.Orders;
using InstallmentPaymentDto = Hbpos.Contracts.Installments.InstallmentPaymentDto;

namespace Hbpos.Client.Tests;

public sealed class TransactionHistoryViewModelTests
{
    [Theory]
    [InlineData("Synced", true)]
    [InlineData("Pending", true)]
    [InlineData("Failed", true)]
    [InlineData("Syncing", false)]
    public void Local_order_reupload_eligibility_matches_sync_status(string status, bool expected)
    {
        var item = new HistoryOrderListItem(
            Guid.NewGuid(), TransactionHistorySource.LocalOrders, "S001", "POS-01", "Alice",
            DateTimeOffset.UtcNow, 1m, 0m, 1m, 1, "Cash", status, SyncStatus: status);

        Assert.Equal(expected, item.CanReupload);
        Assert.False((item with { Source = TransactionHistorySource.RemoteOrders }).CanReupload);
        Assert.False((item with { IsSuspendedOrder = true }).CanReupload);
        Assert.False((item with { IsInstallmentOrder = true }).CanReupload);
    }

    [Fact]
    public async Task Reupload_selected_executes_ids_and_refreshes_history_status()
    {
        var orderGuid = Guid.NewGuid();
        var failed = new LocalOrderSummary(
            orderGuid, "S001", "POS-01", "Alice", DateTimeOffset.UtcNow,
            10m, 0m, 10m, "Failed", 1, "Cash");
        var receiptQuery = new CapturingReceiptQueryService { Orders = [failed] };
        var executor = new CallbackOrderExecutor(ids =>
        {
            receiptQuery.Orders = [failed with { SyncStatus = "Synced" }];
            return new OrderUploadExecutionResult(ids.Count, ids.Count, 0);
        });
        var viewModel = new TransactionHistoryViewModel(
            receiptQuery,
            null,
            null,
            CreateSession(),
            orderUploadExecutionService: executor);
        await viewModel.LoadAsync();
        viewModel.Orders[0].Selection.IsSelected = true;

        await viewModel.ReuploadSelectedCommand.ExecuteAsync(null);

        Assert.Equal([orderGuid], executor.SelectedIds);
        var refreshed = Assert.Single(viewModel.Orders);
        Assert.Equal("Synced", refreshed.SyncStatus);
        Assert.False(refreshed.Selection.IsSelected);
    }

    [Fact]
    public async Task Reupload_selected_with_empty_selection_does_nothing()
    {
        var failed = new LocalOrderSummary(
            Guid.NewGuid(), "S001", "POS-01", "Alice", DateTimeOffset.UtcNow,
            10m, 0m, 10m, "Failed", 1, "Cash");
        var executor = new CallbackOrderExecutor(
            ids => new OrderUploadExecutionResult(ids.Count, ids.Count, 0));
        var viewModel = new TransactionHistoryViewModel(
            new CapturingReceiptQueryService { Orders = [failed] },
            null,
            null,
            CreateSession(),
            orderUploadExecutionService: executor);
        await viewModel.LoadAsync();

        await viewModel.ReuploadSelectedCommand.ExecuteAsync(null);

        Assert.Equal(0, executor.SelectedCallCount);
        Assert.Equal(string.Empty, viewModel.StatusMessage);
    }

    [Fact]
    public void Constructor_initializes_readonly_store_and_terminal_dropdown()
    {
        var session = CreateSession(deviceCode: "POS-09");
        var viewModel = new TransactionHistoryViewModel(
            new CapturingReceiptQueryService(),
            new CapturingSuspendedOrderService(),
            new CapturingRemoteOrderHistoryService(),
            session);

        Assert.Equal("Main Store (S001)", viewModel.StoreFilterText);
        Assert.Collection(
            viewModel.TerminalOptions,
            option =>
            {
                Assert.Null(option.DeviceCode);
                Assert.Equal("All Terminals", option.Label);
            },
            option =>
            {
                Assert.Equal("POS-09", option.DeviceCode);
                Assert.Equal("POS-09", option.Label);
            });
        Assert.Equal("POS-09", viewModel.SelectedTerminalOption?.DeviceCode);
        Assert.Equal("POS-09", viewModel.TerminalFilterText);
        Assert.True(viewModel.IsLocalSourceSelected);
        Assert.False(viewModel.IsOnlineSourceSelected);
        Assert.True(viewModel.IsStandardSourceSelected);
    }

    [Fact]
    public void Session_change_preserves_all_terminal_selection_and_refreshes_current_terminal_option()
    {
        var viewModel = new TransactionHistoryViewModel(
            new CapturingReceiptQueryService(),
            new CapturingSuspendedOrderService(),
            new CapturingRemoteOrderHistoryService(),
            CreateSession(deviceCode: "POS-01"));
        viewModel.SelectedTerminalOption = viewModel.TerminalOptions.Single(option => option.DeviceCode is null);

        viewModel.Session = CreateSession(deviceCode: "POS-02", storeCode: "S002", storeName: "Second Store");

        Assert.Equal("Second Store (S002)", viewModel.StoreFilterText);
        Assert.Null(viewModel.SelectedTerminalOption?.DeviceCode);
        Assert.Equal("All Terminals", viewModel.TerminalFilterText);
        Assert.Contains(viewModel.TerminalOptions, option => option.DeviceCode == "POS-02");
    }

    [Fact]
    public async Task LoadAsync_passes_selected_terminal_filter_to_all_history_sources()
    {
        var receiptQuery = new CapturingReceiptQueryService();
        var suspendedOrders = new CapturingSuspendedOrderService();
        var remoteOrders = new CapturingRemoteOrderHistoryService();
        var viewModel = new TransactionHistoryViewModel(
            receiptQuery,
            suspendedOrders,
            remoteOrders,
            CreateSession(deviceCode: "POS-04"));

        await viewModel.LoadAsync();
        Assert.Equal("POS-04", receiptQuery.LastQuery?.DeviceCode);
        Assert.Equal("POS-04", suspendedOrders.LastDeviceCode);

        viewModel.SelectedTerminalOption = viewModel.TerminalOptions.Single(option => option.DeviceCode is null);
        await viewModel.LoadAsync();
        Assert.Null(receiptQuery.LastQuery?.DeviceCode);
        Assert.Null(suspendedOrders.LastDeviceCode);

        viewModel.IsOnlineSourceSelected = true;
        viewModel.SelectedTerminalOption = viewModel.TerminalOptions.Single(option => option.DeviceCode == "POS-04");
        await viewModel.LoadAsync();
        Assert.Equal("POS-04", remoteOrders.LastQuery?.DeviceCode);

        viewModel.SelectedTerminalOption = viewModel.TerminalOptions.Single(option => option.DeviceCode is null);
        await viewModel.LoadAsync();
        Assert.Null(remoteOrders.LastQuery?.DeviceCode);
    }

    [Fact]
    public async Task Local_history_merges_local_and_suspended_orders_and_sorts_descending()
    {
        var localOrderGuid = Guid.NewGuid();
        var suspendedOrderGuid = Guid.NewGuid();
        var receiptQuery = new CapturingReceiptQueryService
        {
            Orders =
            [
                new LocalOrderSummary(
                    localOrderGuid,
                    "S001",
                    "POS-01",
                    "Alice",
                    new DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.Zero),
                    16m,
                    1m,
                    15m,
                    "Synced",
                    2,
                    "Cash")
            ]
        };
        var suspendedOrders = new CapturingSuspendedOrderService
        {
            PendingOrders =
            [
                new SuspendedOrderSummary(
                    suspendedOrderGuid,
                    "S001",
                    "POS-02",
                    "Bob",
                    new DateTimeOffset(2026, 5, 10, 10, 0, 0, TimeSpan.Zero),
                    12m,
                    0m,
                    12m,
                    1,
                    SuspendedOrderStatus.Pending)
            ]
        };
        var viewModel = new TransactionHistoryViewModel(
            receiptQuery,
            suspendedOrders,
            new CapturingRemoteOrderHistoryService(),
            CreateSession())
        {
            DateFrom = new DateTime(2026, 5, 10),
            DateTo = new DateTime(2026, 5, 10)
        };

        await viewModel.LoadAsync();

        Assert.Collection(
            viewModel.Orders,
            order =>
            {
                Assert.Equal(suspendedOrderGuid, order.OrderGuid);
                Assert.True(order.IsSuspendedOrder);
                Assert.True(order.CanRecall);
                Assert.Equal("Suspended", order.PaymentSummary);
                Assert.Equal("Pending recall", order.StatusLabel);
            },
            order =>
            {
                Assert.Equal(localOrderGuid, order.OrderGuid);
                Assert.False(order.IsSuspendedOrder);
                Assert.False(order.CanRecall);
                Assert.Equal("Cash", order.PaymentSummary);
            });
    }

    [Fact]
    public async Task LoadAsync_applies_date_range_to_local_and_suspended_orders()
    {
        var receiptQuery = new CapturingReceiptQueryService();
        var suspendedOrders = new CapturingSuspendedOrderService
        {
            PendingOrders =
            [
                new SuspendedOrderSummary(
                    Guid.NewGuid(),
                    "S001",
                    "POS-01",
                    "Alice",
                    new DateTimeOffset(2026, 5, 3, 9, 0, 0, TimeSpan.Zero),
                    10m,
                    0m,
                    10m,
                    1,
                    SuspendedOrderStatus.Pending),
                new SuspendedOrderSummary(
                    Guid.NewGuid(),
                    "S001",
                    "POS-01",
                    "Alice",
                    new DateTimeOffset(2026, 5, 7, 9, 0, 0, TimeSpan.Zero),
                    20m,
                    0m,
                    20m,
                    1,
                    SuspendedOrderStatus.Pending)
            ]
        };
        var viewModel = new TransactionHistoryViewModel(
            receiptQuery,
            suspendedOrders,
            new CapturingRemoteOrderHistoryService(),
            CreateSession())
        {
            DateFrom = new DateTime(2026, 5, 5),
            DateTo = new DateTime(2026, 5, 6)
        };

        await viewModel.LoadAsync();

        Assert.Equal(new DateTime(2026, 5, 5), receiptQuery.LastQuery?.SoldFrom?.Date);
        Assert.Equal(new DateTime(2026, 5, 6), receiptQuery.LastQuery?.SoldTo?.Date);
        Assert.Empty(viewModel.Orders);
    }

    [Fact]
    public async Task Recall_order_command_uses_row_parameter_refreshes_list_and_invokes_callback()
    {
        var auditLogger = new RecordingOperationAuditLogger();
        var recalled = false;
        var suspendedOrderGuid = Guid.NewGuid();
        var suspendedOrders = new CapturingSuspendedOrderService
        {
            PendingOrders =
            [
                new SuspendedOrderSummary(
                    suspendedOrderGuid,
                    "S001",
                    "POS-01",
                    "Alice",
                    DateTimeOffset.Now,
                    10m,
                    0m,
                    10m,
                    1,
                    SuspendedOrderStatus.Pending)
            ]
        };
        var viewModel = new TransactionHistoryViewModel(
            new CapturingReceiptQueryService(),
            suspendedOrders,
            new CapturingRemoteOrderHistoryService(),
            CreateSession(),
            () =>
            {
                recalled = true;
                return Task.CompletedTask;
            },
            operationAuditLogger: auditLogger);

        await viewModel.LoadAsync();
        var suspendedOrder = Assert.Single(viewModel.Orders);

        Assert.True(viewModel.RecallOrderCommand.CanExecute(suspendedOrder));

        await viewModel.RecallOrderCommand.ExecuteAsync(suspendedOrder);

        Assert.True(recalled);
        Assert.Equal(suspendedOrderGuid, suspendedOrders.RecalledOrderGuid);
        Assert.Empty(viewModel.Orders);
        var auditEvent = Assert.Single(auditLogger.Events);
        Assert.Equal("ORDER_RECALL", auditEvent.OperationType);
        Assert.Equal("Succeeded", auditEvent.Outcome);
        Assert.Equal(suspendedOrderGuid.ToString("D"), auditEvent.OrderGuid);
        Assert.Equal(0m, auditEvent.BeforeActual);
        Assert.Equal(10m, auditEvent.AfterActual);
        Assert.Equal(10m, auditEvent.AmountDelta);
    }

    [Fact]
    public async Task Selected_suspended_order_shows_recall_action_and_hides_reprint()
    {
        var suspendedOrderGuid = Guid.NewGuid();
        var suspendedOrders = new CapturingSuspendedOrderService
        {
            PendingOrders =
            [
                new SuspendedOrderSummary(
                    suspendedOrderGuid,
                    "S001",
                    "POS-01",
                    "Alice",
                    DateTimeOffset.Now,
                    10m,
                    0m,
                    10m,
                    1,
                    SuspendedOrderStatus.Pending)
            ]
        };
        var viewModel = new TransactionHistoryViewModel(
            new CapturingReceiptQueryService(),
            suspendedOrders,
            new CapturingRemoteOrderHistoryService(),
            CreateSession());

        await viewModel.ShowSuspendedOrdersAsync();

        Assert.True(viewModel.IsRecallVisible);
        Assert.False(viewModel.IsReprintVisible);
        Assert.True(viewModel.RecallSelectedCommand.CanExecute(null));
        Assert.Equal(suspendedOrderGuid, viewModel.SelectedOrder?.OrderGuid);
        Assert.True(viewModel.SelectedOrder?.CanRecall);
    }

    [Fact]
    public async Task Remote_history_shows_reprint_hidden_and_hides_recall()
    {
        var viewModel = new TransactionHistoryViewModel(
            new CapturingReceiptQueryService(),
            new CapturingSuspendedOrderService(),
            new CapturingRemoteOrderHistoryService
            {
                QueryResult = new RemoteOrderHistoryResult(
                [
                    new RemoteOrderHistorySummary(
                        Guid.NewGuid(),
                        "S001",
                        "POS-01",
                        "Alice",
                        DateTimeOffset.Now,
                        12m,
                        0m,
                        12m,
                        1,
                        "Cash",
                        "Synced")
                ])
            },
            CreateSession());

        viewModel.IsOnlineSourceSelected = true;
        await viewModel.LoadAsync();

        Assert.False(viewModel.IsRecallVisible);
        Assert.False(viewModel.IsReprintVisible);
        Assert.False(viewModel.RecallSelectedCommand.CanExecute(null));
        Assert.False(viewModel.ReprintCommand.CanExecute(null));
    }

    [Fact]
    public async Task Local_history_selection_builds_formatter_backed_preview_and_reprint_event()
    {
        var orderGuid = Guid.NewGuid();
        var reprintRequested = false;
        var receiptQuery = new CapturingReceiptQueryService
        {
            Orders =
            [
                new LocalOrderSummary(
                    orderGuid,
                    "S001",
                    "POS-01",
                    "Alice",
                    new DateTimeOffset(2026, 5, 27, 9, 0, 0, TimeSpan.Zero),
                    5m,
                    0m,
                    5m,
                    "Synced",
                    1,
                    "Cash")
            ],
            Receipts =
            {
                [orderGuid] = new ReceiptDetails(
                    orderGuid,
                    "S001",
                    "POS-01",
                    "Alice",
                    new DateTimeOffset(2026, 5, 27, 9, 0, 0, TimeSpan.Zero),
                    5m,
                    0m,
                    5m,
                    [new ReceiptPreviewLine("Receipt Tea", "930001", 1m, 5m, 0m, 5m)],
                    [new ReceiptPaymentLine(PaymentMethodKind.Cash, 5m, null)])
            }
        };
        var viewModel = new TransactionHistoryViewModel(
            receiptQuery,
            new CapturingSuspendedOrderService(),
            new CapturingRemoteOrderHistoryService(),
            CreateSession());
        viewModel.ReprintRequested += (_, _) => reprintRequested = true;

        await viewModel.LoadAsync();

        Assert.True(viewModel.IsReprintVisible);
        Assert.True(viewModel.ReprintCommand.CanExecute(null));
        Assert.Contains(viewModel.ReceiptPreviewRows, row => row.Text.Contains("===== TAX INVOICE =====", StringComparison.Ordinal));

        viewModel.ReprintCommand.Execute(null);

        Assert.True(reprintRequested);
    }

    [Fact]
    public void Return_to_pos_command_invokes_callback()
    {
        var returned = false;
        var viewModel = new TransactionHistoryViewModel(
            new CapturingReceiptQueryService(),
            new CapturingSuspendedOrderService(),
            new CapturingRemoteOrderHistoryService(),
            CreateSession(),
            returnToPos: () => returned = true);

        Assert.True(viewModel.ReturnToPosCommand.CanExecute(null));

        viewModel.ReturnToPosCommand.Execute(null);

        Assert.True(returned);
    }

    [Fact]
    public async Task Suspended_order_labels_follow_localization_culture()
    {
        var suspendedOrderGuid = Guid.NewGuid();
        var localization = new LocalizationService();
        var suspendedOrders = new CapturingSuspendedOrderService
        {
            PendingOrders =
            [
                new SuspendedOrderSummary(
                    suspendedOrderGuid,
                    "S001",
                    "POS-01",
                    "Alice",
                    DateTimeOffset.Now,
                    10m,
                    0m,
                    10m,
                    1,
                    SuspendedOrderStatus.Pending)
            ]
        };
        var viewModel = new TransactionHistoryViewModel(
            new CapturingReceiptQueryService(),
            suspendedOrders,
            new CapturingRemoteOrderHistoryService(),
            CreateSession(),
            localization: localization);

        await viewModel.LoadAsync();
        Assert.Equal("Suspended", viewModel.SelectedOrder?.PaymentSummary);
        Assert.Equal("Local", viewModel.SourceOptions[0].Label);

        localization.SetCulture("zh-CN");

        Assert.Equal("\u6302\u5355", viewModel.SelectedOrder?.PaymentSummary);
        Assert.Equal("\u5F85\u53D6\u56DE", viewModel.SelectedOrder?.StatusLabel);
        Assert.Equal("\u672C\u5730", viewModel.SourceOptions[0].Label);
    }

    [Fact]
    public void All_terminal_filter_text_uses_current_culture_when_selected()
    {
        var localization = new LocalizationService();
        var viewModel = new TransactionHistoryViewModel(
            new CapturingReceiptQueryService(),
            new CapturingSuspendedOrderService(),
            new CapturingRemoteOrderHistoryService(),
            CreateSession(deviceCode: "POS-01"),
            localization: localization);
        localization.SetCulture("zh-CN");

        viewModel.SelectedTerminalOption = viewModel.TerminalOptions.Single(option => option.DeviceCode is null);

        Assert.Equal("\u5168\u90E8\u7EC8\u7AEF", viewModel.TerminalFilterText);
    }

    [Fact]
    public async Task Installment_history_source_loads_orders_and_continues_payment()
    {
        var order = CreateInstallmentOrder("IO-20260703-0001", "张三", "0400111222", paidAmount: 30m, outstandingAmount: 90m);
        var localOrder = CreateLocalInstallmentOrder(order);
        InstallmentOrderSummary? continuedOrder = null;
        var viewModel = new TransactionHistoryViewModel(
            new CapturingReceiptQueryService(),
            new CapturingSuspendedOrderService(),
            new CapturingRemoteOrderHistoryService(),
            CreateSession(),
            installmentOrderService: new CapturingInstallmentOrderService
            {
                Orders = [order],
                LocalOrders = { [order.OrderId] = localOrder }
            },
            continueInstallmentPaymentAsync: selected =>
            {
                continuedOrder = selected;
                return Task.CompletedTask;
            });

        viewModel.IsInstallmentSourceSelected = true;
        await viewModel.LoadAsync();

        Assert.False(viewModel.IsStandardSourceSelected);
        var row = Assert.Single(viewModel.Orders);
        Assert.True(row.IsInstallmentOrder);
        Assert.Equal(order.OrderNumber, row.DisplayOrderId);
        Assert.Equal(order.CustomerName, row.CashierName);
        Assert.Equal(order.OutstandingAmount, row.ActualAmount);
        Assert.True(row.CanContinueInstallmentPayment);
        Assert.True(viewModel.IsContinueInstallmentPaymentVisible);
        Assert.True(viewModel.ContinueInstallmentPaymentCommand.CanExecute(row));
        Assert.Contains(viewModel.ReceiptPreviewRows, preview => preview.Text.Contains(order.OrderNumber, StringComparison.Ordinal));
        Assert.Contains(viewModel.ReceiptPreviewRows, preview => preview.Text.Contains(order.CustomerName, StringComparison.Ordinal));
        Assert.Contains(viewModel.ReceiptPreviewRows, preview => preview.Text.Contains("TAX INVOICE", StringComparison.Ordinal));
        Assert.Contains(viewModel.ReceiptPreviewRows, preview => preview.Text.Contains("Receipt Tea", StringComparison.Ordinal));
        Assert.Contains(viewModel.ReceiptPreviewRows, preview => preview.Text.Contains("Cash", StringComparison.Ordinal) && preview.Text.Contains("$30.00", StringComparison.Ordinal));
        Assert.Contains(viewModel.ReceiptPreviewRows, preview => preview.Text.Contains("Balance due", StringComparison.Ordinal) && preview.Text.Contains("$90.00", StringComparison.Ordinal));

        await viewModel.ContinueInstallmentPaymentCommand.ExecuteAsync(row);

        Assert.Same(order, continuedOrder);
    }

    [Fact]
    public async Task Installment_history_paid_off_order_hides_continue_payment()
    {
        var order = CreateInstallmentOrder("IO-20260703-0002", "李四", "0400333444", paidAmount: 120m, outstandingAmount: 0m);
        var localOrder = CreateLocalInstallmentOrder(order);
        var viewModel = new TransactionHistoryViewModel(
            new CapturingReceiptQueryService(),
            new CapturingSuspendedOrderService(),
            new CapturingRemoteOrderHistoryService(),
            CreateSession(),
            installmentOrderService: new CapturingInstallmentOrderService
            {
                Orders = [order],
                LocalOrders = { [order.OrderId] = localOrder }
            },
            continueInstallmentPaymentAsync: _ => Task.CompletedTask);

        viewModel.IsInstallmentSourceSelected = true;
        await viewModel.LoadAsync();

        var row = Assert.Single(viewModel.Orders);
        Assert.False(row.CanContinueInstallmentPayment);
        Assert.False(viewModel.IsContinueInstallmentPaymentVisible);
        Assert.False(viewModel.ContinueInstallmentPaymentCommand.CanExecute(row));
        Assert.Contains(viewModel.ReceiptPreviewRows, preview => preview.Text.Contains(order.OrderNumber, StringComparison.Ordinal));
        Assert.Contains(viewModel.ReceiptPreviewRows, preview => preview.Text.Contains("TAX INVOICE", StringComparison.Ordinal));
        Assert.Contains(viewModel.ReceiptPreviewRows, preview => preview.Text.Contains("Balance due", StringComparison.Ordinal) && preview.Text.Contains("$0.00", StringComparison.Ordinal));
        Assert.Contains(viewModel.ReceiptPreviewRows, preview => preview.Text.Contains("Pickup: Pending", StringComparison.Ordinal));
        Assert.DoesNotContain(viewModel.ReceiptPreviewRows, preview => preview.Text.Contains(nameof(InstallmentStatus.PaidOff), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Installment_history_paid_off_order_confirms_pickup_from_selected_row()
    {
        var order = CreateInstallmentOrder("IO-20260703-0003", "BBB", "0430990026", paidAmount: 55m, outstandingAmount: 0m);
        var installmentService = new CapturingInstallmentOrderService { Orders = [order] };
        var viewModel = new TransactionHistoryViewModel(
            new CapturingReceiptQueryService(),
            new CapturingSuspendedOrderService(),
            new CapturingRemoteOrderHistoryService(),
            CreateSession(),
            installmentOrderService: installmentService);

        viewModel.IsInstallmentSourceSelected = true;
        await viewModel.LoadAsync();

        var row = Assert.Single(viewModel.Orders);
        Assert.True(row.CanConfirmInstallmentPickup);
        Assert.True(viewModel.IsConfirmInstallmentPickupVisible);
        Assert.True(viewModel.ConfirmInstallmentPickupCommand.CanExecute(row));
        Assert.Contains(viewModel.ReceiptPreviewRows, preview => preview.Text.Contains("TAX INVOICE", StringComparison.Ordinal));
        Assert.DoesNotContain(viewModel.ReceiptPreviewRows, preview => preview.Text.Contains("===== INSTALLMENT =====", StringComparison.Ordinal));

        await viewModel.ConfirmInstallmentPickupCommand.ExecuteAsync(row);

        Assert.Equal(order.OrderId, installmentService.LastConfirmPickupOrderId);
        Assert.Equal("confirmed", viewModel.StatusMessage);
    }

    private static PosSessionState CreateSession(
        string storeCode = "S001",
        string storeName = "Main Store",
        string deviceCode = "POS-01")
    {
        return new PosSessionState("HB POS", storeCode, storeName, deviceCode, "C001", "Alice", true, 0);
    }

    private static InstallmentOrderSummary CreateInstallmentOrder(
        string orderNumber,
        string customerName,
        string phone,
        decimal paidAmount,
        decimal outstandingAmount)
    {
        return new InstallmentOrderSummary(
            Guid.NewGuid(),
            orderNumber,
            customerName,
            phone,
            paidAmount + outstandingAmount,
            20m,
            paidAmount,
            outstandingAmount,
            0,
            outstandingAmount > 0m,
            outstandingAmount == 0m,
            outstandingAmount > 0m,
            outstandingAmount > 0m,
            outstandingAmount > 0m ? "待补款" : "待提货",
            "POS-01",
            DateTimeOffset.Now);
    }

    private static LocalInstallmentOrder CreateLocalInstallmentOrder(InstallmentOrderSummary order)
    {
        return new LocalInstallmentOrder(
            order.OrderId,
            order.OrderId,
            order.OrderNumber,
            "S001",
            order.DeviceCode,
            "C001",
            "Alice",
            order.CustomerName,
            order.CustomerPhone,
            DateTimeOffset.Now.AddMinutes(-5),
            order.UpdatedAt,
            order.TotalAmount,
            20m,
            order.DownPaymentAmount,
            order.PaidAmount,
            order.OutstandingAmount,
            order.OutstandingAmount > 0m ? InstallmentStatus.Active : InstallmentStatus.PaidOff,
            [new InstallmentLineDto(Guid.NewGuid(), "P001", null, "Receipt Tea", "930001", 1m, order.TotalAmount, 0m, order.TotalAmount)],
            [new InstallmentPaymentDto(Guid.NewGuid(), PaymentMethodKind.Cash, order.PaidAmount, null, InstallmentPaymentStatus.Recorded, DateTimeOffset.Now, "C001", order.DeviceCode)],
            null);
    }

    private sealed class CapturingReceiptQueryService : IReceiptQueryService
    {
        public IReadOnlyList<LocalOrderSummary> Orders { get; set; } = [];

        public Dictionary<Guid, ReceiptDetails> Receipts { get; } = [];

        public LocalOrderHistoryQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<LocalOrderSummary>> GetRecentOrdersAsync(int take = 50, CancellationToken cancellationToken = default)
        {
            return GetRecentOrdersAsync(new LocalOrderHistoryQuery(), take, cancellationToken);
        }

        public Task<IReadOnlyList<LocalOrderSummary>> GetRecentOrdersAsync(
            LocalOrderHistoryQuery query,
            int take = 50,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(Orders);
        }

        public Task<ReceiptDetails?> GetReceiptAsync(Guid orderGuid, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Receipts.TryGetValue(orderGuid, out var receipt) ? receipt : null);
        }

        public Task<ReceiptDetails?> GetLatestReceiptAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Receipts.Values.FirstOrDefault());
        }
    }

    private sealed class CapturingSuspendedOrderService : ISuspendedOrderService
    {
        public IReadOnlyList<SuspendedOrderSummary> PendingOrders { get; set; } = [];

        public string? LastDeviceCode { get; private set; }

        public Guid? RecalledOrderGuid { get; private set; }

        public Task<SuspendedOrder> SuspendCurrentOrderAsync(PosSessionState session, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<SuspendedOrderSummary>> GetPendingOrdersAsync(
            string storeCode,
            string? deviceCode = null,
            string? keyword = null,
            int take = 100,
            CancellationToken cancellationToken = default)
        {
            LastDeviceCode = deviceCode;
            return Task.FromResult(PendingOrders);
        }

        public Task<SuspendedOrder?> GetOrderAsync(Guid suspendedOrderGuid, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<SuspendedOrder?>(null);
        }

        public Task<SuspendedOrder> RecallOrderAsync(Guid suspendedOrderGuid, CancellationToken cancellationToken = default)
        {
            RecalledOrderGuid = suspendedOrderGuid;
            PendingOrders = [];
            return Task.FromResult(new SuspendedOrder(
                suspendedOrderGuid,
                "S001",
                "POS-01",
                "C001",
                "Alice",
                DateTimeOffset.Now,
                10m,
                0m,
                10m,
                SuspendedOrderStatus.Recalled,
                []));
        }
    }

    private sealed class CapturingRemoteOrderHistoryService : IRemoteOrderHistoryService
    {
        public RemoteOrderHistoryResult QueryResult { get; init; } = new([]);

        public RemoteOrderHistoryQuery? LastQuery { get; private set; }

        public Task<RemoteOrderHistoryResult> QueryAsync(RemoteOrderHistoryQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(QueryResult);
        }

        public Task<ReceiptDetails?> GetDetailsAsync(Guid orderGuid, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ReceiptDetails?>(null);
        }

        public Task<OrderReturnContextDto?> GetReturnContextAsync(Guid orderGuid, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OrderReturnContextDto?>(null);
        }

        public Task<OrderReturnRecordCreateResponse> CreateReturnRecordsAsync(
            OrderReturnRecordCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OrderReturnRecordCreateResponse(request.ReturnOrderGuid, []));
        }
    }

    private sealed class CallbackOrderExecutor(
        Func<IReadOnlyCollection<Guid>, OrderUploadExecutionResult> execute) : IOrderUploadExecutionService
    {
        public IReadOnlyList<Guid> SelectedIds { get; private set; } = [];

        public int SelectedCallCount { get; private set; }

        public Task<OrderUploadExecutionResult> ExecuteOneAsync(Guid orderGuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(execute([orderGuid]));

        public Task<OrderUploadExecutionResult> ExecutePendingAsync(int batchSize = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OrderUploadExecutionResult(0, 0, 0));

        public Task<OrderUploadExecutionResult> ExecuteSelectedAsync(
            IReadOnlyCollection<Guid> orderGuids,
            CancellationToken cancellationToken = default)
        {
            SelectedCallCount++;
            SelectedIds = orderGuids.ToArray();
            return Task.FromResult(execute(orderGuids));
        }
    }

    private sealed class RecordingOperationAuditLogger : IOperationAuditLogger
    {
        public List<OperationAuditEventDto> Events { get; } = [];

        public void Record(OperationAuditEventDto auditEvent)
        {
            Events.Add(auditEvent);
        }
    }

    private sealed class CapturingInstallmentOrderService : IInstallmentOrderService
    {
        public IReadOnlyList<InstallmentOrderSummary> Orders { get; init; } = [];

        public Dictionary<Guid, LocalInstallmentOrder> LocalOrders { get; } = [];

        public Guid? LastConfirmPickupOrderId { get; private set; }

        public Task<IReadOnlyList<InstallmentOrderSummary>> GetOrdersAsync(PosSessionState session, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Orders);
        }

        public Task<IReadOnlyList<InstallmentOrderSummary>> SearchAsync(PosSessionState session, string? keyword, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Orders);
        }

        public Task<LocalInstallmentOrder?> GetLocalOrderAsync(Guid installmentGuid, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LocalOrders.TryGetValue(installmentGuid, out var order) ? order : null);
        }

        public Task<InstallmentWriteResult<InstallmentCreateResponse>> CreateAsync(PosSessionState session, InstallmentCreateRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<InstallmentWriteResult<InstallmentAppendPaymentResponse>> AppendPaymentAsync(PosSessionState session, InstallmentAppendPaymentRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<InstallmentWriteResult<InstallmentConfirmPickupResponse>> ConfirmPickupAsync(PosSessionState session, InstallmentConfirmPickupRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<InstallmentWriteResult<InstallmentCancelResponse>> CancelWithRefundAsync(PosSessionState session, InstallmentCancelRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<InstallmentWriteResult<InstallmentVoidResponse>> VoidCancelAsync(PosSessionState session, InstallmentVoidRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<InstallmentOrderCreateResult> CreateOrderAsync(InstallmentOrderCreateRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<InstallmentOrderActionResult> AddRepaymentAsync(InstallmentOrderRepaymentRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<InstallmentOrderActionResult> CancelWithRefundAsync(Guid orderId, PosSessionState session, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<InstallmentOrderActionResult> VoidCancelAsync(Guid orderId, PosSessionState session, string? reason = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<InstallmentOrderActionResult> ConfirmPickupAsync(Guid orderId, PosSessionState session, CancellationToken cancellationToken = default)
        {
            LastConfirmPickupOrderId = orderId;
            var order = Orders.FirstOrDefault(order => order.OrderId == orderId);
            return Task.FromResult(new InstallmentOrderActionResult(order is not null, order is null ? "missing" : "confirmed", order));
        }
    }
}
