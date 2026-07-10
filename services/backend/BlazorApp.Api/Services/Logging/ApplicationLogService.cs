using System.Collections;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services.Logging
{
    public class ApplicationLogService
    {
        private static readonly HashSet<string> AllowedSourceTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Backend",
            "Web",
            "Mobile",
            "POS",
        };
        private static readonly Regex BearerTokenPattern = new(
            @"\bBearer\s+[^\s,;]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
        );
        private static readonly Regex SensitiveAssignmentPattern = new(
            @"\b(authorization(?:code)?|password|pin|api[-_]?key|token|secret|credential|cvv|pan|cardnumber|voucher[-_]?code|employee[-_]?barcode)\b\s*[:=]\s*[^\s,;]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
        );
        private static readonly Regex PanPattern = new(
            @"(?<!\d)(?:\d[ -]?){13,19}(?!\d)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled
        );
        private static readonly Regex UrlQueryPattern = new(
            @"(?<url>(?:https?://|/)[^\s?]+)\?[^\s]*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
        );
        private static readonly string[] SensitiveKeyFragments =
        [
            "authorization",
            "bearer",
            "password",
            "pin",
            "apikey",
            "token",
            "secret",
            "credential",
            "cvv",
            "pan",
            "cardnumber",
            "vouchercode",
            "employeebarcode",
            "customeremail",
            "customerphone",
            "customeraddress",
            "customername",
            "requestbody",
            "responsebody",
            "rawrequest",
            "rawresponse",
        ];
        private const string RedactedValue = "[REDACTED]";

        private readonly ISqlSugarClient _db;
        private readonly ApplicationLoggingOptions _options;
        private readonly ILogger<ApplicationLogService> _logger;
        private readonly IApplicationLogQueue? _queue;

        public ApplicationLogService(
            ISqlSugarClient db,
            IOptions<ApplicationLoggingOptions> options,
            ILogger<ApplicationLogService> logger,
            IApplicationLogQueue? queue = null
        )
        {
            _db = db;
            _options = options.Value;
            _logger = logger;
            _queue = queue;
        }

        public Task<ApplicationLoggingProjectOptions?> AuthenticateProjectAsync(
            string? projectCode,
            string? apiKey
        )
        {
            if (string.IsNullOrWhiteSpace(projectCode) || string.IsNullOrWhiteSpace(apiKey))
                return Task.FromResult<ApplicationLoggingProjectOptions?>(null);

            var project = FindProject(projectCode);
            if (project == null || !project.Enabled || string.IsNullOrWhiteSpace(project.ApiKeyHash))
                return Task.FromResult<ApplicationLoggingProjectOptions?>(null);

            var incomingHash = ComputeSha256(apiKey);
            var matched = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(incomingHash),
                Encoding.UTF8.GetBytes(project.ApiKeyHash.Trim().ToLowerInvariant())
            );

            return Task.FromResult(matched ? project : null);
        }

        public async Task<ApplicationLogIngestResultDto> IngestAsync(
            string projectCode,
            ApplicationLogIngestRequestDto request,
            string? trustedClientIp = null
        )
        {
            if (request.Logs.Count > _options.MaxBatchSize)
                throw new InvalidOperationException($"单次最多写入 {_options.MaxBatchSize} 条日志");

            var project = FindProject(projectCode);
            var itemResults = request
                .Logs.Select(item => new ApplicationLogIngestItemResultDto
                {
                    ClientEventId = item.ClientEventId,
                })
                .ToList();
            var legacyLogs = new List<(int Index, ApplicationLog Entity)>();
            var idempotentLogs = new List<(int Index, ApplicationLog Entity)>();

            for (var index = 0; index < request.Logs.Count; index++)
            {
                var item = request.Logs[index];
                if (!IsValidIngestItem(item))
                {
                    itemResults[index].Status = "rejected";
                    itemResults[index].ErrorCode = "INVALID_LOG_ITEM";
                    continue;
                }

                var entity = BuildEntity(project, projectCode, item, trustedClientIp);
                if (entity.ClientEventId.HasValue)
                    idempotentLogs.Add((index, entity));
                else
                    legacyLogs.Add((index, entity));
            }

            // 旧客户端没有幂等键，继续保持一次批量写入，避免改变既有吞吐表现。
            if (legacyLogs.Count > 0)
            {
                await _db.Insertable(legacyLogs.Select(item => item.Entity).ToList()).ExecuteCommandAsync();
                foreach (var item in legacyLogs)
                    itemResults[item.Index].Status = "accepted";
            }

            var batchEventIds = new HashSet<Guid>();
            foreach (var item in idempotentLogs)
            {
                var clientEventId = item.Entity.ClientEventId!.Value;
                if (!batchEventIds.Add(clientEventId) || await ClientEventExistsAsync(item.Entity))
                {
                    itemResults[item.Index].Status = "duplicate";
                    continue;
                }

                try
                {
                    await _db.Insertable(item.Entity).ExecuteCommandAsync();
                    itemResults[item.Index].Status = "accepted";
                }
                catch (Exception ex)
                {
                    // 并发请求可能同时通过预检查，唯一索引是最终幂等边界。
                    if (!await ClientEventExistsAsync(item.Entity))
                        throw;

                    _logger.LogDebug(
                        ex,
                        "中心日志并发重复写入已按幂等处理: {ProjectCode}/{ClientEventId}",
                        item.Entity.ProjectCode,
                        clientEventId
                    );
                    itemResults[item.Index].Status = "duplicate";
                }
            }

            return new ApplicationLogIngestResultDto
            {
                AcceptedCount = itemResults.Count(item => item.Status == "accepted"),
                RejectedCount = itemResults.Count(item => item.Status == "rejected"),
                DuplicateCount = itemResults.Count(item => item.Status == "duplicate"),
                Results = itemResults,
            };
        }

        public async Task<PagedResult<ApplicationLogDto>> QueryAsync(ApplicationLogQueryDto query)
        {
            var pageNumber = Math.Max(1, query.PageNumber);
            var pageSize = Math.Clamp(query.PageSize, 1, 200);
            var dbQuery = ApplyQuery(_db.Queryable<ApplicationLog>(), query);

            dbQuery = (query.SortBy ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "level" => IsAsc(query)
                    ? dbQuery.OrderBy(x => x.Level)
                    : dbQuery.OrderByDescending(x => x.Level),
                "projectcode" => IsAsc(query)
                    ? dbQuery.OrderBy(x => x.ProjectCode)
                    : dbQuery.OrderByDescending(x => x.ProjectCode),
                _ => IsAsc(query)
                    ? dbQuery.OrderBy(x => x.TimestampUtc)
                    : dbQuery.OrderByDescending(x => x.TimestampUtc),
            };

            RefAsync<int> total = 0;
            var items = await dbQuery.ToPageListAsync(pageNumber, pageSize, total);

            return new PagedResult<ApplicationLogDto>
            {
                Items = items.Select(ToDto).ToList(),
                Total = total,
                Page = pageNumber,
                PageSize = pageSize,
            };
        }

        public async Task<ApplicationLogDto?> GetAsync(Guid id)
        {
            var entity = await _db.Queryable<ApplicationLog>().FirstAsync(x => x.Id == id);
            return entity == null ? null : ToDto(entity);
        }

        public async Task<ApplicationLogSummaryDto> GetSummaryAsync(ApplicationLogQueryDto query)
        {
            var runtimeSnapshot = _queue?.GetRuntimeSnapshot() ?? new ApplicationLogQueueRuntimeSnapshot();
            return new ApplicationLogSummaryDto
            {
                Total = await ApplyQuery(_db.Queryable<ApplicationLog>(), query).CountAsync(),
                ByProject = await QueryGroupAsync(query, "ProjectCode"),
                ByLevel = await QueryGroupAsync(query, "Level"),
                ByExceptionType = await QueryGroupAsync(query, "ExceptionType"),
                ByRequestPath = await QueryGroupAsync(query, "RequestPath"),
                Pipeline = new ApplicationLogPipelineRuntimeDto
                {
                    DroppedOldestCount = runtimeSnapshot.DroppedOldestCount,
                    EnqueueFailureCount = runtimeSnapshot.EnqueueFailureCount,
                    FailedFlushBatchCount = runtimeSnapshot.FailedFlushBatchCount,
                    FailedFlushLogCount = runtimeSnapshot.FailedFlushLogCount,
                    LastFailedFlushBatchSize = runtimeSnapshot.LastFailedFlushBatchSize,
                    LastFailedFlushReason = runtimeSnapshot.LastFailedFlushReason,
                },
            };
        }

        public async Task<int> CleanupExpiredLogsAsync(DateTime nowUtc)
        {
            var deleted = 0;
            var projects = _options.Projects.Count > 0
                ? _options.Projects
                : new List<ApplicationLoggingProjectOptions>
                {
                    new()
                    {
                        ProjectCode = _options.DefaultProjectCode,
                        RetentionDays = _options.DefaultRetentionDays,
                        Enabled = true,
                    },
                };

            foreach (var project in projects.Where(x => !string.IsNullOrWhiteSpace(x.ProjectCode)))
            {
                var retentionDays = project.RetentionDays ?? _options.DefaultRetentionDays;
                var cutoff = nowUtc.AddDays(-retentionDays);
                deleted += await _db
                    .Deleteable<ApplicationLog>()
                    .Where(x => x.ProjectCode == project.ProjectCode && x.CreatedAt < cutoff)
                    .ExecuteCommandAsync();
            }

            return deleted;
        }

        public ApplicationLogIngestItemDto CreateBackendLogItem(
            string category,
            LogLevel level,
            string message,
            Exception? exception,
            HttpContext? httpContext,
            string? eventId = null,
            IReadOnlyDictionary<string, object?>? properties = null
        )
        {
            return new ApplicationLogIngestItemDto
            {
                ProjectCode = _options.DefaultProjectCode,
                Environment = _options.DefaultEnvironment,
                SourceType = _options.DefaultSourceType,
                ServiceName = _options.ServiceName,
                InstanceId = _options.InstanceId,
                Level = level.ToString(),
                Category = category,
                EventId = eventId,
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
                Properties = properties?.ToDictionary(x => x.Key, x => x.Value),
            };
        }

        private ISugarQueryable<ApplicationLog> ApplyQuery(
            ISugarQueryable<ApplicationLog> dbQuery,
            ApplicationLogQueryDto query
        )
        {
            var projectCodes = NormalizeProjectCodes(query);
            if (projectCodes.Count > 0)
                // 多选项目过滤供中心日志页面使用；ProjectCode 单值保留给旧入口。
                dbQuery = dbQuery.Where(x => projectCodes.Contains(x.ProjectCode));
            else if (!string.IsNullOrWhiteSpace(query.ProjectCode))
                dbQuery = dbQuery.Where(x => x.ProjectCode == query.ProjectCode);
            if (!string.IsNullOrWhiteSpace(query.Environment))
                dbQuery = dbQuery.Where(x => x.Environment == query.Environment);
            if (!string.IsNullOrWhiteSpace(query.SourceType))
                dbQuery = dbQuery.Where(x => x.SourceType == query.SourceType);
            if (!string.IsNullOrWhiteSpace(query.Level))
                dbQuery = dbQuery.Where(x => x.Level == query.Level);
            if (!string.IsNullOrWhiteSpace(query.Category))
                dbQuery = dbQuery.Where(x => x.Category != null && x.Category.Contains(query.Category));
            if (!string.IsNullOrWhiteSpace(query.RequestPath))
                dbQuery = dbQuery.Where(x => x.RequestPath != null && x.RequestPath.Contains(query.RequestPath));
            if (!string.IsNullOrWhiteSpace(query.TraceId))
                dbQuery = dbQuery.Where(x => x.TraceId == query.TraceId);
            if (!string.IsNullOrWhiteSpace(query.UserId))
                dbQuery = dbQuery.Where(x => x.UserId == query.UserId);
            if (!string.IsNullOrWhiteSpace(query.UserName))
                dbQuery = dbQuery.Where(x => x.UserName != null && x.UserName.Contains(query.UserName));
            if (!string.IsNullOrWhiteSpace(query.StoreCode))
                dbQuery = dbQuery.Where(x => x.StoreCode == query.StoreCode);
            if (!string.IsNullOrWhiteSpace(query.DeviceCode))
                dbQuery = dbQuery.Where(x => x.DeviceCode == query.DeviceCode);
            if (!string.IsNullOrWhiteSpace(query.AppVersion))
                dbQuery = dbQuery.Where(x => x.AppVersion == query.AppVersion);
            if (!string.IsNullOrWhiteSpace(query.InstanceId))
                dbQuery = dbQuery.Where(x => x.InstanceId == query.InstanceId);
            if (!string.IsNullOrWhiteSpace(query.EventId))
                dbQuery = dbQuery.Where(x => x.EventId == query.EventId);
            if (query.StartUtc.HasValue)
                dbQuery = dbQuery.Where(x => x.TimestampUtc >= query.StartUtc.Value);
            if (query.EndUtc.HasValue)
                // 时间窗统一按 [StartUtc, EndUtc) 处理，方便前端直接传“下一本地日开始时刻”做整日本地统计。
                dbQuery = dbQuery.Where(x => x.TimestampUtc < query.EndUtc.Value);
            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                dbQuery = dbQuery.Where(x =>
                    x.Message.Contains(query.Keyword)
                    || (x.ExceptionMessage != null && x.ExceptionMessage.Contains(query.Keyword))
                    || (x.StackTrace != null && x.StackTrace.Contains(query.Keyword))
                );
            }

            return dbQuery;
        }

        private ApplicationLog BuildEntity(
            ApplicationLoggingProjectOptions? project,
            string authenticatedProjectCode,
            ApplicationLogIngestItemDto item,
            string? trustedClientIp = null
        )
        {
            var projectCode = project?.ProjectCode ?? authenticatedProjectCode;
            return new ApplicationLog
            {
                Id = Guid.NewGuid(),
                TimestampUtc = item.TimestampUtc.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(item.TimestampUtc, DateTimeKind.Utc)
                    : item.TimestampUtc.ToUniversalTime(),
                ProjectCode = Truncate(projectCode, 80) ?? _options.DefaultProjectCode,
                ProjectName = Truncate(project?.DisplayName ?? projectCode, 120),
                Environment = Truncate(item.Environment, 60) ?? _options.DefaultEnvironment,
                SourceType = Truncate(item.SourceType, 60) ?? _options.DefaultSourceType,
                ServiceName = Truncate(item.ServiceName, 120),
                InstanceId = Truncate(item.InstanceId, 120),
                ClientEventId = item.ClientEventId,
                StoreCode = Truncate(item.StoreCode, 80),
                DeviceCode = Truncate(item.DeviceCode, 120),
                AppVersion = Truncate(item.AppVersion, 60),
                Level = Truncate(item.Level, 30) ?? LogLevel.Information.ToString(),
                Category = Truncate(item.Category, 240),
                EventId = Truncate(item.EventId, 80),
                Message = Truncate(SanitizeText(item.Message), _options.MaxMessageLength) ?? string.Empty,
                ExceptionType = Truncate(item.ExceptionType, 240),
                ExceptionMessage = Truncate(SanitizeText(item.ExceptionMessage), _options.MaxMessageLength),
                StackTrace = Truncate(SanitizeText(item.StackTrace), _options.MaxStackTraceLength),
                RequestPath = Truncate(SanitizeRequestPath(item.RequestPath), 500),
                RequestMethod = Truncate(item.RequestMethod, 20),
                StatusCode = item.StatusCode,
                TraceId = Truncate(item.TraceId, 120),
                UserId = Truncate(item.UserId, 120),
                UserName = Truncate(item.UserName, 120),
                ClientIp = Truncate(trustedClientIp ?? item.ClientIp, 80),
                PropertiesJson = Truncate(SerializeSafeProperties(item.Properties), _options.MaxPropertiesLength),
                CreatedAt = DateTime.UtcNow,
            };
        }

        private async Task<bool> ClientEventExistsAsync(ApplicationLog entity)
        {
            return await _db
                .Queryable<ApplicationLog>()
                .AnyAsync(x =>
                    x.ProjectCode == entity.ProjectCode && x.ClientEventId == entity.ClientEventId
                );
        }

        private static bool IsValidIngestItem(ApplicationLogIngestItemDto item)
        {
            return !string.IsNullOrWhiteSpace(item.Level)
                && !string.IsNullOrWhiteSpace(item.Message)
                && !string.IsNullOrWhiteSpace(item.Environment)
                && !string.IsNullOrWhiteSpace(item.SourceType)
                && AllowedSourceTypes.Contains(item.SourceType.Trim());
        }

        private ApplicationLoggingProjectOptions? FindProject(string? projectCode)
        {
            return _options.Projects.FirstOrDefault(project =>
                string.Equals(project.ProjectCode, projectCode, StringComparison.OrdinalIgnoreCase)
            );
        }

        private static bool IsAsc(ApplicationLogQueryDto query)
        {
            return string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        }

        private static List<ApplicationLogGroupCountDto> Group(
            List<ApplicationLog> logs,
            Func<ApplicationLog, string> selector
        )
        {
            return logs
                .GroupBy(selector)
                .Select(group => new ApplicationLogGroupCountDto
                {
                    Name = group.Key,
                    Count = group.Count(),
                })
                .OrderByDescending(item => item.Count)
                .Take(20)
                .ToList();
        }

        private async Task<List<ApplicationLogGroupCountDto>> QueryGroupAsync(
            ApplicationLogQueryDto query,
            string fieldName
        )
        {
            var sqlField = fieldName switch
            {
                "ProjectCode" => nameof(ApplicationLog.ProjectCode),
                "Level" => nameof(ApplicationLog.Level),
                "ExceptionType" => nameof(ApplicationLog.ExceptionType),
                "RequestPath" => nameof(ApplicationLog.RequestPath),
                _ => nameof(ApplicationLog.ProjectCode),
            };
            var dbQuery = ApplyQuery(_db.Queryable<ApplicationLog>(), query);
            var groups = await dbQuery
                .GroupBy(sqlField)
                .Select<ApplicationLogGroupCountDto>(
                    $"{sqlField} AS Name, COUNT(1) AS Count"
                )
                .OrderBy("Count DESC")
                .Take(20)
                .ToListAsync();

            return groups
                .Select(item => new ApplicationLogGroupCountDto
                {
                    Name = string.IsNullOrWhiteSpace(item.Name)
                        ? fieldName switch
                        {
                            "ExceptionType" => "无异常类型",
                            "RequestPath" => "无请求路径",
                            _ => "未设置",
                        }
                        : item.Name,
                    Count = item.Count,
                })
                .ToList();
        }

        private static ApplicationLogDto ToDto(ApplicationLog entity)
        {
            return new ApplicationLogDto
            {
                Id = entity.Id,
                TimestampUtc = AsUtc(entity.TimestampUtc),
                ProjectCode = entity.ProjectCode,
                ProjectName = entity.ProjectName,
                Environment = entity.Environment,
                SourceType = entity.SourceType,
                ServiceName = entity.ServiceName,
                InstanceId = entity.InstanceId,
                ClientEventId = entity.ClientEventId,
                StoreCode = entity.StoreCode,
                DeviceCode = entity.DeviceCode,
                AppVersion = entity.AppVersion,
                Level = entity.Level,
                Category = entity.Category,
                EventId = entity.EventId,
                Message = entity.Message,
                ExceptionType = entity.ExceptionType,
                ExceptionMessage = entity.ExceptionMessage,
                StackTrace = entity.StackTrace,
                RequestPath = entity.RequestPath,
                RequestMethod = entity.RequestMethod,
                StatusCode = entity.StatusCode,
                TraceId = entity.TraceId,
                UserId = entity.UserId,
                UserName = entity.UserName,
                ClientIp = entity.ClientIp,
                PropertiesJson = entity.PropertiesJson,
                CreatedAtUtc = AsUtc(entity.CreatedAt),
            };
        }

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        private static string ComputeSha256(string value)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            return value.Length <= maxLength ? value : value[..maxLength];
        }

        private static List<string> NormalizeProjectCodes(ApplicationLogQueryDto query)
        {
            return (query.ProjectCodes ?? new List<string>())
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string? SerializeSafeProperties(Dictionary<string, object?>? properties)
        {
            if (properties == null || properties.Count == 0)
                return null;

            var safeProperties = properties.ToDictionary(
                item => item.Key,
                item => ToJsonSafeValue(item.Value, key: item.Key)
            );
            return JsonSerializer.Serialize(safeProperties);
        }

        private static object? ToJsonSafeValue(object? value, int depth = 0, string? key = null)
        {
            if (IsSensitiveKey(key))
                return RedactedValue;
            if (value == null)
                return null;
            if (depth >= 4)
                return SanitizeText(value.ToString());

            return value switch
            {
                string stringValue => SanitizeText(stringValue),
                bool or char => value,
                byte or sbyte or short or ushort or int or uint or long or ulong => value,
                double doubleValue => double.IsFinite(doubleValue)
                    ? doubleValue
                    : doubleValue.ToString(CultureInfo.InvariantCulture),
                float floatValue => float.IsFinite(floatValue)
                    ? floatValue
                    : floatValue.ToString(CultureInfo.InvariantCulture),
                decimal => value,
                DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                Guid guid => guid.ToString(),
                Enum enumValue => enumValue.ToString(),
                JsonElement jsonElement => ToJsonSafeValueFromElement(jsonElement, depth, key),
                IDictionary dictionary => dictionary
                    .Keys.Cast<object?>()
                    .ToDictionary(
                        dictionaryKey => dictionaryKey?.ToString() ?? string.Empty,
                        dictionaryKey => ToJsonSafeValue(
                            dictionaryKey == null ? null : dictionary[dictionaryKey],
                            depth + 1,
                            dictionaryKey?.ToString()
                        )
                    ),
                IEnumerable enumerable when value is not string => enumerable
                    .Cast<object?>()
                    .Select(item => ToJsonSafeValue(item, depth + 1))
                    .ToList(),
                _ => SanitizeText(value.ToString()),
            };
        }

        private static object? ToJsonSafeValueFromElement(
            JsonElement element,
            int depth,
            string? key = null
        )
        {
            if (IsSensitiveKey(key))
                return RedactedValue;
            if (depth >= 4)
                return SanitizeText(element.ToString());

            return element.ValueKind switch
            {
                JsonValueKind.Object => element
                    .EnumerateObject()
                    .ToDictionary(
                        property => property.Name,
                        property => ToJsonSafeValueFromElement(
                            property.Value,
                            depth + 1,
                            property.Name
                        )
                    ),
                JsonValueKind.Array => element
                    .EnumerateArray()
                    .Select(item => ToJsonSafeValueFromElement(item, depth + 1))
                    .ToList(),
                JsonValueKind.String => SanitizeText(element.GetString()),
                JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
                JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => SanitizeText(element.ToString()),
            };
        }

        private static bool IsSensitiveKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            var normalized = new string(key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            return SensitiveKeyFragments.Any(normalized.Contains);
        }

        private static string? SanitizeRequestPath(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var queryIndex = value.IndexOfAny(new[] { '?', '#' });
            return queryIndex < 0 ? value : value[..queryIndex];
        }

        private static string? SanitizeText(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var sanitized = UrlQueryPattern.Replace(value, "${url}");
            sanitized = BearerTokenPattern.Replace(sanitized, RedactedValue);
            sanitized = SensitiveAssignmentPattern.Replace(sanitized, "$1=[REDACTED]");
            return PanPattern.Replace(sanitized, "[REDACTED_CARD]");
        }
    }
}
