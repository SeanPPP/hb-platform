using System.Diagnostics;
using System.Globalization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Wpf.ViewModels;

internal sealed class PosTerminalScanController
{
    private readonly PosCartService _cart;
    private int _scanTraceSequence;

    public PosTerminalScanController(PosCartService cart)
    {
        _cart = cart;
    }

    public PosTerminalScanPlan CreateManual(string scanText)
    {
        // 中文注释：手动检索允许关键词搜索，不强制 exact lookup。
        return new PosTerminalScanPlan(
            Barcode: scanText,
            Source: "manual",
            PreferExactLookup: false,
            TraceId: NextScanTraceId("manual"),
            StartedAt: DateTimeOffset.Now,
            CartLinesBefore: _cart.Lines.Count,
            DevicePath: null,
            ScannedAt: null,
            ApplyBarcodeToScanText: false,
            CloseTouchKeyboard: false);
    }

    public PosTerminalScanPlan CreateScanner(string barcode, string devicePath, string source, DateTimeOffset? scannedAt)
    {
        // 中文注释：扫描枪入口保持 exact lookup 优先，并在这里统一创建 trace 元数据。
        var normalizedSource = string.IsNullOrWhiteSpace(source) ? "scan" : source.Trim();
        return new PosTerminalScanPlan(
            Barcode: barcode,
            Source: normalizedSource,
            PreferExactLookup: true,
            TraceId: NextScanTraceId(normalizedSource),
            StartedAt: DateTimeOffset.Now,
            CartLinesBefore: _cart.Lines.Count,
            DevicePath: devicePath,
            ScannedAt: scannedAt,
            ApplyBarcodeToScanText: true,
            CloseTouchKeyboard: true);
    }

    public void LogStarted(PosTerminalScanPlan plan, string storeCode)
    {
        if (plan.ApplyBarcodeToScanText)
        {
            ConsoleLog.Write(
                "PosScan",
                $"traceId={plan.TraceId} {plan.Source} scanner event received barcodeInfo={BarcodeLogFormatter.FormatBarcodeInfo(plan.Barcode)} devicePath={plan.DevicePath} eventAgeMs={FormatElapsedSince(plan.ScannedAt)} cartLinesBefore={plan.CartLinesBefore}");
            return;
        }

        ConsoleLog.Write(
            "PosScan",
            $"traceId={plan.TraceId} manual scan flow start barcodeInfo={BarcodeLogFormatter.FormatBarcodeInfo(plan.Barcode)} storeCode={storeCode} cartLinesBefore={plan.CartLinesBefore}");
    }

    public void LogInputApplied(PosTerminalScanPlan plan, long setInputElapsedMs)
    {
        if (!plan.ApplyBarcodeToScanText)
        {
            return;
        }

        ConsoleLog.Write(
            "PosScan",
            $"traceId={plan.TraceId} scanner ui input applied barcodeInfo={BarcodeLogFormatter.FormatBarcodeInfo(plan.Barcode)} setInputElapsedMs={setInputElapsedMs}");
    }

    public void LogFinished(
        PosTerminalScanPlan plan,
        string? statusKey,
        bool autoAdded,
        int cartLinesAfter,
        long workflowElapsedMs,
        long applyResultElapsedMs,
        long totalElapsedMs)
    {
        var message = plan.ApplyBarcodeToScanText
            ? $"traceId={plan.TraceId} scanner flow end barcodeInfo={BarcodeLogFormatter.FormatBarcodeInfo(plan.Barcode)} statusKey={statusKey ?? "<null>"} autoAdded={FormatBool(autoAdded)} cartLinesBefore={plan.CartLinesBefore} cartLinesAfter={cartLinesAfter} workflowElapsedMs={workflowElapsedMs} applyResultElapsedMs={applyResultElapsedMs} totalElapsedMs={totalElapsedMs}"
            : $"traceId={plan.TraceId} manual scan flow end barcodeInfo={BarcodeLogFormatter.FormatBarcodeInfo(plan.Barcode)} statusKey={statusKey ?? "<null>"} autoAdded={FormatBool(autoAdded)} cartLinesBefore={plan.CartLinesBefore} cartLinesAfter={cartLinesAfter} workflowElapsedMs={workflowElapsedMs} applyResultElapsedMs={applyResultElapsedMs} totalElapsedMs={totalElapsedMs}";
        ConsoleLog.Write("PosScan", message);
    }

    private string NextScanTraceId(string source)
    {
        var normalizedSource = string.IsNullOrWhiteSpace(source) ? "scan" : source.Trim();
        return $"{normalizedSource}-{++_scanTraceSequence}";
    }

    private static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string FormatElapsedSince(DateTimeOffset? startedAt)
    {
        return startedAt is null
            ? "<none>"
            : Math.Max(0, (DateTimeOffset.Now - startedAt.Value).TotalMilliseconds).ToString("0.###", CultureInfo.InvariantCulture);
    }
}

internal readonly record struct PosTerminalScanPlan(
    string Barcode,
    string Source,
    bool PreferExactLookup,
    string TraceId,
    DateTimeOffset StartedAt,
    int CartLinesBefore,
    string? DevicePath,
    DateTimeOffset? ScannedAt,
    bool ApplyBarcodeToScanText,
    bool CloseTouchKeyboard);
