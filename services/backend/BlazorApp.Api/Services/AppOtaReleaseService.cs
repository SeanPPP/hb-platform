using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services;

public sealed partial class AppOtaReleaseService(
    ISqlSugarClient db,
    IOptions<EasWebhookOptions> easOptions,
    ILogger<AppOtaReleaseService> logger
) : IAppOtaReleaseService
{
    private const int MaxRows = 500;

    public async Task<ApiResponse<List<AppOtaReleaseDto>>> ListAsync(
        AppOtaReleaseQuery query
    )
    {
        if (!TryNormalizeLane(
                query.AppKey,
                query.Environment,
                query.Platform,
                out var appKey,
                out var environment,
                out var platform
            ))
        {
            return ApiResponse<List<AppOtaReleaseDto>>.Error(
                "发布事实查询 lane 无效",
                AppOtaReleaseErrorCodes.IdentityInvalid
            );
        }

        var rows = await db.Queryable<AppOtaRelease>()
            .Where(item =>
                !item.IsDeleted
                && item.AppKey == appKey
                && item.Environment == environment
                && item.Platform == platform
            )
            .OrderByDescending(item => item.PublishedAtUtc)
            .OrderByDescending(item => item.CreatedAt)
            .Take(MaxRows)
            .ToListAsync();
        return ApiResponse<List<AppOtaReleaseDto>>.OK(rows.Select(Map).ToList());
    }

    public async Task<ApiResponse<AppOtaReleasePreflightDto>> PreflightAsync(
        AppOtaReleasePreflightRequest request
    )
    {
        var validation = ValidateIdentity(request, requirePublicationFacts: false);
        if (!validation.Success)
        {
            return ApiResponse<AppOtaReleasePreflightDto>.Error(
                validation.Message,
                AppOtaReleaseErrorCodes.IdentityInvalid
            );
        }

        if (request.RollbackOfReleaseId.HasValue)
        {
            var rollbackSourceExists = await db.Queryable<AppOtaRelease>().AnyAsync(item =>
                item.Id == request.RollbackOfReleaseId.Value
                && !item.IsDeleted
                && item.AppKey == validation.AppKey
                && item.Environment == validation.Environment
                && item.Platform == validation.Platform
            );
            if (!rollbackSourceExists)
            {
                return ApiResponse<AppOtaReleasePreflightDto>.Error(
                    "rollback 来源不存在或不属于同一发布 lane",
                    AppOtaReleaseErrorCodes.IdentityInvalid
                );
            }
        }

        if (validation.LegacyBootstrap)
        {
            if (!easOptions.Value.AllowLegacyOtaBootstrapRegistration)
            {
                return ApiResponse<AppOtaReleasePreflightDto>.Error(
                    "legacy bootstrap 登记窗口未开启，请使用不可变发布事实接口",
                    AppOtaReleaseErrorCodes.LegacyEndpointMigrated
                );
            }

            return ApiResponse<AppOtaReleasePreflightDto>.OK(
                new AppOtaReleasePreflightDto { Valid = true }
            );
        }

        if (
            validation.AppKey == MobileAppKeys.PosHandheld
            && !await IsPosHandheldReleasePublishingReadyAsync(
                validation.Platform,
                validation.ProjectName
            )
        )
        {
            return ApiResponse<AppOtaReleasePreflightDto>.Error(
                "手持 POS release-channel 发布尚未完成迁移放行",
                AppOtaReleaseErrorCodes.PosHandheldMigrationNotReady
            );
        }

        var exists = await db.Queryable<AppOtaRelease>().AnyAsync(item =>
            !item.IsDeleted
            && item.AppKey == validation.AppKey
            && item.Platform == validation.Platform
            && item.ReleaseChannel == validation.ReleaseChannel
        );
        return exists
            ? ApiResponse<AppOtaReleasePreflightDto>.Error(
                "release channel 已登记且永久禁止复用",
                AppOtaReleaseErrorCodes.FactConflict
            )
            : ApiResponse<AppOtaReleasePreflightDto>.OK(
                new AppOtaReleasePreflightDto { Valid = true }
            );
    }

    public async Task<ApiResponse<AppOtaReleaseRegistrationResultDto>> RegisterAsync(
        AppOtaReleaseRegisterRequest request,
        string currentUser
    )
    {
        var validation = ValidateIdentity(
            new AppOtaReleasePreflightRequest
            {
                ReleaseBatchId = request.ReleaseBatchId,
                AppKey = request.AppKey,
                Environment = request.Environment,
                ClientChannel = request.ClientChannel,
                ReleaseChannel = request.ReleaseChannel,
                EasBranch = request.EasBranch,
                ProjectName = request.ProjectName,
                EasProjectId = request.EasProjectId,
                Platform = request.Platform,
                RuntimeVersion = request.RuntimeVersion,
            },
            requirePublicationFacts: true
        );
        if (!validation.Success)
        {
            return ApiResponse<AppOtaReleaseRegistrationResultDto>.Error(
                validation.Message,
                AppOtaReleaseErrorCodes.IdentityInvalid
            );
        }

        if (
            request.ReleaseBatchId == Guid.Empty
            || !Guid.TryParse(Normalize(request.UpdateGroupId), out var updateGroupId)
            || !Guid.TryParse(Normalize(request.UpdateId), out var updateId)
            || request.PublishedAtUtc == default
            || NormalizeOptional(request.Message)?.Length > 1000
            || NormalizeOptional(request.GitCommitHash)?.Length > 120
            || !IsOptionalHttpsUrl(request.DashboardUrl)
            || request.IsRollback != request.RollbackOfReleaseId.HasValue
        )
        {
            return ApiResponse<AppOtaReleaseRegistrationResultDto>.Error(
                "OTA 发布事实字段不完整或格式无效",
                AppOtaReleaseErrorCodes.IdentityInvalid
            );
        }

        if (request.IsRollback)
        {
            var rollbackSource = await db.Queryable<AppOtaRelease>().FirstAsync(item =>
                item.Id == request.RollbackOfReleaseId
                && !item.IsDeleted
                && item.AppKey == validation.AppKey
                && item.Environment == validation.Environment
                && item.Platform == validation.Platform
            );
            if (rollbackSource is null)
            {
                return ApiResponse<AppOtaReleaseRegistrationResultDto>.Error(
                    "rollback 来源不存在或不属于同一发布 lane",
                    AppOtaReleaseErrorCodes.IdentityInvalid
                );
            }
        }

        var entity = new AppOtaRelease
        {
            Id = Guid.NewGuid(),
            ReleaseBatchId = request.ReleaseBatchId,
            AppKey = validation.AppKey,
            Environment = validation.Environment,
            ClientChannel = validation.ClientChannel,
            ReleaseChannel = validation.ReleaseChannel,
            EasBranch = Normalize(request.EasBranch),
            ProjectName = validation.ProjectName,
            Platform = validation.Platform,
            RuntimeVersion = validation.RuntimeVersion,
            UpdateGroupId = updateGroupId.ToString("D"),
            UpdateId = updateId.ToString("D"),
            Message = NormalizeOptional(request.Message),
            GitCommitHash = NormalizeOptional(request.GitCommitHash),
            DashboardUrl = NormalizeOptional(request.DashboardUrl),
            PublishedAtUtc = NormalizeUtcTimestamp(request.PublishedAtUtc),
            IsRollback = request.IsRollback,
            RollbackOfReleaseId = request.RollbackOfReleaseId,
            Legacy = false,
            RegistrationSource = "app-ota-release-api",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Normalize(currentUser),
            UpdatedAt = null,
            UpdatedBy = null,
            IsDeleted = false,
        };
        entity.FactFingerprint = ComputeFingerprint(entity);

        var existing = await FindIdentityConflictAsync(entity);
        if (existing is not null)
        {
            return CompareExisting(existing, entity);
        }

        if (
            validation.AppKey == MobileAppKeys.PosHandheld
            && !await IsPosHandheldReleasePublishingReadyAsync(
                validation.Platform,
                validation.ProjectName
            )
        )
        {
            return ApiResponse<AppOtaReleaseRegistrationResultDto>.Error(
                "手持 POS release-channel 发布尚未完成迁移放行",
                AppOtaReleaseErrorCodes.PosHandheldMigrationNotReady
            );
        }

        try
        {
            await db.Insertable(entity).ExecuteCommandAsync();
            return RegistrationSuccess(entity, idempotent: false);
        }
        catch (Exception ex) when (AppUpdatePolicyMutationLock.IsUniqueConflict(ex))
        {
            // 并发登记只允许在最终不可变快照完全一致时收敛为幂等成功。
            existing = await FindIdentityConflictAsync(entity);
            if (existing is not null)
            {
                return CompareExisting(existing, entity);
            }

            logger.LogWarning(ex, "OTA release unique conflict could not be resolved");
            return FactConflict();
        }
    }

    private ValidationResult ValidateIdentity(
        AppOtaReleasePreflightRequest request,
        bool requirePublicationFacts
    )
    {
        if (!TryNormalizeLane(
                request.AppKey,
                request.Environment,
                request.Platform,
                out var appKey,
                out var environment,
                out var platform
            ))
        {
            return ValidationResult.Error("appKey、environment 或 platform 无效");
        }

        var projectName = Normalize(request.ProjectName);
        var clientChannel = Normalize(request.ClientChannel).ToLowerInvariant();
        var releaseChannel = Normalize(request.ReleaseChannel).ToLowerInvariant();
        var runtimeVersion = Normalize(request.RuntimeVersion);
        var easBranch = Normalize(request.EasBranch);
        var easProjectId = NormalizeOptional(request.EasProjectId);
        var expectedClientChannel = appKey == MobileAppKeys.Mobile
            ? environment
            : "pos-handheld-production";
        var expectedPrefix = appKey == MobileAppKeys.Mobile
            ? $"mobile-{environment}-{platform}-release-"
            : $"pos-handheld-production-{platform}-release-";
        var isLegacyBootstrapPreflight = !requirePublicationFacts
            && request.BootstrapLegacyFixedChannel
            && (
                appKey == MobileAppKeys.Mobile
                    && clientChannel == environment
                    && releaseChannel == environment
                || appKey == MobileAppKeys.PosHandheld
                    && environment == "production"
                    && clientChannel == "pos-handheld-production"
                    && releaseChannel == "pos-handheld-production"
            )
            && (easBranch.Length == 0 || easBranch == releaseChannel);

        if (
            projectName.Length is 0 or > 120
            || runtimeVersion.Length is 0 or > 120
            || !string.Equals(
                clientChannel,
                expectedClientChannel,
                StringComparison.Ordinal
            )
            || !isLegacyBootstrapPreflight
                && (
                    !releaseChannel.StartsWith(expectedPrefix, StringComparison.Ordinal)
                    || releaseChannel.Length <= expectedPrefix.Length
                )
            || releaseChannel.Length > 160
            || !ReleaseChannelPattern().IsMatch(releaseChannel)
            || !isLegacyBootstrapPreflight
                && !string.Equals(easBranch, releaseChannel, StringComparison.Ordinal)
            || request.BootstrapLegacyFixedChannel && !isLegacyBootstrapPreflight
            || easProjectId is not null && !Guid.TryParse(easProjectId, out _)
        )
        {
            return ValidationResult.Error("客户端 channel 或 release channel 身份无效");
        }

        var projectMatches = easOptions.Value.ProjectAppKeys.Any(mapping =>
            string.Equals(
                Normalize(mapping.Key),
                projectName,
                StringComparison.OrdinalIgnoreCase
            )
            && MobileAppKeys.TryNormalize(mapping.Value, out var mappedAppKey)
            && mappedAppKey == appKey
        );
        if (!projectMatches)
        {
            return ValidationResult.Error("EAS project 与 appKey 不匹配");
        }

        var configuredProjectId = easOptions.Value.ProjectIds
            .FirstOrDefault(mapping =>
                string.Equals(
                    Normalize(mapping.Key),
                    projectName,
                    StringComparison.OrdinalIgnoreCase
                )
            ).Value;
        if (!string.IsNullOrWhiteSpace(configuredProjectId))
        {
            if (
                !Guid.TryParse(Normalize(configuredProjectId), out var expectedProjectId)
                || !Guid.TryParse(easProjectId, out var actualProjectId)
                || expectedProjectId != actualProjectId
            )
            {
                return ValidationResult.Error("EAS projectId 与受控 project 映射不匹配");
            }
        }
        // 兼容尚未配置 project UUID 的环境：此时现有 ProjectAppKeys 的
        // projectName -> appKey 映射是权威身份，easProjectId 只做 UUID 形状校验。

        return ValidationResult.Ok(
            appKey,
            environment,
            platform,
            clientChannel,
            releaseChannel,
            projectName,
            runtimeVersion,
            isLegacyBootstrapPreflight
        );
    }

    private async Task<AppOtaRelease?> FindIdentityConflictAsync(AppOtaRelease entity)
    {
        var rows = await db.Queryable<AppOtaRelease>()
            .Where(item =>
                !item.IsDeleted
                && item.AppKey == entity.AppKey
                && item.Platform == entity.Platform
                && (
                    item.ReleaseChannel == entity.ReleaseChannel
                    || item.Environment == entity.Environment
                        && (
                            item.UpdateId == entity.UpdateId
                            || item.UpdateGroupId == entity.UpdateGroupId
                        )
                )
            )
            .Take(3)
            .ToListAsync();
        if (rows.Count == 0)
        {
            return null;
        }

        var exact = rows.FirstOrDefault(item =>
            string.Equals(
                item.FactFingerprint,
                entity.FactFingerprint,
                StringComparison.Ordinal
            )
        );
        return exact ?? rows[0];
    }

    private async Task<bool> IsPosHandheldLaneMigrationReadyAsync(
        string platform,
        string projectName
    )
    {
        var lane = platform == "ios"
            ? PosHandheldUpdateLanes.IosOta
            : PosHandheldUpdateLanes.AndroidOta;
        var policy = await db.Queryable<PosHandheldUpdatePolicy>()
            .FirstAsync(item => item.Lane == lane && !item.IsDeleted);
        if (policy is null || !policy.Enabled)
        {
            // 无 active target 时由服务端发布开关单独证明迁移窗口已经放行。
            return true;
        }

        if (!policy.CandidateId.HasValue)
        {
            return false;
        }

        var release = await db.Queryable<AppOtaRelease>().FirstAsync(item =>
            item.Id == policy.CandidateId.Value
            && !item.IsDeleted
            && item.AppKey == MobileAppKeys.PosHandheld
            && item.Environment == "production"
            && item.ClientChannel == "pos-handheld-production"
            && item.ProjectName == projectName
            && item.Platform == platform
        );
        if (
            release is null
            || !string.Equals(
                release.FactFingerprint,
                ComputeFingerprint(release),
                StringComparison.Ordinal
            )
        )
        {
            return false;
        }

        var candidate = PosHandheldUpdatePolicyService.MapOtaCandidate(
            release,
            lane,
            isCurrentHead: true
        );
        if (
            candidate is null
            || !string.Equals(
                policy.CandidateFingerprint,
                PosHandheldUpdatePolicyService.ComputeCandidateFingerprint(candidate),
                StringComparison.Ordinal
            )
        )
        {
            return false;
        }

        if (!release.Legacy)
        {
            var expectedPrefix =
                $"pos-handheld-production-{platform}-release-";
            return release.ReleaseChannel.StartsWith(
                    expectedPrefix,
                    StringComparison.Ordinal
                )
                && release.ReleaseChannel.Length > expectedPrefix.Length
                && string.Equals(
                    release.EasBranch,
                    release.ReleaseChannel,
                    StringComparison.Ordinal
                );
        }

        if (release.ReleaseChannel != "pos-handheld-production")
        {
            return false;
        }

        var head = await db.Queryable<AppOtaRelease>()
            .Where(item =>
                !item.IsDeleted
                && item.Legacy
                && item.AppKey == MobileAppKeys.PosHandheld
                && item.Environment == "production"
                && item.ProjectName == projectName
                && item.Platform == platform
                && item.ReleaseChannel == "pos-handheld-production"
                && item.RuntimeVersion == release.RuntimeVersion
            )
            .OrderByDescending(item => item.PublishedAtUtc)
            .OrderByDescending(item => item.CreatedAt)
            .FirstAsync();
        return head?.Id == release.Id;
    }

    private async Task<bool> IsPosHandheldReleasePublishingReadyAsync(
        string platform,
        string projectName
    ) =>
        easOptions.Value.PosHandheldReleaseChannelPublishingEnabled
        && await IsPosHandheldLaneMigrationReadyAsync(platform, projectName);

    private static ApiResponse<AppOtaReleaseRegistrationResultDto> CompareExisting(
        AppOtaRelease existing,
        AppOtaRelease proposed
    ) =>
        string.Equals(
            existing.FactFingerprint,
            proposed.FactFingerprint,
            StringComparison.Ordinal
        )
            ? RegistrationSuccess(existing, idempotent: true)
            : FactConflict();

    private static ApiResponse<AppOtaReleaseRegistrationResultDto> RegistrationSuccess(
        AppOtaRelease entity,
        bool idempotent
    ) =>
        ApiResponse<AppOtaReleaseRegistrationResultDto>.OK(
            new AppOtaReleaseRegistrationResultDto
            {
                Release = Map(entity),
                Idempotent = idempotent,
            },
            idempotent ? "发布事实已存在，幂等返回" : "发布事实登记成功"
        );

    private static ApiResponse<AppOtaReleaseRegistrationResultDto> FactConflict() =>
        ApiResponse<AppOtaReleaseRegistrationResultDto>.Error(
            "相同发布身份已存在，但不可变字段不一致",
            AppOtaReleaseErrorCodes.FactConflict
        );

    internal static string ComputeFingerprint(AppOtaRelease item)
    {
        var canonical = string.Join(
            '\u001f',
            item.ReleaseBatchId.ToString("D"),
            Normalize(item.AppKey).ToLowerInvariant(),
            Normalize(item.Environment).ToLowerInvariant(),
            Normalize(item.ClientChannel).ToLowerInvariant(),
            Normalize(item.ReleaseChannel).ToLowerInvariant(),
            Normalize(item.EasBranch),
            Normalize(item.ProjectName),
            Normalize(item.Platform).ToLowerInvariant(),
            Normalize(item.RuntimeVersion),
            Normalize(item.UpdateGroupId).ToLowerInvariant(),
            Normalize(item.UpdateId).ToLowerInvariant(),
            NormalizeOptional(item.Message) ?? string.Empty,
            NormalizeOptional(item.GitCommitHash) ?? string.Empty,
            NormalizeOptional(item.DashboardUrl) ?? string.Empty,
            NormalizeUtcTimestamp(item.PublishedAtUtc).ToString("O"),
            item.IsRollback ? "1" : "0",
            item.RollbackOfReleaseId?.ToString("D") ?? string.Empty,
            item.Legacy ? "1" : "0"
        );
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    internal static AppOtaReleaseDto Map(AppOtaRelease item) =>
        new()
        {
            Id = item.Id,
            ReleaseBatchId = item.ReleaseBatchId,
            AppKey = item.AppKey,
            Environment = item.Environment,
            ClientChannel = item.ClientChannel,
            ReleaseChannel = item.ReleaseChannel,
            EasBranch = item.EasBranch,
            ProjectName = item.ProjectName,
            Platform = item.Platform,
            RuntimeVersion = item.RuntimeVersion,
            UpdateGroupId = item.UpdateGroupId,
            UpdateId = item.UpdateId,
            Message = item.Message,
            GitCommitHash = item.GitCommitHash,
            DashboardUrl = item.DashboardUrl,
            PublishedAtUtc = item.PublishedAtUtc,
            IsRollback = item.IsRollback,
            RollbackOfReleaseId = item.RollbackOfReleaseId,
            FactFingerprint = item.FactFingerprint,
            Legacy = item.Legacy,
            RegistrationSource = item.RegistrationSource,
            CreatedAt = item.CreatedAt,
            CreatedBy = item.CreatedBy,
        };

    internal static bool TryNormalizeLane(
        string? rawAppKey,
        string? rawEnvironment,
        string? rawPlatform,
        out string appKey,
        out string environment,
        out string platform
    )
    {
        appKey = string.Empty;
        environment = Normalize(rawEnvironment).ToLowerInvariant();
        platform = Normalize(rawPlatform).ToLowerInvariant();
        if (
            !MobileAppKeys.TryNormalize(rawAppKey, out appKey)
            || appKey is not (MobileAppKeys.Mobile or MobileAppKeys.PosHandheld)
            || platform is not ("android" or "ios")
            || environment is not ("production" or "preview")
            || appKey == MobileAppKeys.PosHandheld && environment != "production"
        )
        {
            appKey = string.Empty;
            environment = string.Empty;
            platform = string.Empty;
            return false;
        }

        return true;
    }

    private static bool IsOptionalHttpsUrl(string? value)
    {
        var normalized = NormalizeOptional(value);
        return normalized is null
            || normalized.Length <= 2048
                && Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps;
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static string? NormalizeOptional(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }

    internal static DateTime NormalizeUtcTimestamp(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            // SQL datetime/datetime2 回读通常丢失 Kind，但 PublishedAtUtc 的存储语义仍是 UTC。
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseChannelPattern();

    private sealed record ValidationResult(
        bool Success,
        string Message,
        string AppKey,
        string Environment,
        string Platform,
        string ClientChannel,
        string ReleaseChannel,
        string ProjectName,
        string RuntimeVersion,
        bool LegacyBootstrap
    )
    {
        internal static ValidationResult Error(string message) =>
            new(false, message, "", "", "", "", "", "", "", false);

        internal static ValidationResult Ok(
            string appKey,
            string environment,
            string platform,
            string clientChannel,
            string releaseChannel,
            string projectName,
            string runtimeVersion,
            bool legacyBootstrap
        ) =>
            new(
                true,
                string.Empty,
                appKey,
                environment,
                platform,
                clientChannel,
                releaseChannel,
                projectName,
                runtimeVersion,
                legacyBootstrap
            );
    }
}
