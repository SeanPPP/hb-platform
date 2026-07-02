namespace Hbpos.Client.Wpf.Services.Facades;

public interface IPrintFacade
{
    IReceiptPrintService? ReceiptPrintService { get; }
    IReceiptPrinterSettingsStore? ReceiptPrinterSettingsStore { get; }
    IReceiptTextFormatter? ReceiptTextFormatter { get; }
    ILinklyBankReceiptPrinter? LinklyBankReceiptPrinter { get; }
}
