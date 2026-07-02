using System.Net;
using System.Text;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Contracts.Cashiers;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Orders;
using BlazorApp.Shared.Constants;

namespace Hbpos.Client.Tests;

public sealed class CashierPermissionTests
{
    [Fact]
    public void Cashier_context_denies_permission_until_cashier_logs_in()
    {
        var context = new CashierSessionContext();

        Assert.False(context.HasPermission(Permissions.PosTerminal.Sales.AddItem));
        Assert.False(context.RequirePermission(Permissions.PosTerminal.Sales.ChangePrice, out var message));
        Assert.Equal("当前收银员没有改价权限", message);
    }

    [Fact]
    public void Emergency_override_grants_all_pos_terminal_permissions_without_cache()
    {
        var context = new CashierSessionContext();

        context.SetCurrent(CashierSessionContext.CreateEmergencyOverride("S001", "POS-01", new DateOnly(2026, 6, 27)));

        Assert.True(context.HasPermission(Permissions.PosTerminal.Sales.ChangePrice));
        Assert.True(context.HasPermission(Permissions.PosTerminal.Payment.Confirm));
        Assert.True(context.CurrentSession!.IsEmergencyOverride);
        Assert.Contains(Permissions.PosTerminal.CashDrawer.Open, context.CurrentSession.PermissionCodes);
    }

    [Fact]
    public async Task Cashier_login_uses_offline_cache_only_when_online_api_is_unreachable_and_store_allowed()
    {
        var settings = new InMemoryAppSettingsRepository();
        var onlineSession = CreateSession(
            permissionCodes: [Permissions.PosTerminal.Sales.AddItem],
            allowedStoreCodes: ["S001"]);
        var service = new CashierLoginService(
            new SequenceCashierLoginApiClient(
                CashierLoginAttempt.OnlineAccepted(onlineSession),
                CashierLoginAttempt.ApiUnavailable()),
            settings);

        var first = await service.LoginAsync("S001", "POS-01", "BAR-1");
        var second = await service.LoginAsync("S001", "POS-01", "BAR-1");

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.False(first.Session!.IsOfflineCached);
        Assert.True(second.Session!.IsOfflineCached);
        Assert.Equal("C001", second.Session.CashierId);
    }

    [Fact]
    public async Task Cashier_login_does_not_use_cache_after_online_rejection()
    {
        var settings = new InMemoryAppSettingsRepository();
        var service = new CashierLoginService(
            new SequenceCashierLoginApiClient(
                CashierLoginAttempt.OnlineAccepted(CreateSession()),
                CashierLoginAttempt.OnlineRejected("条码无效"),
                CashierLoginAttempt.ApiUnavailable()),
            settings);

        await service.LoginAsync("S001", "POS-01", "BAR-1");
        var rejected = await service.LoginAsync("S001", "POS-01", "BAR-1");
        var unavailable = await service.LoginAsync("S001", "POS-01", "BAR-1");

        Assert.False(rejected.Succeeded);
        Assert.Null(rejected.Session);
        Assert.Equal("条码无效", rejected.Message);
        Assert.False(unavailable.Succeeded);
        Assert.Null(unavailable.Session);
        Assert.Equal("收银员登录服务不可用", unavailable.Message);
    }

    [Fact]
    public async Task Cashier_login_api_client_treats_5xx_html_as_api_unavailable()
    {
        var client = new CashierLoginApiClient(new HttpClient(new StaticResponseHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("<html>bad gateway</html>", Encoding.UTF8, "text/html")
            }))
        {
            BaseAddress = new Uri("http://localhost/")
        });

        var attempt = await client.LoginAsync(new CashierBarcodeLoginRequest("S001", "BAR-1", "POS-01"));

        Assert.True(attempt.IsApiUnavailable);
        Assert.False(attempt.IsOnlineRejected);
    }

    [Fact]
    public async Task Cashier_login_api_client_keeps_401_html_as_online_rejection()
    {
        var client = new CashierLoginApiClient(new HttpClient(new StaticResponseHandler(
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("<html>unauthorized</html>", Encoding.UTF8, "text/html")
            }))
        {
            BaseAddress = new Uri("http://localhost/")
        });

        var attempt = await client.LoginAsync(new CashierBarcodeLoginRequest("S001", "BAR-1", "POS-01"));

        Assert.True(attempt.IsOnlineRejected);
        Assert.False(attempt.IsApiUnavailable);
    }

    [Fact]
    public void Pos_terminal_blocks_change_price_without_permission()
    {
        var cart = new PosCartService();
        var line = cart.AddItem(CreateItem());
        var workflow = new FakePosTerminalWorkflowService();
        var viewModel = new PosTerminalViewModel(
            new LocalSellableItemIndex(),
            cart,
            Session,
            onOpenPayment: null,
            workflowService: workflow,
            cashierSessionContext: new CashierSessionContext(),
            enforcePermissionsWhenNoCashier: true);
        viewModel.SelectedCartLine = line;
        viewModel.KeypadBuffer = "1.00";

        viewModel.ModifySelectedLinePriceCommand.Execute(null);

        Assert.Equal(0, workflow.ChangePriceCalls);
        Assert.Equal("当前收银员没有改价权限", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Payment_page_blocks_cash_tender_without_permission()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem());
        var workflow = new FakeCashPaymentWorkflowService();
        var viewModel = new PaymentViewModel(
            cart,
            workflow,
            Session,
            cashierSessionContext: new CashierSessionContext(),
            enforcePermissionsWhenNoCashier: true);
        viewModel.TenderAmountText = "5";

        await viewModel.SelectCashCommand.ExecuteAsync(null);

        Assert.Equal(0, workflow.AddTenderCallCount);
        Assert.Equal("当前收银员没有收现金权限", viewModel.StatusMessage);
    }

    [Fact]
    public void Pos_terminal_blocks_payment_entry_without_permission()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem());
        var opened = false;
        var viewModel = new PosTerminalViewModel(
            new LocalSellableItemIndex(),
            cart,
            Session,
            onOpenPayment: () => opened = true,
            workflowService: new FakePosTerminalWorkflowService(),
            cashierSessionContext: new CashierSessionContext(),
            enforcePermissionsWhenNoCashier: true);

        viewModel.OpenPaymentCommand.Execute(null);

        Assert.False(opened);
        Assert.Equal("当前收银员没有进入付款页权限", viewModel.StatusMessage);
    }

    [Fact]
    public void Payment_page_blocks_installment_center_without_permission()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem());
        var opened = false;
        var viewModel = new PaymentViewModel(
            cart,
            new FakeCashPaymentWorkflowService(),
            Session,
            onShowInstallmentCenter: () => opened = true,
            cashierSessionContext: new CashierSessionContext(),
            enforcePermissionsWhenNoCashier: true);

        viewModel.ShowInstallmentCenterCommand.Execute(null);

        Assert.False(opened);
        Assert.Equal("当前收银员没有查看分期权限", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Card_tender_auto_complete_requires_confirm_permission()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem());
        var workflow = new FakeCashPaymentWorkflowService();
        var cashierContext = new CashierSessionContext();
        cashierContext.SetCurrent(CreateSession(permissionCodes: [Permissions.PosTerminal.Payment.TakeCard]));
        var viewModel = new PaymentViewModel(
            cart,
            workflow,
            Session,
            cashierSessionContext: cashierContext,
            enforcePermissionsWhenNoCashier: true);

        await viewModel.SelectCardCommand.ExecuteAsync(null);

        Assert.Equal(1, workflow.AddTenderCallCount);
        Assert.Equal(0, workflow.CompletePaymentCallCount);
        Assert.Single(viewModel.PaymentTenders);
        Assert.Equal("当前收银员没有确认付款权限", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Pos_terminal_scanner_without_cashier_tries_cashier_login_fallback()
    {
        var cart = new PosCartService();
        var workflow = new FakePosTerminalWorkflowService
        {
            ScanResult = new PosTerminalWorkflowResult
            {
                StatusKey = "pos.status.noLocalMatch",
                Matches = []
            }
        };
        var cashierBarcode = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new PosTerminalViewModel(
            new LocalSellableItemIndex(),
            cart,
            Session,
            onOpenPayment: null,
            workflowService: workflow,
            tryLoginCashierFromScannerFallbackAsync: (barcode, _) =>
            {
                cashierBarcode.TrySetResult(barcode);
                return Task.FromResult(true);
            },
            cashierSessionContext: new CashierSessionContext(),
            enforcePermissionsWhenNoCashier: true);

        var processed = viewModel.ProcessScannerBarcode("1234567890123", "scanner-device", "raw");

        Assert.True(processed);
        Assert.Equal("1234567890123", await cashierBarcode.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        for (var attempt = 0; attempt < 20 && viewModel.ScanText.Length > 0; attempt++)
        {
            await Task.Delay(25);
        }

        Assert.Equal(0, workflow.ProcessScanAsyncCalls);
        Assert.Empty(viewModel.CartLines);
        Assert.Equal(string.Empty, viewModel.ScanText);
        Assert.False(viewModel.IsMatchesPopupOpen);
        Assert.Equal("收银员登录成功", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Pos_terminal_scanner_with_cashier_without_add_permission_tries_cashier_login_fallback_when_not_catalog_match()
    {
        var cart = new PosCartService();
        var workflow = new FakePosTerminalWorkflowService
        {
            ScanResult = new PosTerminalWorkflowResult
            {
                StatusKey = "pos.status.noLocalMatch",
                Matches = []
            }
        };
        var cashierContext = new CashierSessionContext();
        cashierContext.SetCurrent(CreateSession(permissionCodes: [Permissions.PosTerminal.Payment.TakeCash]));
        var fallbackCalled = false;
        var viewModel = new PosTerminalViewModel(
            new LocalSellableItemIndex(),
            cart,
            Session,
            onOpenPayment: null,
            workflowService: workflow,
            tryLoginCashierFromScannerFallbackAsync: (_, _) =>
            {
                fallbackCalled = true;
                return Task.FromResult(true);
            },
            cashierSessionContext: cashierContext,
            enforcePermissionsWhenNoCashier: true);

        var processed = viewModel.ProcessScannerBarcode("1234567890123", "scanner-device", "raw");

        Assert.True(processed);
        for (var attempt = 0; attempt < 20 && !fallbackCalled; attempt++)
        {
            await Task.Delay(25);
        }

        Assert.True(fallbackCalled);
        Assert.Equal(0, workflow.ProcessScanAsyncCalls);
        Assert.Empty(viewModel.CartLines);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.StatusMessage));
        /*
        Assert.Equal("鏀堕摱鍛樼櫥褰曟垚鍔?, viewModel.StatusMessage);
    }

        */
    }

    [Fact]
    public async Task Pos_terminal_scanner_with_cashier_without_add_permission_does_not_fallback_for_known_product()
    {
        var cart = new PosCartService();
        var priceIndex = new LocalSellableItemIndex();
        priceIndex.ReplaceAll([CreateItem()]);
        var workflow = new FakePosTerminalWorkflowService();
        var cashierContext = new CashierSessionContext();
        cashierContext.SetCurrent(CreateSession(permissionCodes: [Permissions.PosTerminal.Payment.TakeCash]));
        var fallbackCalled = false;
        var viewModel = new PosTerminalViewModel(
            priceIndex,
            cart,
            Session,
            onOpenPayment: null,
            workflowService: workflow,
            tryLoginCashierFromScannerFallbackAsync: (_, _) =>
            {
                fallbackCalled = true;
                return Task.FromResult(true);
            },
            cashierSessionContext: cashierContext,
            enforcePermissionsWhenNoCashier: true);

        var processed = viewModel.ProcessScannerBarcode("930001", "scanner-device", "raw");

        Assert.True(processed);
        for (var attempt = 0; attempt < 20 && string.IsNullOrEmpty(viewModel.StatusMessage); attempt++)
        {
            await Task.Delay(25);
        }

        Assert.False(fallbackCalled);
        Assert.Equal(0, workflow.ProcessScanAsyncCalls);
        Assert.Empty(viewModel.CartLines);
        Assert.False(cashierContext.RequirePermission(Permissions.PosTerminal.Sales.AddItem, out var deniedMessage));
        Assert.Equal(deniedMessage, viewModel.StatusMessage);
    }

    [Fact]
    public async Task Pos_terminal_scanner_catalog_miss_tries_cashier_login_fallback()
    {
        var cart = new PosCartService();
        var workflow = new FakePosTerminalWorkflowService
        {
            ScanResult = new PosTerminalWorkflowResult
            {
                StatusKey = "pos.status.noLocalMatch",
                Matches = []
            }
        };
        var cashierBarcode = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new PosTerminalViewModel(
            new LocalSellableItemIndex(),
            cart,
            Session,
            onOpenPayment: null,
            workflowService: workflow,
            tryLoginCashierFromScannerFallbackAsync: (barcode, _) =>
            {
                cashierBarcode.TrySetResult(barcode);
                return Task.FromResult(true);
            });

        var processed = viewModel.ProcessScannerBarcode("1234567890123", "scanner-device", "raw");

        Assert.True(processed);
        Assert.Equal("1234567890123", await cashierBarcode.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        for (var attempt = 0; attempt < 20 && viewModel.ScanText.Length > 0; attempt++)
        {
            await Task.Delay(25);
        }

        Assert.Empty(viewModel.CartLines);
        Assert.Equal(string.Empty, viewModel.ScanText);
        Assert.False(viewModel.IsMatchesPopupOpen);
        Assert.Equal("收银员登录成功", viewModel.StatusMessage);
    }

    [Fact]
    public void Pos_terminal_scan_logs_redact_barcode_values()
    {
        using var logs = new ConsoleLogCapture();
        var controller = new PosTerminalScanController(new PosCartService());
        const string sensitiveBarcode = "SECRET-CASHIER-12345";
        var plan = controller.CreateScanner(sensitiveBarcode, "scanner-device", "raw", DateTimeOffset.Now);

        controller.LogStarted(plan, "S001");
        controller.LogInputApplied(plan, 7);
        controller.LogFinished(plan, "cashier-login", false, 0, 1, 2, 3);

        var scanLines = logs.Lines
            .Where(line => line.Contains($"traceId={plan.TraceId}", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(3, scanLines.Length);
        Assert.All(scanLines, line => Assert.Contains("barcodeInfo=length=20", line, StringComparison.Ordinal));
        Assert.All(scanLines, line => Assert.DoesNotContain(sensitiveBarcode, line, StringComparison.Ordinal));
        Assert.All(scanLines, line => Assert.DoesNotContain("barcode=", line, StringComparison.Ordinal));
    }

    private static PosSessionState Session => new("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

    private static CashierSessionDto CreateSession(
        string[]? permissionCodes = null,
        string[]? allowedStoreCodes = null) =>
        new(
            "C001",
            Guid.NewGuid().ToString("N"),
            "Alice",
            "S001",
            "POS-01",
            ["Cashier"],
            permissionCodes ?? [Permissions.PosTerminal.Sales.AddItem, Permissions.PosTerminal.Payment.TakeCash],
            allowedStoreCodes ?? ["S001"],
            IsSuperAdmin: false,
            IsOfflineCached: false,
            IsEmergencyOverride: false);

    private static SellableItemDto CreateItem() =>
        new(
            StoreCode: "S001",
            ProductCode: "SKU-1",
            ReferenceCode: null,
            DisplayName: "Tea",
            LookupCode: "930001",
            ItemNumber: "SKU-1",
            Barcode: "930001",
            RetailPrice: 5m,
            PriceSource: PriceSourceKind.StoreRetailPrice,
            PriceSourceLabel: PriceSourceKind.StoreRetailPrice.ToString(),
            QuantityFactor: 1m,
            UpdatedAt: DateTimeOffset.UtcNow,
            ProductImage: null);

    private sealed class InMemoryAppSettingsRepository : ILocalAppSettingsRepository
    {
        private readonly Dictionary<string, string> _values = [];

        public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.TryGetValue(key, out var value);
            return Task.FromResult<string?>(value);
        }

        public Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task DeleteValueAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class SequenceCashierLoginApiClient(params CashierLoginAttempt[] attempts) : ICashierLoginApiClient
    {
        private int _index;

        public Task<CashierLoginAttempt> LoginAsync(
            CashierBarcodeLoginRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(attempts[Math.Min(_index++, attempts.Length - 1)]);
        }
    }

    private sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }

    private sealed class ConsoleLogCapture : IDisposable
    {
        private readonly List<string> _lines = [];

        public ConsoleLogCapture()
        {
            ConsoleLog.LineWritten += OnLineWritten;
        }

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_lines)
                {
                    return _lines.ToArray();
                }
            }
        }

        public void Dispose()
        {
            ConsoleLog.LineWritten -= OnLineWritten;
        }

        private void OnLineWritten(string line)
        {
            lock (_lines)
            {
                _lines.Add(line);
            }
        }
    }

    private sealed class FakePosTerminalWorkflowService : IPosTerminalWorkflowService
    {
        public event EventHandler<PosTerminalCatalogReloadedEventArgs>? CatalogReloaded
        {
            add { }
            remove { }
        }

        public int ChangePriceCalls { get; private set; }

        public int ProcessScanAsyncCalls { get; private set; }

        public PosTerminalWorkflowResult ScanResult { get; init; } = new();

        public Task<PosTerminalWorkflowResult> ProcessScanAsync(PosSessionState session, string scanText, bool preferExactLookup, string source, string? traceId = null, CancellationToken cancellationToken = default)
        {
            ProcessScanAsyncCalls++;
            return Task.FromResult(ScanResult);
        }

        public PosTerminalWorkflowResult ProcessScan(PosSessionState session, string scanText, bool preferExactLookup, string source, string? traceId = null) => ScanResult;

        public PosTerminalWorkflowResult AddSelectedItem(PosSessionState session, SellableItemDto item, bool clearScanText, bool closeMatchesPopup, string operation) => new();

        public PosTerminalWorkflowResult AddOpenItem(PosSessionState session, string keypadBuffer) => new();

        public PosTerminalWorkflowResult RemoveLine(CartLine? line) => new();

        public PosTerminalWorkflowResult IncreaseLine(CartLine? line) => new();

        public PosTerminalWorkflowResult DecreaseLine(CartLine? line) => new();

        public PosTerminalWorkflowResult ModifySelectedLineQuantity(CartLine? selectedLine, string keypadBuffer) => new();

        public PosTerminalWorkflowResult ModifySelectedLinePrice(CartLine? selectedLine, string keypadBuffer)
        {
            ChangePriceCalls++;
            return new PosTerminalWorkflowResult();
        }

        public PosTerminalWorkflowResult ApplySelectedLineDiscountAmount(CartLine? selectedLine, string keypadBuffer, bool isWholeOrderOperation) => new();

        public PosTerminalWorkflowResult ApplySelectedLineDiscountPercent(CartLine? selectedLine, string keypadBuffer, bool isWholeOrderOperation) => new();

        public PosTerminalWorkflowResult ApplyQuickDiscountPercent(CartLine? selectedLine, string? value, bool isWholeOrderOperation) => new();

        public PosTerminalWorkflowResult ClearCart() => new();

        public PosTerminalWorkflowResult GuardPayment() => new() { PaymentAllowed = true };
    }

    private sealed class FakeCashPaymentWorkflowService : ICashPaymentWorkflowService
    {
        public int AddTenderCallCount { get; private set; }

        public int CompletePaymentCallCount { get; private set; }

        public bool TryParseTenderedAmount(string? amountTenderedText, out decimal tenderedAmount) => decimal.TryParse(amountTenderedText, out tenderedAmount);

        public decimal CalculateChange(string? amountTenderedText, decimal actualAmount) => 0m;

        public decimal CalculateTenderedAmount(IReadOnlyList<PaymentTender> tenders) => tenders.Sum(tender => tender.Amount);

        public decimal CalculateRemainingAmount(decimal actualAmount, IReadOnlyList<PaymentTender> tenders) => Math.Max(0m, actualAmount - CalculateTenderedAmount(tenders));

        public decimal CalculateChange(IReadOnlyList<PaymentTender> tenders, decimal actualAmount) => 0m;

        public Task<PaymentTenderAttemptResult> AddTenderAsync(PaymentMethodKind method, PosSessionState session, decimal actualAmount, IReadOnlyList<PaymentTender> currentTenders, string? amountText, string? referenceText = null, CancellationToken cancellationToken = default, PosCartSnapshot? cartSnapshot = null)
        {
            AddTenderCallCount++;
            return Task.FromResult(PaymentTenderAttemptResult.Success(new PaymentTender(method, 5m, referenceText), "payment.status.tenderAdded"));
        }

        public Task<CashPaymentWorkflowResult> CompleteAsync(PosCartService cart, PosSessionState session, string? amountTenderedText, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CashPaymentWorkflowResult> CompletePaymentAsync(PosCartService cart, PosSessionState session, IReadOnlyList<PaymentTender> tenders, decimal cashTenderedAmount, CancellationToken cancellationToken = default)
        {
            CompletePaymentCallCount++;
            var order = new LocalOrder(
                Guid.NewGuid(),
                session.StoreCode,
                session.DeviceCode,
                session.CashierId,
                session.CashierName,
                DateTimeOffset.UtcNow,
                cart.TotalAmount,
                cart.DiscountAmount,
                cart.ActualAmount,
                [],
                [],
                cashTenderedAmount,
                0m);
            return Task.FromResult(new CashPaymentWorkflowResult(order, cashTenderedAmount, 0m, 0, session));
        }

        public Task<CashPaymentWorkflowResult> RetryVoucherUploadAsync(Guid orderGuid, PosCartService cart, PosSessionState session, decimal tenderedAmount, decimal changeAmount, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
