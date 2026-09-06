using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;

namespace Hbpos.RemoteMaintenance.Setup;

public interface IRemoteMaintenanceCommandRunner
{
    Task<int> RunAsync(string fileName, string arguments, CancellationToken cancellationToken);
    async Task<RemoteMaintenanceCommandResult> RunWithOutputAsync(string fileName, string arguments, CancellationToken cancellationToken) =>
        new(await RunAsync(fileName, arguments, cancellationToken), string.Empty, string.Empty);
}
public sealed record RemoteMaintenanceCommandResult(int ExitCode, string StandardOutput, string StandardError);
public interface IRemoteMaintenanceServiceControl
{
    Task<string?> QueryAsync(string serviceName, CancellationToken cancellationToken);
    Task<int> CreateOrUpdateAsync(string serviceName, string binaryPath, string accountName, CancellationToken cancellationToken);
    Task<int> StartAsync(string serviceName, CancellationToken cancellationToken);
    Task<int> StopAsync(string serviceName, CancellationToken cancellationToken);
}

public sealed class WindowsRemoteMaintenanceCommandRunner : IRemoteMaintenanceCommandRunner
{
    public async Task<int> RunAsync(string fileName, string arguments, CancellationToken cancellationToken) =>
        (await RunWithOutputAsync(fileName, arguments, cancellationToken)).ExitCode;

    public async Task<RemoteMaintenanceCommandResult> RunWithOutputAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var process = new Process { StartInfo = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(fileName))!
        }};
        process.Start();
        // 同时读取两个管道，禁止记录参数；其中可能含本机生成的无人值守密码。
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr);
            return new(process.ExitCode, stdout.Result, stderr.Result);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }

    // Windows CRT 参数编码：内层引号与末尾反斜杠需要分别处理，尤其 SCM binPath。
    internal static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"') result.Append('\\', backslashes * 2 + 1).Append(c);
            else result.Append('\\', backslashes).Append(c);
            backslashes = 0;
        }
        return result.Append('\\', backslashes * 2).Append('"').ToString();
    }
}

public sealed class WindowsRemoteMaintenanceServiceControl(IRemoteMaintenanceCommandRunner commandRunner) : IRemoteMaintenanceServiceControl
{
    private static string ScPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");
    private static string Q(string value) => WindowsRemoteMaintenanceCommandRunner.Quote(value);
    public Task<string?> QueryAsync(string serviceName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var service = new ServiceController(serviceName);
            return Task.FromResult<string?>(service.Status switch
            {
                ServiceControllerStatus.Running => "running",
                ServiceControllerStatus.StartPending => "starting",
                ServiceControllerStatus.StopPending => "stopping",
                _ => "stopped"
            });
        }
        catch (InvalidOperationException ex) when (ex.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: 1060 })
        { return Task.FromResult<string?>(null); }
    }
    public async Task<int> CreateOrUpdateAsync(string serviceName, string binaryPath, string accountName, CancellationToken cancellationToken)
    {
        var action = await QueryAsync(serviceName, cancellationToken) is null ? "create" : "config";
        return await commandRunner.RunAsync(ScPath,
            $"{action} {Q(serviceName)} binPath= {Q(binaryPath)} start= auto obj= {Q(accountName)}", cancellationToken);
    }
    public async Task<int> StartAsync(string serviceName, CancellationToken cancellationToken)
    {
        if (await QueryAsync(serviceName, cancellationToken) == "running") return 0;
        var result = await commandRunner.RunAsync(ScPath, $"start {Q(serviceName)}", cancellationToken);
        if (result is not (0 or 1056)) return result;
        return await WaitAsync(serviceName, "running", cancellationToken) ? 0 : 1460;
    }
    public async Task<int> StopAsync(string serviceName, CancellationToken cancellationToken)
    {
        var current = await QueryAsync(serviceName, cancellationToken);
        if (current is null or "stopped") return 0;
        var result = await commandRunner.RunAsync(ScPath, $"stop {Q(serviceName)}", cancellationToken);
        if (result is not (0 or 1062)) return result;
        return await WaitAsync(serviceName, "stopped", cancellationToken) ? 0 : 1460;
    }
    private async Task<bool> WaitAsync(string name, string expected, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 60; i++)
        {
            if (await QueryAsync(name, cancellationToken) == expected) return true;
            await Task.Delay(500, cancellationToken);
        }
        return false;
    }
}

/// <summary>只允许公司服务器和官方固定版本；提权时重新校验，绝不执行 journal 指定的任意文件。</summary>
public sealed class WindowsRemoteMaintenanceInstaller(
    IRemoteMaintenanceCommandRunner commandRunner,
    IRemoteMaintenanceServiceControl serviceControl,
    Func<RemoteMaintenanceInstallationResult, Task>? progressWriter = null) : IRemoteMaintenanceInstaller
{
    internal const string StatusServiceName = "HBPOSRemoteStatus";
    private const string RustDeskServiceName = "RustDesk";
    internal const string TrustedRustDeskSha256 = "eaedeb0088e687bf46f7c46a9c6ea5493ce51f3134dfd6acbedb47b5b9136274";
    private const long TrustedRustDeskSize = 24472432;
    private static string ProgramRoot => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static string StatusProgramDirectory => Path.Combine(ProgramRoot, "HBPOS", "RemoteStatus");
    private static string InstalledRustDesk => Path.Combine(ProgramRoot, "RustDesk", "rustdesk.exe");
    private static string Q(string value) => WindowsRemoteMaintenanceCommandRunner.Quote(value);

    public async Task<RemoteMaintenanceInstallationResult> InstallAsync(RemoteMaintenanceInstallationRequest request, CancellationToken cancellationToken = default)
    {
        ValidateConfig(request.Prepare.Config);
        if (request.Prepare.OperationId == Guid.Empty || request.Prepare.DeviceId == Guid.Empty ||
            request.Password.Length is < 12 or > 128 || request.Password.Any(char.IsControl))
            throw new InvalidDataException("远程维护安装参数无效。");
        // 先确认随 WPF 发布的 Agent 位于 Program Files；禁止从下载 ZIP/用户 journal 提权复制程序。
        var statusSource = ResolveTrustedStatusAgentPath();
        await EnsureNoForeignRustDeskConfigAsync(request.Prepare.Config, cancellationToken);
        Directory.CreateDirectory(StatusProgramDirectory);
        EnsureProtectedProgramPath(StatusProgramDirectory);
        ApplyDirectoryAcl(StatusProgramDirectory, localServiceCanRead: true);
        var staged = Path.Combine(StatusProgramDirectory, "rustdesk-install-" + Guid.NewGuid().ToString("N") + ".exe");
        var rustDeskTouched = false;
        var statusAgentTouched = false;
        try
        {
            // 源文件打开期间禁止写入/删除，复制到管理员目录后再验证并执行，封住校验到执行的替换窗口。
            await using (var source = new FileStream(request.RustDeskArtifactPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var target = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (source.Length != TrustedRustDeskSize) throw new InvalidDataException("RustDesk 文件大小错误。");
                await source.CopyToAsync(target, cancellationToken);
            }
            await using (var check = File.OpenRead(staged))
                if (!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(check, cancellationToken)), TrustedRustDeskSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("RustDesk 官方文件哈希错误。");
            // 外部配置预检查在此之前完成；从实际触发 RustDesk 安装起才允许外层清理该服务。
            if (progressWriter is not null)
                await progressWriter(new(true, false, string.Empty, request.ClientVersion, request.DataDirectory));
            rustDeskTouched = true;
            if (await commandRunner.RunAsync(staged, "--silent-install", cancellationToken) != 0)
                throw new InvalidOperationException("RustDesk 安装未完成。");
            for (var i = 0; !File.Exists(InstalledRustDesk) && i < 30; i++) await Task.Delay(500, cancellationToken);
            if (!File.Exists(InstalledRustDesk)) throw new InvalidOperationException("RustDesk 安装文件不存在。");
            EnsureProtectedProgramPath(InstalledRustDesk);
            if (await serviceControl.QueryAsync(RustDeskServiceName, cancellationToken) is null &&
                await commandRunner.RunAsync(InstalledRustDesk, "--install-service", cancellationToken) != 0)
                throw new InvalidOperationException("RustDesk 服务安装失败。");
            if (await serviceControl.StartAsync(RustDeskServiceName, cancellationToken) != 0)
                throw new InvalidOperationException("RustDesk 服务启动失败。");

            await ConfigureRustDeskAsync(InstalledRustDesk, request.Prepare.Config, request.Password, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(StatusProgramDirectory, "managed-server.json"),
                JsonSerializer.Serialize(request.Prepare.Config), cancellationToken);
            var rustdeskId = await GetRustdeskIdAsync(cancellationToken);
            if (string.IsNullOrEmpty(rustdeskId)) throw new InvalidOperationException("无法取得 RustDesk ID。");
            // 停止旧状态服务是本次安装对它的第一次实际修改，失败时也必须保留清理依据。
            if (progressWriter is not null)
                await progressWriter(new(true, true, rustdeskId, request.ClientVersion, request.DataDirectory));
            statusAgentTouched = true;
            if (await serviceControl.StopAsync(StatusServiceName, cancellationToken) != 0)
                throw new InvalidOperationException("旧状态服务无法停止。");
            var statusBinary = Path.Combine(StatusProgramDirectory, "Hbpos.RemoteStatus.exe");
            if (!string.Equals(Path.GetFullPath(statusSource), Path.GetFullPath(statusBinary), StringComparison.OrdinalIgnoreCase))
                File.Copy(statusSource, statusBinary, overwrite: true);
            if (await serviceControl.CreateOrUpdateAsync(StatusServiceName, Q(statusBinary) + " --service", "NT AUTHORITY\\LocalService", cancellationToken) != 0)
                throw new InvalidOperationException("状态服务安装失败。");
            return new(true, true, rustdeskId, request.ClientVersion, request.DataDirectory);
        }
        catch
        {
            if (statusAgentTouched)
                try { await serviceControl.StopAsync(StatusServiceName, CancellationToken.None); } catch { }
            if (rustDeskTouched)
                try { await serviceControl.StopAsync(RustDeskServiceName, CancellationToken.None); } catch { }
            throw;
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }

    internal async Task ConfigureRustDeskAsync(string executable, RemoteMaintenanceConfig config, string password, CancellationToken cancellationToken)
    {
        ValidateConfig(config);
        // 官方 CLI 按精确参数数量分支，--config 和 --password 必须分开调用。
        var value = $"host={config.IdServer},key={config.PublicKey},relay={config.RelayServer},";
        if (await commandRunner.RunAsync(executable, "--config " + Q(value), cancellationToken) != 0)
            throw new InvalidOperationException("RustDesk 服务器配置失败。");
        var passwordResult = await commandRunner.RunWithOutputAsync(executable, "--password " + Q(password), cancellationToken);
        if (passwordResult.ExitCode != 0 || passwordResult.StandardOutput.Trim() != "Done!")
            throw new InvalidOperationException("RustDesk 密码设置未确认。");
        // 仅密码认证允许无人值守；禁止仅靠界面点击确认，逐项读回真实服务配置。
        foreach (var option in new[] { ("verification-method", "use-permanent-password"), ("approve-mode", "password"), ("allow-only-conn-window-open", "N") })
            if (await commandRunner.RunAsync(executable, "--option " + Q(option.Item1) + " " + Q(option.Item2), cancellationToken) != 0)
                throw new InvalidOperationException("RustDesk 无人值守配置失败。");
        var expected = new[] { ("custom-rendezvous-server", config.IdServer), ("relay-server", config.RelayServer), ("key", config.PublicKey),
            ("verification-method", "use-permanent-password"), ("approve-mode", "password"), ("allow-only-conn-window-open", "N") };
        foreach (var option in expected)
        {
            var matched = false;
            for (var i = 0; i < 10 && !matched; i++)
            {
                var actual = await commandRunner.RunWithOutputAsync(executable, "--option " + Q(option.Item1), cancellationToken);
                matched = actual.ExitCode == 0 && actual.StandardOutput.Trim() == option.Item2;
                if (!matched) await Task.Delay(300, cancellationToken);
            }
            if (!matched) throw new InvalidOperationException("RustDesk 配置读回不一致。");
        }
    }

    public async Task ConfigureStatusAgentAsync(RemoteMaintenancePrepareResponse prepare, RemoteMaintenanceCommitResponse commit,
        string rustdeskId, string clientVersion, CancellationToken cancellationToken = default)
    {
        if (commit.DeviceId != prepare.DeviceId || !Uri.TryCreate(commit.HeartbeatUrl, UriKind.Absolute, out var url) ||
            url.Scheme != "https" || url.Host != "hotbargain.vip" || !url.IsDefaultPort || !string.IsNullOrEmpty(url.UserInfo) ||
            !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment) ||
            url.AbsolutePath != $"/api/remote-maintenance/devices/{commit.DeviceId:D}/heartbeat")
            throw new InvalidDataException("状态服务地址不属于公司设备接口。");
        if (await serviceControl.StopAsync(StatusServiceName, cancellationToken) != 0)
            throw new InvalidOperationException("状态服务无法停止以应用配置。");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HBPOS", "RemoteStatus");
        Directory.CreateDirectory(directory);
        EnsureNoReparsePoints(directory);
        ApplyDirectoryAcl(directory, localServiceCanRead: true);
        var sequenceDirectory = Path.Combine(directory, "state");
        Directory.CreateDirectory(sequenceDirectory);
        ApplyDirectoryAcl(sequenceDirectory, localServiceCanRead: true, localServiceCanWrite: true);
        var bytes = Encoding.UTF8.GetBytes(commit.MonitorToken);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.LocalMachine); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        var temporary = Path.Combine(directory, "agent-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new
            {
                agentVersion = prepare.ArtifactManifest.StatusAgent.Version,
                rustDeskId = rustdeskId, clientVersion, heartbeatUrl = url.AbsoluteUri,
                protectedMonitorToken = Convert.ToBase64String(encrypted)
            }), cancellationToken);
            File.Move(temporary, Path.Combine(directory, "agent.json"), overwrite: true);
            if (await serviceControl.StartAsync(RustDeskServiceName, cancellationToken) != 0 ||
                await serviceControl.StartAsync(StatusServiceName, cancellationToken) != 0)
                throw new InvalidOperationException("状态服务未启动。");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task FailClosedAsync(RemoteMaintenanceInstallationResult installation, CancellationToken cancellationToken = default)
    {
        if (installation.StatusAgentInstalled) await serviceControl.StopAsync(StatusServiceName, cancellationToken);
        if (installation.RustDeskInstalled) await serviceControl.StopAsync(RustDeskServiceName, cancellationToken);
    }
    public async Task<string?> GetRustdeskIdAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(InstalledRustDesk)) return null;
        EnsureProtectedProgramPath(InstalledRustDesk);
        for (var i = 0; i < 20; i++)
        {
            var result = await commandRunner.RunWithOutputAsync(InstalledRustDesk, "--get-id", cancellationToken);
            var id = result.StandardOutput.Trim();
            if (result.ExitCode == 0 && id.Length is > 0 and <= 120 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return id;
            await Task.Delay(500, cancellationToken);
        }
        return null;
    }
    public async Task<RemoteMaintenanceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await serviceControl.QueryAsync(RustDeskServiceName, cancellationToken);
        return new(status is not null, string.Empty, string.Empty, status ?? "notInstalled");
    }
    private async Task EnsureNoForeignRustDeskConfigAsync(RemoteMaintenanceConfig config, CancellationToken cancellationToken)
    {
        var status = await serviceControl.QueryAsync(RustDeskServiceName, cancellationToken);
        if (status is null) return;
        EnsureProtectedProgramPath(InstalledRustDesk);
        if (status != "running")
        {
            var marker = Path.Combine(StatusProgramDirectory, "managed-server.json");
            // 只有管理员目录中已确认接管的服务才允许为恢复事务重新启动。
            if (!File.Exists(marker)) throw new InvalidOperationException("已有 RustDesk 未运行，无法核验其配置。");
            EnsureProtectedProgramPath(marker);
            if (JsonSerializer.Deserialize<RemoteMaintenanceConfig>(await File.ReadAllTextAsync(marker, cancellationToken)) != config ||
                await serviceControl.StartAsync(RustDeskServiceName, cancellationToken) != 0)
                throw new InvalidOperationException("已有 RustDesk 不属于本次公司配置。");
        }
        foreach (var expected in new[] { ("custom-rendezvous-server", config.IdServer), ("relay-server", config.RelayServer), ("key", config.PublicKey) })
        {
            var actual = await commandRunner.RunWithOutputAsync(InstalledRustDesk, "--option " + Q(expected.Item1), cancellationToken);
            if (actual.ExitCode != 0 || actual.StandardOutput.Trim() != expected.Item2)
                throw new InvalidOperationException("检测到已有其他 RustDesk 配置，已停止接管。");
        }
    }
    internal static void ValidateConfig(RemoteMaintenanceConfig config)
    {
        if (config.IdServer != "hotbargain.vip:21116" || config.RelayServer != "hotbargain.vip:21117" ||
            Convert.FromBase64String(config.PublicKey).Length != 32)
            throw new InvalidDataException("RustDesk 配置必须指向公司服务器。");
    }
    private static string ResolveTrustedStatusAgentPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Hbpos.RemoteStatus.exe");
        EnsureProtectedProgramPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("WPF 安装目录缺少状态服务程序。");
        return path;
    }
    private static void EnsureProtectedProgramPath(string path)
    {
        if (!Path.GetFullPath(path).StartsWith(ProgramRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("远程维护程序必须安装到受保护的 Program Files 目录。");
        EnsureNoReparsePoints(path);
        var writeRights = FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
            FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        for (var current = Path.GetFullPath(path); current.Length >= ProgramRoot.Length; current = Path.GetDirectoryName(current)!)
        {
            if (!File.Exists(current) && !Directory.Exists(current)) continue;
            FileSystemSecurity security = Directory.Exists(current)
                ? new DirectoryInfo(current).GetAccessControl() : new FileInfo(current).GetAccessControl();
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow || (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0 ||
                    (rule.FileSystemRights & writeRights) == 0) continue;
                var sid = rule.IdentityReference.Value;
                if (sid is not ("S-1-5-18" or "S-1-5-32-544" or "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"))
                    throw new InvalidDataException("程序目录允许非管理员写入，拒绝提权安装。");
            }
        }
    }
    private static void EnsureNoReparsePoints(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("远程维护路径不能包含重解析点。");
    }
    private static void ApplyDirectoryAcl(string path, bool localServiceCanRead, bool localServiceCanWrite = false)
    {
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(true, false);
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            acl.AddAccessRule(new(new SecurityIdentifier(sid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        if (localServiceCanRead)
            acl.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null),
                localServiceCanWrite ? FileSystemRights.Modify : FileSystemRights.ReadAndExecute,
                inherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(acl);
    }
}
