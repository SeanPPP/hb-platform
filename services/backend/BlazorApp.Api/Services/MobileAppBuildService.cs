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
            var queryable = _db.Queryable<MobileAppBuild>();
            if (!string.IsNullOrWhiteSpace(query.Profile))
            {
                var profile = NormalizeProfile(query.Profile);
                queryable = queryable.Where(x => x.BuildProfile == profile);
            }

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
