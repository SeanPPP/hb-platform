using BlazorApp.Api.Data;
using Microsoft.Extensions.Options;

namespace BlazorApp.Api.Services.Performance;

public sealed class PerformanceMetricFlushService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PerformanceMetricBuffer _buffer;
    private readonly IOptions<PerformanceMetricsOptions> _options;
    private readonly ILogger<PerformanceMetricFlushService> _logger;

    public PerformanceMetricFlushService(
        IServiceScopeFactory scopeFactory,
        PerformanceMetricBuffer buffer,
        IOptions<PerformanceMetricsOptions> options,
        ILogger<PerformanceMetricFlushService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _buffer = buffer;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(_options.Value.FlushIntervalSeconds, 5, 300));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await FlushAsync(stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await FlushAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (!_options.Value.Enabled || _buffer.BufferedSeriesCount == 0)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SqlSugarContext>();
            await _buffer.FlushAsync(context.Db, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "性能指标定时刷新失败");
        }
    }
}
