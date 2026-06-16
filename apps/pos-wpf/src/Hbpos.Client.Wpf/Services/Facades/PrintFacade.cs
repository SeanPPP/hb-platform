namespace Hbpos.Client.Wpf.Services.Facades;

public sealed class PrintFacade : IPrintFacade
{
    public IReceiptPrintService? ReceiptPrintService { get; }
    public IReceiptPrinterSettingsStore? ReceiptPrinterSettingsStore { get; }
    public IReceiptTextFormatter? ReceiptTextFormatter { get; }

    public PrintFacade(
        IReceiptPrintService? receiptPrintService,
        IReceiptPrinterSettingsStore? receiptPrinterSettingsStore,
        IReceiptTextFormatter? receiptTextFormatter)
    {
        ReceiptPrintService = receiptPrintService;
        ReceiptPrinterSettingsStore = receiptPrinterSettingsStore;
        ReceiptTextFormatter = receiptTextFormatter;
    }
}
