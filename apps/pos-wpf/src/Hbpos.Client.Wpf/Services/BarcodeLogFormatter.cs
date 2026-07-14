namespace Hbpos.Client.Wpf.Services;

internal static class BarcodeLogFormatter
{
    public static string FormatBarcodeInfo(string? barcode)
    {
        // 关键逻辑：扫码内容可能是收银员条码或紧急二维码，日志只保留长度避免泄露凭据。
        return string.IsNullOrEmpty(barcode)
            ? "length=0"
            : $"length={barcode.Length}";
    }
}
