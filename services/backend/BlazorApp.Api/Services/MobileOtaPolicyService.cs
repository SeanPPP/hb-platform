using System.Globalization;
using System.Text.Json;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using SqlSugar;

namespace BlazorApp.Api.Services;

public sealed class MobileOtaPolicyService(
    ISqlSugarClient db,
    ILogger<MobileOtaPolicyService> logger
) : IMobileOtaPolicyService
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<ApiResponse<MobileOtaPolicyDto>> GetAsync(
        string environment,
        string platform
    )
    {
        if (!TryNormalizeLane(environment, platform, out environment, out platform))
        {
            return Error(
                "Mobile OTA 策略 lane 无效",
                MobileOtaPolicyErrorCodes.LaneInvalid
            );
        }

        var policy = await FindPolicyAsync(environment, platform);
        if (policy is null)
        {
            return ApiResponse<MobileOtaPolicyDto>.OK(
                EmptyPolicy(environment, platform)
            );
        }

        var release = policy.TargetReleaseId.HasValue
            ? await FindReleaseAsync(
                environment,
                platform,
                policy.TargetReleaseId.Value
            )
            : null;
        return ApiResponse<MobileOtaPolicyDto>.OK(MapPolicy(policy, release));
    }

    public async Task<ApiResponse<MobileOtaPolicyDto>> SetAsync(
        string environment,
        string platform,
        MobileOtaPolicyRequest request,
        string currentUser
    )
    {
        if (!TryNormalizeLane(environment, platform, out environment, out platform))
        {
            return Error(
                "Mobile OTA 策略 lane 无效",
                MobileOtaPolicyErrorCodes.LaneInvalid
            );
        }

        if (!request.ExpectedPolicyVersion.HasValue)
        {
            var current = await FindPolicyAsync(environment, platform);
            return VersionError(
                AppUpdatePolicyErrorCodes.VersionRequired,
                request.ExpectedPolicyVersion,
                current?.PolicyVersion ?? 0
            );
        }

        var normalizedMessage = NormalizeOptional(request.ReleaseMessage);
        if (normalizedMessage?.Length > 1000)
        {
            return Error(
                "投放说明不能超过 1000 个字符",
                MobileOtaPolicyErrorCodes.ReleaseMessageInvalid
            );
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            MobileOtaPolicyDto? saved = null;
            ApiResponse<MobileOtaPolicyDto>? mutationError = null;
            var transaction = await db.Ado.UseTranAsync(async () =>
            {
                await AppUpdatePolicyMutationLock.AcquireAsync(
                    db,
                    $"app-update-policy:mobile-ota:{environment}:{platform}"
                );
                var existing = await FindPolicyAsync(environment, platform);
                var actualVersion = existing?.PolicyVersion ?? 0;
                if (request.ExpectedPolicyVersion.Value != actualVersion)
                {
                    mutationError = VersionError(
                        AppUpdatePolicyErrorCodes.VersionConflict,
                        request.ExpectedPolicyVersion,
                        actualVersion
                    );
                    return;
                }

                AppOtaRelease? release = null;
                if (request.Enabled)
                {
                    if (!request.TargetReleaseId.HasValue)
                    {
                        mutationError = Error(
                            "启用策略时必须选择目标发布",
                            MobileOtaPolicyErrorCodes.TargetRequired
                        );
                        return;
                    }

                    release = await FindReleaseAsync(
                        environment,
                        platform,
                        request.TargetReleaseId.Value
                    );
                    if (release is null || release.Legacy)
                    {
                        mutationError = Error(
                            "目标发布不存在、身份不匹配或属于只读 legacy 历史",
                            MobileOtaPolicyErrorCodes.TargetInvalid
                        );
                        return;
                    }
                }

                var enabled = request.Enabled;
                var required = enabled && request.Required;
                var targetReleaseId = enabled ? request.TargetReleaseId : null;
                var targetRuntimeVersion = enabled ? release!.RuntimeVersion : null;
                var releaseMessage = enabled ? normalizedMessage : null;
                if (
                    existing is not null
                    && existing.Enabled == enabled
                    && existing.Required == required
                    && existing.TargetReleaseId == targetReleaseId
                    && string.Equals(
                        existing.TargetRuntimeVersion,
                        targetRuntimeVersion,
                        StringComparison.Ordinal
                    )
                    && string.Equals(
                        existing.ReleaseMessage,
                        releaseMessage,
                        StringComparison.Ordinal
                    )
                )
                {
                    saved = MapPolicy(existing, release);
                    return;
                }

                var now = DateTime.UtcNow;
                var user = NormalizeOptional(currentUser) ?? "System";
                var entity = existing ?? new MobileOtaPolicy
                {
                    Id = Guid.NewGuid(),
                    Environment = environment,
                    Platform = platform,
                    CreatedAt = now,
                    CreatedBy = user,
                    IsDeleted = false,
                };
                entity.Enabled = enabled;
                entity.Required = required;
                entity.TargetReleaseId = targetReleaseId;
                entity.TargetRuntimeVersion = targetRuntimeVersion;
                entity.ReleaseMessage = releaseMessage;
                entity.PolicyVersion = actualVersion + 1;
                entity.UpdatedAt = now;
                entity.UpdatedBy = user;

                if (existing is null)
                {
                    await db.Insertable(entity).ExecuteCommandAsync();
                }
                else
                {
                    await db.Updateable(entity).ExecuteCommandAsync();
                }

                saved = MapPolicy(entity, release);
                await db.Insertable(
                    new MobileOtaPolicyRevision
                    {
                        Id = Guid.NewGuid(),
                        PolicyId = entity.Id,
                        Environment = environment,
                        Platform = platform,
                        PolicyVersion = entity.PolicyVersion,
                        Operation = enabled ? "save" : "disable",
                        SnapshotJson = JsonSerializer.Serialize(
                            saved,
                            SnapshotJsonOptions
                        ),
                        CreatedAt = now,
                        CreatedBy = user,
                        UpdatedAt = null,
                        UpdatedBy = null,
                        IsDeleted = false,
                    }
                ).ExecuteCommandAsync();
            });

            if (mutationError is not null)
            {
                return mutationError;
            }

            if (transaction.IsSuccess && saved is not null)
            {
                return ApiResponse<MobileOtaPolicyDto>.OK(saved);
            }

            if (
                attempt == 0
                && AppUpdatePolicyMutationLock.IsUniqueConflict(
                    transaction.ErrorException
                )
            )
            {
                logger.LogInformation(
                    transaction.ErrorException,
                    "Mobile OTA policy concurrent insert retry environment={Environment} platform={Platform}",
                    environment,
                    platform
                );
                continue;
            }

            logger.LogError(
                transaction.ErrorException,
                "Mobile OTA policy save failed environment={Environment} platform={Platform}",
                environment,
                platform
            );
            return Error(
                "Mobile OTA 策略保存失败",
                "MOBILE_OTA_POLICY_SAVE_FAILED"
            );
        }

        throw new InvalidOperationException("Mobile OTA 策略重试状态无效");
    }

    public async Task<ApiResponse<List<MobileOtaPolicyRevisionDto>>> GetRevisionsAsync(
        string environment,
        string platform
    )
    {
        if (!TryNormalizeLane(environment, platform, out environment, out platform))
        {
            return ApiResponse<List<MobileOtaPolicyRevisionDto>>.Error(
                "Mobile OTA 策略 lane 无效",
                MobileOtaPolicyErrorCodes.LaneInvalid
            );
        }

        var rows = await db.Queryable<MobileOtaPolicyRevision>()
            .Where(item =>
                !item.IsDeleted
                && item.Environment == environment
                && item.Platform == platform
            )
            .OrderByDescending(item => item.PolicyVersion)
            .Take(200)
            .ToListAsync();
        return ApiResponse<List<MobileOtaPolicyRevisionDto>>.OK(
            rows.Select(item => new MobileOtaPolicyRevisionDto
            {
                Id = item.Id,
                Environment = item.Environment,
                Platform = item.Platform,
                PolicyVersion = item.PolicyVersion,
                Operation = item.Operation,
                SnapshotJson = item.SnapshotJson,
                CreatedAt = item.CreatedAt,
                CreatedBy = item.CreatedBy,
            }).ToList()
        );
    }

    public async Task<MobileOtaDecisionDto?> GetDecisionAsync(
        MobileOtaDecisionRequest request
    )
    {
        if (
            !TryNormalizeDecisionLane(
                request.ClientChannel,
                request.Platform,
                out var environment,
                out var platform,
                out var platformDisplay
            )
        )
        {
            return null;
        }

        var runtimeVersion = Normalize(request.RuntimeVersion);
        if (runtimeVersion.Length == 0)
        {
            return null;
        }

        var policy = await FindPolicyAsync(environment, platform);
        if (policy is null)
        {
            return NoneDecision(
                AppUpdateStates.None,
                platformDisplay,
                environment,
                runtimeVersion
            );
        }

        var policyVersion = policy.PolicyVersion.ToString(
            CultureInfo.InvariantCulture
        );
        if (!policy.Enabled)
        {
            return NoneDecision(
                policyVersion,
                platformDisplay,
                environment,
                runtimeVersion
            );
        }

        var release = policy.TargetReleaseId.HasValue
            ? await FindReleaseAsync(
                environment,
                platform,
                policy.TargetReleaseId.Value
            )
            : null;
        var invalidIdentityReason = GetInvalidTargetIdentityReason(
            release,
            environment,
            platform,
            policy.TargetRuntimeVersion
        );
        if (invalidIdentityReason is not null)
        {
            logger.LogWarning(
                "Mobile OTA target identity invalid environment={Environment} platform={Platform} policyVersion={PolicyVersion} required={Required} reason={InvalidReason}",
                environment,
                platform,
                policy.PolicyVersion,
                policy.Required,
                invalidIdentityReason
            );
            return policy.Required
                ? null
                : NoneDecision(
                    policyVersion,
                    platformDisplay,
                    environment,
                    runtimeVersion
                );
        }

        // Runtime 不兼容代表该策略不覆盖当前构建，不应将客户端误判为策略损坏。
        if (!string.Equals(runtimeVersion, release!.RuntimeVersion, StringComparison.Ordinal))
        {
            return NoneDecision(
                policyVersion,
                platformDisplay,
                environment,
                runtimeVersion
            );
        }

        var currentUpdateId = Normalize(request.CurrentUpdateId);
        var currentUpdateGroupId = Normalize(request.CurrentUpdateGroupId);
        var updateIdMatches = currentUpdateId.Length > 0
            && string.Equals(
                currentUpdateId,
                release.UpdateId,
                StringComparison.OrdinalIgnoreCase
            );
        // 只有 Update ID 能证明客户端已经运行目标；Group ID 只能作为附加一致性校验。
        var updateGroupMatches = currentUpdateGroupId.Length == 0
            || string.Equals(
                currentUpdateGroupId,
                release.UpdateGroupId,
                StringComparison.OrdinalIgnoreCase
            );
        var alreadyCurrent = updateIdMatches && updateGroupMatches;
        if (alreadyCurrent)
        {
            return NoneDecision(
                policyVersion,
                platformDisplay,
                environment,
                runtimeVersion
            );
        }

        return new MobileOtaDecisionDto
        {
            State = policy.Required
                ? AppUpdateStates.Required
                : AppUpdateStates.Optional,
            PolicyVersion = policyVersion,
            AppKey = MobileAppKeys.Mobile,
            Platform = platformDisplay,
            Required = policy.Required,
            ClientChannel = environment,
            ReleaseChannel = release.ReleaseChannel,
            RuntimeVersion = release.RuntimeVersion,
            UpdateId = release.UpdateId,
            UpdateGroupId = release.UpdateGroupId,
            ReleaseMessage = policy.ReleaseMessage,
        };
    }

    internal static bool TryNormalizeDecisionLane(
        string? rawClientChannel,
        string? rawPlatform,
        out string environment,
        out string platform,
        out string platformDisplay
    )
    {
        environment = Normalize(rawClientChannel).ToLowerInvariant();
        platform = Normalize(rawPlatform).ToLowerInvariant();
        platformDisplay = platform == "ios" ? "iOS" : "Android";
        return environment is "production" or "preview"
            && platform is "android" or "ios";
    }

    private static bool TryNormalizeLane(
        string? rawEnvironment,
        string? rawPlatform,
        out string environment,
        out string platform
    )
    {
        environment = Normalize(rawEnvironment).ToLowerInvariant();
        platform = Normalize(rawPlatform).ToLowerInvariant();
        return environment is "production" or "preview"
            && platform is "android" or "ios";
    }

    private async Task<MobileOtaPolicy?> FindPolicyAsync(
        string environment,
        string platform
    ) =>
        await db.Queryable<MobileOtaPolicy>().FirstAsync(item =>
            !item.IsDeleted
            && item.Environment == environment
            && item.Platform == platform
        );

    private async Task<AppOtaRelease?> FindReleaseAsync(
        string environment,
        string platform,
        Guid releaseId
    ) =>
        await db.Queryable<AppOtaRelease>().FirstAsync(item =>
            item.Id == releaseId
            && !item.IsDeleted
            && item.AppKey == MobileAppKeys.Mobile
            && item.Environment == environment
            && item.Platform == platform
        );

    private static string? GetInvalidTargetIdentityReason(
        AppOtaRelease? release,
        string environment,
        string platform,
        string? targetRuntimeVersion
    )
    {
        if (release is null)
        {
            return "missing-or-lane-mismatch";
        }

        if (release.Legacy)
        {
            return "legacy-target";
        }

        if (
            release.AppKey != MobileAppKeys.Mobile
            || release.Environment != environment
            || release.Platform != platform
        )
        {
            return "app-environment-platform-mismatch";
        }

        if (release.ClientChannel != environment)
        {
            return "client-channel-mismatch";
        }

        var expectedPrefix = $"mobile-{environment}-{platform}-release-";
        if (
            !release.ReleaseChannel.StartsWith(expectedPrefix, StringComparison.Ordinal)
            || release.ReleaseChannel.Length <= expectedPrefix.Length
        )
        {
            return "release-channel-mismatch";
        }

        if (!string.Equals(release.EasBranch, release.ReleaseChannel, StringComparison.Ordinal))
        {
            return "eas-branch-mismatch";
        }

        if (
            !string.Equals(
                release.RuntimeVersion,
                targetRuntimeVersion,
                StringComparison.Ordinal
            )
        )
        {
            return "runtime-mismatch";
        }

        return string.Equals(
            release.FactFingerprint,
            AppOtaReleaseService.ComputeFingerprint(release),
            StringComparison.Ordinal
        )
            ? null
            : "fact-fingerprint-mismatch";
    }

    private static MobileOtaPolicyDto EmptyPolicy(
        string environment,
        string platform
    ) =>
        new()
        {
            Environment = environment,
            Platform = platform,
            Enabled = false,
            Required = false,
            PolicyVersion = 0,
        };

    private static MobileOtaPolicyDto MapPolicy(
        MobileOtaPolicy item,
        AppOtaRelease? release
    ) =>
        new()
        {
            Id = item.Id,
            Environment = item.Environment,
            Platform = item.Platform,
            Enabled = item.Enabled,
            Required = item.Required,
            PolicyVersion = item.PolicyVersion,
            TargetReleaseId = item.TargetReleaseId,
            TargetRuntimeVersion = item.TargetRuntimeVersion,
            ReleaseMessage = item.ReleaseMessage,
            TargetRelease = release is null ? null : AppOtaReleaseService.Map(release),
            UpdatedAt = item.UpdatedAt,
            UpdatedBy = item.UpdatedBy,
        };

    private static MobileOtaDecisionDto NoneDecision(
        string policyVersion,
        string platform,
        string clientChannel,
        string runtimeVersion
    ) =>
        new()
        {
            State = AppUpdateStates.None,
            PolicyVersion = policyVersion,
            AppKey = MobileAppKeys.Mobile,
            Platform = platform,
            Required = false,
            ClientChannel = clientChannel,
            ReleaseChannel = null,
            RuntimeVersion = runtimeVersion,
            UpdateId = null,
            UpdateGroupId = null,
            ReleaseMessage = null,
        };

    private static ApiResponse<MobileOtaPolicyDto> VersionError(
        string code,
        long? expected,
        long actual
    ) =>
        ApiResponse<MobileOtaPolicyDto>.Error(
            code == AppUpdatePolicyErrorCodes.VersionRequired
                ? "保存策略必须携带 expectedPolicyVersion"
                : "策略已被其他管理员修改，请刷新后重试",
            code,
            new { expectedPolicyVersion = expected, actualPolicyVersion = actual }
        );

    private static ApiResponse<MobileOtaPolicyDto> Error(
        string message,
        string code
    ) => ApiResponse<MobileOtaPolicyDto>.Error(message, code);

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static string? NormalizeOptional(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }
}
