using Microsoft.Extensions.Options;

namespace BlazorApp.Api.Services.Logging
{
    public class ApplicationLogLoggerProvider : ILoggerProvider
    {
        private readonly IApplicationLogQueue _queue;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IOptionsMonitor<ApplicationLoggingOptions> _options;

        public ApplicationLogLoggerProvider(
            IApplicationLogQueue queue,
            IHttpContextAccessor httpContextAccessor,
            IOptionsMonitor<ApplicationLoggingOptions> options
        )
        {
            _queue = queue;
            _httpContextAccessor = httpContextAccessor;
            _options = options;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new ApplicationLogLogger(categoryName, _queue, _httpContextAccessor, _options);
        }

        public void Dispose() { }
    }

    internal class ApplicationLogLogger : ILogger
    {
        private static readonly string[] ExcludedCategories =
        [
            typeof(ApplicationLogLogger).Namespace ?? "BlazorApp.Api.Services.Logging",
            "Microsoft.Hosting.Lifetime",
        ];

        private readonly string _categoryName;
        private readonly IApplicationLogQueue _queue;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IOptionsMonitor<ApplicationLoggingOptions> _options;

        public ApplicationLogLogger(
            string categoryName,
            IApplicationLogQueue queue,
            IHttpContextAccessor httpContextAccessor,
            IOptionsMonitor<ApplicationLoggingOptions> options
        )
        {
            _categoryName = categoryName;
            _queue = queue;
            _httpContextAccessor = httpContextAccessor;
            _options = options;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            var options = _options.CurrentValue;
            if (!options.Enabled)
                return false;
            if (logLevel == LogLevel.None)
                return false;
            if (ExcludedCategories.Any(category =>
                    _categoryName.StartsWith(category, StringComparison.OrdinalIgnoreCase)
                ))
                return false;
            return logLevel >= ParseLevel(options.MinimumLevel);
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (!IsEnabled(logLevel))
                return;

            var options = _options.CurrentValue;
            var httpContext = _httpContextAccessor.HttpContext;
            var properties = ExtractProperties(state);
            var message = formatter(state, exception);

            _queue.TryEnqueue(
                new()
                {
                    ProjectCode = options.DefaultProjectCode,
                    Environment = options.DefaultEnvironment,
                    SourceType = options.DefaultSourceType,
                    ServiceName = options.ServiceName,
                    InstanceId = options.InstanceId,
                    Level = logLevel.ToString(),
                    Category = _categoryName,
                    EventId = eventId.Id == 0 ? null : eventId.Id.ToString(),
                    Message = message,
                    TimestampUtc = DateTime.UtcNow,
                    TraceId = httpContext?.TraceIdentifier,
                    RequestPath = httpContext?.Request.Path.Value,
                    RequestMethod = httpContext?.Request.Method,
                    StatusCode = httpContext?.Response.StatusCode,
                    UserId =
                        httpContext?.User.FindFirst("userId")?.Value
                        ?? httpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                    UserName = httpContext?.User.Identity?.Name,
                    ClientIp = httpContext?.Connection.RemoteIpAddress?.ToString(),
                    ExceptionType = exception?.GetType().Name,
                    ExceptionMessage = exception?.Message,
                    StackTrace = exception?.ToString(),
                    Properties = properties,
                }
            );
        }

        private static LogLevel ParseLevel(string value)
        {
            return Enum.TryParse<LogLevel>(value, ignoreCase: true, out var level)
                ? level
                : LogLevel.Warning;
        }

        private static Dictionary<string, object?>? ExtractProperties<TState>(TState state)
        {
            if (state is not IEnumerable<KeyValuePair<string, object?>> values)
                return null;

            return values
                .Where(item => item.Key != "{OriginalFormat}")
                .ToDictionary(item => item.Key, item => item.Value);
        }
    }
}
