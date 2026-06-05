using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BlazorApp.Api.Services.Logging
{
    public class ApplicationLogRateLimiter
    {
        private readonly IMemoryCache _cache;
        private readonly IOptionsMonitor<ApplicationLoggingOptions> _options;

        public ApplicationLogRateLimiter(
            IMemoryCache cache,
            IOptionsMonitor<ApplicationLoggingOptions> options
        )
        {
            _cache = cache;
            _options = options;
        }

        public bool TryConsume(string projectCode, int logCount, out string message)
        {
            var options = _options.CurrentValue;
            var window = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmm");
            var requests = Increment($"application-log:req:{projectCode}:{window}", 1);
            var logs = Increment($"application-log:logs:{projectCode}:{window}", Math.Max(1, logCount));

            if (requests > options.MaxIngestRequestsPerMinute)
            {
                message = "日志写入请求过于频繁，请稍后重试";
                return false;
            }

            if (logs > options.MaxIngestLogsPerMinute)
            {
                message = "日志写入数量超过项目每分钟限制，请稍后重试";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private int Increment(string key, int value)
        {
            lock (_cache)
            {
                var current = _cache.Get<int?>(key) ?? 0;
                var next = current + value;
                _cache.Set(
                    key,
                    next,
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2),
                    }
                );
                return next;
            }
        }
    }
}
