using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services;

public sealed class PosHandheldUpdatePolicyService(
    ISqlSugarClient db,
    IOptions<PosHandheldUpdatePolicyOptions> policyOptions,
    IOptions<EasWebhookOptions> easOptions,
    ILogger<PosHandheldUpdatePolicyService> logger
) : IPosHandheldUpdatePolicyService
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<ApiResponse<List<PosHandheldUpdatePolicyDto>>> GetPoliciesAsync()
    {
        var rows = await db.Queryable<PosHandheldUpdatePolicy>()
            .Where(item => !item.IsDeleted)
            .ToListAsync();
        var byLane = rows.ToDictionary(item => item.Lane, StringComparer.Ordinal);
        var policies = new List<PosHandheldUpdatePolicyDto>(
            PosHandheldUpdateLanes.All.Length
        );
        foreach (var lane in PosHandheldUpdateLanes.All)
        {
            if (!byLane.TryGetValue(lane, out var row))
            {
                policies.Add(MapLegacyPolicy(lane));
                continue;
            }

            if (!row.Enabled)
            {
                policies.Add(MapPolicy(row, candidateValid: true));
                continue;
            }

            var validation = row.CandidateId.HasValue
                ? await ValidateCandidateAsync(lane, row.CandidateId.Value)
                : CandidateValidation.Error(
                    PosHandheldUpdatePolicyErrorCodes.CandidateRequired,
                    "启用策略缺少候选版本"
                );
            var fingerprintMatches = validation.Success
                && string.Equals(
                    validation.Fingerprint,
                    row.CandidateFingerprint,
                    StringComparison.Ordinal
                );
            policies.Add(
                MapPolicy(
                    row,
                    fingerprintMatches,
                    fingerprintMatches
                        ? null
                        : validation.Success
                            ? PosHandheldUpdatePolicyErrorCodes.CandidateFingerprintMismatch
                            : validation.ErrorCode,
                    validation.Candidate
                )
            );
        }

        return ApiResponse<List<PosHandheldUpdatePolicyDto>>.OK(policies);
    }

    public async Task<ApiResponse<List<PosHandheldUpdateCandidateDto>>> GetCandidatesAsync(
        string lane
    )
    {
        var normalizedLane = NormalizeLane(lane);
        if (normalizedLane is null)
        {
            return ApiResponse<List<PosHandheldUpdateCandidateDto>>.Error(
                "手持 POS 更新 lane 无效",
                PosHandheldUpdatePolicyErrorCodes.LaneInvalid
            );
        }

        return ApiResponse<List<PosHandheldUpdateCandidateDto>>.OK(
            await GetCandidateListAsync(normalizedLane)
        );
    }

    public async Task<ApiResponse<PosHandheldUpdatePolicyDto>> SetLaneAsync(
        string lane,
        PosHandheldUpdatePolicyRequest request,
        string currentUser
    )
    {
        var normalizedLane = NormalizeLane(lane);
        if (normalizedLane is null)
        {
            return Error(
                "手持 POS 更新 lane 无效",
                PosHandheldUpdatePolicyErrorCodes.LaneInvalid
            );
        }

        if (!request.ExpectedPolicyVersion.HasValue)
        {
            var current = await FindPolicyAsync(normalizedLane);
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
                "发布说明不能超过 1000 个字符",
                PosHandheldUpdatePolicyErrorCodes.ReleaseMessageInvalid
            );
        }

        var isNative = IsNativeLane(normalizedLane);
        var minimumVersion = request.Enabled
            ? NormalizeOptional(request.MinimumSupportedVersion)
            : null;
        var minimumBuild = request.Enabled
            ? request.MinimumSupportedBuildNumber
            : null;
        if (
            request.Enabled
            && isNative
            && (
                minimumVersion is not null && !IsVersion(minimumVersion)
                || minimumBuild is <= 0
            )
        )
        {
            return Error(
                "原生最低版本或 build 无效",
                PosHandheldUpdatePolicyErrorCodes.NativeMinimumInvalid
            );
        }

        if (
            request.Enabled
            && !isNative
            && (minimumVersion is not null || minimumBuild is not null)
        )
        {
            return Error(
                "OTA 策略不接受原生最低版本或 build",
                PosHandheldUpdatePolicyErrorCodes.OtaMinimumNotAllowed
            );
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            PosHandheldUpdatePolicyDto? saved = null;
            ApiResponse<PosHandheldUpdatePolicyDto>? mutationError = null;
            var transaction = await db.Ado.UseTranAsync(async () =>
            {
                await AppUpdatePolicyMutationLock.AcquireAsync(
                    db,
                    $"app-update-policy:pos-handheld:{normalizedLane}"
                );
                var existing = await FindPolicyAsync(normalizedLane);
                var actualPolicyVersion = existing?.PolicyVersion ?? 0;
                if (request.ExpectedPolicyVersion.Value != actualPolicyVersion)
                {
                    mutationError = VersionError(
                        AppUpdatePolicyErrorCodes.VersionConflict,
                        request.ExpectedPolicyVersion,
                        actualPolicyVersion
                    );
                    return;
                }

                CandidateValidation? candidateValidation = null;
                if (request.Enabled)
                {
                    if (!request.CandidateId.HasValue)
                    {
                        mutationError = Error(
                            "启用策略时必须选择候选版本",
                            PosHandheldUpdatePolicyErrorCodes.CandidateRequired
                        );
                        return;
                    }

                    candidateValidation = await ValidateCandidateAsync(
                        normalizedLane,
                        request.CandidateId.Value
                    );
                    if (!candidateValidation.Success)
                    {
                        mutationError = Error(
                            candidateValidation.Message!,
                            candidateValidation.ErrorCode!
                        );
                        return;
                    }

                    // 最低门槛不能高于目标候选，否则客户端会被判定必须更新，
                    // 但又没有一个足以越过门槛的可安装版本。
                    if (
                        isNative
                        && !CandidateSatisfiesMinimum(
                            candidateValidation.Candidate!,
                            minimumVersion,
                            minimumBuild
                        )
                    )
                    {
                        mutationError = Error(
                            "原生最低版本或 build 不能高于所选候选",
                            PosHandheldUpdatePolicyErrorCodes.NativeMinimumInvalid
                        );
                        return;
                    }
                }

                var enabled = request.Enabled;
                var required = enabled && request.Required;
                var candidateId = enabled ? request.CandidateId : null;
                var candidateFingerprint = enabled
                    ? candidateValidation!.Fingerprint
                    : null;
                var releaseMessage = enabled ? normalizedMessage : null;
                var normalizedMinimumVersion = enabled && isNative
                    ? minimumVersion
                    : null;
                var normalizedMinimumBuild = enabled && isNative
                    ? minimumBuild
                    : null;
                if (
                    existing is not null
                    && IsSamePolicy(
                        existing,
                        enabled,
                        required,
                        candidateId,
                        candidateFingerprint,
                        normalizedMinimumVersion,
                        normalizedMinimumBuild,
                        releaseMessage
                    )
                )
                {
                    saved = MapPolicy(
                        existing,
                        candidateValid: true,
                        candidate: candidateValidation?.Candidate
                    );
                    return;
                }

                var now = DateTime.UtcNow;
                var user = NormalizeOptional(currentUser) ?? "System";
                var entity = existing ?? new PosHandheldUpdatePolicy
                {
                    Id = Guid.NewGuid(),
                    Lane = normalizedLane,
                    CreatedAt = now,
                    CreatedBy = user,
                    IsDeleted = false,
                };
                entity.Enabled = enabled;
                entity.Required = required;
                entity.CandidateId = candidateId;
                entity.CandidateFingerprint = candidateFingerprint;
                entity.MinimumSupportedVersion = normalizedMinimumVersion;
                entity.MinimumSupportedBuildNumber = normalizedMinimumBuild;
                entity.ReleaseMessage = releaseMessage;
                entity.PolicyVersion = actualPolicyVersion + 1;
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

                saved = MapPolicy(
                    entity,
                    candidateValid: true,
                    candidate: candidateValidation?.Candidate
                );
                await db.Insertable(
                    new PosHandheldUpdatePolicyRevision
                    {
                        Id = Guid.NewGuid(),
                        PolicyId = entity.Id,
                        Lane = entity.Lane,
                        PolicyVersion = entity.PolicyVersion,
                        Action = "save",
                        SnapshotJson = JsonSerializer.Serialize(saved, SnapshotJsonOptions),
                        CreatedAt = now,
                        CreatedBy = user,
                        UpdatedAt = null,
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
                return ApiResponse<PosHandheldUpdatePolicyDto>.OK(saved);
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
                    "手持 POS 策略首次并发写入冲突，锁内重读后重试 lane={Lane}",
                    normalizedLane
                );
                continue;
            }

            logger.LogError(
                transaction.ErrorException,
                "手持 POS 策略事务保存失败 lane={Lane}",
                normalizedLane
            );
            return Error(
                "手持 POS 更新策略保存失败",
                "POS_HANDHELD_UPDATE_POLICY_SAVE_FAILED"
            );
        }

        throw new InvalidOperationException("手持 POS 更新策略重试状态无效");
    }

    public async Task<ApiResponse<List<PosHandheldUpdatePolicyRevisionDto>>> GetRevisionsAsync(
        string lane
    )
    {
        var normalizedLane = NormalizeLane(lane);
        if (normalizedLane is null)
        {
            return ApiResponse<List<PosHandheldUpdatePolicyRevisionDto>>.Error(
                "手持 POS 更新 lane 无效",
                PosHandheldUpdatePolicyErrorCodes.LaneInvalid
            );
        }

        var rows = await db.Queryable<PosHandheldUpdatePolicyRevision>()
            .Where(item => item.Lane == normalizedLane && !item.IsDeleted)
            .OrderByDescending(item => item.PolicyVersion)
            .Take(100)
            .ToListAsync();
        var revisions = rows.Select(item => new PosHandheldUpdatePolicyRevisionDto
        {
            Id = item.Id,
            Lane = item.Lane,
            PolicyVersion = item.PolicyVersion,
            Action = item.Action,
            Snapshot =
                JsonSerializer.Deserialize<PosHandheldUpdatePolicyDto>(
                    item.SnapshotJson,
                    SnapshotJsonOptions
                ) ?? new PosHandheldUpdatePolicyDto { Lane = item.Lane },
            CreatedAt = item.CreatedAt,
            CreatedBy = item.CreatedBy,
        }).ToList();
        return ApiResponse<List<PosHandheldUpdatePolicyRevisionDto>>.OK(revisions);
    }

    public async Task<PosHandheldManagedLane?> ResolveManagedLaneAsync(string lane)
    {
        var normalizedLane = NormalizeLane(lane);
        if (normalizedLane is null)
        {
            return null;
        }

        var policy = await FindPolicyAsync(normalizedLane);
        if (policy is null)
        {
            return null;
        }

        if (!policy.Enabled)
        {
            return new PosHandheldManagedLane
            {
                Policy = policy,
                CandidateValid = true,
            };
        }

        if (!policy.CandidateId.HasValue)
        {
            return new PosHandheldManagedLane
            {
                Policy = policy,
                CandidateValid = false,
            };
        }

        var validation = await ValidateCandidateAsync(
            normalizedLane,
            policy.CandidateId.Value
        );
        var fingerprintMatches = validation.Success
            && string.Equals(
                validation.Fingerprint,
                policy.CandidateFingerprint,
                StringComparison.Ordinal
            );
        return new PosHandheldManagedLane
        {
            Policy = policy,
            Candidate = validation.Candidate,
            CandidateValid = fingerprintMatches,
        };
    }

    private async Task<List<PosHandheldUpdateCandidateDto>> GetCandidateListAsync(
        string lane
    ) =>
        lane switch
        {
            PosHandheldUpdateLanes.AndroidNative => await GetAndroidCandidatesAsync(),
            PosHandheldUpdateLanes.IosNative => await GetIosCandidatesAsync(),
            PosHandheldUpdateLanes.AndroidOta => await GetOtaCandidatesAsync(
                PosHandheldUpdateLanes.AndroidOta,
                "android"
            ),
            PosHandheldUpdateLanes.IosOta => await GetOtaCandidatesAsync(
                PosHandheldUpdateLanes.IosOta,
                "ios"
            ),
            _ => [],
        };

    private async Task<PosHandheldUpdateCandidateDto?> GetCandidateByIdAsync(
        string lane,
        Guid candidateId
    ) =>
        lane switch
        {
            PosHandheldUpdateLanes.AndroidNative =>
                await GetAndroidCandidateByIdAsync(candidateId),
            PosHandheldUpdateLanes.IosNative =>
                await GetIosCandidateByIdAsync(candidateId),
            PosHandheldUpdateLanes.AndroidOta =>
                await GetOtaCandidateByIdAsync(lane, "android", candidateId),
            PosHandheldUpdateLanes.IosOta =>
                await GetOtaCandidateByIdAsync(lane, "ios", candidateId),
            _ => null,
        };

    private async Task<PosHandheldUpdateCandidateDto?> GetAndroidCandidateByIdAsync(
        Guid candidateId
    )
    {
        var configuration = policyOptions.Value;
        var projectName = Normalize(configuration.EasProjectName);
        var profile = Normalize(configuration.AndroidProfile);
        if (
            projectName.Length == 0
            || profile.Length == 0
            || !IsConfiguredProject(projectName)
            || !IsAndroidTrustRootValid(configuration)
        )
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var item = await db.Queryable<MobileAppBuild>()
            .FirstAsync(row =>
                row.Id == candidateId
                && !row.IsDeleted
                && row.AppKey == MobileAppKeys.PosHandheld
                && row.ProjectName == projectName
                && row.Platform == "android"
                && row.Status == "finished"
                && row.BuildProfile == profile
                && row.CosMirrorStatus != MobileAppBuildService.CosMirrorStatusUnsafe
                && row.ArtifactSize > 0
                && row.ArtifactSha256 != null
                && (
                    row.CosArtifactUrl != null && row.CosArtifactUrl != ""
                    || row.ArtifactUrl != ""
                        && (row.ExpirationDate == null || row.ExpirationDate > now)
                )
            );
        return item is null ? null : MapAndroidCandidate(item, configuration);
    }

    private async Task<PosHandheldUpdateCandidateDto?> GetIosCandidateByIdAsync(
        Guid candidateId
    )
    {
        var expectedBundle = Normalize(policyOptions.Value.IosBundleIdentifier);
        if (
            !string.Equals(
                expectedBundle,
                PosHandheldIosUpdateIdentity.BundleIdentifier,
                StringComparison.Ordinal
            )
        )
        {
            return null;
        }

        var item = await db.Queryable<IosAppStoreRelease>()
            .FirstAsync(row =>
                row.Id == candidateId
                && !row.IsDeleted
                && row.App == AppUpdateApps.PosHandheld
                && row.BundleIdentifier == expectedBundle
            );
        return item is null ? null : MapIosCandidate(item);
    }

    private async Task<PosHandheldUpdateCandidateDto?> GetOtaCandidateByIdAsync(
        string lane,
        string platform,
        Guid candidateId
    )
    {
        var projectName = Normalize(policyOptions.Value.EasProjectName);
        var channel = Normalize(policyOptions.Value.OtaChannel).ToLowerInvariant();
        if (
            projectName.Length == 0
            || channel.Length == 0
            || !IsConfiguredProject(projectName)
        )
        {
            return null;
        }

        var item = await db.Queryable<MobileAppOtaUpdate>()
            .FirstAsync(row =>
                row.Id == candidateId
                && !row.IsDeleted
                && row.AppKey == MobileAppKeys.PosHandheld
                && row.ProjectName == projectName
                && row.Platform == platform
                && row.Channel == channel
            );
        if (item is null || !IsValidOtaIdentity(item))
        {
            return null;
        }

        // 设备决策只读取绑定主键，再用现有复合索引确认同 runtime 的当前头部。
        // 若最新登记本身不完整则整体 fail closed，不回退到更旧 OTA。
        var head = await db.Queryable<MobileAppOtaUpdate>()
            .Where(row =>
                !row.IsDeleted
                && row.AppKey == MobileAppKeys.PosHandheld
                && row.ProjectName == projectName
                && row.Platform == platform
                && row.Channel == channel
                && row.RuntimeVersion == item.RuntimeVersion
            )
            .OrderByDescending(row => row.PublishedAt)
            .OrderByDescending(row => row.CreatedAt)
            .FirstAsync();
        var isCurrentHead = head is not null
            && IsValidOtaIdentity(head)
            && head.Id == item.Id;
        return MapOtaCandidate(item, lane, isCurrentHead);
    }

    private async Task<List<PosHandheldUpdateCandidateDto>> GetAndroidCandidatesAsync()
    {
        var configuration = policyOptions.Value;
        var projectName = Normalize(configuration.EasProjectName);
        var profile = Normalize(configuration.AndroidProfile);
        if (
            projectName.Length == 0
            || profile.Length == 0
            || !IsConfiguredProject(projectName)
            || !IsAndroidTrustRootValid(configuration)
        )
        {
            return [];
        }

        var now = DateTime.UtcNow;
        var rows = await db.Queryable<MobileAppBuild>()
            .Where(item =>
                !item.IsDeleted
                && item.AppKey == MobileAppKeys.PosHandheld
                && item.ProjectName == projectName
                && item.Platform == "android"
                && item.Status == "finished"
                && item.BuildProfile == profile
                && item.CosMirrorStatus != MobileAppBuildService.CosMirrorStatusUnsafe
                && item.ArtifactSize > 0
                && item.ArtifactSha256 != null
                && (
                    item.CosArtifactUrl != null
                        && item.CosArtifactUrl != ""
                    || item.ArtifactUrl != ""
                        && (item.ExpirationDate == null || item.ExpirationDate > now)
                )
            )
            .OrderByDescending(item => item.CompletedAt)
            .OrderByDescending(item => item.ReceivedAt)
            .Take(200)
            .ToListAsync();

        var candidates = rows
            .Select(item => MapAndroidCandidate(item, configuration))
            .Where(item => item is not null)
            .Cast<PosHandheldUpdateCandidateDto>()
            .ToList();
        if (candidates.Count > 0)
        {
            candidates[0].IsCurrentHead = true;
        }

        return candidates;
    }

    private async Task<List<PosHandheldUpdateCandidateDto>> GetIosCandidatesAsync()
    {
        var expectedBundle = Normalize(policyOptions.Value.IosBundleIdentifier);
        if (
            !string.Equals(
                expectedBundle,
                PosHandheldIosUpdateIdentity.BundleIdentifier,
                StringComparison.Ordinal
            )
        )
        {
            return [];
        }

        var rows = await db.Queryable<IosAppStoreRelease>()
            .Where(item =>
                !item.IsDeleted
                && item.App == AppUpdateApps.PosHandheld
                && item.BundleIdentifier == expectedBundle
            )
            .OrderByDescending(item => item.AppleVerifiedAtUtc)
            .Take(200)
            .ToListAsync();
        return rows
            .Select(MapIosCandidate)
            .Where(item => item is not null)
            .Cast<PosHandheldUpdateCandidateDto>()
            .ToList();
    }

    private async Task<List<PosHandheldUpdateCandidateDto>> GetOtaCandidatesAsync(
        string lane,
        string platform
    )
    {
        var projectName = Normalize(policyOptions.Value.EasProjectName);
        var channel = Normalize(policyOptions.Value.OtaChannel).ToLowerInvariant();
        if (
            projectName.Length == 0
            || channel.Length == 0
            || !IsConfiguredProject(projectName)
        )
        {
            return [];
        }

        var rows = await db.Queryable<MobileAppOtaUpdate>()
            .Where(item =>
                !item.IsDeleted
                && item.AppKey == MobileAppKeys.PosHandheld
                && item.ProjectName == projectName
                && item.Platform == platform
                && item.Channel == channel
            )
            .OrderByDescending(item => item.PublishedAt)
            .OrderByDescending(item => item.CreatedAt)
            .Take(500)
            .ToListAsync();
        var headIds = rows
            .Where(IsValidOtaIdentity)
            .GroupBy(item => item.RuntimeVersion!, StringComparer.Ordinal)
            .Select(group => group.First().Id)
            .ToHashSet();
        return rows
            .Select(item => MapOtaCandidate(item, lane, headIds.Contains(item.Id)))
            .Where(item => item is not null)
            .Cast<PosHandheldUpdateCandidateDto>()
            .ToList();
    }

    private async Task<CandidateValidation> ValidateCandidateAsync(
        string lane,
        Guid candidateId
    )
    {
        var candidate = await GetCandidateByIdAsync(lane, candidateId);
        if (candidate is null)
        {
            return CandidateValidation.Error(
                PosHandheldUpdatePolicyErrorCodes.CandidateInvalid,
                "候选版本不存在、已失效或不属于手持 POS 的目标 lane"
            );
        }

        if (
            lane is PosHandheldUpdateLanes.AndroidOta or PosHandheldUpdateLanes.IosOta
            && !candidate.IsCurrentHead
        )
        {
            return CandidateValidation.Error(
                PosHandheldUpdatePolicyErrorCodes.OtaCandidateNotChannelHead,
                "只能激活相同平台、channel 与 runtimeVersion 下最新登记的 OTA"
            );
        }

        return CandidateValidation.Ok(candidate, Fingerprint(candidate));
    }

    private PosHandheldUpdateCandidateDto? MapAndroidCandidate(
        MobileAppBuild item,
        PosHandheldUpdatePolicyOptions configuration
    )
    {
        var artifactUrl = NormalizeOptional(item.CosArtifactUrl)
            ?? NormalizeOptional(item.ArtifactUrl);
        var sha256 = NormalizeFingerprint(item.ArtifactSha256);
        if (
            !IsVersion(item.AppVersion)
            || !IsBuild(item.AppBuildVersion)
            || !IsTrustedHttpsUrl(artifactUrl)
            || item.ArtifactSize is not > 0
            || sha256 is null
        )
        {
            return null;
        }

        return new PosHandheldUpdateCandidateDto
        {
            Id = item.Id,
            Lane = PosHandheldUpdateLanes.AndroidNative,
            Kind = "native",
            Platform = "Android",
            ProjectName = item.ProjectName,
            Profile = item.BuildProfile,
            Version = Normalize(item.AppVersion),
            BuildNumber = Normalize(item.AppBuildVersion),
            RuntimeVersion = NormalizeOptional(item.RuntimeVersion),
            Channel = NormalizeOptional(item.Channel),
            ArtifactUrl = artifactUrl,
            FileSize = item.ArtifactSize,
            Sha256 = sha256,
            Distribution = "apk",
            PackageName = Normalize(configuration.AndroidPackageName),
            SigningCertificateSha256 = NormalizeFingerprint(
                configuration.AndroidSigningCertificateSha256
            ),
            PublishedAtUtc = item.CompletedAt ?? item.ReceivedAt,
            Activatable = true,
        };
    }

    private static PosHandheldUpdateCandidateDto? MapIosCandidate(
        IosAppStoreRelease item
    )
    {
        if (
            !IsVersion(item.Version)
            || !IsBuild(item.BuildNumber)
            || !PosHandheldIosUpdateIdentity.IsValidAppStoreId(item.AppStoreId)
            || !PosHandheldIosUpdateIdentity.IsValidDistributionUrl(
                item.AppStoreUrl,
                "app-store",
                item.AppStoreId
            )
        )
        {
            return null;
        }

        return new PosHandheldUpdateCandidateDto
        {
            Id = item.Id,
            Lane = PosHandheldUpdateLanes.IosNative,
            Kind = "native",
            Platform = "iOS",
            Version = Normalize(item.Version),
            BuildNumber = Normalize(item.BuildNumber),
            ArtifactUrl = Normalize(item.AppStoreUrl),
            Distribution = "app-store",
            AppStoreId = Normalize(item.AppStoreId),
            BundleIdentifier = Normalize(item.BundleIdentifier),
            PublishedAtUtc = item.AppleVerifiedAtUtc,
            IsCurrentHead = true,
            Activatable = true,
        };
    }

    private static PosHandheldUpdateCandidateDto? MapOtaCandidate(
        MobileAppOtaUpdate item,
        string lane,
        bool isCurrentHead
    )
    {
        if (!IsValidOtaIdentity(item))
        {
            return null;
        }

        return new PosHandheldUpdateCandidateDto
        {
            Id = item.Id,
            Lane = lane,
            Kind = "ota",
            Platform = item.Platform == "ios" ? "iOS" : "Android",
            ProjectName = Normalize(item.ProjectName),
            RuntimeVersion = Normalize(item.RuntimeVersion),
            Channel = Normalize(item.Channel),
            UpdateId = Normalize(item.UpdateId ?? item.AndroidUpdateId),
            UpdateGroupId = Normalize(item.UpdateGroupId),
            ReleaseMessage = NormalizeOptional(item.Message),
            PublishedAtUtc = item.PublishedAt,
            IsCurrentHead = isCurrentHead,
            Activatable = isCurrentHead,
            BlockedReason = isCurrentHead
                ? null
                : PosHandheldUpdatePolicyErrorCodes.OtaCandidateNotChannelHead,
        };
    }

    private static bool IsValidOtaIdentity(MobileAppOtaUpdate item) =>
        Normalize(item.ProjectName).Length > 0
        && Normalize(item.Channel).Length > 0
        && Normalize(item.RuntimeVersion).Length > 0
        && Normalize(item.UpdateId ?? item.AndroidUpdateId).Length > 0
        && Guid.TryParse(Normalize(item.UpdateGroupId), out _);

    private bool IsConfiguredProject(string projectName) =>
        easOptions.Value.ProjectAppKeys.Any(mapping =>
            string.Equals(
                Normalize(mapping.Key),
                projectName,
                StringComparison.OrdinalIgnoreCase
            )
            && MobileAppKeys.TryNormalize(mapping.Value, out var appKey)
            && appKey == MobileAppKeys.PosHandheld
        );

    private static bool IsAndroidTrustRootValid(
        PosHandheldUpdatePolicyOptions configuration
    ) =>
        string.Equals(
            Normalize(configuration.AndroidProfile),
            "android-internal",
            StringComparison.Ordinal
        )
        && string.Equals(
            Normalize(configuration.AndroidPackageName),
            "com.hbweb.poshandheld",
            StringComparison.Ordinal
        )
        && NormalizeFingerprint(configuration.AndroidSigningCertificateSha256) is not null;

    private async Task<PosHandheldUpdatePolicy?> FindPolicyAsync(string lane) =>
        await db.Queryable<PosHandheldUpdatePolicy>()
            .FirstAsync(item => item.Lane == lane && !item.IsDeleted);

    private PosHandheldUpdatePolicyDto MapLegacyPolicy(string lane)
    {
        var configuration = policyOptions.Value;
        return new PosHandheldUpdatePolicyDto
        {
            Lane = lane,
            Managed = false,
            Source = "legacy",
            Enabled = configuration.Enabled,
            Required = lane switch
            {
                PosHandheldUpdateLanes.AndroidNative => configuration.AndroidRequired,
                PosHandheldUpdateLanes.IosNative => configuration.IosRequired,
                _ => configuration.OtaRequired,
            },
            PolicyVersion = 0,
            MinimumSupportedVersion = lane switch
            {
                PosHandheldUpdateLanes.AndroidNative =>
                    NormalizeOptional(configuration.AndroidMinimumSupportedVersion),
                PosHandheldUpdateLanes.IosNative =>
                    NormalizeOptional(configuration.IosMinimumSupportedVersion),
                _ => null,
            },
            MinimumSupportedBuildNumber = lane switch
            {
                PosHandheldUpdateLanes.AndroidNative =>
                    configuration.AndroidMinimumSupportedBuild,
                PosHandheldUpdateLanes.IosNative =>
                    configuration.IosMinimumSupportedBuild,
                _ => null,
            },
            ReleaseMessage = NormalizeOptional(configuration.ReleaseMessage),
        };
    }

    private static PosHandheldUpdatePolicyDto MapPolicy(
        PosHandheldUpdatePolicy item,
        bool candidateValid,
        string? blockedReason = null,
        PosHandheldUpdateCandidateDto? candidate = null
    ) =>
        new()
        {
            Id = item.Id,
            Lane = item.Lane,
            Managed = true,
            Source = "database",
            Enabled = item.Enabled,
            Required = item.Required,
            PolicyVersion = item.PolicyVersion,
            CandidateId = item.CandidateId,
            CandidateValid = candidateValid,
            BlockedReason = blockedReason,
            Candidate = candidate,
            MinimumSupportedVersion = item.MinimumSupportedVersion,
            MinimumSupportedBuildNumber = item.MinimumSupportedBuildNumber,
            ReleaseMessage = item.ReleaseMessage,
            UpdatedAt = item.UpdatedAt,
            UpdatedBy = item.UpdatedBy,
        };

    private static bool IsSamePolicy(
        PosHandheldUpdatePolicy existing,
        bool enabled,
        bool required,
        Guid? candidateId,
        string? candidateFingerprint,
        string? minimumVersion,
        int? minimumBuild,
        string? releaseMessage
    ) =>
        existing.Enabled == enabled
        && existing.Required == required
        && existing.CandidateId == candidateId
        && string.Equals(
            existing.CandidateFingerprint,
            candidateFingerprint,
            StringComparison.Ordinal
        )
        && string.Equals(
            existing.MinimumSupportedVersion,
            minimumVersion,
            StringComparison.Ordinal
        )
        && existing.MinimumSupportedBuildNumber == minimumBuild
        && string.Equals(
            existing.ReleaseMessage,
            releaseMessage,
            StringComparison.Ordinal
        );

    private static string Fingerprint(PosHandheldUpdateCandidateDto candidate)
    {
        var identity = string.Join(
            "\n",
            candidate.Id.ToString("D"),
            candidate.Lane,
            candidate.Kind,
            candidate.Platform,
            candidate.ProjectName,
            candidate.Profile,
            candidate.Version,
            candidate.BuildNumber,
            candidate.RuntimeVersion,
            candidate.Channel,
            candidate.UpdateId,
            candidate.UpdateGroupId,
            candidate.ArtifactUrl,
            candidate.FileSize?.ToString(CultureInfo.InvariantCulture),
            candidate.Sha256,
            candidate.Distribution,
            candidate.PackageName,
            candidate.SigningCertificateSha256,
            candidate.AppStoreId,
            candidate.BundleIdentifier
        );
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity))
        ).ToLowerInvariant();
    }

    private static string? NormalizeLane(string? lane)
    {
        var normalized = Normalize(lane).ToLowerInvariant();
        return PosHandheldUpdateLanes.All.Contains(normalized, StringComparer.Ordinal)
            ? normalized
            : null;
    }

    private static bool IsNativeLane(string lane) =>
        lane
            is PosHandheldUpdateLanes.AndroidNative
                or PosHandheldUpdateLanes.IosNative;

    private static bool IsTrustedHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo);

    private static bool IsVersion(string? value) =>
        Version.TryParse(Normalize(value).TrimStart('v', 'V'), out _);

    private static bool IsBuild(string? value) =>
        PosHandheldIosUpdateIdentity.IsValidBuildNumber(value);

    private static bool CandidateSatisfiesMinimum(
        PosHandheldUpdateCandidateDto candidate,
        string? minimumVersion,
        int? minimumBuild
    )
    {
        if (
            minimumVersion is not null
            && (
                !Version.TryParse(
                    Normalize(candidate.Version).TrimStart('v', 'V'),
                    out var candidateVersion
                )
                || !Version.TryParse(
                    minimumVersion.TrimStart('v', 'V'),
                    out var parsedMinimumVersion
                )
                || candidateVersion < parsedMinimumVersion
            )
        )
        {
            return false;
        }

        return minimumBuild is not > 0
            || long.TryParse(candidate.BuildNumber, out var candidateBuild)
                && candidateBuild >= minimumBuild.Value;
    }

    private static string? NormalizeFingerprint(string? value)
    {
        var normalized = Normalize(value).Replace(":", string.Empty).ToLowerInvariant();
        return normalized.Length == 64 && normalized.All(char.IsAsciiHexDigit)
            ? normalized
            : null;
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();

    private static string? NormalizeOptional(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private static ApiResponse<PosHandheldUpdatePolicyDto> Error(
        string message,
        string code
    ) => ApiResponse<PosHandheldUpdatePolicyDto>.Error(message, code);

    private static ApiResponse<PosHandheldUpdatePolicyDto> VersionError(
        string code,
        long? expected,
        long actual
    ) =>
        ApiResponse<PosHandheldUpdatePolicyDto>.Error(
            code == AppUpdatePolicyErrorCodes.VersionRequired
                ? "expectedPolicyVersion 不能为空"
                : "策略版本已变化，请刷新后重试",
            code,
            new
            {
                ExpectedPolicyVersion = expected,
                ActualPolicyVersion = actual,
            }
        );

    private sealed record CandidateValidation(
        bool Success,
        PosHandheldUpdateCandidateDto? Candidate,
        string? Fingerprint,
        string? ErrorCode,
        string? Message
    )
    {
        public static CandidateValidation Ok(
            PosHandheldUpdateCandidateDto candidate,
            string fingerprint
        ) => new(true, candidate, fingerprint, null, null);

        public static CandidateValidation Error(string code, string message) =>
            new(false, null, null, code, message);
    }
}
