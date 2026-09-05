using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hbpos.RemoteStatus;

public static class Program
{
    public static Task Main(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            // SCM 名称由安装器固定为 HBPOSRemoteStatus；显示名称可由打包器另行配置。
            .UseWindowsService(options => options.ServiceName = "HBPOSRemoteStatus")
            .ConfigureServices((context, services) =>
            {
                var dataDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "HBPOS",
                    "RemoteStatus");
                // 配置由安装事务生成并用 DPAPI 保护 token；服务启动失败即停止，不猜测缺失配置。
                var configuration = RemoteStatusConfiguration.Load(dataDirectory);
                services.AddSingleton(configuration.Options);
                services.AddSingleton<IRemoteStatusProbe, WindowsRustDeskProbe>();
                services.AddSingleton<IRemoteStatusSequenceStore>(
                    new FileRemoteStatusSequenceStore(Path.Combine(dataDirectory, "state", "sequence")));
                services.AddHttpClient<IRemoteStatusHeartbeatSender, RemoteStatusHttpSender>(client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
                services.AddHostedService<RemoteStatusWorker>();
            })
            .Build()
            .RunAsync();
    }
}
