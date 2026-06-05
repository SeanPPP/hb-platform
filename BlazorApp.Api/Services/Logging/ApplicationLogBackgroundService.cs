using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.Options;

namespace BlazorApp.Api.Services.Logging
{
    public class ApplicationLogBackgroundService : BackgroundService
    {
        private readonly IApplicationLogQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ApplicationLogBackgroundService> _logger;
        private readonly ApplicationLoggingOptions _options;

        public ApplicationLogBackgroundService(
            IApplicationLogQueue queue,
            IServiceScopeFactory scopeFactory,
            ILogger<ApplicationLogBackgroundService> logger,
            IOptions<ApplicationLoggingOptions> options
        )
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _options = options.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var buffer = new List<ApplicationLogIngestItemDto>();
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var item = await _queue.ReadAsync(stoppingToken);
                    buffer.Add(item);

                    // 短时间日志风暴时吸收已排队的数据，减少数据库写入次数。
                    while (
                        buffer.Count < _options.MaxBatchSize
                        && _queue.TryRead(out var queuedItem)
                        && queuedItem != null
                    )
                    {
                        buffer.Add(queuedItem);
                    }

                    await FlushAsync(buffer, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }

            await FlushAsync(buffer, CancellationToken.None);
        }

        private async Task FlushAsync(
            List<ApplicationLogIngestItemDto> buffer,
            CancellationToken cancellationToken
        )
        {
            if (buffer.Count == 0)
                return;

            var batch = buffer.ToList();
            buffer.Clear();

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ApplicationLogService>();
                await service.IngestAsync(
                    _options.DefaultProjectCode,
                    new ApplicationLogIngestRequestDto { Logs = batch }
                );
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // 日志中心不能反向影响业务请求；后台写入失败只输出到普通日志提供器。
                _logger.LogWarning(ex, "中心日志后台写入失败，本批次已丢弃: {Count}", batch.Count);
            }
        }
    }
}
