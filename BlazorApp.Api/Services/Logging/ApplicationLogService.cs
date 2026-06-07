using System.Collections;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
            ApplicationLogIngestRequestDto request
        )
        {
            if (request.Logs.Count > _options.MaxBatchSize)
                throw new InvalidOperationException($"单次最多写入 {_options.MaxBatchSize} 条日志");

            var project = FindProject(projectCode);
            var logs = request
                .Logs.Where(item =>
                    !string.IsNullOrWhiteSpace(item.Level)
                    && !string.IsNullOrWhiteSpace(item.Message)
                    && !string.IsNullOrWhiteSpace(item.Environment)
                    && !string.IsNullOrWhiteSpace(item.SourceType)
                    && AllowedSourceTypes.Contains(item.SourceType.Trim())
                )
                .Select(item => BuildEntity(project, projectCode, item))
                .ToList();

            if (logs.Count > 0)
                await _db.Insertable(logs).ExecuteCommandAsync();

            return new ApplicationLogIngestResultDto
            {
                AcceptedCount = logs.Count,
                RejectedCount = request.Logs.Count - logs.Count,
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
                    .Where(x => x.ProjectCode == project.ProjectCode && x.TimestampUtc < cutoff)
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
            ApplicationLogIngestItemDto item
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
                Level = Truncate(item.Level, 30) ?? LogLevel.Information.ToString(),
                Category = Truncate(item.Category, 240),
                EventId = Truncate(item.EventId, 80),
                Message = Truncate(item.Message, _options.MaxMessageLength) ?? string.Empty,
                ExceptionType = Truncate(item.ExceptionType, 240),
                ExceptionMessage = Truncate(item.ExceptionMessage, _options.MaxMessageLength),
                StackTrace = Truncate(item.StackTrace, _options.MaxStackTraceLength),
                RequestPath = Truncate(item.RequestPath, 500),
                RequestMethod = Truncate(item.RequestMethod, 20),
                StatusCode = item.StatusCode,
                TraceId = Truncate(item.TraceId, 120),
                UserId = Truncate(item.UserId, 120),
                UserName = Truncate(item.UserName, 120),
                ClientIp = Truncate(item.ClientIp, 80),
                PropertiesJson = Truncate(SerializeSafeProperties(item.Properties), _options.MaxPropertiesLength),
            };
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
                TimestampUtc = entity.TimestampUtc,
                ProjectCode = entity.ProjectCode,
                ProjectName = entity.ProjectName,
                Environment = entity.Environment,
                SourceType = entity.SourceType,
                ServiceName = entity.ServiceName,
                Level = entity.Level,
                Category = entity.Category,
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
            };
        }

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
                item => ToJsonSafeValue(item.Value)
            );
            return JsonSerializer.Serialize(safeProperties);
        }

        private static object? ToJsonSafeValue(object? value, int depth = 0)
        {
            if (value == null)
                return null;
            if (depth >= 4)
                return value.ToString();

            return value switch
            {
                string or bool or char => value,
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
                JsonElement jsonElement => ToJsonSafeValueFromElement(jsonElement, depth),
                IDictionary dictionary => dictionary
                    .Cast<DictionaryEntry>()
                    .ToDictionary(
                        entry => entry.Key?.ToString() ?? string.Empty,
                        entry => ToJsonSafeValue(entry.Value, depth + 1)
                    ),
                IEnumerable enumerable when value is not string => enumerable
                    .Cast<object?>()
                    .Select(item => ToJsonSafeValue(item, depth + 1))
                    .ToList(),
                _ => value.ToString(),
            };
        }

        private static object? ToJsonSafeValueFromElement(JsonElement element, int depth)
        {
            if (depth >= 4)
                return element.ToString();

            return element.ValueKind switch
            {
                JsonValueKind.Object => element
                    .EnumerateObject()
                    .ToDictionary(
                        property => property.Name,
                        property => ToJsonSafeValueFromElement(property.Value, depth + 1)
                    ),
                JsonValueKind.Array => element
                    .EnumerateArray()
                    .Select(item => ToJsonSafeValueFromElement(item, depth + 1))
                    .ToList(),
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
                JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.ToString(),
            };
        }
    }
}
