using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BlazorApp.Api.Services.Logging
{
    public class ApplicationLogRateLimiter
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly JsonSerializerOptions IngestJsonOptions = new(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
        };
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
            // 保留原有调用契约，内部调用方不因 HTTP 请求体预算而改变行为。
            return TryConsume(projectCode, logCount, payloadBytes: 0, out message);
        }

        public bool TryConsume(
            string projectCode,
            int logCount,
            long payloadBytes,
            out string message
        )
        {
            var requestAllowed = TryConsumeRequestBudget(
                projectCode,
                payloadBytes,
                out var requestMessage
            );
            var logsAllowed = TryConsumeLogBudget(projectCode, logCount, out var logsMessage);

            message = requestAllowed ? logsMessage : requestMessage;
            return requestAllowed && logsAllowed;
        }

        public bool TryConsumeRequestBudget(
            string projectCode,
            long payloadBytes,
            out string message
        )
        {
            var options = _options.CurrentValue;
            var window = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmm");
            var requests = Increment($"application-log:req:{projectCode}:{window}", 1);
            var bytes = Increment(
                $"application-log:bytes:{projectCode}:{window}",
                Math.Max(0, payloadBytes)
            );

            if (requests > options.MaxIngestRequestsPerMinute)
            {
                message = "日志写入请求过于频繁，请稍后重试";
                return false;
            }

            if (bytes > options.MaxIngestBytesPerMinute)
            {
                message = "日志写入字节数超过项目每分钟限制，请稍后重试";
                return false;
            }

            message = string.Empty;
            return true;
        }

        public bool TryConsumeLogBudget(string projectCode, int logCount, out string message)
        {
            var options = _options.CurrentValue;
            var window = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmm");
            var logs = Increment(
                $"application-log:logs:{projectCode}:{window}",
                Math.Max(1, logCount)
            );

            if (logs > options.MaxIngestLogsPerMinute)
            {
                message = "日志写入数量超过项目每分钟限制，请稍后重试";
                return false;
            }

            message = string.Empty;
            return true;
        }

        public bool TryMeasureCanonicalRequestBytes(
            ApplicationLogIngestRequestDto? request,
            out long payloadBytes
        )
        {
            return TryGetCanonicalJsonBytes(request, out payloadBytes);
        }

        public bool TryValidateIngestRequest(
            ApplicationLogIngestRequestDto? request,
            out long payloadBytes,
            out string message
        )
        {
            return TryValidateIngestRequest(
                request,
                actualPayloadBytes: null,
                out payloadBytes,
                out message
            );
        }

        public bool TryValidateIngestRequest(
            ApplicationLogIngestRequestDto? request,
            long? actualPayloadBytes,
            out long payloadBytes,
            out string message
        )
        {
            payloadBytes = 0;
            var options = _options.CurrentValue;
            if (request?.Logs is not { Count: > 0 })
            {
                message = "日志列表不能为空";
                return false;
            }

            if (request.Logs.Count > options.MaxBatchSize)
            {
                message = $"单次最多写入 {options.MaxBatchSize} 条日志";
                return false;
            }

            foreach (var item in request.Logs)
            {
                if (!TryValidateItemFields(item, options.MaxIngestFieldBytes))
                {
                    message = $"单个日志字段不能超过 {options.MaxIngestFieldBytes} 字节";
                    return false;
                }

                if (!TryGetCanonicalJsonBytes(item, out var itemBytes))
                {
                    message = "日志内容无法进行规范 JSON 序列化";
                    return false;
                }

                if (itemBytes > options.MaxIngestItemBytes)
                {
                    message = $"单条日志不能超过 {options.MaxIngestItemBytes} 字节";
                    return false;
                }
            }

            if (!TryGetCanonicalJsonBytes(request, out payloadBytes))
            {
                message = "日志请求无法进行规范 JSON 序列化";
                return false;
            }

            var batchPayloadBytes = actualPayloadBytes is >= 0
                ? actualPayloadBytes.Value
                : payloadBytes;
            if (batchPayloadBytes > options.MaxIngestBatchBytes)
            {
                message = $"单次日志总计不能超过 {options.MaxIngestBatchBytes} 字节";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private static bool TryValidateItemFields(
            ApplicationLogIngestItemDto? item,
            int maxFieldBytes
        )
        {
            // JSON 绑定后先拒绝大字段，避免进入多轮正则脱敏；结构预算另按规范 JSON 计量。
            if (item == null)
                return true;

            foreach (
                var value in new[]
                {
                    item.Level,
                    item.Message,
                    item.ProjectCode,
                    item.Environment,
                    item.SourceType,
                    item.ServiceName,
                    item.InstanceId,
                    item.StoreCode,
                    item.DeviceCode,
                    item.AppVersion,
                    item.Category,
                    item.EventId,
                    item.TraceId,
                    item.RequestPath,
                    item.RequestMethod,
                    item.UserId,
                    item.UserName,
                    item.ClientIp,
                    item.ExceptionType,
                    item.ExceptionMessage,
                    item.StackTrace,
                }
            )
            {
                if (!IsTextWithinFieldLimit(value, maxFieldBytes))
                    return false;
            }

            if (item.Properties == null)
                return true;

            foreach (var property in item.Properties)
            {
                if (!IsTextWithinFieldLimit(property.Key, maxFieldBytes))
                    return false;
                if (!IsValueWithinFieldLimit(property.Value, maxFieldBytes))
                    return false;
            }

            return true;
        }

        private static bool IsValueWithinFieldLimit(object? value, int maxFieldBytes)
        {
            if (value == null)
                return true;

            if (value is JsonElement jsonElement)
                return AreJsonElementFieldsWithinLimit(jsonElement, maxFieldBytes);

            var text = value switch
            {
                string stringValue => stringValue,
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            };
            return IsTextWithinFieldLimit(text, maxFieldBytes);
        }

        private static bool AreJsonElementFieldsWithinLimit(
            JsonElement element,
            int maxFieldBytes
        )
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (!IsTextWithinFieldLimit(property.Name, maxFieldBytes))
                            return false;
                        if (!AreJsonElementFieldsWithinLimit(property.Value, maxFieldBytes))
                            return false;
                    }
                    return true;
                case JsonValueKind.Array:
                    return element
                        .EnumerateArray()
                        .All(item => AreJsonElementFieldsWithinLimit(item, maxFieldBytes));
                case JsonValueKind.String:
                    return IsTextWithinFieldLimit(element.GetString(), maxFieldBytes);
                case JsonValueKind.Number:
                    return IsTextWithinFieldLimit(element.GetRawText(), maxFieldBytes);
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsTextWithinFieldLimit(string? value, int maxFieldBytes)
        {
            if (string.IsNullOrEmpty(value))
                return true;

            return Utf8.GetByteCount(value) <= maxFieldBytes;
        }

        private static bool TryGetCanonicalJsonBytes<T>(T value, out long payloadBytes)
        {
            try
            {
                payloadBytes = checked(
                    0L + JsonSerializer.SerializeToUtf8Bytes(value, IngestJsonOptions).LongLength
                );
                return true;
            }
            catch (JsonException)
            {
                payloadBytes = 0;
                return false;
            }
            catch (NotSupportedException)
            {
                payloadBytes = 0;
                return false;
            }
            catch (InvalidOperationException)
            {
                payloadBytes = 0;
                return false;
            }
            catch (OverflowException)
            {
                payloadBytes = 0;
                return false;
            }
        }

        private long Increment(string key, long value)
        {
            lock (_cache)
            {
                var current = _cache.Get<long?>(key) ?? 0;
                long next;
                try
                {
                    next = checked(current + value);
                }
                catch (OverflowException)
                {
                    // 极端配置下饱和到上限，保持 fail-closed，不能因计数溢出重新放行。
                    next = long.MaxValue;
                }
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
