using System.Diagnostics;
using System.Text;
using Hbpos.RemoteMaintenance.Setup;

namespace Hbpos.RemoteStatus.Tests;

public sealed class RemoteMaintenanceCommandRunnerTests
{
    private static string PowerShell => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell", "v1.0", "powershell.exe");
    private static string Encoded(string script) => "-NoProfile -NonInteractive -EncodedCommand " +
        Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    [Fact]
    public async Task 安装命令退出后不等待继承输出管道的后台进程()
    {
        var pidFile = Path.Combine(Path.GetTempPath(), "hbpos-runner-" + Guid.NewGuid().ToString("N") + ".pid");
        var childArguments = Encoded("Start-Sleep -Seconds 30");
        var script = $"$s = [Diagnostics.ProcessStartInfo]::new('{PowerShell}', '{childArguments}'); " +
            "$s.UseShellExecute = $false; $s.CreateNoWindow = $true; " +
            $"$p = [Diagnostics.Process]::Start($s); [IO.File]::WriteAllText('{pidFile}', [string]$p.Id); exit 7";
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var exitCode = await new WindowsRemoteMaintenanceCommandRunner().RunAsync(PowerShell, Encoded(script), deadline.Token);
            Assert.Equal(7, exitCode);
            // 后台进程仍活着，证明返回依赖安装进程退出，而非等待管道 EOF。
            using var child = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(pidFile)));
            Assert.False(child.HasExited);
        }
        finally
        {
            if (File.Exists(pidFile))
            {
                try
                {
                    using var child = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(pidFile)));
                    if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
                }
                catch (ArgumentException) { }
                File.Delete(pidFile);
            }
        }
    }

    [Fact]
    public async Task 已取消的安装命令不会启动进程()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WindowsRemoteMaintenanceCommandRunner().RunAsync("missing-executable.exe", "", cancelled.Token));
    }

    [Fact]
    public async Task 配置查询仍保留退出码和两个输出流()
    {
        var result = await new WindowsRemoteMaintenanceCommandRunner().RunWithOutputAsync(PowerShell,
            Encoded("[Console]::Out.Write('configured'); [Console]::Error.Write('diagnostic'); exit 9"), CancellationToken.None);
        Assert.Equal(9, result.ExitCode);
        Assert.Equal("configured", result.StandardOutput);
        Assert.Equal("diagnostic", result.StandardError);
    }
}
