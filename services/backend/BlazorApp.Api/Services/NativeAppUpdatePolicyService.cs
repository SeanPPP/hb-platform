using System.Globalization;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using SqlSugar;

namespace BlazorApp.Api.Services;

public sealed class NativeAppUpdatePolicyService(
    ISqlSugarClient db,
    ILogger<NativeAppUpdatePolicyService> logger
) : INativeAppUpdatePolicyService
{
    private const string MobilePolicyKey = AppUpdateApps.MobileIos;
    private const string PosIpadPolicyKey = "pos-ipad-native";

    public async Task<ApiResponse<NativeUpdatePolicyDto>> GetMobileIosPolicyAsync()
    {
        var policy = await db.Queryable<MobileIosNativeUpdatePolicy>()
            .FirstAsync(item => item.PolicyKey == MobilePolicyKey && !item.IsDeleted);
        return ApiResponse<NativeUpdatePolicyDto>.OK(
            policy is null
                ? EmptyPolicy()
                : await MapMobilePolicyAsync(policy)
        );
    }

    public async Task<ApiResponse<NativeUpdatePolicyDto>> SetMobileIosPolicyAsync(
        NativeUpdatePolicyRequest request,
        string currentUser
    )
    {
        if (!request.ExpectedPolicyVersion.HasValue)
        {
            var current = await db.Queryable<MobileIosNativeUpdatePolicy>()
                .FirstAsync(item =>
                    item.PolicyKey == MobilePolicyKey && !item.IsDeleted
                );
            return PolicyVersionError(
                AppUpdatePolicyErrorCodes.VersionRequired,
                request.ExpectedPolicyVersion,
                current?.PolicyVersion ?? 0
            );
        }

        var validation = await ValidateNativePolicyAsync(
            request,
            AppUpdateApps.MobileIos
        );
        if (!validation.Success)
        {
            return validation.Error!;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            NativeUpdatePolicyDto? saved = null;
            ApiResponse<NativeUpdatePolicyDto>? mutationError = null;
            var transaction = await db.Ado.UseTranAsync(async () =>
            {
                await AppUpdatePolicyMutationLock.AcquireAsync(
                    db,
                    $"app-update-policy:native:{MobilePolicyKey}"
                );
                var existing = await db.Queryable<MobileIosNativeUpdatePolicy>()
                    .FirstAsync(item =>
                        item.PolicyKey == MobilePolicyKey && !item.IsDeleted
                    );
                Guid? releaseId = request.Enabled ? validation.Release!.Id : null;
                var minimumVersion = request.Enabled ? validation.MinimumVersion : null;
                var releaseMessage = request.Enabled ? validation.ReleaseMessage : null;
                var actualPolicyVersion = existing?.PolicyVersion ?? 0;
                if (
                    existing is null
                        ? !request.Enabled
                        : IsSameMobilePolicy(
                            existing,
                            request.Enabled,
                            releaseId,
                            minimumVersion,
                            releaseMessage
                        )
                )
                {
                    saved = existing is null
                        ? EmptyPolicy()
                        : await MapMobilePolicyAsync(existing);
                    return;
                }

                if (request.ExpectedPolicyVersion.Value != actualPolicyVersion)
                {
                    mutationError = PolicyVersionError(
                        AppUpdatePolicyErrorCodes.VersionConflict,
                        request.ExpectedPolicyVersion,
                        actualPolicyVersion
                    );
                    return;
                }

                var now = DateTime.UtcNow;
                var entity = existing ?? new MobileIosNativeUpdatePolicy
                {
                    Id = Guid.NewGuid(),
                    PolicyKey = MobilePolicyKey,
                    CreatedAt = now,
                    CreatedBy = NormalizeUser(currentUser),
                    IsDeleted = false,
                };
                entity.Enabled = request.Enabled;
                entity.ReleaseId = releaseId;
                entity.MinimumSupportedVersion = minimumVersion;
                entity.ReleaseMessage = releaseMessage;
                entity.PolicyVersion = (existing?.PolicyVersion ?? 0) + 1;
                entity.UpdatedAt = now;
                entity.UpdatedBy = NormalizeUser(currentUser);

                if (existing is null)
                {
                    await db.Insertable(entity).ExecuteCommandAsync();
                }
                else
                {
                    await db.Updateable(entity).ExecuteCommandAsync();
                }

                saved = await MapMobilePolicyAsync(entity);
            });

            if (mutationError is not null)
            {
                return mutationError;
            }

            if (transaction.IsSuccess && saved is not null)
            {
                return ApiResponse<NativeUpdatePolicyDto>.OK(saved!);
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
                    "Mobile iOS 原生策略首次并发写入冲突，锁内重读后重试"
                );
                continue;
            }

            logger.LogError(transaction.ErrorException, "Mobile iOS 原生策略事务保存失败");
            return ApiResponse<NativeUpdatePolicyDto>.Error(
                "Mobile iOS 原生策略保存失败",
                "MOBILE_IOS_NATIVE_POLICY_SAVE_FAILED"
            );
        }

        throw new InvalidOperationException("Mobile iOS 原生策略重试状态无效");
    }

    public async Task<ApiResponse<NativeUpdatePolicyDto>> GetPosIpadNativePolicyAsync()
    {
        var policy = await db.Queryable<PosIpadNativeUpdatePolicy>()
            .FirstAsync(item => item.PolicyKey == PosIpadPolicyKey && !item.IsDeleted);
        return ApiResponse<NativeUpdatePolicyDto>.OK(
            policy is null
                ? EmptyPolicy()
                : await MapPosIpadPolicyAsync(policy)
        );
    }

    public async Task<ApiResponse<NativeUpdatePolicyDto>> SetPosIpadNativePolicyAsync(
        PosIpadNativeUpdatePolicyRequest request,
        string currentUser
    )
    {
        if (!request.ExpectedPolicyVersion.HasValue)
        {
            var current = await db.Queryable<PosIpadNativeUpdatePolicy>()
                .FirstAsync(item =>
                    item.PolicyKey == PosIpadPolicyKey && !item.IsDeleted
                );
            return PolicyVersionError(
                AppUpdatePolicyErrorCodes.VersionRequired,
                request.ExpectedPolicyVersion,
                current?.PolicyVersion ?? 0
            );
        }

        var validation = await ValidateNativePolicyAsync(request, AppUpdateApps.PosIpad);
        if (!validation.Success)
        {
            return validation.Error!;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            NativeUpdatePolicyDto? saved = null;
            ApiResponse<NativeUpdatePolicyDto>? validationError = null;
            var transaction = await db.Ado.UseTranAsync(async () =>
            {
                await AppUpdatePolicyMutationLock.AcquireAsync(
                    db,
                    $"app-update-policy:native:{PosIpadPolicyKey}"
                );
                var targetValidation = request.Enabled
                    ? await ValidateTargetsAsync(
                        request.TargetScope,
                        request.TargetStoreGuids
                    )
                    : TargetValidation.All;
                if (!targetValidation.Success)
                {
                    validationError = ApiResponse<NativeUpdatePolicyDto>.Error(
                        targetValidation.Message!,
                        targetValidation.ErrorCode
                    );
                    return;
                }

                var existing = await db.Queryable<PosIpadNativeUpdatePolicy>()
                    .FirstAsync(item =>
                        item.PolicyKey == PosIpadPolicyKey && !item.IsDeleted
                    );
                var existingTargets = existing is null
                    ? new List<string>()
                    : await db.Queryable<PosIpadNativeUpdatePolicyTarget>()
                        .Where(item =>
                            item.PolicyId == existing.Id && !item.IsDeleted
                        )
                        .Select(item => item.StoreGuid)
                        .ToListAsync();
                Guid? releaseId = request.Enabled ? validation.Release!.Id : null;
                var minimumVersion = request.Enabled
                    ? validation.MinimumVersion
                    : null;
                var minimumBuildNumber = request.Enabled
                    ? validation.MinimumBuildNumber
                    : null;
                var releaseMessage = request.Enabled
                    ? validation.ReleaseMessage
                    : null;
                var targetScope = request.Enabled
                    ? targetValidation.TargetScope
                    : AppUpdateTargetScopes.All;
                var actualPolicyVersion = existing?.PolicyVersion ?? 0;
                if (
                    existing is null
                        ? !request.Enabled
                        : IsSamePosIpadPolicy(
                            existing,
                            existingTargets,
                            request.Enabled,
                            releaseId,
                            minimumVersion,
                            minimumBuildNumber,
                            releaseMessage,
                            targetScope,
                            targetValidation.StoreGuids
                        )
                )
                {
                    saved = existing is null
                        ? EmptyPolicy()
                        : await MapPosIpadPolicyAsync(existing);
                    return;
                }

                if (request.ExpectedPolicyVersion.Value != actualPolicyVersion)
                {
                    validationError = PolicyVersionError(
                        AppUpdatePolicyErrorCodes.VersionConflict,
                        request.ExpectedPolicyVersion,
                        actualPolicyVersion
                    );
                    return;
                }

                var now = DateTime.UtcNow;
                var entity = existing ?? new PosIpadNativeUpdatePolicy
                {
                    Id = Guid.NewGuid(),
                    PolicyKey = PosIpadPolicyKey,
                    CreatedAt = now,
                    CreatedBy = NormalizeUser(currentUser),
                    IsDeleted = false,
                };
                entity.Enabled = request.Enabled;
                entity.ReleaseId = releaseId;
                entity.MinimumSupportedVersion = minimumVersion;
                entity.MinimumSupportedBuildNumber = minimumBuildNumber;
                entity.ReleaseMessage = releaseMessage;
                entity.TargetScope = targetScope;
                entity.PolicyVersion = (existing?.PolicyVersion ?? 0) + 1;
                entity.UpdatedAt = now;
                entity.UpdatedBy = NormalizeUser(currentUser);

                if (existing is null)
                {
                    await db.Insertable(entity).ExecuteCommandAsync();
                }
                else
                {
                    await db.Updateable(entity).ExecuteCommandAsync();
                }

                await db.Deleteable<PosIpadNativeUpdatePolicyTarget>()
                    .Where(item => item.PolicyId == entity.Id)
                    .ExecuteCommandAsync();
                if (targetValidation.StoreGuids.Count > 0)
                {
                    var targets = targetValidation.StoreGuids.Select(storeGuid =>
                        new PosIpadNativeUpdatePolicyTarget
                        {
                            Id = Guid.NewGuid(),
                            PolicyId = entity.Id,
                            StoreGuid = storeGuid,
                            CreatedAt = now,
                            CreatedBy = NormalizeUser(currentUser),
                            UpdatedAt = null,
                            IsDeleted = false,
                        }
                    ).ToList();
                    await db.Insertable(targets).ExecuteCommandAsync();
                }

                saved = await MapPosIpadPolicyAsync(entity);
            });

            if (validationError is not null)
            {
                return validationError;
            }

            if (transaction.IsSuccess && saved is not null)
            {
                return ApiResponse<NativeUpdatePolicyDto>.OK(saved!);
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
                    "iPad 原生策略首次并发写入冲突，锁内重读后重试"
                );
                continue;
            }

            logger.LogError(transaction.ErrorException, "iPad 原生更新策略事务保存失败");
            return ApiResponse<NativeUpdatePolicyDto>.Error(
                "iPad 原生更新策略保存失败",
                "POS_IPAD_NATIVE_POLICY_SAVE_FAILED"
            );
        }

        throw new InvalidOperationException("iPad 原生策略重试状态无效");
    }

    public async Task<ApiResponse<List<AppUpdateTargetStoreOptionDto>>> GetStoreOptionsAsync()
    {
        var stores = await db.Queryable<Store>()
            .Where(item => item.IsActive && !item.IsDeleted)
            .OrderBy(item => item.StoreCode)
            .ToListAsync();
        return ApiResponse<List<AppUpdateTargetStoreOptionDto>>.OK(
            stores.Select(item => new AppUpdateTargetStoreOptionDto
            {
                StoreGuid = item.StoreGUID,
                StoreCode = item.StoreCode,
                StoreName = item.StoreName,
            }).ToList()
        );
    }

    public async Task<NativeAppUpdateDecisionDto> GetMobileIosDecisionAsync(
        string? version,
        string? build
    )
    {
        _ = build; // build number 仅用于调用审计，不参与营销版本比较。
        var policy = await db.Queryable<MobileIosNativeUpdatePolicy>()
            .FirstAsync(item =>
                item.PolicyKey == MobilePolicyKey
                && item.Enabled
                && !item.IsDeleted
            );
        if (policy?.ReleaseId is null)
        {
            return NoNativeDecision();
        }

        var release = await LoadVerifiedReleaseAsync(policy.ReleaseId.Value, AppUpdateApps.MobileIos);
        return release is null
            ? NoNativeDecision()
            : BuildNativeDecision(
                version,
                policy.PolicyVersion,
                release,
                policy.MinimumSupportedVersion,
                policy.ReleaseMessage
            );
    }

    public async Task<NativeAppUpdateDecisionDto> GetPosIpadNativeDecisionAsync(
        PosIpadNativeDecisionRequest request
    )
    {
        var store = await ResolveActiveStoreAsync(request.StoreCode);
        if (store is null)
        {
            return NoNativeDecision();
        }

        var policy = await db.Queryable<PosIpadNativeUpdatePolicy>()
            .FirstAsync(item =>
                item.PolicyKey == PosIpadPolicyKey
                && item.Enabled
                && !item.IsDeleted
            );
        if (policy?.ReleaseId is null || !await MatchesNativeTargetAsync(policy, store.StoreGUID))
        {
            return NoNativeDecision();
        }

        var release = await LoadVerifiedReleaseAsync(policy.ReleaseId.Value, AppUpdateApps.PosIpad);
        return release is null
            ? NoNativeDecision()
            : BuildPosIpadNativeDecision(
                request.Version,
                request.Build,
                policy.PolicyVersion,
                release,
                policy.MinimumSupportedVersion,
                policy.MinimumSupportedBuildNumber,
                policy.ReleaseMessage
            );
    }

    private async Task<NativePolicyValidation> ValidateNativePolicyAsync(
        NativeUpdatePolicyRequest request,
        string expectedApp
    )
    {
        var message = NormalizeOptional(request.ReleaseMessage);
        if (message?.Length > 1000)
        {
            return NativePolicyValidation.Fail(
                "RELEASE_MESSAGE_TOO_LONG",
                "更新说明不能超过 1000 个字符"
            );
        }

        if (!request.Enabled)
        {
            return NativePolicyValidation.Disabled;
        }

        if (request.ReleaseId is null)
        {
            return NativePolicyValidation.Fail(
                "APP_STORE_RELEASE_REQUIRED",
                "启用策略必须选择已验证的 App Store 发布"
            );
        }

        var release = await LoadVerifiedReleaseAsync(request.ReleaseId.Value, expectedApp);
        if (release is null)
        {
            return NativePolicyValidation.Fail(
                "APP_STORE_RELEASE_INVALID",
                "App Store 发布不存在、未验证或不属于目标 App"
            );
        }

        var isPosIpad = string.Equals(
            expectedApp,
            AppUpdateApps.PosIpad,
            StringComparison.Ordinal
        );
        if (
            isPosIpad
                ? !PosIpadEffectiveVersion.TryParseMarketing(
                    release.Version,
                    out _
                )
                : !AppMarketingVersion.TryParse(release.Version, out _)
        )
        {
            return NativePolicyValidation.Fail(
                "LATEST_VERSION_INVALID",
                "App Store 发布版本无效"
            );
        }

        PosIpadEffectiveVersion latestIpadVersion = default;
        if (
            isPosIpad
            && !PosIpadEffectiveVersion.TryCreate(
                release.Version,
                release.BuildNumber,
                out latestIpadVersion
            )
        )
        {
            return NativePolicyValidation.Fail(
                "LATEST_BUILD_NUMBER_INVALID",
                "iPad App Store 发布 build 必须是 0 到 Int32.MaxValue 的整数"
            );
        }

        var minimum = NormalizeOptional(request.MinimumSupportedVersion);
        var minimumBuild = isPosIpad
            ? ((PosIpadNativeUpdatePolicyRequest)request).MinimumSupportedBuildNumber
            : null;
        if (minimumBuild.HasValue && minimum is null)
        {
            return NativePolicyValidation.Fail(
                "MINIMUM_BUILD_REQUIRES_VERSION",
                "设置最低支持 build 时必须同时设置最低支持版本"
            );
        }

        if (minimumBuild < 0)
        {
            return NativePolicyValidation.Fail(
                "MINIMUM_BUILD_INVALID",
                "最低支持 build 必须是非负整数"
            );
        }

        if (minimum is not null)
        {
            if (isPosIpad)
            {
                if (
                    !PosIpadEffectiveVersion.TryCreate(
                        minimum,
                        minimumBuild ?? 0,
                        out var minimumIpadVersion
                    )
                )
                {
                    return NativePolicyValidation.Fail(
                        "MINIMUM_VERSION_INVALID",
                        "最低支持版本无效"
                    );
                }

                if (minimumIpadVersion.CompareTo(latestIpadVersion) > 0)
                {
                    var errorCode =
                        minimumIpadVersion.Major == latestIpadVersion.Major
                        && minimumIpadVersion.Minor == latestIpadVersion.Minor
                        && minimumIpadVersion.Patch == latestIpadVersion.Patch
                            ? "MINIMUM_BUILD_ABOVE_LATEST"
                            : "MINIMUM_VERSION_ABOVE_LATEST";
                    return NativePolicyValidation.Fail(
                        errorCode,
                        "最低支持版本不能高于 App Store 最新版本"
                    );
                }
            }
            else
            {
                if (!AppMarketingVersion.TryParse(minimum, out var parsedMinimum))
                {
                    return NativePolicyValidation.Fail(
                        "MINIMUM_VERSION_INVALID",
                        "最低支持版本无效"
                    );
                }

                AppMarketingVersion.TryParse(release.Version, out var latest);
                if (parsedMinimum.CompareTo(latest) > 0)
                {
                    return NativePolicyValidation.Fail(
                        "MINIMUM_VERSION_ABOVE_LATEST",
                        "最低支持版本不能高于 App Store 最新版本"
                    );
                }
            }
        }

        return NativePolicyValidation.Ok(
            release,
            minimum,
            minimumBuild,
            message
        );
    }

    private async Task<TargetValidation> ValidateTargetsAsync(
        string? requestedScope,
        IReadOnlyCollection<string>? requestedStoreGuids
    )
    {
        var scope = NormalizeOptional(requestedScope)?.ToLowerInvariant()
            ?? AppUpdateTargetScopes.All;
        if (scope == AppUpdateTargetScopes.All)
        {
            return TargetValidation.All;
        }

        if (scope != AppUpdateTargetScopes.Stores)
        {
            return TargetValidation.Fail(
                "TARGET_SCOPE_INVALID",
                "目标范围必须是 all 或 stores"
            );
        }

        var requested = (requestedStoreGuids ?? Array.Empty<string>())
            .Select(NormalizeOptional)
            .Where(item => item is not null)
            .Select(item => NormalizeStoreGuid(item!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requested.Count == 0)
        {
            return TargetValidation.Fail(
                "TARGET_STORES_REQUIRED",
                "按分店投放时至少选择一个活动分店"
            );
        }

        var activeStores = await db.Queryable<Store>()
            .Where(item =>
                item.IsActive
                && !item.IsDeleted
                && requested.Contains(item.StoreGUID)
            )
            .ToListAsync();
        var canonical = activeStores.ToDictionary(
            item => item.StoreGUID,
            item => item.StoreGUID,
            StringComparer.OrdinalIgnoreCase
        );
        if (requested.Any(item => !canonical.ContainsKey(item)))
        {
            return TargetValidation.Fail(
                "TARGET_STORES_INVALID",
                "目标分店必须全部处于活动状态"
            );
        }

        return TargetValidation.Stores(requested.Select(item => canonical[item]).ToList());
    }

    private async Task<IosAppStoreRelease?> LoadVerifiedReleaseAsync(Guid id, string app) =>
        await db.Queryable<IosAppStoreRelease>()
            .FirstAsync(item =>
                item.Id == id
                && item.App == app
                && item.AppleVerifiedAtUtc > DateTime.MinValue
                && !item.IsDeleted
            );

    private async Task<bool> MatchesNativeTargetAsync(
        PosIpadNativeUpdatePolicy policy,
        string storeGuid
    )
    {
        if (policy.TargetScope == AppUpdateTargetScopes.All)
        {
            return true;
        }

        if (policy.TargetScope != AppUpdateTargetScopes.Stores)
        {
            return false;
        }

        return await db.Queryable<PosIpadNativeUpdatePolicyTarget>()
            .AnyAsync(item =>
                item.PolicyId == policy.Id
                && item.StoreGuid == storeGuid
                && !item.IsDeleted
        );
    }

    private async Task<Store?> ResolveActiveStoreAsync(string? storeCode)
    {
        var normalized = NormalizeOptional(storeCode);
        if (normalized is null)
        {
            return null;
        }

        var normalizedLower = normalized.ToLowerInvariant();
        return await db.Queryable<Store>()
            .FirstAsync(item =>
                item.IsActive
                && !item.IsDeleted
                && item.StoreCode.ToLower() == normalizedLower
            );
    }

    private static NativeAppUpdateDecisionDto BuildNativeDecision(
        string? currentVersion,
        long policyVersion,
        IosAppStoreRelease release,
        string? minimumVersion,
        string? releaseMessage
    )
    {
        if (!AppMarketingVersion.TryParse(release.Version, out var latest))
        {
            return NoNativeDecision();
        }

        string state;
        if (!AppMarketingVersion.TryParse(currentVersion, out var current))
        {
            state = minimumVersion is null
                ? AppUpdateStates.Optional
                : AppUpdateStates.Required;
        }
        else if (
            minimumVersion is not null
            && AppMarketingVersion.TryParse(minimumVersion, out var minimum)
            && current.CompareTo(minimum) < 0
        )
        {
            state = AppUpdateStates.Required;
        }
        else if (current.CompareTo(latest) < 0)
        {
            state = AppUpdateStates.Optional;
        }
        else
        {
            return NoNativeDecision();
        }

        return new NativeAppUpdateDecisionDto
        {
            State = state,
            PolicyVersion = policyVersion.ToString(CultureInfo.InvariantCulture),
            LatestVersion = release.Version,
            MinimumSupportedVersion = minimumVersion,
            AppStoreUrl = release.AppStoreUrl,
            ReleaseMessage = releaseMessage,
        };
    }

    private static NativeAppUpdateDecisionDto BuildPosIpadNativeDecision(
        string? currentVersion,
        string? currentBuild,
        long policyVersion,
        IosAppStoreRelease release,
        string? minimumVersion,
        int? minimumBuildNumber,
        string? releaseMessage
    )
    {
        if (
            !PosIpadEffectiveVersion.TryCreate(
                release.Version,
                release.BuildNumber,
                out var latest
            )
        )
        {
            return NoNativeDecision();
        }

        PosIpadEffectiveVersion? minimum = null;
        if (minimumVersion is not null)
        {
            if (
                !PosIpadEffectiveVersion.TryCreate(
                    minimumVersion,
                    minimumBuildNumber ?? 0,
                    out var parsedMinimum
                )
            )
            {
                return NoNativeDecision();
            }

            minimum = parsedMinimum;
        }

        if (
            !PosIpadEffectiveVersion.TryCreate(
                currentVersion,
                0,
                out var currentMarketing
            )
        )
        {
            return BuildPosIpadDecision(
                minimum.HasValue
                    ? AppUpdateStates.Required
                    : AppUpdateStates.Optional,
                policyVersion,
                latest,
                minimum,
                release,
                releaseMessage
            );
        }

        var hasCurrentBuild = PosIpadEffectiveVersion.TryParseBuild(
            currentBuild,
            out var parsedCurrentBuild
        );
        if (minimum.HasValue)
        {
            var minimumMarketingComparison = currentMarketing.CompareMarketingTo(
                minimum.Value
            );
            if (
                minimumMarketingComparison < 0
                || (
                    minimumMarketingComparison == 0
                    && (
                        !hasCurrentBuild
                        || parsedCurrentBuild < minimum.Value.Build
                    )
                )
            )
            {
                return BuildPosIpadDecision(
                    AppUpdateStates.Required,
                    policyVersion,
                    latest,
                    minimum,
                    release,
                    releaseMessage
                );
            }
        }

        var latestMarketingComparison = currentMarketing.CompareMarketingTo(latest);
        if (
            latestMarketingComparison < 0
            || (
                latestMarketingComparison == 0
                && (!hasCurrentBuild || parsedCurrentBuild < latest.Build)
            )
        )
        {
            return BuildPosIpadDecision(
                AppUpdateStates.Optional,
                policyVersion,
                latest,
                minimum,
                release,
                releaseMessage
            );
        }

        return NoNativeDecision();
    }

    private static NativeAppUpdateDecisionDto BuildPosIpadDecision(
        string state,
        long policyVersion,
        PosIpadEffectiveVersion latest,
        PosIpadEffectiveVersion? minimum,
        IosAppStoreRelease release,
        string? releaseMessage
    )
    {
        return new NativeAppUpdateDecisionDto
        {
            State = state,
            PolicyVersion = policyVersion.ToString(CultureInfo.InvariantCulture),
            // 决策 DTO 字段保持冻结，iPad 用四段有效版本承载营销版本与 build。
            LatestVersion = latest.ToString(),
            MinimumSupportedVersion = minimum?.ToString(),
            AppStoreUrl = release.AppStoreUrl,
            ReleaseMessage = releaseMessage,
        };
    }

    private async Task<NativeUpdatePolicyDto> MapMobilePolicyAsync(
        MobileIosNativeUpdatePolicy policy
    )
    {
        var release = policy.ReleaseId is null
            ? null
            : await LoadVerifiedReleaseAsync(policy.ReleaseId.Value, AppUpdateApps.MobileIos);
        return new NativeUpdatePolicyDto
        {
            Id = policy.Id,
            Enabled = policy.Enabled,
            PolicyVersion = policy.PolicyVersion,
            ReleaseId = policy.ReleaseId,
            LatestVersion = release?.Version,
            MinimumSupportedVersion = policy.MinimumSupportedVersion,
            AppStoreUrl = release?.AppStoreUrl,
            ReleaseMessage = policy.ReleaseMessage,
            TargetScope = AppUpdateTargetScopes.All,
            UpdatedAt = policy.UpdatedAt,
            UpdatedBy = policy.UpdatedBy,
        };
    }

    private async Task<NativeUpdatePolicyDto> MapPosIpadPolicyAsync(
        PosIpadNativeUpdatePolicy policy
    )
    {
        var release = policy.ReleaseId is null
            ? null
            : await LoadVerifiedReleaseAsync(policy.ReleaseId.Value, AppUpdateApps.PosIpad);
        var targets = await db.Queryable<PosIpadNativeUpdatePolicyTarget>()
            .Where(item => item.PolicyId == policy.Id && !item.IsDeleted)
            .ToListAsync();
        return new NativeUpdatePolicyDto
        {
            Id = policy.Id,
            Enabled = policy.Enabled,
            PolicyVersion = policy.PolicyVersion,
            ReleaseId = policy.ReleaseId,
            LatestVersion = release?.Version,
            MinimumSupportedVersion = policy.MinimumSupportedVersion,
            MinimumSupportedBuildNumber = policy.MinimumSupportedBuildNumber,
            AppStoreUrl = release?.AppStoreUrl,
            ReleaseMessage = policy.ReleaseMessage,
            TargetScope = policy.TargetScope,
            TargetStoreGuids = targets.Select(item => item.StoreGuid).ToList(),
            UpdatedAt = policy.UpdatedAt,
            UpdatedBy = policy.UpdatedBy,
        };
    }

    private static bool IsSameMobilePolicy(
        MobileIosNativeUpdatePolicy existing,
        bool enabled,
        Guid? releaseId,
        string? minimumVersion,
        string? releaseMessage
    ) =>
        existing.Enabled == enabled
        && existing.ReleaseId == releaseId
        && string.Equals(
            existing.MinimumSupportedVersion,
            minimumVersion,
            StringComparison.Ordinal
        )
        && string.Equals(
            existing.ReleaseMessage,
            releaseMessage,
            StringComparison.Ordinal
        );

    private static bool IsSamePosIpadPolicy(
        PosIpadNativeUpdatePolicy existing,
        IReadOnlyCollection<string> existingTargets,
        bool enabled,
        Guid? releaseId,
        string? minimumVersion,
        int? minimumBuildNumber,
        string? releaseMessage,
        string targetScope,
        IReadOnlyCollection<string> targetStoreGuids
    ) =>
        existing.Enabled == enabled
        && existing.ReleaseId == releaseId
        && string.Equals(
            existing.MinimumSupportedVersion,
            minimumVersion,
            StringComparison.Ordinal
        )
        && existing.MinimumSupportedBuildNumber == minimumBuildNumber
        && string.Equals(
            existing.ReleaseMessage,
            releaseMessage,
            StringComparison.Ordinal
        )
        && string.Equals(existing.TargetScope, targetScope, StringComparison.Ordinal)
        && HaveSameStores(existingTargets, targetStoreGuids);

    private static bool HaveSameStores(
        IEnumerable<string> left,
        IEnumerable<string> right
    ) =>
        new HashSet<string>(left, StringComparer.OrdinalIgnoreCase).SetEquals(right);

    private static NativeUpdatePolicyDto EmptyPolicy() => new();

    private static NativeAppUpdateDecisionDto NoNativeDecision() => new();

    private static ApiResponse<NativeUpdatePolicyDto> PolicyVersionError(
        string errorCode,
        long? expectedPolicyVersion,
        long actualPolicyVersion
    ) =>
        ApiResponse<NativeUpdatePolicyDto>.Error(
            errorCode == AppUpdatePolicyErrorCodes.VersionRequired
                ? "expectedPolicyVersion 不能为空"
                : "更新策略版本已变化，请刷新后重试",
            errorCode,
            new
            {
                ExpectedPolicyVersion = expectedPolicyVersion,
                ActualPolicyVersion = actualPolicyVersion,
            }
        );

    private static string NormalizeUser(string? value) =>
        NormalizeOptional(value) ?? "System";

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeStoreGuid(string value) =>
        Guid.TryParse(value, out var parsed) ? parsed.ToString() : value.Trim();

    private sealed record NativePolicyValidation(
        bool Success,
        IosAppStoreRelease? Release,
        string? MinimumVersion,
        int? MinimumBuildNumber,
        string? ReleaseMessage,
        ApiResponse<NativeUpdatePolicyDto>? Error
    )
    {
        public static NativePolicyValidation Disabled { get; } =
            new(true, null, null, null, null, null);

        public static NativePolicyValidation Ok(
            IosAppStoreRelease release,
            string? minimum,
            int? minimumBuild,
            string? message
        ) => new(true, release, minimum, minimumBuild, message, null);

        public static NativePolicyValidation Fail(string code, string message) =>
            new(
                false,
                null,
                null,
                null,
                null,
                ApiResponse<NativeUpdatePolicyDto>.Error(message, code)
            );
    }

    private sealed record TargetValidation(
        bool Success,
        string TargetScope,
        List<string> StoreGuids,
        string? ErrorCode,
        string? Message
    )
    {
        public static TargetValidation All { get; } =
            new(true, AppUpdateTargetScopes.All, new(), null, null);

        public static TargetValidation Stores(List<string> storeGuids) =>
            new(true, AppUpdateTargetScopes.Stores, storeGuids, null, null);

        public static TargetValidation Fail(string code, string message) =>
            new(false, string.Empty, new(), code, message);
    }
}
