using System.Text.Json;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    public class MobileAppBuildService : IMobileAppBuildService, IMobileAppBuildMirrorQueue
    {
        private readonly ISqlSugarClient _db;
        private readonly EasWebhookOptions _options;
        private readonly ILogger<MobileAppBuildService> _logger;
        private const string UnsafeArtifactMirrorErrorPrefix = "UNSAFE_ARTIFACT:";
        public const string CosMirrorStatusPending = "pending";
        public const string CosMirrorStatusRunning = "running";
        public const string CosMirrorStatusSucceeded = "succeeded";
        public const string CosMirrorStatusFailed = "failed";
        public const string CosMirrorStatusUnsafe = "unsafe";
        private const int CosMirrorErrorMaxLength = 1000;

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

            var appKey = ResolveAppKeyForProject(payload.ProjectName)
                ?? throw new InvalidOperationException("已通过校验的 EAS project 缺少 AppKey 映射。");

            var now = DateTime.UtcNow;
            var existing = await _db
                .Queryable<MobileAppBuild>()
                .FirstAsync(x => x.AppKey == appKey && x.EasBuildId == payload.EasBuildId);
            var action = existing == null ? "saved" : "updated";
            var entity = existing ?? new MobileAppBuild { Id = Guid.NewGuid() };
            var previousArtifactUrl = existing?.ArtifactUrl;

            // EAS 会重试同一个 buildId；这里用幂等更新保留单条最新产物记录。
            ApplyPayload(entity, payload, appKey, now);
            var artifactChanged = existing != null && HasArtifactUrlChanged(previousArtifactUrl, entity.ArtifactUrl);
            QueueCosMirrorIfNeeded(entity, existing == null, artifactChanged);

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
                        .FirstAsync(x => x.AppKey == appKey && x.EasBuildId == payload.EasBuildId);
                    if (concurrentExisting == null)
                    {
                        throw;
                    }

                    // 并发插入冲突后必须以数据库现有行为基准，避免新建实体的空 COS 字段覆盖已镜像结果。
                    var concurrentPreviousArtifactUrl = concurrentExisting.ArtifactUrl;
                    ApplyPayload(concurrentExisting, payload, appKey, now);
                    var concurrentArtifactChanged = HasArtifactUrlChanged(
                        concurrentPreviousArtifactUrl,
                        concurrentExisting.ArtifactUrl
                    );
                    QueueCosMirrorIfNeeded(concurrentExisting, false, concurrentArtifactChanged);

                    await _db.Updateable(concurrentExisting).ExecuteCommandAsync();
                    existing = concurrentExisting;
                    entity = concurrentExisting;
                    artifactChanged = concurrentArtifactChanged;
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

        public Task<ApiResponse<MobileAppBuildDto?>> GetLatestAsync(string profile)
        {
            return GetLatestAsync(MobileAppKeys.Mobile, profile);
        }

        public async Task<ApiResponse<MobileAppBuildDto?>> GetLatestAsync(
            string appKey,
            string profile
        )
        {
            if (!MobileAppKeys.TryNormalize(appKey, out var normalizedAppKey))
            {
                return ApiResponse<MobileAppBuildDto?>.OK(null);
            }

            var normalizedProfile = NormalizeProfile(profile);
            var now = DateTime.UtcNow;
            var entity = await _db
                .Queryable<MobileAppBuild>()
                .Where(x =>
                    x.AppKey == normalizedAppKey
                    && x.Platform == "android"
                    && x.Status == "finished"
                    && x.BuildProfile == normalizedProfile
                    && (x.CosMirrorStatus == null || x.CosMirrorStatus != CosMirrorStatusUnsafe)
                    && (
                        !string.IsNullOrEmpty(x.CosArtifactUrl)
                        || (
                            !string.IsNullOrEmpty(x.ArtifactUrl)
                            && (x.ExpirationDate == null || x.ExpirationDate > now)
                        )
                    )
                )
                .OrderByDescending(x => x.CompletedAt)
                .OrderByDescending(x => x.ReceivedAt)
                .FirstAsync();

            return ApiResponse<MobileAppBuildDto?>.OK(entity == null ? null : MapToDto(entity));
        }

        public Task<ApiResponse<MobileAppBuildDto?>> GetByBuildIdAsync(
            string easBuildId,
            string profile
        )
        {
            return GetByBuildIdAsync(MobileAppKeys.Mobile, easBuildId, profile);
        }

        public async Task<ApiResponse<MobileAppBuildDto?>> GetByBuildIdAsync(
            string appKey,
            string easBuildId,
            string profile
        )
        {
            if (!MobileAppKeys.TryNormalize(appKey, out var normalizedAppKey))
            {
                return ApiResponse<MobileAppBuildDto?>.OK(null);
            }

            var normalizedBuildId = NormalizeRequiredText(easBuildId);
            if (string.IsNullOrWhiteSpace(normalizedBuildId))
            {
                return ApiResponse<MobileAppBuildDto?>.OK(null);
            }

            var normalizedProfile = NormalizeProfile(profile);
            var now = DateTime.UtcNow;
            var entity = await _db
                .Queryable<MobileAppBuild>()
                .Where(x =>
                    x.AppKey == normalizedAppKey
                    && x.EasBuildId == normalizedBuildId
                    && x.Platform == "android"
                    && x.Status == "finished"
                    && x.BuildProfile == normalizedProfile
                    && (x.CosMirrorStatus == null || x.CosMirrorStatus != CosMirrorStatusUnsafe)
                    && (
                        !string.IsNullOrEmpty(x.CosArtifactUrl)
                        || (
                            !string.IsNullOrEmpty(x.ArtifactUrl)
                            && (x.ExpirationDate == null || x.ExpirationDate > now)
                        )
                    )
                )
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
            if (!MobileAppKeys.TryNormalizeOrLegacyMobile(query.AppKey, out var appKey))
            {
                return ApiResponse<PagedResult<MobileAppBuildDto>>.OK(
                    new PagedResult<MobileAppBuildDto>
                    {
                        Items = [],
                        Total = 0,
                        Page = page,
                        PageSize = pageSize,
                    }
                );
            }

            // 历史记录默认也按 production 过滤，避免漏传 profile 时把 preview 和 production 混在一起。
            var queryable = _db
                .Queryable<MobileAppBuild>()
                .Where(x => x.AppKey == appKey && x.BuildProfile == profile);

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
            var projectName = NormalizeOptionalText(dto.ProjectName)
                ?? NormalizeOptionalText(_options.AllowedProjectName)
                ?? string.Empty;
            var appKey = ResolveAppKeyForProject(projectName);
            if (appKey == null)
            {
                return ApiResponse<MobileAppOtaUpdateDto>.Error(
                    "ProjectName 未映射到受控 AppKey",
                    "PROJECT_NOT_ALLOWED"
                );
            }

            var updateGroupId = NormalizeRequiredText(dto.UpdateGroupId);
            if (!IsValidUpdateGroupId(updateGroupId))
            {
                return ApiResponse<MobileAppOtaUpdateDto>.Error(
                    "UpdateGroupId 必须是 EAS update group UUID",
                    "INVALID_UPDATE_GROUP_ID"
                );
            }

            // 关键逻辑：OTA 平台只接受 iOS/Android，未知输入不得静默伪装成 Android。
            var platform = NormalizePlatform(dto.Platform);
            if (platform == null)
            {
                return ApiResponse<MobileAppOtaUpdateDto>.Error(
                    "Platform 必须是 ios 或 android",
                    "INVALID_OTA_PLATFORM"
                );
            }

            var existing = await _db
                .Queryable<MobileAppOtaUpdate>()
                .FirstAsync(x =>
                    x.AppKey == appKey
                    && x.UpdateGroupId == updateGroupId
                    && x.Platform == platform
                );
            var entity = existing ?? new MobileAppOtaUpdate { Id = Guid.NewGuid() };

            // EAS update webhook 或人工登记可能重复提交同一 group；按 appKey+group+platform 幂等更新。
            ApplyOtaUpdate(entity, dto, appKey, projectName, updateGroupId, platform);

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
                        .FirstAsync(x =>
                            x.AppKey == appKey
                            && x.UpdateGroupId == updateGroupId
                            && x.Platform == platform
                        );
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
            var projectName = NormalizeOptionalText(query.ProjectName);
            var platform = NormalizeOptionalText(query.Platform)?.ToLowerInvariant();
            if (!MobileAppKeys.TryNormalizeOrLegacyMobile(query.AppKey, out var appKey)
                || projectName != null
                    && !string.Equals(
                        ResolveAppKeyForProject(projectName),
                        appKey,
                        StringComparison.Ordinal
                    ))
            {
                return ApiResponse<PagedResult<MobileAppOtaUpdateDto>>.OK(
                    new PagedResult<MobileAppOtaUpdateDto>
                    {
                        Items = [],
                        Total = 0,
                        Page = page,
                        PageSize = pageSize,
                    }
                );
            }

            // OTA 列表默认只看 production，避免未带 channel 时混入 preview 更新。
            var queryable = _db
                .Queryable<MobileAppOtaUpdate>()
                .Where(x => x.AppKey == appKey && x.Channel == channel);

            if (projectName != null)
            {
                queryable = queryable.Where(x => x.ProjectName == projectName);
            }

            if (platform is "android" or "ios")
            {
                queryable = queryable.Where(x => x.Platform == platform);
            }

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
            if (platform == null)
            {
                return Task.FromResult(
                    ApiResponse<MobileAppOtaRollbackCommandDto>.Error(
                        "Platform 必须是 ios 或 android",
                        "INVALID_OTA_PLATFORM"
                    )
                );
            }

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
            var appKey = ResolveAppKeyForProject(payload.ProjectName);
            if (appKey == null)
                return "project_not_allowed";
            if (!AcceptedProfiles().Contains(payload.BuildProfile, StringComparer.OrdinalIgnoreCase))
                return "profile_not_accepted";
            // 移动端继续接收 preview；独立手持项目只允许生产 APK 进入镜像与下载链路。
            if (
                appKey == MobileAppKeys.PosHandheld
                && !string.Equals(payload.BuildProfile, "production", StringComparison.OrdinalIgnoreCase)
            )
                return "profile_not_accepted";
            if (!string.Equals(payload.Platform, "android", StringComparison.OrdinalIgnoreCase))
                return "platform_not_android";
            if (!string.Equals(payload.Status, "finished", StringComparison.OrdinalIgnoreCase))
                return "status_not_finished";
            if (string.IsNullOrWhiteSpace(payload.ArtifactUrl))
                return "missing_artifact_url";
            if (!IsHttpsUrl(payload.ArtifactUrl))
                return "invalid_artifact_url";
            if (!IsAllowedArtifactUrl(payload.ArtifactUrl))
                return "artifact_url_not_allowed";

            return null;
        }

        private static bool MatchesConfiguredValue(string? expected, string actual)
        {
            return !string.IsNullOrWhiteSpace(expected)
                && string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        }

        private string? ResolveAppKeyForProject(string? projectName)
        {
            var normalizedProjectName = NormalizeRequiredText(projectName);
            if (normalizedProjectName.Length == 0)
            {
                return null;
            }

            foreach (var mapping in _options.ProjectAppKeys ?? [])
            {
                if (string.Equals(
                        NormalizeRequiredText(mapping.Key),
                        normalizedProjectName,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && MobileAppKeys.TryNormalize(mapping.Value, out var mappedAppKey))
                {
                    return mappedAppKey;
                }
            }

            // 旧单 project 配置继续属于 mobile，避免升级后已有移动端构建查询突然为空。
            return MatchesConfiguredValue(_options.AllowedProjectName, normalizedProjectName)
                ? MobileAppKeys.Mobile
                : null;
        }

        private string[] AcceptedProfiles()
        {
            return _options.AcceptedProfiles is not { Length: > 0 }
                ? ["preview", "production", "android-internal"]
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

        private static string? NormalizePlatform(string? platform)
        {
            var normalized = platform?.Trim();
            if (string.Equals(normalized, "android", StringComparison.OrdinalIgnoreCase))
            {
                return "android";
            }

            return string.Equals(normalized, "ios", StringComparison.OrdinalIgnoreCase)
                ? "ios"
                : null;
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
                    message.Contains("IX_MobileAppBuild_AppKey_EasBuildId", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("IX_MobileAppBuild_EasBuildId", StringComparison.OrdinalIgnoreCase)
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
                        "IX_MobileAppOtaUpdate_AppKey_Group_Platform",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || message.Contains(
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

        public async Task<MobileAppBuild?> ClaimNextCosMirrorJobAsync(
            DateTime now,
            int maxAttempts,
            TimeSpan staleRunningAfter
        )
        {
            var staleCutoff = now.Subtract(staleRunningAfter);
            var job = await _db
                .Queryable<MobileAppBuild>()
                .Where(x =>
                    x.Platform == "android"
                    && x.Status == "finished"
                    && !string.IsNullOrEmpty(x.ArtifactUrl)
                    && string.IsNullOrEmpty(x.CosArtifactUrl)
                    && (
                        x.CosMirrorStatus == null
                        || x.CosMirrorStatus == CosMirrorStatusPending
                        || (x.CosMirrorStatus == CosMirrorStatusFailed && x.CosMirrorAttempts < maxAttempts)
                        || (
                            x.CosMirrorStatus == CosMirrorStatusRunning
                            && x.CosMirrorLastAttemptAtUtc != null
                            && x.CosMirrorLastAttemptAtUtc < staleCutoff
                        )
                    )
                )
                .OrderByDescending(x => x.CompletedAt)
                .OrderByDescending(x => x.ReceivedAt)
                .FirstAsync();

            if (job == null)
            {
                return null;
            }

            var attempts = Math.Max(0, job.CosMirrorAttempts) + 1;
            var affected = await _db
                .Updateable<MobileAppBuild>()
                .SetColumns(x => new MobileAppBuild
                {
                    CosMirrorStatus = CosMirrorStatusRunning,
                    CosMirrorAttempts = attempts,
                    CosMirrorLastAttemptAtUtc = now,
                    CosMirrorError = null,
                })
                // 当前服务每轮只认领一条；这里仍按 artifact 和空 COS 地址保护，避免旧任务覆盖新产物。
                .Where(x =>
                    x.Id == job.Id
                    && x.ArtifactUrl == job.ArtifactUrl
                    && string.IsNullOrEmpty(x.CosArtifactUrl)
                    && (
                        x.CosMirrorStatus == null
                        || x.CosMirrorStatus == CosMirrorStatusPending
                        || (x.CosMirrorStatus == CosMirrorStatusFailed && x.CosMirrorAttempts < maxAttempts)
                        || (
                            x.CosMirrorStatus == CosMirrorStatusRunning
                            && x.CosMirrorLastAttemptAtUtc != null
                            && x.CosMirrorLastAttemptAtUtc < staleCutoff
                        )
                    )
                )
                .ExecuteCommandAsync();

            if (affected <= 0)
            {
                return null;
            }

            job.CosMirrorStatus = CosMirrorStatusRunning;
            job.CosMirrorAttempts = attempts;
            job.CosMirrorLastAttemptAtUtc = now;
            job.CosMirrorError = null;
            return job;
        }

        public async Task CompleteCosMirrorSuccessAsync(
            MobileAppBuild entity,
            MobileAppBuildArtifactMirrorResult mirror
        )
        {
            entity.CosArtifactUrl = NormalizeOptionalText(mirror.ArtifactUrl);
            entity.CosObjectKey = NormalizeOptionalText(mirror.ObjectKey);
            entity.ArtifactSha256 = NormalizeOptionalText(mirror.Sha256)?.ToLowerInvariant();
            entity.ArtifactSize = mirror.FileSize > 0 ? mirror.FileSize : null;
            entity.CosMirroredAt = mirror.MirroredAt;
            entity.CosMirrorStatus = CosMirrorStatusSucceeded;
            entity.CosMirrorError = null;

            await _db
                .Updateable<MobileAppBuild>()
                .SetColumns(x => new MobileAppBuild
                {
                    CosArtifactUrl = entity.CosArtifactUrl,
                    CosObjectKey = entity.CosObjectKey,
                    ArtifactSha256 = entity.ArtifactSha256,
                    ArtifactSize = entity.ArtifactSize,
                    CosMirroredAt = entity.CosMirroredAt,
                    CosMirrorStatus = CosMirrorStatusSucceeded,
                    CosMirrorError = null,
                })
                // ArtifactUrl 变化时旧镜像任务不允许回写当前行。
                .Where(x => x.Id == entity.Id && x.ArtifactUrl == entity.ArtifactUrl)
                .ExecuteCommandAsync();
        }

        public async Task CompleteCosMirrorFailureAsync(MobileAppBuild entity, Exception exception)
        {
            var status = exception is MobileAppBuildArtifactMirrorException { IsDownloadUnsafe: true }
                ? CosMirrorStatusUnsafe
                : CosMirrorStatusFailed;
            var error = TruncateForColumn(FormatCosMirrorError(exception), CosMirrorErrorMaxLength);
            entity.CosMirrorStatus = status;
            entity.CosMirrorError = error;

            await _db
                .Updateable<MobileAppBuild>()
                .SetColumns(x => new MobileAppBuild
                {
                    CosMirrorStatus = status,
                    CosMirrorError = error,
                })
                // 失败结果只写入尚未镜像成功的行，避免并发失败覆盖另一个请求的成功 COS 地址。
                .Where(
                    x =>
                        x.Id == entity.Id
                        && x.ArtifactUrl == entity.ArtifactUrl
                        && string.IsNullOrEmpty(x.CosArtifactUrl)
                )
                .ExecuteCommandAsync();
        }

        private static bool HasArtifactUrlChanged(string? previousArtifactUrl, string currentArtifactUrl)
        {
            return !string.Equals(
                NormalizeOptionalText(previousArtifactUrl),
                NormalizeOptionalText(currentArtifactUrl),
                StringComparison.Ordinal
            );
        }

        private static void QueueCosMirrorIfNeeded(
            MobileAppBuild entity,
            bool isNewBuild,
            bool artifactChanged
        )
        {
            if (isNewBuild || artifactChanged)
            {
                ResetCosMirrorQueue(entity);
                return;
            }

            // 旧数据可能没有状态字段；只在空状态时补成 pending，不覆盖 failed/unsafe 的审计结果。
            if (
                string.IsNullOrWhiteSpace(entity.CosArtifactUrl)
                && string.IsNullOrWhiteSpace(entity.CosMirrorStatus)
            )
            {
                entity.CosMirrorStatus = CosMirrorStatusPending;
            }
        }

        private static void ResetCosMirrorQueue(MobileAppBuild entity)
        {
            // 原始 EAS artifact 变化时，旧 COS 地址不能继续代表当前构建产物，并重新进入后台镜像队列。
            entity.CosArtifactUrl = null;
            entity.CosObjectKey = null;
            entity.ArtifactSha256 = null;
            entity.ArtifactSize = null;
            entity.CosMirroredAt = null;
            entity.CosMirrorError = null;
            entity.CosMirrorStatus = CosMirrorStatusPending;
            entity.CosMirrorAttempts = 0;
            entity.CosMirrorLastAttemptAtUtc = null;
        }

        private static void ApplyPayload(
            MobileAppBuild entity,
            EasBuildPayload payload,
            string appKey,
            DateTime now
        )
        {
            entity.AppKey = appKey;
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
                AppKey = entity.AppKey,
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
                ArtifactUrl = ResolveDownloadUrl(entity),
                OriginalArtifactUrl = entity.ArtifactUrl,
                CosArtifactUrl = entity.CosArtifactUrl,
                CosObjectKey = entity.CosObjectKey,
                ArtifactSha256 = entity.ArtifactSha256,
                ArtifactSize = entity.ArtifactSize,
                CosMirroredAt = entity.CosMirroredAt,
                CosMirrorError = entity.CosMirrorError,
                CosMirrorStatus = string.IsNullOrWhiteSpace(entity.CosMirrorStatus)
                    ? CosMirrorStatusPending
                    : entity.CosMirrorStatus,
                CosMirrorAttempts = entity.CosMirrorAttempts,
                CosMirrorLastAttemptAtUtc = entity.CosMirrorLastAttemptAtUtc,
                BuildDetailsPageUrl = entity.BuildDetailsPageUrl,
                GitCommitHash = entity.GitCommitHash,
                GitCommitMessage = entity.GitCommitMessage,
                CreatedAt = entity.CreatedAt,
                CompletedAt = entity.CompletedAt,
                ExpirationDate = entity.ExpirationDate,
                ReceivedAt = entity.ReceivedAt,
            };
        }

        private static string ResolveDownloadUrl(MobileAppBuild entity)
        {
            return NormalizeOptionalText(entity.CosArtifactUrl) ?? entity.ArtifactUrl;
        }

        private static string FormatCosMirrorError(Exception ex)
        {
            var prefix = ex is MobileAppBuildArtifactMirrorException { IsDownloadUnsafe: true }
                ? UnsafeArtifactMirrorErrorPrefix
                : $"{ex.GetType().Name}:";
            return $"{prefix} {ex.Message}";
        }

        private static string TruncateForColumn(string value, int maxLength)
        {
            return value.Length <= maxLength ? value : value[..maxLength];
        }

        private static bool IsAllowedArtifactUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && TencentCosMobileAppBuildArtifactMirror.IsAllowedArtifactHost(uri.Host);
        }

        private static void ApplyOtaUpdate(
            MobileAppOtaUpdate entity,
            MobileAppOtaUpdateUpsertDto dto,
            string appKey,
            string projectName,
            string updateGroupId,
            string platform
        )
        {
            entity.AppKey = appKey;
            entity.ProjectName = projectName;
            entity.UpdateGroupId = updateGroupId;
            entity.UpdateId = NormalizeOptionalText(dto.UpdateId)
                ?? NormalizeOptionalText(dto.AndroidUpdateId);
            entity.AndroidUpdateId = platform == "android"
                ? NormalizeOptionalText(dto.AndroidUpdateId) ?? entity.UpdateId
                : null;
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
                AppKey = entity.AppKey,
                ProjectName = entity.ProjectName,
                UpdateGroupId = entity.UpdateGroupId,
                UpdateId = entity.UpdateId ?? entity.AndroidUpdateId,
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
