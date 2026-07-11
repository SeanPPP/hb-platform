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

public sealed class WpfAppFixture : IDisposable
{
    public FlaUI.Core.Application? App { get; private set; }
    public UIA3Automation? Automation { get; private set; }
    public Window? MainWindow { get; private set; }
    public string EvidenceDirectory { get; } = Path.Combine(
        Path.GetTempPath(), "hbpos-ui-tests", Guid.NewGuid().ToString("N"));

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
        startInfo.Environment["HBPOS_CLIENT_LOG_FILE"] = Path.Combine(EvidenceDirectory, "hbpos-client.log");
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
        var isPreview = arguments.Contains("--preview", StringComparison.OrdinalIgnoreCase) ||
                        arguments.Contains("--screen", StringComparison.OrdinalIgnoreCase);
        if (isPreview)
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

    public string CaptureFailure(string step)
    {
        Directory.CreateDirectory(EvidenceDirectory);
        var path = Path.Combine(EvidenceDirectory, $"{step}.png");
        if (MainWindow is not null)
        {
            using var image = Capture.Element(MainWindow);
            image.ToFile(path);
        }
        return path;
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
