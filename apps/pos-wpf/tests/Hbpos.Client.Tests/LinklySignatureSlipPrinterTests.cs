using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class LinklySignatureSlipPrinterTests
{
    [Fact]
    public async Task PrintAsync_returns_success_when_backend_marker_fails_after_physical_print()
    {
        var driver = new RecordingReceiptPrinterDriver();
        var printer = new LinklyBankReceiptPrinter(
            new StaticReceiptPrinterSettingsStore(),
            driver,
            [new ThrowingCardReceiptPrintedNotifier()]);

        var result = await printer.PrintAsync(
            "Sandbox",
            "signature-session-1",
            "LINE2\nCREDIT ACCOUNT\nPURCHASE AUD $10.08\nAPPROVE WITH SIG - 08\nPLEASE SIGN:");

        Assert.True(result.Succeeded);
        var document = Assert.Single(driver.Documents);
        Assert.Contains("*** SIGNATURE REQUIRED ***", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("CUSTOMER SIGNATURE", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("PLEASE SIGN:", document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("ITEM", document.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintAsync_declined_prints_bank_receipt_without_signature_area_or_order_lines()
    {
        var driver = new RecordingReceiptPrinterDriver();
        var printer = new LinklyBankReceiptPrinter(
            new StaticReceiptPrinterSettingsStore(),
            driver);

        var result = await printer.PrintAsync(
            "Sandbox",
            "signature-declined-session-1",
            "DECLINED - Q6\nSIGNATURE ERROR",
            LinklyBankReceiptKind.Declined);

        Assert.True(result.Succeeded);
        var document = Assert.Single(driver.Documents);
        Assert.Contains("*** DECLINED ***", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("DECLINED - Q6", document.PlainText, StringComparison.Ordinal);
        Assert.Contains("SIGNATURE ERROR", document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("CUSTOMER SIGNATURE", document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("PLEASE SIGN:", document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("ITEM", document.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintAsync_masks_full_pan_and_prints_masked_card_summary()
    {
        var driver = new RecordingReceiptPrinterDriver();
        var printer = new LinklyBankReceiptPrinter(
            new StaticReceiptPrinterSettingsStore(),
            driver);

        var result = await printer.PrintAsync(
            "Sandbox",
            "masked-card-session-1",
            "CARD 4111111111111234\n" +
            "ALT 4111 1111 1111 5678\n" +
            "DASH 4111-1111-1111-9999\n" +
            "TAB 4111\t1111\t1111\t2468\n" +
            "NBSP 4111\u00A01111\u00A01111\u00A01357\n" +
            "MULTI 4111  1111  1111  8642\n" +
            "DOT 4111.1111.1111.9753\n" +
            "APPROVED",
            LinklyBankReceiptKind.Declined,
            cardType: "VISA",
            maskedCardNumber: "****1234");

        Assert.True(result.Succeeded);
        var document = Assert.Single(driver.Documents);
        Assert.Contains("Card: VISA ****1234", document.PlainText, StringComparison.Ordinal);
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
    public async Task PrintAsync_declined_does_not_mark_backend_receipt_printed()
    {
        var driver = new RecordingReceiptPrinterDriver();
        var notifier = new RecordingCardReceiptPrintedNotifier();
        var printer = new LinklyBankReceiptPrinter(
            new StaticReceiptPrinterSettingsStore(),
            driver,
            [notifier]);

        var result = await printer.PrintAsync(
            "Sandbox",
            "declined-session-1",
            "DECLINED - Q6\nSIGNATURE ERROR",
            LinklyBankReceiptKind.Declined);

        Assert.True(result.Succeeded);
        Assert.Empty(notifier.Markers);
    }

    [Fact]
    public async Task PrintAsync_signature_required_marks_backend_receipt_printed()
    {
        var driver = new RecordingReceiptPrinterDriver();
        var notifier = new RecordingCardReceiptPrintedNotifier();
        var printer = new LinklyBankReceiptPrinter(
            new StaticReceiptPrinterSettingsStore(),
            driver,
            [notifier]);

        var result = await printer.PrintAsync(
            "Sandbox",
            "signature-session-2",
            "APPROVE WITH SIG - 08\nPLEASE SIGN:",
            LinklyBankReceiptKind.SignatureRequired);

        Assert.True(result.Succeeded);
        var marker = Assert.Single(notifier.Markers);
        Assert.Equal(("Sandbox", "signature-session-2"), marker);
    }

    private sealed class StaticReceiptPrinterSettingsStore : IReceiptPrinterSettingsStore
    {
        public Task<ReceiptPrinterSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ReceiptPrinterSettings.Default);
        }

        public Task SaveAsync(ReceiptPrinterSettings settings, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingReceiptPrinterDriver : IReceiptPrinterDriver
    {
        public List<ReceiptPrintDocument> Documents { get; } = [];

        public Task<ReceiptPrinterDriverResult> PrintAsync(
            ReceiptPrintDocument document,
            ReceiptPrinterSettings settings,
            CancellationToken cancellationToken = default)
        {
            Documents.Add(document);
            return Task.FromResult(new ReceiptPrinterDriverResult(true, "printed"));
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
            return Task.FromResult(new ReceiptPrinterDriverResult(true, "opened"));
        }
    }

    private sealed class ThrowingCardReceiptPrintedNotifier : ICardReceiptPrintedNotifier
    {
        public Task MarkReceiptPrintedAsync(
            string environment,
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            throw new HttpRequestException("backend marker failed");
        }
    }

    private sealed class RecordingCardReceiptPrintedNotifier : ICardReceiptPrintedNotifier
    {
        public List<(string Environment, string SessionId)> Markers { get; } = [];

        public Task MarkReceiptPrintedAsync(
            string environment,
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            Markers.Add((environment, sessionId));
            return Task.CompletedTask;
        }
    }
}
