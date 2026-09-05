using System.Diagnostics;
using System.ServiceProcess;

namespace Hbpos.RemoteStatus;

public sealed class WindowsRustDeskProbe : IRemoteStatusProbe
{
    private const string ServiceName = "RustDesk";

    public Task<RustDeskProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var service = new ServiceController(ServiceName);
            var status = service.Status;
            return Task.FromResult(new RustDeskProbeResult(
                status switch
                {
                    ServiceControllerStatus.Running => RemoteRustDeskServiceStatus.Running,
                    ServiceControllerStatus.StartPending => RemoteRustDeskServiceStatus.Starting,
                    ServiceControllerStatus.StopPending => RemoteRustDeskServiceStatus.Stopping,
                    _ => RemoteRustDeskServiceStatus.Stopped
                },
                ReadRustDeskId(),
                ReadClientVersion()));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(new RustDeskProbeResult(
                RemoteRustDeskServiceStatus.NotInstalled,
                string.Empty,
                string.Empty));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Task.FromResult(new RustDeskProbeResult(
                RemoteRustDeskServiceStatus.CheckFailed,
                string.Empty,
                string.Empty));
        }
    }

    private static string ReadRustDeskId()
    {
        var paths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RustDesk", "config", "RustDesk.toml"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustDesk", "config", "RustDesk.toml")
        };
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            var line = File.ReadLines(path).FirstOrDefault(x => x.TrimStart().StartsWith("id =", StringComparison.OrdinalIgnoreCase));
            if (line is not null) return line[(line.IndexOf('=') + 1)..].Trim().Trim('"', '\'');
        }

        return string.Empty;
    }

    private static string ReadClientVersion()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RustDesk", "rustdesk.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "RustDesk", "rustdesk.exe")
        };
        var path = candidates.FirstOrDefault(File.Exists);
        return path is null ? string.Empty : FileVersionInfo.GetVersionInfo(path).ProductVersion ?? string.Empty;
    }
}
