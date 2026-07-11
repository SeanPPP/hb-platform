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

    public Window Launch(string arguments, IReadOnlyDictionary<string, string?>? environment = null)
    {
        if (App is not null) throw new InvalidOperationException("测试夹具已经拥有一个 WPF 进程。");
        var assemblyPath = typeof(Hbpos.Client.Wpf.App).Assembly.Location;
        var executablePath = Path.ChangeExtension(assemblyPath, ".exe");
        if (!File.Exists(executablePath)) throw new FileNotFoundException("找不到 WPF 可执行文件。", executablePath);
        Directory.CreateDirectory(EvidenceDirectory);
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
        App = FlaUI.Core.Application.Launch(startInfo);
        Automation = new UIA3Automation();
        MainWindow = App.GetMainWindow(Automation, TimeSpan.FromSeconds(30))
            ?? throw new TimeoutException("30 秒内未找到 WPF 主窗口。");
        return MainWindow;
    }

    public AutomationElement WaitForAutomationId(string automationId, TimeSpan? timeout = null) =>
        Retry.WhileNull(
            () => MainWindow?.FindFirstDescendant(automationId),
            timeout: timeout ?? TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(100),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: $"未找到 AutomationId={automationId}。")
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
