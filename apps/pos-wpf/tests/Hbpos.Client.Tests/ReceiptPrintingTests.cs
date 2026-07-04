using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Installments;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Tests;

public sealed class ReceiptPrintingTests
{
    [Fact]
    public void Installment_receipt_mapper_builds_deposit_receipt_document()
    {
        var depositTime = new DateTimeOffset(2026, 7, 4, 12, 30, 0, TimeSpan.Zero);
        var repaymentTime = new DateTimeOffset(2026, 7, 4, 12, 45, 0, TimeSpan.Zero);
        var order = new LocalInstallmentOrder(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            "IO-20260704-0001",
            "S001",
            "POS-01",
            "user-1",
            "Alice",
            "Bob Buyer",
            "0400111222",
            depositTime,
            new DateTimeOffset(2026, 7, 4, 12, 31, 0, TimeSpan.Zero),
            80m,
            20m,
            20m,
            35m,
            45m,
            InstallmentStatus.Active,
            [
                new InstallmentLineDto(
                    Guid.NewGuid(),
                    "SKU-INST",
                    null,
                    "Installment Tea",
                    "939003",
                    1m,
                    80m,
                    0m,
                    80m)
            ],
            [
                new InstallmentPaymentDto(
                    Guid.NewGuid(),
                    PaymentMethodKind.Cash,
                    20m,
                    "CASH",
                    InstallmentPaymentStatus.Recorded,
                    depositTime,
                    "user-1",
                    "POS-01"),
                new InstallmentPaymentDto(
                    Guid.NewGuid(),
                    PaymentMethodKind.Card,
                    15m,
                    "CARD-REF",
                    InstallmentPaymentStatus.Recorded,
                    repaymentTime,
                    "user-1",
                    "POS-01")
            ],
            null);
        var formatter = new ReceiptTextFormatter();

        var receipt = InstallmentReceiptMapper.CreateReceipt(order);
        var document = formatter.Build(receipt, ReceiptPrinterSettings.Default, order.CreatedAt);

        Assert.DoesNotContain("INSTALLMENT ORDER", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("TAX INVOICE", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("Installment No: IO-20260704-0001", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("IO-20260704-0001", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("Bob Buyer", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("0400111222", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("Deposit paid: $20.00", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("Balance due: $45.00", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("Payment history:", document.PlainText, StringComparison.Ordinal);
        Assert.Contains($"{depositTime.ToLocalTime():yyyy-MM-dd HH:mm} Cash $20.00", document.PlainText, StringComparison.Ordinal);
        Assert.Contains($"{repaymentTime.ToLocalTime():yyyy-MM-dd HH:mm} Card $15.00", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("Ref: CARD-REF", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("Installment Tea", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("Cash", document.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void Installment_receipt_mapper_prints_pending_pickup_for_paid_off_order()
    {
        var order = CreateInstallmentOrder(
            InstallmentStatus.PaidOff,
            paidAmount: 80m,
            balanceAmount: 0m);
        var formatter = new ReceiptTextFormatter();

        var receipt = InstallmentReceiptMapper.CreateReceipt(order);
        var document = formatter.Build(receipt, ReceiptPrinterSettings.Default, order.CreatedAt);

        Assert.Equal("*** Paid - Pickup Pending ***", receipt.StatusText);
        Assert.Contains("TAX INVOICE", document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("INSTALLMENT ORDER", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("Installment No: IO-20260704-0002", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("Balance due: $0.00", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("Pickup: Pending", document.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void Installment_receipt_mapper_does_not_mark_cancelled_zero_balance_order_as_pickup_pending()
    {
        var order = CreateInstallmentOrder(
            InstallmentStatus.Cancelled,
            paidAmount: 0m,
            balanceAmount: 0m);
        var formatter = new ReceiptTextFormatter();

        var receipt = InstallmentReceiptMapper.CreateReceipt(order);
        var document = formatter.Build(receipt, ReceiptPrinterSettings.Default, order.CreatedAt);

        Assert.Equal("*** Installment Cancelled ***", receipt.StatusText);
        Assert.DoesNotContain("Pickup: Pending", document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("Paid - Pickup Pending", document.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void Installment_receipt_mapper_prints_confirmed_pickup_details()
    {
        var pickedUpAt = new DateTimeOffset(2026, 7, 4, 13, 0, 0, TimeSpan.Zero);
        var order = CreateInstallmentOrder(
            InstallmentStatus.PickedUp,
            paidAmount: 80m,
            balanceAmount: 0m,
            pickupInfo: new InstallmentPickupInfoDto(pickedUpAt, "Alice", "Customer collected at counter"));
        var formatter = new ReceiptTextFormatter();

        var receipt = InstallmentReceiptMapper.CreateReceipt(order);
        var document = formatter.Build(receipt, ReceiptPrinterSettings.Default, order.CreatedAt);

        Assert.Equal("*** Paid - Picked Up ***", receipt.StatusText);
        Assert.Contains("Pickup: Confirmed", document.PlainText, StringComparison.Ordinal);
        Assert.Contains($"Picked up at: {pickedUpAt.ToLocalTime():yyyy-MM-dd HH:mm}", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("Picked up by: Alice", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("Pickup note: Customer collected at counter", document.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void Receipt_text_formatter_builds_print_commands_and_preview_from_same_document()
    {
        var orderGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var receipt = CreateReceipt(orderGuid);
        var settings = new ReceiptPrinterSettings(
            "USB,",
            "HotBargain",
            "Main Store",
            "1 Main Street Brisbane",
            "07 3000 0000",
            "12 345 678 901",
            "Keep receipt for refunds.",
            60);
        var formatter = new ReceiptTextFormatter();

        var document = formatter.Build(
            receipt,
            settings,
            new DateTimeOffset(2026, 5, 27, 10, 30, 0, TimeSpan.Zero));

        Assert.Contains(document.PreviewRows, row => row.Text == "HotBargain" && row.IsEmphasized && row.IsCentered);
        Assert.Contains(document.PreviewRows, row => row.Text.Contains("===== TAX INVOICE =====", StringComparison.Ordinal));
        Assert.Contains(document.PreviewRows, row => row.Text.Contains("Organic Gala Apples", StringComparison.Ordinal));
        Assert.Contains(document.PreviewRows, row => row.Text.Contains("GST", StringComparison.Ordinal));
        Assert.Contains(document.PreviewRows, row => row.Text.Contains("APPROVED CARD RECEIPT", StringComparison.Ordinal));
        Assert.Contains(document.Elements, element => element.Kind == ReceiptPrintElementKind.Barcode && element.Text == orderGuid.ToString());
        Assert.Contains(document.Elements, element => element.Kind == ReceiptPrintElementKind.QrCode && element.Text == orderGuid.ToString());
    }

    [Fact]
    public void Receipt_text_formatter_does_not_print_success_page_cash_change_preview_rows()
    {
        var receipt = CreateReceipt(Guid.NewGuid(), tenderedAmount: 10m, changeAmount: 1m);
        var formatter = new ReceiptTextFormatter();

        var document = formatter.Build(receipt, ReceiptPrinterSettings.Default, receipt.SoldAt);

        Assert.DoesNotContain(document.PreviewRows, row => row.Text.Contains("Tendered", StringComparison.Ordinal));
        Assert.DoesNotContain(document.PreviewRows, row => row.Text.Contains("Change", StringComparison.Ordinal));
        Assert.DoesNotContain(document.Elements, element => element.Text.Contains("Tendered", StringComparison.Ordinal));
        Assert.DoesNotContain(document.Elements, element => element.Text.Contains("Change", StringComparison.Ordinal));
    }

    [Fact]
    public void Receipt_text_formatter_prints_emergency_override_username_without_password_label()
    {
        var session = CashierSessionContext.CreateEmergencyOverride("S001", "POS-01", new DateOnly(2026, 6, 27));
        var receipt = CreateReceipt(Guid.NewGuid(), cashierName: session.CashierName);
        var formatter = new ReceiptTextFormatter();

        var document = formatter.Build(receipt, ReceiptPrinterSettings.Default, receipt.SoldAt);

        Assert.Contains("Cashier: EMERGENCY", document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("\u8d85\u7ea7\u5bc6\u7801", document.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void Receipt_text_formatter_prints_remaining_voucher_balance_as_complete_voucher_section()
    {
        var receipt = CreateReceipt(
            Guid.NewGuid(),
            paymentReference: "VOUCHER:VC200:LOCK-1:12.34",
            paymentMethod: PaymentMethodKind.Voucher);
        var formatter = new ReceiptTextFormatter();

        var document = formatter.Build(receipt, ReceiptPrinterSettings.Default, receipt.SoldAt);

        Assert.Contains("VOUCHER BALANCE", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("Voucher: VC200", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("Balance: $12.34", document.PlainText, StringComparison.Ordinal);
        Assert.Contains(document.Elements, element => element.Kind == ReceiptPrintElementKind.Barcode && element.Text == "VC200");
        Assert.Contains(document.Elements, element => element.Kind == ReceiptPrintElementKind.QrCode && element.Text == "VC200");
    }

    [Fact]
    public void Receipt_text_formatter_skips_remaining_voucher_section_without_positive_balance()
    {
        var receipt = CreateReceipt(
            Guid.NewGuid(),
            paymentReference: "VOUCHER:VC201:LOCK-1:0.00",
            paymentMethod: PaymentMethodKind.Voucher);
        var formatter = new ReceiptTextFormatter();

        var document = formatter.Build(receipt, ReceiptPrinterSettings.Default, receipt.SoldAt);

        Assert.DoesNotContain("VOUCHER BALANCE", document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain(document.Elements, element => element.Kind == ReceiptPrintElementKind.Barcode && element.Text == "VC201");
        Assert.DoesNotContain(document.Elements, element => element.Kind == ReceiptPrintElementKind.QrCode && element.Text == "VC201");
    }

    [Fact]
    public void Receipt_text_formatter_masks_full_pan_in_embedded_bank_receipt_text()
    {
        var receipt = CreateReceipt(
            Guid.NewGuid(),
            bankReceiptText:
                "APPROVED CARD RECEIPT\n" +
                "CARD 4111111111111234\n" +
                "ALT 4111 1111 1111 5678\n" +
                "DASH 4111-1111-1111-9999\n" +
                "TAB 4111\t1111\t1111\t2468\n" +
                "NBSP 4111\u00A01111\u00A01111\u00A01357\n" +
                "MULTI 4111  1111  1111  8642\n" +
                "DOT 4111.1111.1111.9753");
        var formatter = new ReceiptTextFormatter();

        var document = formatter.Build(receipt, ReceiptPrinterSettings.Default, receipt.SoldAt);

        Assert.Contains("CARD ****1234", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("ALT ****5678", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("DASH ****9999", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("TAB ****2468", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("NBSP ****1357", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("MULTI ****8642", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("DOT ****9753", document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("4111111111111234", document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("4111 1111 1111 5678", document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("4111-1111-1111-9999", document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("4111\t1111\t1111\t2468", document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("4111\u00A01111\u00A01111\u00A01357", document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("4111  1111  1111  8642", document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("4111.1111.1111.9753", document.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void Receipt_text_formatter_keeps_bank_reference_numbers_while_masking_pan()
    {
        var receipt = CreateReceipt(
            Guid.NewGuid(),
            bankReceiptText:
                "TXN REF 260601120038\n" +
                "RRN 123456789012\n" +
                "CARD 4111111111111234");
        var formatter = new ReceiptTextFormatter();

        var document = formatter.Build(receipt, ReceiptPrinterSettings.Default, receipt.SoldAt);

        Assert.Contains("TXN REF 260601120038", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("RRN 123456789012", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("CARD ****1234", document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("4111111111111234", document.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Receipt_print_service_prints_latest_receipt_with_configured_settings()
    {
        var receipt = CreateReceipt(Guid.NewGuid());
        var query = new FakeReceiptQueryService { LatestReceipt = receipt };
        var settingsStore = new FakeReceiptPrinterSettingsStore
        {
            Settings = ReceiptPrinterSettings.Default with
            {
                PrinterPort = "COM3",
                BrandName = "HotBargain",
                StoreName = "Main Store"
            }
        };
        var driver = new RecordingReceiptPrinterDriver();
        var service = new ReceiptPrintService(query, settingsStore, new ReceiptTextFormatter(), driver);

        var result = await service.PrintLatestReceiptAsync(ReceiptPrintReason.LastReceipt);

        Assert.True(result.Succeeded);
        Assert.Equal(receipt.OrderGuid, result.OrderGuid);
        Assert.NotNull(driver.LastDocument);
        Assert.Equal("COM3", driver.LastSettings?.PrinterPort);
        Assert.Contains(driver.LastDocument!.PreviewRows, row => row.Text == "Main Store");
        Assert.Contains(driver.LastDocument.PreviewRows, row => row.Text == $"Print Time: {receipt.SoldAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
    }

    [Fact]
    public async Task Receipt_print_service_returns_failure_when_latest_receipt_is_missing()
    {
        var driver = new RecordingReceiptPrinterDriver();
        var service = new ReceiptPrintService(
            new FakeReceiptQueryService(),
            new FakeReceiptPrinterSettingsStore(),
            new ReceiptTextFormatter(),
            driver);

        var result = await service.PrintLatestReceiptAsync(ReceiptPrintReason.LastReceipt);

        Assert.False(result.Succeeded);
        Assert.Null(result.OrderGuid);
        Assert.Null(driver.LastDocument);
    }

    [Fact]
    public async Task Receipt_print_service_returns_failure_when_driver_fails()
    {
        var receipt = CreateReceipt(Guid.NewGuid());
        var query = new FakeReceiptQueryService { LatestReceipt = receipt };
        var notifier = new RecordingCardReceiptPrintedNotifier();
        var driver = new RecordingReceiptPrinterDriver
        {
            PrintResult = new ReceiptPrinterDriverResult(false, "paper out")
        };
        var service = new ReceiptPrintService(
            query,
            new FakeReceiptPrinterSettingsStore(),
            new ReceiptTextFormatter(),
            driver,
            [notifier]);

        var result = await service.PrintReceiptAsync(receipt, ReceiptPrintReason.Manual);

        Assert.False(result.Succeeded);
        Assert.Equal("paper out", result.Message);
        Assert.Equal(receipt.OrderGuid, result.OrderGuid);
        Assert.Empty(notifier.Calls);
    }

    [Fact]
    public async Task Receipt_print_service_marks_linkly_backend_receipt_printed_after_driver_success()
    {
        var receipt = CreateReceipt(
            Guid.NewGuid(),
            paymentReference: "ANZBACKEND:260601120001:session=11111111-2222-3333-4444-555555555555:environment=Sandbox");
        var notifier = new RecordingCardReceiptPrintedNotifier();
        var service = new ReceiptPrintService(
            new FakeReceiptQueryService(),
            new FakeReceiptPrinterSettingsStore(),
            new ReceiptTextFormatter(),
            new RecordingReceiptPrinterDriver(),
            [notifier]);

        var result = await service.PrintReceiptAsync(receipt, ReceiptPrintReason.CardAuto);

        Assert.True(result.Succeeded);
        var call = Assert.Single(notifier.Calls);
        Assert.Equal("Sandbox", call.Environment);
        Assert.Equal("11111111-2222-3333-4444-555555555555", call.SessionId);
    }

    [Fact]
    public async Task Receipt_print_service_keeps_success_when_printed_marker_times_out()
    {
        var receipt = CreateReceipt(
            Guid.NewGuid(),
            paymentReference: "ANZBACKEND:260601120002:session=22222222-2222-4222-8222-222222222222:environment=Sandbox");
        var notifier = new RecordingCardReceiptPrintedNotifier
        {
            Exception = new TaskCanceledException("backend timeout")
        };
        var service = new ReceiptPrintService(
            new FakeReceiptQueryService(),
            new FakeReceiptPrinterSettingsStore(),
            new ReceiptTextFormatter(),
            new RecordingReceiptPrinterDriver(),
            [notifier]);

        var result = await service.PrintReceiptAsync(receipt, ReceiptPrintReason.CardAuto);

        Assert.True(result.Succeeded);
        Assert.Equal(receipt.OrderGuid, result.OrderGuid);
    }

    [Fact]
    public async Task Receipt_print_service_serializes_concurrent_prints()
    {
        var first = CreateReceipt(Guid.NewGuid());
        var second = CreateReceipt(Guid.NewGuid());
        var query = new FakeReceiptQueryService();
        query.Receipts[first.OrderGuid] = first;
        query.Receipts[second.OrderGuid] = second;
        var driver = new DelayedReceiptPrinterDriver();
        var service = new ReceiptPrintService(
            query,
            new FakeReceiptPrinterSettingsStore(),
            new ReceiptTextFormatter(),
            driver);

        await Task.WhenAll(
            service.PrintReceiptAsync(first.OrderGuid, ReceiptPrintReason.Manual),
            service.PrintReceiptAsync(second.OrderGuid, ReceiptPrintReason.Reprint));

        Assert.Equal(2, driver.PrintCount);
        Assert.Equal(1, driver.MaxConcurrentPrints);
    }

    [Fact]
    public async Task Cash_drawer_service_opens_with_configured_printer_settings()
    {
        var settingsStore = new FakeReceiptPrinterSettingsStore
        {
            Settings = ReceiptPrinterSettings.Default with { PrinterPort = "COM5" }
        };
        var driver = new RecordingReceiptPrinterDriver
        {
            OpenCashDrawerResult = new ReceiptPrinterDriverResult(true, "Cash drawer opened.")
        };
        var service = new CashDrawerService(settingsStore, driver);

        var result = await service.OpenAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("Cash drawer opened.", result.Message);
        Assert.Equal("COM5", driver.LastCashDrawerSettings?.PrinterPort);
        Assert.Equal(1, driver.OpenCashDrawerCallCount);
    }

    [Fact]
    public async Task Cash_drawer_service_returns_failure_when_driver_fails()
    {
        var driver = new RecordingReceiptPrinterDriver
        {
            OpenCashDrawerResult = new ReceiptPrinterDriverResult(false, "drawer offline")
        };
        var service = new CashDrawerService(new FakeReceiptPrinterSettingsStore(), driver);

        var result = await service.OpenAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("drawer offline", result.Message);
    }

    [Fact]
    public async Task Cash_drawer_service_returns_failure_when_driver_throws()
    {
        var driver = new RecordingReceiptPrinterDriver
        {
            OpenCashDrawerException = new InvalidOperationException("sdk missing")
        };
        var service = new CashDrawerService(new FakeReceiptPrinterSettingsStore(), driver);

        var result = await service.OpenAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("sdk missing", result.Message);
    }

    [Fact]
    public async Task Receipt_printer_settings_store_persists_fields_and_defaults_port()
    {
        var repository = new InMemorySettingsRepository();
        var store = new ReceiptPrinterSettingsStore(repository);
        var settings = ReceiptPrinterSettings.Default with
        {
            PrinterPort = "   ",
            BrandName = "HB",
            StoreName = "Sunnybank",
            StoreAddress = "Shop 1",
            StorePhone = "07",
            Abn = "ABN",
            ReturnPolicy = "Return within 7 days",
            CutDistance = 80
        };

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.Equal("USB,", loaded.PrinterPort);
        Assert.Equal("HB", loaded.BrandName);
        Assert.Equal("Sunnybank", loaded.StoreName);
        Assert.Equal("Shop 1", loaded.StoreAddress);
        Assert.Equal("07", loaded.StorePhone);
        Assert.Equal("ABN", loaded.Abn);
        Assert.Equal("Return within 7 days", loaded.ReturnPolicy);
        Assert.Equal(80, loaded.CutDistance);
    }

    private static ReceiptDetails CreateReceipt(
        Guid orderGuid,
        decimal? tenderedAmount = null,
        decimal? changeAmount = null,
        string paymentReference = "ANZ:123",
        string bankReceiptText = "APPROVED CARD RECEIPT",
        PaymentMethodKind paymentMethod = PaymentMethodKind.Card,
        string cashierName = "Alice")
    {
        var cardTransactions = paymentMethod == PaymentMethodKind.Card
            ? new[]
            {
                new CardTransactionDto(
                    "Linkly",
                    "TXN-1",
                    "AUTH1",
                    "VISA",
                    411111,
                    "****1111",
                    "M1",
                    "00",
                    "APPROVED",
                    "123456",
                    new DateTimeOffset(2026, 5, 27, 9, 1, 0, TimeSpan.Zero),
                    9.00m,
                    bankReceiptText)
            }
            : null;

        return new ReceiptDetails(
            orderGuid,
            "S001",
            "POS-01",
            cashierName,
            new DateTimeOffset(2026, 5, 27, 9, 0, 0, TimeSpan.Zero),
            9.20m,
            0.20m,
            9.00m,
            [
                new ReceiptPreviewLine("Organic Gala Apples", "690101", 2m, 2.50m, 0m, 5.00m),
                new ReceiptPreviewLine("Whole Grain Bread", "690102", 1m, 4.20m, 0.20m, 4.00m)
            ],
            [
                new ReceiptPaymentLine(
                    paymentMethod,
                    9.00m,
                    paymentReference,
                    cardTransactions)
            ],
            tenderedAmount,
            changeAmount);
    }

    private static LocalInstallmentOrder CreateInstallmentOrder(
        InstallmentStatus status,
        decimal paidAmount,
        decimal balanceAmount,
        InstallmentPickupInfoDto? pickupInfo = null)
    {
        var createdAt = new DateTimeOffset(2026, 7, 4, 12, 30, 0, TimeSpan.Zero);
        return new LocalInstallmentOrder(
            Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"),
            Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"),
            "IO-20260704-0002",
            "S001",
            "POS-01",
            "user-1",
            "Alice",
            "Bob Buyer",
            "0400111222",
            createdAt,
            createdAt,
            80m,
            20m,
            20m,
            paidAmount,
            balanceAmount,
            status,
            [
                new InstallmentLineDto(
                    Guid.NewGuid(),
                    "SKU-INST",
                    null,
                    "Installment Tea",
                    "939003",
                    1m,
                    80m,
                    0m,
                    80m)
            ],
            [
                new InstallmentPaymentDto(
                    Guid.NewGuid(),
                    PaymentMethodKind.Cash,
                    paidAmount,
                    "CASH",
                    InstallmentPaymentStatus.Recorded,
                    createdAt,
                    "user-1",
                    "POS-01")
            ],
            pickupInfo);
    }

    private sealed class RecordingCardReceiptPrintedNotifier : ICardReceiptPrintedNotifier
    {
        public List<(string Environment, string SessionId)> Calls { get; } = [];

        public Exception? Exception { get; init; }

        public Task MarkReceiptPrintedAsync(
            string environment,
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((environment, sessionId));
            if (Exception is not null)
            {
                return Task.FromException(Exception);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeReceiptQueryService : IReceiptQueryService
    {
        public ReceiptDetails? LatestReceipt { get; init; }

        public Dictionary<Guid, ReceiptDetails> Receipts { get; } = [];

        public Task<IReadOnlyList<LocalOrderSummary>> GetRecentOrdersAsync(int take = 50, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalOrderSummary>>([]);
        }

        public Task<IReadOnlyList<LocalOrderSummary>> GetRecentOrdersAsync(
            LocalOrderHistoryQuery query,
            int take = 50,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalOrderSummary>>([]);
        }

        public Task<ReceiptDetails?> GetReceiptAsync(Guid orderGuid, CancellationToken cancellationToken = default)
        {
            if (Receipts.TryGetValue(orderGuid, out var receipt))
            {
                return Task.FromResult<ReceiptDetails?>(receipt);
            }

            return Task.FromResult(LatestReceipt?.OrderGuid == orderGuid ? LatestReceipt : null);
        }

        public Task<ReceiptDetails?> GetLatestReceiptAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LatestReceipt);
        }
    }

    private sealed class FakeReceiptPrinterSettingsStore : IReceiptPrinterSettingsStore
    {
        public ReceiptPrinterSettings Settings { get; init; } = ReceiptPrinterSettings.Default;

        public Task<ReceiptPrinterSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Settings);
        }

        public Task SaveAsync(ReceiptPrinterSettings settings, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingReceiptPrinterDriver : IReceiptPrinterDriver
    {
        public ReceiptPrintDocument? LastDocument { get; private set; }

        public ReceiptPrinterSettings? LastSettings { get; private set; }

        public ReceiptPrinterSettings? LastCashDrawerSettings { get; private set; }

        public ReceiptPrinterDriverResult PrintResult { get; init; } = new(true, "printed");

        public ReceiptPrinterDriverResult OpenCashDrawerResult { get; init; } = new(true, "drawer opened");

        public Exception? OpenCashDrawerException { get; init; }

        public int OpenCashDrawerCallCount { get; private set; }

        public Task<ReceiptPrinterDriverResult> PrintAsync(
            ReceiptPrintDocument document,
            ReceiptPrinterSettings settings,
            CancellationToken cancellationToken = default)
        {
            LastDocument = document;
            LastSettings = settings;
            return Task.FromResult(PrintResult);
        }

        public Task<ReceiptPrinterDriverResult> TestAsync(
            ReceiptPrinterSettings settings,
            CancellationToken cancellationToken = default)
        {
            LastSettings = settings;
            return Task.FromResult(new ReceiptPrinterDriverResult(true, "tested"));
        }

        public Task<ReceiptPrinterDriverResult> OpenCashDrawerAsync(
            ReceiptPrinterSettings settings,
            CancellationToken cancellationToken = default)
        {
            OpenCashDrawerCallCount++;
            LastCashDrawerSettings = settings;
            if (OpenCashDrawerException is not null)
            {
                throw OpenCashDrawerException;
            }

            return Task.FromResult(OpenCashDrawerResult);
        }
    }

    private sealed class DelayedReceiptPrinterDriver : IReceiptPrinterDriver
    {
        private readonly object _gate = new();
        private int _activePrints;

        public int PrintCount { get; private set; }

        public int MaxConcurrentPrints { get; private set; }

        public async Task<ReceiptPrinterDriverResult> PrintAsync(
            ReceiptPrintDocument document,
            ReceiptPrinterSettings settings,
            CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _activePrints);
            lock (_gate)
            {
                PrintCount++;
                MaxConcurrentPrints = Math.Max(MaxConcurrentPrints, active);
            }

            try
            {
                await Task.Delay(40, cancellationToken);
                return new ReceiptPrinterDriverResult(true, "printed");
            }
            finally
            {
                Interlocked.Decrement(ref _activePrints);
            }
        }

        public Task<ReceiptPrinterDriverResult> TestAsync(
            ReceiptPrinterSettings settings,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ReceiptPrinterDriverResult(true, "tested"));
        }

        public Task<ReceiptPrinterDriverResult> OpenCashDrawerAsync(
            ReceiptPrinterSettings settings,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ReceiptPrinterDriverResult(true, "drawer opened"));
        }
    }

    private sealed class InMemorySettingsRepository : ILocalAppSettingsRepository
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);
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
}
