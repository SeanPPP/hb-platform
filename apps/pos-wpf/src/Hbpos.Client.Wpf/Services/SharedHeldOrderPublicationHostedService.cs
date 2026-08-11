using Microsoft.Extensions.Hosting;

namespace Hbpos.Client.Wpf.Services;

/// <summary>
/// WPF 共享挂单发布循环：设备与收银员 scope 一致时立即执行一轮，之后每 10 秒重试。
/// worker 自身保证本地先存、发布幂等与失败退避；本 hosted service 只负责生命周期，
/// 未授权或 scope 不一致时不访问 API，退出时等待当前轮次响应取消。
/// </summary>
public sealed class SharedHeldOrderPublicationHostedService(
    ISharedHeldOrderPublicationWorker worker,
    DeviceAuthorizationState authorizationState,
    ICashierSessionContext cashierSessionContext) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    internal async Task<bool> RunOnceIfAuthorizedAsync(CancellationToken cancellationToken)
    {
        var authorization = authorizationState.Current;
        var cashier = cashierSessionContext.CurrentSession;
        if (authorization is null
            || cashier is null
            || !string.Equals(
                authorization.StoreCode,
                cashier.StoreCode,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                authorization.DeviceCode,
                cashier.DeviceCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await worker.RunOnceAsync(
            authorization.StoreCode,
            authorization.DeviceCode,
            cancellationToken);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceIfAuthorizedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // 单轮异常不能终止后台发布；异常文本来自受控 repository/adapter，
                // 不记录 canonical payload 明文。
                ConsoleLog.WriteError(
                    "SharedHeldOrders",
                    "共享挂单后台发布失败，将在下个周期重试。",
                    exception: exception);
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
