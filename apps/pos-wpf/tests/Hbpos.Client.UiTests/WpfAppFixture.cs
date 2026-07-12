using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Hbpos.Client.UiTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfUiCollection : ICollectionFixture<WpfAppFixture>
{
    public const string Name = "WPF UI";
}

public sealed record WpfFailureEvidence(
    string ScreenshotPath,
    string ClientLogPath,
    bool ScreenshotCaptured = false)
{
    public Exception Wrap(Exception original)
    {
        var screenshot = ScreenshotCaptured
            ? ScreenshotPath
            : $"{ScreenshotPath}（脱敏未确认，未生成）";
        return new InvalidOperationException(
            $"WPF UI 测试失败。脱敏截图：{screenshot}；客户端日志：{ClientLogPath}。",
            original);
    }
}

public sealed class WpfAppFixture : IDisposable
{
    public FlaUI.Core.Application? App { get; private set; }
    public UIA3Automation? Automation { get; private set; }
    public Window? MainWindow { get; private set; }
    public string EvidenceDirectory { get; } = Path.Combine(
        Path.GetTempPath(), "hbpos-ui-tests", Guid.NewGuid().ToString("N"));
    public string ClientLogPath => Path.Combine(EvidenceDirectory, "hbpos-client.log");

    internal ProcessStartInfo CreateStartInfo(
        string executablePath,
        string arguments,
        IReadOnlyDictionary<string, string?>? environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = false,
        };
        // 每次测试使用独立日志，避免污染正常客户端日志。
        startInfo.Environment["HBPOS_CLIENT_LOG_FILE"] = ClientLogPath;
        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                if (pair.Value is null) startInfo.Environment.Remove(pair.Key);
                else startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        // 测试专用门禁和密钥只留在测试进程，调用者也不能重新注入 WPF 子进程。
        foreach (var key in startInfo.Environment.Keys
                     .Where(key => key.StartsWith("HBPOS_E2E_", StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            startInfo.Environment.Remove(key);
        }

        // 宁可保守隔离，也不能因 Windows 命令行的引号或空白规则漏掉 Preview。
        if (IsPreviewArguments(arguments))
        {
            // 安全值必须最后写入，禁止父进程或调用者把 Preview 指向真实后台。
            startInfo.Environment["HBPOS_API_BASE_URL"] = "http://127.0.0.1:0/";
            startInfo.Environment["HBPOS_LOG_CENTER_ENABLED"] = "false";
            startInfo.Environment["HBPOS_OPERATION_AUDIT_UPLOAD_ENABLED"] = "false";
        }

        return startInfo;
    }

    public Window Launch(string arguments, IReadOnlyDictionary<string, string?>? environment = null)
    {
        if (App is not null) throw new InvalidOperationException("测试夹具已经拥有一个 WPF 进程。");
        var assemblyPath = typeof(Hbpos.Client.Wpf.App).Assembly.Location;
        var executablePath = Path.ChangeExtension(assemblyPath, ".exe");
        if (!File.Exists(executablePath)) throw new FileNotFoundException("找不到 WPF 可执行文件。", executablePath);
        EnsureSafeToLaunch(executablePath, arguments, FindExistingExecutablePaths);
        Directory.CreateDirectory(EvidenceDirectory);
        var startInfo = CreateStartInfo(executablePath, arguments, environment);
        App = FlaUI.Core.Application.Launch(startInfo);
        Automation = new UIA3Automation();
        MainWindow = App.GetMainWindow(Automation, TimeSpan.FromSeconds(30))
            ?? throw new TimeoutException("30 秒内未找到 WPF 主窗口。");
        return MainWindow;
    }

    public AutomationElement WaitForAutomationId(
        string automationId,
        TimeSpan? timeout = null,
        string step = "等待控件") =>
        Retry.WhileNull(
            () => MainWindow?.FindFirstDescendant(automationId),
            timeout: timeout ?? TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(100),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: $"{step}超时，AutomationId={automationId}。")
        .Result!;

    public WpfFailureEvidence CaptureFailure(string step, bool allowScreenshot = true) => CaptureFailure(
        step,
        allowScreenshot,
        ClearSensitiveInputs,
        path =>
        {
            if (MainWindow is null) return;
            using var image = Capture.Element(MainWindow);
            image.ToFile(path);
        });

    internal WpfFailureEvidence CaptureFailure(
        string step,
        Action redact,
        Action<string> capture) => CaptureFailure(step, true, redact, capture);

    internal WpfFailureEvidence CaptureFailure(
        string step,
        bool allowScreenshot,
        Action redact,
        Action<string> capture)
    {
        var path = Path.Combine(EvidenceDirectory, $"{step}.png");
        // 目录、脱敏或截图失败时仍必须保留并报告原业务异常。
        var directoryReady = BestEffort(() => Directory.CreateDirectory(EvidenceDirectory));
        if (!allowScreenshot) return new WpfFailureEvidence(path, ClientLogPath, false);
        var redacted = BestEffort(redact);
        var captured = directoryReady &&
                       redacted &&
                       BestEffort(() => capture(path)) &&
                       File.Exists(path);
        return new WpfFailureEvidence(path, ClientLogPath, captured);
    }

    private void ClearSensitiveInputs()
    {
        Exception? failure = null;
        foreach (var automationId in new[] { "CashierLoginInput", "ProductBarcodeInput" })
        {
            try
            {
                var input = MainWindow?.FindFirstDescendant(automationId);
                if (input is not null) input.AsTextBox().Text = string.Empty;
            }
            catch (Exception error)
            {
                // 控件不存在是安全状态；控件存在但无法清空则禁止截图。
                failure ??= error;
            }
        }
        if (failure is not null)
            throw new InvalidOperationException("敏感输入未能安全清空。", failure);
    }

    internal static void EnsureSafeToLaunch(
        string executablePath,
        string arguments,
        Func<string, IReadOnlyList<string?>> findExistingExecutables)
    {
        if (IsPreviewArguments(arguments)) return;
        var targetPath = Path.GetFullPath(executablePath);
        if (findExistingExecutables(targetPath).Any(path => IsSameOrUnknownExecutable(path, targetPath)))
            throw new InvalidOperationException("检测到同一 WPF 可执行文件已有进程，live 测试已在启动前中止。");
    }

    private static bool IsPreviewArguments(string arguments) =>
        arguments.Contains("--preview", StringComparison.OrdinalIgnoreCase) ||
        arguments.Contains("--screen", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string?> FindExistingExecutablePaths(string executablePath)
    {
        var paths = new List<string?>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executablePath)))
        {
            using (process)
            {
                try
                {
                    paths.Add(process.MainModule?.FileName);
                }
                catch
                {
                    // 同名进程路径不可读时按可能冲突处理，禁止 live 启动。
                    paths.Add(null);
                }
            }
        }
        return paths;
    }

    private static bool IsSameOrUnknownExecutable(string? candidatePath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath)) return true;
        try
        {
            return string.Equals(
                Path.GetFullPath(candidatePath),
                targetPath,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static bool BestEffort(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch
        {
            // 证据收集不能覆盖原始测试失败。
            return false;
        }
    }

    public bool CloseOwnedProcess()
    {
        if (App is null) return true;
        var exited = App.HasExited || App.Close(killIfCloseFails: false);
        if (!exited) App.Kill();
        Automation?.Dispose();
        App.Dispose();
        Automation = null;
        App = null;
        MainWindow = null;
        return exited;
    }

    public void Dispose() => CloseOwnedProcess();
}
