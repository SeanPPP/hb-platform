using System.Security.Cryptography;
using System.Text;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services;

/// <summary>
/// 手持 POS fixed-channel OTA 的显式迁移边界。启动过程只注册服务，绝不自动 Apply。
/// </summary>
public sealed class PosHandheldOtaLegacyBackfillService(
    ISqlSugarClient db,
    IOptions<PosHandheldUpdatePolicyOptions> policyOptions,
    ILogger<PosHandheldOtaLegacyBackfillService> logger
) : IPosHandheldOtaLegacyBackfillService
{
    public async Task<ApiResponse<PosHandheldOtaLegacyBackfillPreviewDto>> PrepareAsync()
    {
        var plan = await BuildPlanAsync();
        return plan.Preview.Prepared
            ? ApiResponse<PosHandheldOtaLegacyBackfillPreviewDto>.OK(plan.Preview)
            : ApiResponse<PosHandheldOtaLegacyBackfillPreviewDto>.Error(
                "手持 POS legacy OTA 回填预检失败",
                "POS_HANDHELD_OTA_BACKFILL_PRECHECK_FAILED",
                plan.Preview
            );
    }

    public async Task<ApiResponse<PosHandheldOtaLegacyBackfillApplyDto>> ApplyAsync(
        string expectedPreparationFingerprint,
        string currentUser
    )
    {
        var normalizedExpected = Normalize(expectedPreparationFingerprint).ToLowerInvariant();
        if (normalizedExpected.Length != 64)
        {
            return ApplyError(
                "必须提供 prepare 返回的 preparationFingerprint",
                "POS_HANDHELD_OTA_BACKFILL_PREPARATION_REQUIRED"
            );
        }

        PosHandheldOtaLegacyBackfillApplyDto? applied = null;
        ApiResponse<PosHandheldOtaLegacyBackfillApplyDto>? applyError = null;
        var transaction = await db.Ado.UseTranAsync(async () =>
        {
            await AppUpdatePolicyMutationLock.AcquireAsync(
                db,
                "app-ota-release:pos-handheld:legacy-backfill"
            );
            // Apply 前在同一事务和写锁内重新生成计划，任何 head、策略或指纹漂移都中止。
            var plan = await BuildPlanAsync();
            if (!plan.Preview.Prepared)
            {
                applyError = ApplyError(
                    "回填预检已失效，存在阻断项",
                    "POS_HANDHELD_OTA_BACKFILL_PRECHECK_FAILED",
                    plan.Preview
                );
                return;
            }

            if (!string.Equals(
                    normalizedExpected,
                    plan.Preview.PreparationFingerprint,
                    StringComparison.Ordinal
                ))
            {
                applyError = ApplyError(
                    "prepare 后源事实或 active policy 已变化，请重新预检",
                    "POS_HANDHELD_OTA_BACKFILL_STALE",
                    plan.Preview
                );
                return;
            }

            var toInsert = plan.Entities
                .Where(item => !plan.ExistingIds.Contains(item.Id))
                .ToList();
            if (toInsert.Count > 0)
            {
                await db.Insertable(toInsert).ExecuteCommandAsync();
            }

            applied = new PosHandheldOtaLegacyBackfillApplyDto
            {
                PreparationFingerprint = plan.Preview.PreparationFingerprint,
                Inserted = toInsert.Count,
                AlreadyBackfilled = plan.Entities.Count - toInsert.Count,
            };
        });

        if (applyError is not null)
        {
            return applyError;
        }

        if (!transaction.IsSuccess || applied is null)
        {
            logger.LogError(
                transaction.ErrorException,
                "pos-handheld legacy OTA backfill transaction failed"
            );
            return ApplyError(
                "手持 POS legacy OTA 回填事务失败",
                "POS_HANDHELD_OTA_BACKFILL_APPLY_FAILED"
            );
        }

        return ApiResponse<PosHandheldOtaLegacyBackfillApplyDto>.OK(applied);
    }

    private async Task<BackfillPlan> BuildPlanAsync()
    {
        var projectName = Normalize(policyOptions.Value.EasProjectName);
        var fixedChannel = Normalize(policyOptions.Value.OtaChannel).ToLowerInvariant();
        var blocking = new List<string>();
        if (projectName.Length == 0 || fixedChannel != "pos-handheld-production")
        {
            blocking.Add("手持 POS EAS project 或 fixed production channel 配置无效");
        }

        var rows = await db.Queryable<MobileAppOtaUpdate>()
            .Where(item =>
                !item.IsDeleted
                && item.AppKey == MobileAppKeys.PosHandheld
                && item.ProjectName == projectName
                && item.Channel == fixedChannel
                && (item.Platform == "android" || item.Platform == "ios")
            )
            .OrderByDescending(item => item.PublishedAt)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync();
        var validRows = rows.Where(IsValidLegacyRaw).ToList();
        var heads = validRows
            .GroupBy(item => (item.Platform, Runtime: Normalize(item.RuntimeVersion)))
            .ToDictionary(group => group.Key, group => group.First());

        var policies = await db.Queryable<PosHandheldUpdatePolicy>()
            .Where(item =>
                !item.IsDeleted
                && item.Enabled
                && (
                    item.Lane == PosHandheldUpdateLanes.AndroidOta
                    || item.Lane == PosHandheldUpdateLanes.IosOta
                )
            )
            .ToListAsync();
        foreach (var policy in policies)
        {
            var expectedPlatform = policy.Lane == PosHandheldUpdateLanes.IosOta
                ? "ios"
                : "android";
            var target = policy.CandidateId.HasValue
                ? validRows.FirstOrDefault(item => item.Id == policy.CandidateId.Value)
                : null;
            if (target is null || target.Platform != expectedPlatform)
            {
                blocking.Add($"active {policy.Lane} target 不属于有效 fixed-channel 事实");
                continue;
            }

            var headKey = (target.Platform, Runtime: Normalize(target.RuntimeVersion));
            if (!heads.TryGetValue(headKey, out var head) || head.Id != target.Id)
            {
                blocking.Add($"active {policy.Lane} target 不是对应 Runtime 的真实 head");
                continue;
            }

            var surface = PosHandheldUpdatePolicyService.MapOtaCandidate(
                target,
                policy.Lane,
                isCurrentHead: true
            );
            var fingerprint = surface is null
                ? null
                : PosHandheldUpdatePolicyService.ComputeCandidateFingerprint(surface);
            if (!string.Equals(
                    fingerprint,
                    policy.CandidateFingerprint,
                    StringComparison.Ordinal
                ))
            {
                blocking.Add($"active {policy.Lane} target 候选表面 fingerprint 不一致");
            }
        }

        var rawByGroup = validRows
            .GroupBy(item => (item.Platform, Group: Normalize(item.UpdateGroupId)))
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var item in validRows)
        {
            var rollbackGroup = Normalize(item.RollbackOfGroupId);
            if (!item.IsRollback && rollbackGroup.Length == 0)
            {
                continue;
            }

            if (
                !item.IsRollback
                || rollbackGroup.Length == 0
                || !rawByGroup.TryGetValue(
                    (item.Platform, rollbackGroup),
                    out var rollbackSource
                )
                || rollbackSource.Id == item.Id
            )
            {
                // 回填必须先证明 rollback 来源，不能把半成对身份写入不可变事实表。
                blocking.Add($"legacy {item.Id:D} rollback 来源缺失或无效");
            }
        }

        var entities = validRows.Select(item => MapLegacyRelease(item, rawByGroup)).ToList();
        var existing = await db.Queryable<AppOtaRelease>()
            .Where(item =>
                !item.IsDeleted
                && item.AppKey == MobileAppKeys.PosHandheld
                && item.Environment == "production"
            )
            .ToListAsync();
        var existingIds = new HashSet<Guid>();
        foreach (var entity in entities)
        {
            var sameId = existing.FirstOrDefault(item => item.Id == entity.Id);
            var identityConflict = existing.FirstOrDefault(item =>
                item.Id != entity.Id
                && item.Platform == entity.Platform
                && (
                    item.UpdateId == entity.UpdateId
                    || item.UpdateGroupId == entity.UpdateGroupId
                )
            );
            if (identityConflict is not null)
            {
                blocking.Add($"legacy {entity.Id:D} 的 update identity 已被其他事实占用");
            }

            if (sameId is not null)
            {
                if (
                    !sameId.Legacy
                    || !string.Equals(
                        sameId.FactFingerprint,
                        entity.FactFingerprint,
                        StringComparison.Ordinal
                    )
                )
                {
                    blocking.Add($"legacy {entity.Id:D} 已存在但不可变字段不一致");
                }
                else
                {
                    existingIds.Add(entity.Id);
                }
            }
        }

        var items = entities.Select(item => new PosHandheldOtaLegacyBackfillItemDto
        {
            Id = item.Id,
            Platform = item.Platform,
            RuntimeVersion = item.RuntimeVersion,
            UpdateId = item.UpdateId,
            UpdateGroupId = item.UpdateGroupId,
            FactFingerprint = item.FactFingerprint,
            AlreadyBackfilled = existingIds.Contains(item.Id),
        }).ToList();
        var preparationFingerprint = ComputePreparationFingerprint(
            items,
            policies,
            blocking
        );
        return new BackfillPlan(
            new PosHandheldOtaLegacyBackfillPreviewDto
            {
                Prepared = blocking.Count == 0,
                PreparationFingerprint = preparationFingerprint,
                BlockingReasons = blocking,
                Items = items,
            },
            entities,
            existingIds
        );
    }

    private static AppOtaRelease MapLegacyRelease(
        MobileAppOtaUpdate item,
        IReadOnlyDictionary<(string Platform, string Group), MobileAppOtaUpdate> rawByGroup
    )
    {
        var groupId = Guid.Parse(Normalize(item.UpdateGroupId));
        Guid? rollbackOfReleaseId = null;
        var rollbackGroup = Normalize(item.RollbackOfGroupId);
        if (
            rollbackGroup.Length > 0
            && rawByGroup.TryGetValue((item.Platform, rollbackGroup), out var rollbackSource)
        )
        {
            rollbackOfReleaseId = rollbackSource.Id;
        }

        var entity = new AppOtaRelease
        {
            Id = item.Id,
            ReleaseBatchId = groupId,
            AppKey = MobileAppKeys.PosHandheld,
            Environment = "production",
            ClientChannel = "pos-handheld-production",
            ReleaseChannel = "pos-handheld-production",
            EasBranch = NormalizeOptional(item.Branch) ?? "pos-handheld-production",
            ProjectName = Normalize(item.ProjectName),
            Platform = Normalize(item.Platform).ToLowerInvariant(),
            RuntimeVersion = Normalize(item.RuntimeVersion),
            UpdateGroupId = groupId.ToString("D"),
            UpdateId = Guid.Parse(Normalize(item.UpdateId ?? item.AndroidUpdateId))
                .ToString("D"),
            Message = NormalizeOptional(item.Message),
            GitCommitHash = NormalizeOptional(item.GitCommitHash),
            DashboardUrl = NormalizeOptional(item.DashboardUrl),
            PublishedAtUtc = AppOtaReleaseService.NormalizeUtcTimestamp(item.PublishedAt),
            IsRollback = item.IsRollback,
            RollbackOfReleaseId = rollbackOfReleaseId,
            Legacy = true,
            RegistrationSource = "mobile-app-ota-legacy-backfill",
            CreatedAt = item.CreatedAt,
            CreatedBy = item.CreatedBy,
            UpdatedAt = null,
            UpdatedBy = null,
            IsDeleted = false,
        };
        entity.FactFingerprint = AppOtaReleaseService.ComputeFingerprint(entity);
        return entity;
    }

    private static string ComputePreparationFingerprint(
        IEnumerable<PosHandheldOtaLegacyBackfillItemDto> items,
        IEnumerable<PosHandheldUpdatePolicy> policies,
        IEnumerable<string> blocking
    )
    {
        var canonical = string.Join(
            "\n",
            items.OrderBy(item => item.Id).Select(item =>
                $"fact|{item.Id:D}|{item.FactFingerprint}|{item.AlreadyBackfilled}"
            ).Concat(
                policies.OrderBy(item => item.Lane).Select(item =>
                    $"policy|{item.Lane}|{item.PolicyVersion}|{item.CandidateId:D}|{item.CandidateFingerprint}"
                )
            ).Concat(blocking.OrderBy(item => item, StringComparer.Ordinal))
        );
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static bool IsValidLegacyRaw(MobileAppOtaUpdate item) =>
        item.Platform is "android" or "ios"
        && Normalize(item.RuntimeVersion).Length > 0
        && Guid.TryParse(Normalize(item.UpdateGroupId), out _)
        && Guid.TryParse(Normalize(item.UpdateId ?? item.AndroidUpdateId), out _);

    private static ApiResponse<PosHandheldOtaLegacyBackfillApplyDto> ApplyError(
        string message,
        string code,
        object? details = null
    ) => ApiResponse<PosHandheldOtaLegacyBackfillApplyDto>.Error(message, code, details);

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static string? NormalizeOptional(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private sealed record BackfillPlan(
        PosHandheldOtaLegacyBackfillPreviewDto Preview,
        List<AppOtaRelease> Entities,
        HashSet<Guid> ExistingIds
    );
}
