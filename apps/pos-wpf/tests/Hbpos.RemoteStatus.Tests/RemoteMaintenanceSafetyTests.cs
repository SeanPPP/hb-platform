using System.Net;
using Hbpos.RemoteMaintenance.Setup;

namespace Hbpos.RemoteStatus.Tests;

public sealed class RemoteMaintenanceSafetyTests
{
    private static readonly RemoteMaintenanceConfig Config = new("hotbargain.vip:21116", "hotbargain.vip:21117", Convert.ToBase64String(new byte[32]));

    [Fact]
    public async Task 配置密码分开调用且读回实际服务选项()
    {
        var runner = new CliRunner(Config);
        var installer = new WindowsRemoteMaintenanceInstaller(runner, null!);
        await installer.ConfigureRustDeskAsync("unused.exe", Config, "test-password-only", CancellationToken.None);
        Assert.Single(runner.Commands.Where(x => x.StartsWith("--config ")));
        Assert.Single(runner.Commands.Where(x => x.StartsWith("--password ")));
        Assert.DoesNotContain(runner.Commands, x => x.Contains("--config") && x.Contains("--password"));
        Assert.Contains("--option \"custom-rendezvous-server\"", runner.Commands);
        Assert.Contains("--option \"relay-server\"", runner.Commands);
        Assert.Contains("--option \"key\"", runner.Commands);
    }

    [Fact]
    public async Task 密码命令退出零但未确认成功必须失败()
    {
        var installer = new WindowsRemoteMaintenanceInstaller(new CliRunner(Config, passwordAcknowledged: false), null!);
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.ConfigureRustDeskAsync("unused.exe", Config, "test-password-only", CancellationToken.None));
    }

    [Theory]
    [InlineData("api/remote-maintenance/artifacts/rustdesk")]
    [InlineData("/api/remote-maintenance/artifacts/status-agent")]
    public async Task 下载保留生产POS前缀(string path)
    {
        var handler = new CaptureHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://hotbargain.vip/pos-api/") };
        await using var stream = await new RemoteMaintenanceApiClient(http).DownloadArtifactAsync(path);
        Assert.StartsWith("https://hotbargain.vip/pos-api/api/remote-maintenance/artifacts/", handler.Url);
    }

    [Theory]
    [InlineData("https://evil.example/payload.exe")]
    [InlineData("//evil.example/payload.exe")]
    [InlineData("../artifacts/rustdesk")]
    public async Task 外部下载地址在发送设备票据之前拒绝(string path)
    {
        var handler = new CaptureHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://hotbargain.vip/pos-api/") };
        await Assert.ThrowsAsync<RemoteMaintenanceApiException>(() => new RemoteMaintenanceApiClient(http).DownloadArtifactAsync(path));
        Assert.Null(handler.Url);
    }

    [Fact]
    public void SCM内层路径引号不会丢失()
    {
        var encoded = WindowsRemoteMaintenanceCommandRunner.Quote("\"C:\\Program Files\\HBPOS\\Agent.exe\" --service");
        Assert.Equal("\"\\\"C:\\Program Files\\HBPOS\\Agent.exe\\\" --service\"", encoded);
        Assert.Equal(32, Convert.FromHexString(WindowsRemoteMaintenanceInstaller.TrustedRustDeskSha256).Length);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Url { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) });
        }
    }
    private sealed class CliRunner(RemoteMaintenanceConfig config, bool passwordAcknowledged = true) : IRemoteMaintenanceCommandRunner
    {
        public List<string> Commands { get; } = [];
        public Task<int> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
        { Commands.Add(arguments); return Task.FromResult(0); }
        public Task<RemoteMaintenanceCommandResult> RunWithOutputAsync(string fileName, string arguments, CancellationToken cancellationToken)
        {
            Commands.Add(arguments);
            var output = arguments switch
            {
                "--option \"custom-rendezvous-server\"" => config.IdServer,
                "--option \"relay-server\"" => config.RelayServer,
                "--option \"key\"" => config.PublicKey,
                "--option \"approve-mode\"" => "password",
                "--option \"verification-method\"" => "use-permanent-password",
                "--option \"allow-only-conn-window-open\"" => "N",
                _ when arguments.StartsWith("--password ") => passwordAcknowledged ? "Done!\r\n" : "Installation required!",
                _ => throw new InvalidOperationException("unexpected command")
            };
            return Task.FromResult(new RemoteMaintenanceCommandResult(0, output, ""));
        }
    }
}
