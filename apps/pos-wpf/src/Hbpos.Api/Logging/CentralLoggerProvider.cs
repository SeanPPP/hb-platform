using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Hbpos.Api.Logging;

internal sealed class CentralLoggerProvider(
    CentralLoggingOptions options,
    CentralLogQueue queue,
    IHttpContextAccessor httpContextAccessor) : ILoggerProvider
{
    private static readonly string[] ExcludedCategoryPrefixes =
    [
        "Hbpos.Api.Logging.Central",
        "Microsoft.Hosting.Lifetime",
        "System.Net.Http.HttpClient."
    ];

    private readonly ConcurrentDictionary<string, CentralLogger> loggers = new(StringComparer.Ordinal);
    private static readonly Regex ConnectionStringPattern = new(
        @"\b(?:Server|Data Source)\s*=[^\r\n]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex UrlQueryPattern = new(
        @"(?<base>(?:https?://|/)[^\s?]+)\?[^\s]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SensitivePairPattern = new(
        """(?<![\w-])(?<key>["']?(?:access[-_]?token|refresh[-_]?token|client[-_]?secret|api[-_]?key|token|secret|password|authorization|credential(?:s)?|voucher(?:code)?|card(?:number)?|pan|cvv|cvc|pin)["']?)\s*[:=]\s*(?<value>\[REDACTED\]|\{[^{}\r\n]+\}|Bearer\s+[^\s,;}\]]+|"[^"]*"|'[^']*'|[^\s,;}\]]+)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BearerPattern = new(
        @"\bBearer\s+[^\s,;]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PaymentCardCandidatePattern = new(
        @"(?<!\d)(?:\d[ -]?){12,18}\d(?!\d)",
        RegexOptions.CultureInvariant);
    private static readonly Regex StructuredPlaceholderPattern = new(
        @"^\{[^{}\r\n]+\}$",
        RegexOptions.CultureInvariant);

    public ILogger CreateLogger(string categoryName)
    {
        return loggers.GetOrAdd(
            categoryName,
            category => new CentralLogger(category, options, queue, httpContextAccessor));
    }

    public void Dispose()
    {
        loggers.Clear();
    }

    private sealed class CentralLogger(
        string category,
        CentralLoggingOptions options,
        CentralLogQueue queue,
        IHttpContextAccessor httpContextAccessor) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return options.IsConfigured &&
                logLevel is >= LogLevel.Warning and <= LogLevel.Critical &&
                logLevel >= options.MinimumLevel &&
                !ExcludedCategoryPrefixes.Any(prefix => category.StartsWith(prefix, StringComparison.Ordinal));
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            try
            {
                var formattedMessage = FormatMessage(state, exception, formatter, out var isStructuredTemplate);
                var message = SanitizeMessage(formattedMessage, isStructuredTemplate);
                if (string.IsNullOrWhiteSpace(message))
                {
                    message = exception?.GetType().Name ?? string.Empty;
                    if (message.Length == 0)
                    {
                        return;
                    }
                }

                var httpContext = httpContextAccessor.HttpContext;
                var routePattern = (httpContext?.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
                // 请求上下文只采集固定字段，禁止把 query、headers、body 或结构化 state 上传。
                queue.Enqueue(new ApplicationLogIngestItemDto
                {
                    ClientEventId = Guid.NewGuid(),
                    Level = logLevel.ToString(),
                    Message = message,
                    TimestampUtc = DateTime.UtcNow,
                    ProjectCode = options.ProjectCode,
                    Environment = options.Environment,
                    SourceType = options.SourceType,
                    ServiceName = options.ServiceName,
                    Category = category,
                    EventId = FormatEventId(eventId),
                    TraceId = Activity.Current?.TraceId.ToString(),
                    RequestPath = routePattern,
                    RequestMethod = httpContext?.Request.Method,
                    StatusCode = httpContext?.Response.StatusCode,
                    ExceptionType = exception?.GetType().FullName,
                    ExceptionMessage = null,
                    StackTrace = exception?.StackTrace,
                    Properties = null
                });
            }
            catch
            {
                // 远程日志永远不能反向打断业务请求或 Host。
            }
        }

        private static string? FormatEventId(EventId eventId)
        {
            if (!string.IsNullOrWhiteSpace(eventId.Name))
            {
                return eventId.Name;
            }

            return eventId.Id == 0 ? null : eventId.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FormatMessage<TState>(
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter,
            out bool isStructuredTemplate)
        {
            isStructuredTemplate = false;
            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            {
                var originalFormat = properties.FirstOrDefault(property =>
                    string.Equals(property.Key, "{OriginalFormat}", StringComparison.Ordinal));
                if (originalFormat.Value is string template)
                {
                    // 结构化日志只上传模板，不能让 formatter 把凭证、卡号等参数值渲染进消息。
                    isStructuredTemplate = true;
                    return template;
                }
            }

            return formatter(state, exception);
        }

        private static string SanitizeMessage(string message, bool isStructuredTemplate)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }

            var sanitized = ConnectionStringPattern.Replace(message, "[CONNECTION_STRING_REDACTED]");
            sanitized = UrlQueryPattern.Replace(sanitized, "${base}?[REDACTED]");
            // 必须先识别完整的空格/连字符卡号，再处理键值，避免只吃掉 PAN 后的第一组数字。
            sanitized = PaymentCardCandidatePattern.Replace(
                sanitized,
                match => IsValidPaymentCardNumber(match.Value) ? "[REDACTED]" : match.Value);
            sanitized = SensitivePairPattern.Replace(sanitized, match =>
            {
                if (string.Equals(match.Groups["value"].Value, "[REDACTED]", StringComparison.Ordinal))
                {
                    return match.Value;
                }

                // 结构化模板中的占位符不含真实值，保留原文可避免生成多余括号等畸形消息。
                if (isStructuredTemplate && StructuredPlaceholderPattern.IsMatch(match.Groups["value"].Value))
                {
                    return match.Value;
                }

                return $"{match.Groups["key"].Value}=[REDACTED]";
            });
            sanitized = BearerPattern.Replace(sanitized, "Bearer [REDACTED]");
            return sanitized;
        }

        private static bool IsValidPaymentCardNumber(string candidate)
        {
            var digits = candidate.Where(char.IsAsciiDigit).Select(character => character - '0').ToArray();
            if (digits.Length is < 13 or > 19)
            {
                return false;
            }

            var sum = 0;
            var doubleDigit = false;
            for (var index = digits.Length - 1; index >= 0; index--)
            {
                var digit = digits[index];
                if (doubleDigit)
                {
                    digit *= 2;
                    if (digit > 9)
                    {
                        digit -= 9;
                    }
                }

                sum += digit;
                doubleDigit = !doubleDigit;
            }

            return sum % 10 == 0;
        }
    }
}
