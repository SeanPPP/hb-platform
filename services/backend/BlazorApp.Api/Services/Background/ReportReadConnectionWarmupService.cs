using BlazorApp.Api.Data;
using Microsoft.Data.SqlClient;

namespace BlazorApp.Api.Services.Background;

/// <summary>
/// 在宿主开始接受请求前预热报表读取使用的非 MARS SQL Server 连接池。
/// 这里只建立并释放连接，不执行任何查询或统计任务。
/// </summary>
public sealed class ReportReadConnectionWarmupService : IHostedService
{
    private static readonly TimeSpan WarmupTimeout = TimeSpan.FromSeconds(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReportReadConnectionWarmupService> _logger;

    public ReportReadConnectionWarmupService(
        IServiceScopeFactory scopeFactory,
        ILogger<ReportReadConnectionWarmupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(WarmupTimeout);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<SqlSugarContext>();
            if (context.Db.CurrentConnectionConfig.DbType != SqlSugar.DbType.SqlServer)
            {
                _logger.LogDebug("报表读取连接预热跳过：当前数据库不是 SQL Server。");
                return;
            }

            var connectionString = new SqlConnectionStringBuilder(
                context.Db.CurrentConnectionConfig.ConnectionString)
            {
                MultipleActiveResultSets = false,
                // 保留一个可复用连接，避免页面闲置后再次承担远程握手的等待。
                MinPoolSize = 1,
            }.ConnectionString;

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(timeout.Token);
            _logger.LogInformation("报表读取专用 SQL Server 连接预热完成。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("报表读取连接预热因宿主停止而取消。");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("报表读取连接预热在 {TimeoutSeconds} 秒内未完成，宿主继续启动。", WarmupTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "报表读取连接预热失败，宿主继续启动。");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
