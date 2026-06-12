using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Hbpos.Client.Wpf.Services;

internal static class ConsoleLog
{
    private const int AttachParentProcess = -1;
    private static readonly object CenterLogGate = new();
    private static int _attachAttempted;
    private static ApplicationLogDefaults _centerDefaults = ApplicationLogDefaults.Default;
    private static IApplicationLogSink _centerSink = NoopApplicationLogSink.Instance;

    internal static event Action<string>? LineWritten;

    internal static void ConfigureCenterDefaults(ApplicationLogDefaults defaults)
    {
        lock (CenterLogGate)
        {
            _centerDefaults = defaults;
        }
    }

    internal static void ConfigureCenterSink(IApplicationLogSink? sink)
    {
        lock (CenterLogGate)
        {
            _centerSink = sink ?? NoopApplicationLogSink.Instance;
        }
    }

    public static void Write(string category, string message)
    {
        Write(category, message, "Information");
    }

    internal static void WriteError(
        string category,
        string message,
        ApplicationLogContext? context = null,
        Exception? exception = null)
    {
        Write(category, message, "Error", context, exception);
    }

    private static void Write(
        string category,
        string message,
        string level,
        ApplicationLogContext? context = null,
        Exception? exception = null)
    {
        EnsureConsoleAttached();
        var line = $"[HBPOS][Client][{category}] {DateTimeOffset.Now:O} {message}";
        Console.WriteLine(line);
        Debug.WriteLine(line);
        Trace.WriteLine(line);
        WriteDebuggerOutput(line);
        WriteFileLog(line);
        EnqueueCenterLog(category, message, level, context, exception);
        LineWritten?.Invoke(line);
    }

    private static void EnqueueCenterLog(
        string category,
        string message,
        string level,
        ApplicationLogContext? context,
        Exception? exception)
    {
        ApplicationLogDefaults defaults;
        IApplicationLogSink sink;
        lock (CenterLogGate)
        {
            defaults = _centerDefaults;
            sink = _centerSink;
        }

        try
        {
            // 中心日志失败不能影响收银主流程，因此这里只做 best-effort 投递。
            sink.Enqueue(new ApplicationLogEntry(
                level,
                message,
                DateTimeOffset.UtcNow,
                defaults.ProjectCode,
                defaults.Environment,
                defaults.SourceType,
                Category: category,
                ServiceName: category,
                TraceId: context?.TraceId,
                RequestPath: context?.RequestPath,
                RequestMethod: context?.RequestMethod,
                StatusCode: context?.StatusCode,
                UserId: context?.UserId,
                UserName: context?.UserName,
                ExceptionType: exception?.GetType().Name,
                ExceptionMessage: exception?.Message,
                StackTrace: exception?.StackTrace,
                Properties: BuildProperties(category, context?.Properties)));
        }
        catch (Exception)
        {
            // 日志通道不能反向打断 POS UI、支付和订单同步。
        }
    }

    private static IReadOnlyDictionary<string, object?> BuildProperties(
        string category,
        IReadOnlyDictionary<string, object?>? properties)
    {
        var result = properties is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(properties, StringComparer.OrdinalIgnoreCase);
        result["category"] = category;
        return result;
    }

    private static void WriteFileLog(string line)
    {
        var logPath = Environment.GetEnvironmentVariable("HBPOS_CLIENT_LOG_FILE");
        if (string.IsNullOrWhiteSpace(logPath))
        {
            return;
        }

        try
        {
            var logDirectory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrWhiteSpace(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            File.AppendAllText(logPath, line + Environment.NewLine);
        }
        catch (Exception)
        {
            // Startup diagnostics should never block the POS app.
        }
    }

    private static void EnsureConsoleAttached()
    {
        if (!OperatingSystem.IsWindows() || Interlocked.Exchange(ref _attachAttempted, 1) != 0)
        {
            return;
        }

        _ = AttachConsole(AttachParentProcess);
    }

    private static void WriteDebuggerOutput(string line)
    {
        if (OperatingSystem.IsWindows())
        {
            OutputDebugString(line + Environment.NewLine);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern void OutputDebugString(string lpOutputString);
}
