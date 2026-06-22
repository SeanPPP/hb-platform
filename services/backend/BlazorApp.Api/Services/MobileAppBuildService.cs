using System.Text.Json;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    public class MobileAppBuildService : IMobileAppBuildService
    {
        private readonly ISqlSugarClient _db;
        private readonly EasWebhookOptions _options;
        private readonly ILogger<MobileAppBuildService> _logger;

        public MobileAppBuildService(
            ISqlSugarClient db,
            IOptions<EasWebhookOptions> options,
            ILogger<MobileAppBuildService> logger
        )
        {
            _db = db;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<ApiResponse<MobileAppBuildWebhookResultDto>> HandleEasWebhookAsync(
            string json
        )
        {
            EasBuildPayload payload;
            try
            {
                payload = ParsePayload(json);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "EAS Webhook JSON 解析失败");
                return ApiResponse<MobileAppBuildWebhookResultDto>.OK(
                    new MobileAppBuildWebhookResultDto
                    {
                        Action = "ignored",
                        Reason = "invalid_webhook_json",
                    }
                );
            }

            var ignoreReason = GetIgnoreReason(payload);
            if (ignoreReason != null)
            {
                _logger.LogInformation(
                    "EAS Webhook 已忽略，Reason: {Reason}, EasBuildId: {EasBuildId}",
                    ignoreReason,
                    payload.EasBuildId
                );
                return ApiResponse<MobileAppBuildWebhookResultDto>.OK(
                    new MobileAppBuildWebhookResultDto
                    {
                        Action = "ignored",
                        Reason = ignoreReason,
                        EasBuildId = payload.EasBuildId,
                    }
                );
            }

            var now = DateTime.UtcNow;
            var existing = await _db
                .Queryable<MobileAppBuild>()
                .FirstAsync(x => x.EasBuildId == payload.EasBuildId);
            var action = existing == null ? "saved" : "updated";
            var entity = existing ?? new MobileAppBuild { Id = Guid.NewGuid() };

            // EAS 会重试同一个 buildId；这里用幂等更新保留单条最新产物记录。
            ApplyPayload(entity, payload, now);

            if (existing == null)
            {
                try
                {
                    await _db.Insertable(entity).ExecuteCommandAsync();
                }
                catch (Exception ex) when (IsUniqueBuildIdConflict(ex))
                {
                    _logger.LogInformation(
                        ex,
                        "EAS Webhook 并发写入检测到重复 buildId，转为更新。EasBuildId: {EasBuildId}",
                        payload.EasBuildId
                    );
                    var concurrentExisting = await _db
                        .Queryable<MobileAppBuild>()
                        .FirstAsync(x => x.EasBuildId == payload.EasBuildId);
                    if (concurrentExisting == null)
                    {
                        throw;
                    }

                    entity.Id = concurrentExisting.Id;
                    await _db.Updateable(entity).ExecuteCommandAsync();
                    action = "updated";
                }
            }
            else
            {
                await _db.Updateable(entity).ExecuteCommandAsync();
            }

            return ApiResponse<MobileAppBuildWebhookResultDto>.OK(
                new MobileAppBuildWebhookResultDto
                {
                    Action = action,
                    Reason = "ok",
                    EasBuildId = payload.EasBuildId,
                }
            );
        }

        public async Task<ApiResponse<MobileAppBuildDto?>> GetLatestAsync(string profile)
        {
            var normalizedProfile = NormalizeProfile(profile);
            var entity = await _db
                .Queryable<MobileAppBuild>()
                .Where(x =>
                    x.Platform == "android"
                    && x.Status == "finished"
                    && x.BuildProfile == normalizedProfile
                    && !string.IsNullOrEmpty(x.ArtifactUrl)
                    && (x.ExpirationDate == null || x.ExpirationDate > DateTime.UtcNow)
                )
                .OrderByDescending(x => x.CompletedAt)
                .OrderByDescending(x => x.ReceivedAt)
                .FirstAsync();

            return ApiResponse<MobileAppBuildDto?>.OK(entity == null ? null : MapToDto(entity));
        }

        public async Task<ApiResponse<PagedResult<MobileAppBuildDto>>> GetHistoryAsync(
            MobileAppBuildQueryDto query
        )
        {
            var page = Math.Max(query.Page, 1);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);
            var profile = NormalizeProfile(query.Profile);
            // 历史记录默认也按 production 过滤，避免漏传 profile 时把 preview 和 production 混在一起。
            var queryable = _db
                .Queryable<MobileAppBuild>()
                .Where(x => x.BuildProfile == profile);

            var total = await queryable.CountAsync();
            var items = await queryable
                .OrderByDescending(x => x.CompletedAt)
                .OrderByDescending(x => x.ReceivedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return ApiResponse<PagedResult<MobileAppBuildDto>>.OK(
                new PagedResult<MobileAppBuildDto>
                {
                    Items = items.Select(MapToDto).ToList(),
                    Total = total,
                    Page = page,
                    PageSize = pageSize,
                }
            );
        }

        public async Task<ApiResponse<MobileAppOtaUpdateDto>> UpsertOtaUpdateAsync(
            MobileAppOtaUpdateUpsertDto dto
        )
        {
            var updateGroupId = NormalizeRequiredText(dto.UpdateGroupId);
            if (!IsValidUpdateGroupId(updateGroupId))
            {
                return ApiResponse<MobileAppOtaUpdateDto>.Error(
                    "UpdateGroupId 必须是 EAS update group UUID",
                    "INVALID_UPDATE_GROUP_ID"
                );
            }

            // 当前 OTA 发布记录只登记 Android 更新，和 APK 构建表保持分离。
            var platform = NormalizePlatform(dto.Platform);
            var existing = await _db
                .Queryable<MobileAppOtaUpdate>()
                .FirstAsync(x => x.UpdateGroupId == updateGroupId && x.Platform == platform);
            var entity = existing ?? new MobileAppOtaUpdate { Id = Guid.NewGuid() };

            // EAS update webhook 或人工登记可能重复提交同一 group；按 group+platform 幂等更新。
            ApplyOtaUpdate(entity, dto, updateGroupId, platform);

            if (existing == null)
            {
                try
                {
                    await _db.Insertable(entity).ExecuteCommandAsync();
                }
                catch (Exception ex) when (IsUniqueOtaUpdateConflict(ex))
                {
                    _logger.LogInformation(
                        ex,
                        "EAS OTA 并发写入检测到重复 group/platform，转为更新。UpdateGroupId: {UpdateGroupId}, Platform: {Platform}",
                        updateGroupId,
                        platform
                    );
                    var concurrentExisting = await _db
                        .Queryable<MobileAppOtaUpdate>()
                        .FirstAsync(x => x.UpdateGroupId == updateGroupId && x.Platform == platform);
                    if (concurrentExisting == null)
                    {
                        throw;
                    }

                    entity.Id = concurrentExisting.Id;
                    await _db.Updateable(entity).ExecuteCommandAsync();
                }
            }
            else
            {
                await _db.Updateable(entity).ExecuteCommandAsync();
            }

            return ApiResponse<MobileAppOtaUpdateDto>.OK(MapToDto(entity));
        }

        public async Task<ApiResponse<PagedResult<MobileAppOtaUpdateDto>>> GetOtaUpdatesAsync(
            MobileAppOtaUpdateQueryDto query
        )
        {
            var page = Math.Max(query.Page, 1);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);
            var channel = NormalizeChannel(query.Channel);
            var runtimeVersion = NormalizeOptionalText(query.RuntimeVersion);
            // OTA 列表默认只看 production，避免未带 channel 时混入 preview 更新。
            var queryable = _db
                .Queryable<MobileAppOtaUpdate>()
                .Where(x => x.Channel == channel);

            if (!string.IsNullOrWhiteSpace(runtimeVersion))
            {
                queryable = queryable.Where(x => x.RuntimeVersion == runtimeVersion);
            }

            var total = await queryable.CountAsync();
            var items = await queryable
                .OrderByDescending(x => x.PublishedAt)
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return ApiResponse<PagedResult<MobileAppOtaUpdateDto>>.OK(
                new PagedResult<MobileAppOtaUpdateDto>
                {
                    Items = items.Select(MapToDto).ToList(),
                    Total = total,
                    Page = page,
                    PageSize = pageSize,
                }
            );
        }

        public Task<ApiResponse<MobileAppOtaRollbackCommandDto>> CreateOtaRollbackCommandAsync(
            string updateGroupId,
            MobileAppOtaRollbackCommandDto dto
        )
        {
            var normalizedGroupId = NormalizeRequiredText(updateGroupId);
            if (!IsValidUpdateGroupId(normalizedGroupId))
            {
                return Task.FromResult(
                    ApiResponse<MobileAppOtaRollbackCommandDto>.Error(
                        "UpdateGroupId 必须是 EAS update group UUID",
                        "INVALID_UPDATE_GROUP_ID"
                    )
                );
            }

            var platform = NormalizePlatform(dto.Platform);
            var message = NormalizeOptionalText(dto.Message) ?? normalizedGroupId;
            var rollbackMessage = $"回退 OTA：{message}";
            // 这里只生成可审计命令，不在服务端执行 eas-cli，避免 API 请求触发外部发布动作。
            var command =
                $"npx eas-cli@latest update:rollback {ShellQuote(normalizedGroupId)} -p {ShellQuote(platform)} -m {ShellQuote(rollbackMessage)} --non-interactive";

            return Task.FromResult(
                ApiResponse<MobileAppOtaRollbackCommandDto>.OK(
                    new MobileAppOtaRollbackCommandDto
                    {
                        UpdateGroupId = normalizedGroupId,
                        Platform = platform,
                        Message = message,
                        Command = command,
                    }
                )
            );
        }

        private string? GetIgnoreReason(EasBuildPayload payload)
        {
            if (string.IsNullOrWhiteSpace(payload.EasBuildId))
                return "missing_build_id";
            if (!MatchesConfiguredValue(_options.AllowedAccountName, payload.AccountName))
                return "account_not_allowed";
            if (!MatchesConfiguredValue(_options.AllowedProjectName, payload.ProjectName))
                return "project_not_allowed";
            if (!AcceptedProfiles().Contains(payload.BuildProfile, StringComparer.OrdinalIgnoreCase))
                return "profile_not_accepted";
            if (!string.Equals(payload.Platform, "android", StringComparison.OrdinalIgnoreCase))
                return "platform_not_android";
            if (!string.Equals(payload.Status, "finished", StringComparison.OrdinalIgnoreCase))
                return "status_not_finished";
            if (string.IsNullOrWhiteSpace(payload.ArtifactUrl))
                return "missing_artifact_url";
            if (!IsHttpsUrl(payload.ArtifactUrl))
                return "invalid_artifact_url";

            return null;
        }

        private static bool MatchesConfiguredValue(string? expected, string actual)
        {
            return !string.IsNullOrWhiteSpace(expected)
                && string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        }

        private string[] AcceptedProfiles()
        {
            return _options.AcceptedProfiles is not { Length: > 0 }
                ? ["preview", "production"]
                : _options.AcceptedProfiles;
        }

        private static string NormalizeProfile(string? profile)
        {
            return string.IsNullOrWhiteSpace(profile)
                ? "production"
                : profile.Trim().ToLowerInvariant();
        }

        private static string NormalizeChannel(string? channel)
        {
            return string.IsNullOrWhiteSpace(channel)
                ? "production"
                : channel.Trim().ToLowerInvariant();
        }

        private static string NormalizePlatform(string? platform)
        {
            // 目前 EAS OTA 记录只服务 Android 客户端，统一落为 android，避免同 group 被脏输入拆成多条。
            return "android";
        }

        private static string NormalizeRequiredText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string? NormalizeOptionalText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string? NormalizeOptionalHttpsUrl(string? value)
        {
            var normalized = NormalizeOptionalText(value);
            // DashboardUrl 会被后台页面直接打开；可选字段不阻断入库，但只保留 HTTPS 链接。
            return normalized != null && IsHttpsUrl(normalized) ? normalized : null;
        }

        private static bool IsValidUpdateGroupId(string updateGroupId)
        {
            return Guid.TryParse(updateGroupId, out _);
        }

        private static string ShellQuote(string value)
        {
            // 回撤命令展示给管理员复制执行；用 POSIX 单引号防止 $()、反引号等被 shell 展开。
            return $"'{value.Replace("'", "'\"'\"'")}'";
        }

        private static bool IsHttpsUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUniqueBuildIdConflict(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                var message = current.Message;
                if (
                    message.Contains("IX_MobileAppBuild_EasBuildId", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("EasBuildId", StringComparison.OrdinalIgnoreCase)
                        && (
                            message.Contains("unique", StringComparison.OrdinalIgnoreCase)
                            || message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                            || message.Contains("constraint", StringComparison.OrdinalIgnoreCase)
                        )
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsUniqueOtaUpdateConflict(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                var message = current.Message;
                if (
                    message.Contains(
                        "IX_MobileAppOtaUpdate_Group_Platform",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || message.Contains("UpdateGroupId", StringComparison.OrdinalIgnoreCase)
                        && message.Contains("Platform", StringComparison.OrdinalIgnoreCase)
                        && (
                            message.Contains("unique", StringComparison.OrdinalIgnoreCase)
                            || message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                            || message.Contains("constraint", StringComparison.OrdinalIgnoreCase)
                        )
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyPayload(MobileAppBuild entity, EasBuildPayload payload, DateTime now)
        {
            entity.EasBuildId = payload.EasBuildId;
            entity.AccountName = payload.AccountName;
            entity.ProjectName = payload.ProjectName;
            entity.AppName = payload.AppName;
            entity.Platform = payload.Platform.ToLowerInvariant();
            entity.Status = payload.Status.ToLowerInvariant();
            entity.BuildProfile = NormalizeProfile(payload.BuildProfile);
            entity.Distribution = payload.Distribution;
            entity.Channel = payload.Channel;
            entity.RuntimeVersion = payload.RuntimeVersion;
            entity.AppVersion = payload.AppVersion;
            entity.AppBuildVersion = payload.AppBuildVersion;
            entity.ArtifactUrl = payload.ArtifactUrl;
            entity.BuildDetailsPageUrl = payload.BuildDetailsPageUrl;
            entity.GitCommitHash = payload.GitCommitHash;
            entity.GitCommitMessage = payload.GitCommitMessage;
            entity.CreatedAt = payload.CreatedAt ?? entity.CreatedAt;
            entity.CompletedAt = payload.CompletedAt;
            entity.ExpirationDate = payload.ExpirationDate;
            entity.ReceivedAt = now;
        }

        private static MobileAppBuildDto MapToDto(MobileAppBuild entity)
        {
            return new MobileAppBuildDto
            {
                Id = entity.Id,
                EasBuildId = entity.EasBuildId,
                AccountName = entity.AccountName,
                ProjectName = entity.ProjectName,
                AppName = entity.AppName,
                Platform = entity.Platform,
                Status = entity.Status,
                BuildProfile = entity.BuildProfile,
                Distribution = entity.Distribution,
                Channel = entity.Channel,
                RuntimeVersion = entity.RuntimeVersion,
                AppVersion = entity.AppVersion,
                AppBuildVersion = entity.AppBuildVersion,
                ArtifactUrl = entity.ArtifactUrl,
                BuildDetailsPageUrl = entity.BuildDetailsPageUrl,
                GitCommitHash = entity.GitCommitHash,
                GitCommitMessage = entity.GitCommitMessage,
                CreatedAt = entity.CreatedAt,
                CompletedAt = entity.CompletedAt,
                ExpirationDate = entity.ExpirationDate,
                ReceivedAt = entity.ReceivedAt,
            };
        }

        private static void ApplyOtaUpdate(
            MobileAppOtaUpdate entity,
            MobileAppOtaUpdateUpsertDto dto,
            string updateGroupId,
            string platform
        )
        {
            entity.UpdateGroupId = updateGroupId;
            entity.AndroidUpdateId = NormalizeOptionalText(dto.AndroidUpdateId);
            entity.Channel = NormalizeChannel(dto.Channel);
            entity.Branch = NormalizeOptionalText(dto.Branch);
            entity.Platform = platform;
            entity.RuntimeVersion = NormalizeOptionalText(dto.RuntimeVersion);
            entity.Message = NormalizeOptionalText(dto.Message);
            entity.GitCommitHash = NormalizeOptionalText(dto.GitCommitHash);
            entity.DashboardUrl = NormalizeOptionalHttpsUrl(dto.DashboardUrl);
            entity.PublishedAt = dto.PublishedAt ?? DateTime.UtcNow;
            entity.IsRollback = dto.IsRollback;
            entity.RollbackOfGroupId = NormalizeOptionalText(dto.RollbackOfGroupId);
        }

        private static MobileAppOtaUpdateDto MapToDto(MobileAppOtaUpdate entity)
        {
            return new MobileAppOtaUpdateDto
            {
                Id = entity.Id,
                UpdateGroupId = entity.UpdateGroupId,
                AndroidUpdateId = entity.AndroidUpdateId,
                Channel = entity.Channel,
                Branch = entity.Branch,
                Platform = entity.Platform,
                RuntimeVersion = entity.RuntimeVersion,
                Message = entity.Message,
                GitCommitHash = entity.GitCommitHash,
                DashboardUrl = entity.DashboardUrl,
                PublishedAt = entity.PublishedAt,
                IsRollback = entity.IsRollback,
                RollbackOfGroupId = entity.RollbackOfGroupId,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
            };
        }

        private static EasBuildPayload ParsePayload(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new EasBuildPayload
            {
                EasBuildId = ReadString(root, "id", "buildId", "easBuildId"),
                AccountName = ReadString(root, "accountName", "account.name", "account.username"),
                ProjectName = ReadString(root, "projectName", "project.name"),
                AppName = ReadNullableString(root, "metadata.appName", "appName", "app.name"),
                Platform = ReadString(root, "platform"),
                Status = ReadString(root, "status"),
                BuildProfile = NormalizeProfile(
                    ReadString(root, "metadata.buildProfile", "buildProfile", "profile")
                ),
                Distribution = ReadNullableString(root, "metadata.distribution", "distribution"),
                Channel = ReadNullableString(root, "metadata.channel", "channel"),
                RuntimeVersion = ReadNullableString(root, "metadata.runtimeVersion", "runtimeVersion"),
                AppVersion = ReadNullableString(root, "metadata.appVersion", "appVersion", "version"),
                AppBuildVersion = ReadNullableString(
                    root,
                    "metadata.appBuildVersion",
                    "appBuildVersion",
                    "buildVersion"
                ),
                ArtifactUrl = ReadString(root, "artifacts.buildUrl", "artifactUrl"),
                BuildDetailsPageUrl = ReadNullableString(
                    root,
                    "buildDetailsPageUrl",
                    "buildUrl"
                ),
                GitCommitHash = ReadNullableString(
                    root,
                    "metadata.gitCommitHash",
                    "gitCommitHash",
                    "git.commitHash"
                ),
                GitCommitMessage = ReadNullableString(
                    root,
                    "metadata.gitCommitMessage",
                    "metadata.message",
                    "gitCommitMessage",
                    "git.commitMessage"
                ),
                CreatedAt = ReadDate(root, "createdAt", "created"),
                CompletedAt = ReadDate(root, "completedAt", "finishedAt"),
                ExpirationDate = ReadDate(root, "expirationDate", "expiresAt"),
            };
        }

        private static string ReadString(JsonElement root, params string[] paths)
        {
            return ReadNullableString(root, paths) ?? string.Empty;
        }

        private static string? ReadNullableString(JsonElement root, params string[] paths)
        {
            foreach (var path in paths)
            {
                if (!TryGetProperty(root, path, out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }

                if (
                    value.ValueKind is JsonValueKind.Number
                        or JsonValueKind.True
                        or JsonValueKind.False
                )
                {
                    return value.ToString();
                }
            }

            return null;
        }

        private static DateTime? ReadDate(JsonElement root, params string[] paths)
        {
            var value = ReadNullableString(root, paths);
            if (DateTime.TryParse(value, out var parsed))
            {
                return parsed.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                    : parsed.ToUniversalTime();
            }

            return null;
        }

        private static bool TryGetProperty(JsonElement root, string path, out JsonElement value)
        {
            value = root;
            foreach (var segment in path.Split('.'))
            {
                if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class EasBuildPayload
        {
            public string EasBuildId { get; set; } = string.Empty;
            public string AccountName { get; set; } = string.Empty;
            public string ProjectName { get; set; } = string.Empty;
            public string? AppName { get; set; }
            public string Platform { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string BuildProfile { get; set; } = "production";
            public string? Distribution { get; set; }
            public string? Channel { get; set; }
            public string? RuntimeVersion { get; set; }
            public string? AppVersion { get; set; }
            public string? AppBuildVersion { get; set; }
            public string ArtifactUrl { get; set; } = string.Empty;
            public string? BuildDetailsPageUrl { get; set; }
            public string? GitCommitHash { get; set; }
            public string? GitCommitMessage { get; set; }
            public DateTime? CreatedAt { get; set; }
            public DateTime? CompletedAt { get; set; }
            public DateTime? ExpirationDate { get; set; }
        }
    }
}
