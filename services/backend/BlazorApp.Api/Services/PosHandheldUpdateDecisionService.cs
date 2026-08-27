using System.Globalization;
using System.Text.RegularExpressions;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.Options;

namespace BlazorApp.Api.Services;

public sealed partial class PosHandheldUpdateDecisionService(
    IMobileAppBuildService mobileAppBuildService,
    IOptions<PosHandheldUpdatePolicyOptions> policyOptions,
    IOptions<EasWebhookOptions> easOptions,
    ILogger<PosHandheldUpdateDecisionService> logger,
    IPosHandheldUpdatePolicyService? managedPolicyService = null
) : IPosHandheldUpdateDecisionService
{
    private const long JavaScriptSafeIntegerMax = 9007199254740991;

    public async Task<PosHandheldNativeDecisionDto?> GetNativeDecisionAsync(
        PosHandheldNativeDecisionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var platform = NormalizePlatform(request.Platform);
        // handheld 请求的平台字段是安全边界；无效原始值不得在禁用策略下退化为 none 决策。
        if (platform.Length == 0)
        {
            return null;
        }

        if (!IsCanonicalCurrentBuild(request.Build))
        {
            // 当前客户端 build 无法安全比较时必须无决策，不能将其视为低版本而下发更新。
            logger.LogWarning(
                "pos-handheld native update current build is invalid platform={Platform}",
                platform
            );
            return null;
        }

        var managedLane = managedPolicyService is null
            ? null
            : await managedPolicyService.ResolveManagedLaneAsync(
                platform == "Android"
                    ? PosHandheldUpdateLanes.AndroidNative
                    : PosHandheldUpdateLanes.IosNative
            );
        if (managedLane is not null)
        {
            if (!managedLane.Policy.Enabled)
            {
                return NoNativeDecision(platform);
            }

            if (!CanEvaluate(request.StoreCode, platform, out var managedProjectName))
            {
                logger.LogWarning(
                    "pos-handheld managed native update scope or EAS mapping is incomplete platform={Platform}",
                    platform
                );
                return null;
            }

            return GetManagedNativeDecision(
                request,
                platform,
                managedProjectName,
                managedLane
            );
        }

        if (!policyOptions.Value.Enabled)
        {
            return NoNativeDecision(platform);
        }

        if (!CanEvaluate(request.StoreCode, platform, out var projectName))
        {
            logger.LogWarning(
                "pos-handheld native update scope or EAS mapping is incomplete platform={Platform}",
                platform
            );
            return null;
        }

        return platform switch
        {
            "Android" => await GetAndroidDecisionAsync(
                request,
                projectName,
                cancellationToken
            ),
            "iOS" => GetIosDecision(request),
            _ => null,
        };
    }

    public async Task<PosHandheldOtaDecisionDto?> GetOtaDecisionAsync(
        PosHandheldOtaDecisionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var platform = NormalizePlatform(request.Platform);
        // native 与 OTA 使用同一精确平台合同，避免大小写或空白变体绕过请求校验。
        if (platform.Length == 0)
        {
            return null;
        }

        var runtimeVersion = Normalize(request.RuntimeVersion);
        var channel = Normalize(policyOptions.Value.OtaChannel).ToLowerInvariant();
        var managedLane = managedPolicyService is null
            ? null
            : await managedPolicyService.ResolveManagedLaneAsync(
                platform == "Android"
                    ? PosHandheldUpdateLanes.AndroidOta
                    : PosHandheldUpdateLanes.IosOta
            );
        if (managedLane is not null)
        {
            if (!managedLane.Policy.Enabled)
            {
                return NoOtaDecision(platform, null, channel, runtimeVersion);
            }

            if (
                !CanEvaluate(request.StoreCode, platform, out var managedProjectName)
                || runtimeVersion.Length == 0
                || channel.Length == 0
            )
            {
                logger.LogWarning(
                    "pos-handheld managed OTA scope, runtime, channel, or EAS mapping is incomplete platform={Platform}",
                    platform
                );
                return null;
            }

            return GetManagedOtaDecision(
                request,
                platform,
                managedProjectName,
                channel,
                runtimeVersion,
                managedLane
            );
        }

        if (!policyOptions.Value.Enabled)
        {
            return NoOtaDecision(platform, null, channel, runtimeVersion);
        }

        if (!CanEvaluate(request.StoreCode, platform, out var projectName)
            || runtimeVersion.Length == 0
            || channel.Length == 0)
        {
            logger.LogWarning(
                "pos-handheld OTA update scope, runtime, channel, or EAS mapping is incomplete platform={Platform}",
                platform
            );
            return null;
        }

        var updates = await mobileAppBuildService.GetOtaUpdatesAsync(
            new MobileAppOtaUpdateQueryDto
            {
                AppKey = MobileAppKeys.PosHandheld,
                ProjectName = projectName,
                Platform = platform.ToLowerInvariant(),
                Channel = channel,
                RuntimeVersion = runtimeVersion,
                Page = 1,
                PageSize = 1,
            }
        );
        if (!updates.Success || updates.Data?.Items is null)
        {
            logger.LogWarning(
                "pos-handheld OTA update query failed project={ProjectName} platform={Platform}",
                projectName,
                platform
            );
            return null;
        }

        var update = updates.Data.Items.FirstOrDefault();
        if (update is null)
        {
            // 强制 OTA 没有可用目标时不能降级为 none，否则客户端会继续运行过期版本。
            if (policyOptions.Value.OtaRequired)
            {
                logger.LogWarning(
                    "pos-handheld required OTA update is missing project={ProjectName} platform={Platform}",
                    projectName,
                    platform
                );
                return null;
            }

            return NoOtaDecision(platform, projectName, channel, runtimeVersion);
        }

        if (string.IsNullOrWhiteSpace(update.UpdateId)
            || !Guid.TryParse(update.UpdateGroupId, out _))
        {
            logger.LogWarning(
                "pos-handheld OTA update metadata is incomplete project={ProjectName} platform={Platform}",
                projectName,
                platform
            );
            return null;
        }

        var alreadyCurrent = string.Equals(
                Normalize(request.CurrentUpdateId),
                update.UpdateId,
                StringComparison.Ordinal
            )
            || string.Equals(
                Normalize(request.CurrentUpdateGroupId),
                update.UpdateGroupId,
                StringComparison.OrdinalIgnoreCase
            );
        if (alreadyCurrent)
        {
            return NoOtaDecision(platform, projectName, channel, runtimeVersion);
        }

        var required = policyOptions.Value.OtaRequired;
        return new PosHandheldOtaDecisionDto
        {
            State = required ? AppUpdateStates.Required : AppUpdateStates.Optional,
            PolicyVersion = NormalizePolicyVersion(),
            AppKey = MobileAppKeys.PosHandheld,
            ProjectName = projectName,
            Platform = platform,
            Required = required,
            Channel = update.Channel,
            RuntimeVersion = update.RuntimeVersion,
            UpdateId = update.UpdateId,
            UpdateGroupId = update.UpdateGroupId,
            ReleaseMessage = NormalizeOptional(policyOptions.Value.ReleaseMessage),
        };
    }

    private PosHandheldNativeDecisionDto? GetManagedNativeDecision(
        PosHandheldNativeDecisionRequest request,
        string platform,
        string projectName,
        PosHandheldManagedLane managedLane
    )
    {
        var policy = managedLane.Policy;
        var belowMinimum = IsBelowMinimum(
            request.Version,
            request.Build,
            policy.MinimumSupportedVersion,
            policy.MinimumSupportedBuildNumber
        );
        var required = policy.Required || belowMinimum;
        if (!managedLane.CandidateValid || managedLane.Candidate is null)
        {
            logger.LogWarning(
                "pos-handheld managed native candidate is missing, stale, or unsafe lane={Lane} policyVersion={PolicyVersion}",
                policy.Lane,
                policy.PolicyVersion
            );
            return required
                ? null
                : NoNativeDecision(platform);
        }

        var candidate = managedLane.Candidate;
        if (
            !string.Equals(candidate.Platform, platform, StringComparison.Ordinal)
            || !IsVersion(candidate.Version)
            || !IsBuild(candidate.BuildNumber)
        )
        {
            return required ? null : NoNativeDecision(platform);
        }

        if (
            !IsUpdateAvailable(
                request.Version,
                request.Build,
                candidate.Version,
                candidate.BuildNumber
            )
        )
        {
            if (belowMinimum)
            {
                logger.LogWarning(
                    "pos-handheld managed native minimum cannot be satisfied by the pinned candidate lane={Lane} policyVersion={PolicyVersion}",
                    policy.Lane,
                    policy.PolicyVersion
                );
                return null;
            }

            return NoNativeDecision(platform);
        }

        if (platform == "Android")
        {
            var packageName = Normalize(policyOptions.Value.AndroidPackageName);
            var signingFingerprint = NormalizeFingerprint(
                policyOptions.Value.AndroidSigningCertificateSha256
            );
            var sha256 = NormalizeFingerprint(candidate.Sha256);
            if (
                !string.Equals(
                    candidate.ProjectName,
                    projectName,
                    StringComparison.OrdinalIgnoreCase
                )
                || !string.Equals(
                    candidate.Profile,
                    policyOptions.Value.AndroidProfile,
                    StringComparison.Ordinal
                )
                || !IsTrustedHttpsUrl(candidate.ArtifactUrl)
                || candidate.FileSize is not > 0
                || sha256 is null
                || signingFingerprint is null
                || !string.Equals(
                    candidate.PackageName,
                    packageName,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    NormalizeFingerprint(candidate.SigningCertificateSha256),
                    signingFingerprint,
                    StringComparison.Ordinal
                )
                || !AndroidPackagePattern().IsMatch(packageName)
            )
            {
                logger.LogWarning(
                    "pos-handheld managed Android candidate identity is incomplete policyVersion={PolicyVersion}",
                    policy.PolicyVersion
                );
                return required
                    ? null
                    : NoNativeDecision(platform);
            }

            return new PosHandheldNativeDecisionDto
            {
                State = required ? AppUpdateStates.Required : AppUpdateStates.Optional,
                PolicyVersion = policy.PolicyVersion.ToString(
                    CultureInfo.InvariantCulture
                ),
                Platform = "Android",
                Required = required,
                LatestVersion = candidate.Version,
                LatestBuild = candidate.BuildNumber,
                MinimumSupportedVersion = policy.MinimumSupportedVersion,
                Distribution = "apk",
                DownloadUrl = candidate.ArtifactUrl,
                FileSize = candidate.FileSize,
                Sha256 = sha256,
                PackageName = packageName,
                SigningCertificateSha256 = signingFingerprint,
                ReleaseMessage = policy.ReleaseMessage,
            };
        }

        var appStoreId = Normalize(candidate.AppStoreId);
        var bundleIdentifier = Normalize(candidate.BundleIdentifier);
        var downloadUrl = Normalize(candidate.ArtifactUrl);
        if (
            candidate.Distribution != "app-store"
            || !string.Equals(
                bundleIdentifier,
                PosHandheldIosUpdateIdentity.BundleIdentifier,
                StringComparison.Ordinal
            )
            || !PosHandheldIosUpdateIdentity.IsValidAppStoreId(appStoreId)
            || !PosHandheldIosUpdateIdentity.IsValidDistributionUrl(
                downloadUrl,
                "app-store",
                appStoreId
            )
        )
        {
            logger.LogWarning(
                "pos-handheld managed iOS candidate identity is incomplete policyVersion={PolicyVersion}",
                policy.PolicyVersion
            );
            return required
                ? null
                : NoNativeDecision(platform);
        }

        return new PosHandheldNativeDecisionDto
        {
            State = required ? AppUpdateStates.Required : AppUpdateStates.Optional,
            PolicyVersion = policy.PolicyVersion.ToString(CultureInfo.InvariantCulture),
            Platform = "iOS",
            Required = required,
            LatestVersion = candidate.Version,
            LatestBuild = candidate.BuildNumber,
            MinimumSupportedVersion = policy.MinimumSupportedVersion,
            Distribution = "app-store",
            DownloadUrl = downloadUrl,
            BundleIdentifier = bundleIdentifier,
            AppStoreId = appStoreId,
            ReleaseMessage = policy.ReleaseMessage,
        };
    }

    private PosHandheldOtaDecisionDto? GetManagedOtaDecision(
        PosHandheldOtaDecisionRequest request,
        string platform,
        string projectName,
        string channel,
        string runtimeVersion,
        PosHandheldManagedLane managedLane
    )
    {
        var policy = managedLane.Policy;
        if (!managedLane.CandidateValid || managedLane.Candidate is null)
        {
            logger.LogWarning(
                "pos-handheld managed OTA candidate is missing, stale, or no longer channel head lane={Lane} policyVersion={PolicyVersion}",
                policy.Lane,
                policy.PolicyVersion
            );
            return policy.Required
                ? null
                : NoOtaDecision(platform, projectName, channel, runtimeVersion);
        }

        var candidate = managedLane.Candidate;
        if (
            !string.Equals(candidate.Platform, platform, StringComparison.Ordinal)
            || !string.Equals(
                candidate.ProjectName,
                projectName,
                StringComparison.Ordinal
            )
            || !IsAllowedManagedOtaChannel(candidate.Channel, platform, channel)
            || !string.Equals(
                candidate.RuntimeVersion,
                runtimeVersion,
                StringComparison.Ordinal
            )
            || string.IsNullOrWhiteSpace(candidate.UpdateId)
            || !Guid.TryParse(candidate.UpdateGroupId, out _)
        )
        {
            logger.LogWarning(
                "pos-handheld managed OTA request does not match pinned candidate lane={Lane} policyVersion={PolicyVersion}",
                policy.Lane,
                policy.PolicyVersion
            );
            return policy.Required
                ? null
                : NoOtaDecision(platform, projectName, channel, runtimeVersion);
        }

        var alreadyCurrent = string.Equals(
                Normalize(request.CurrentUpdateId),
                candidate.UpdateId,
                StringComparison.Ordinal
            )
            || string.Equals(
                Normalize(request.CurrentUpdateGroupId),
                candidate.UpdateGroupId,
                StringComparison.OrdinalIgnoreCase
            );
        if (alreadyCurrent)
        {
            return NoOtaDecision(platform, projectName, channel, runtimeVersion);
        }

        return new PosHandheldOtaDecisionDto
        {
            State = policy.Required
                ? AppUpdateStates.Required
                : AppUpdateStates.Optional,
            PolicyVersion = policy.PolicyVersion.ToString(CultureInfo.InvariantCulture),
            AppKey = MobileAppKeys.PosHandheld,
            ProjectName = projectName,
            Platform = platform,
            Required = policy.Required,
            Channel = candidate.Channel,
            RuntimeVersion = candidate.RuntimeVersion,
            UpdateId = candidate.UpdateId,
            UpdateGroupId = candidate.UpdateGroupId,
            ReleaseMessage = policy.ReleaseMessage,
        };
    }

    private static bool IsAllowedManagedOtaChannel(
        string? candidateChannel,
        string platform,
        string legacyChannel
    )
    {
        var normalizedCandidate = Normalize(candidateChannel).ToLowerInvariant();
        var normalizedLegacy = Normalize(legacyChannel).ToLowerInvariant();
        if (string.Equals(normalizedCandidate, normalizedLegacy, StringComparison.Ordinal))
        {
            return true;
        }

        var platformSegment = platform == "iOS" ? "ios" : "android";
        var trustedPrefix = $"{normalizedLegacy}-{platformSegment}-release-";
        return normalizedCandidate.StartsWith(trustedPrefix, StringComparison.Ordinal)
            && normalizedCandidate.Length > trustedPrefix.Length;
    }

    private async Task<PosHandheldNativeDecisionDto?> GetAndroidDecisionAsync(
        PosHandheldNativeDecisionRequest request,
        string projectName,
        CancellationToken cancellationToken
    )
    {
        var configuration = policyOptions.Value;
        var latestResponse = await mobileAppBuildService.GetLatestAsync(
            MobileAppKeys.PosHandheld,
            configuration.AndroidProfile
        );
        if (!latestResponse.Success)
        {
            logger.LogWarning(
                "pos-handheld Android update lookup failed project={ProjectName}",
                projectName
            );
            return null;
        }

        var latest = latestResponse.Data;
        var packageName = Normalize(configuration.AndroidPackageName);
        var signingFingerprint = NormalizeFingerprint(
            configuration.AndroidSigningCertificateSha256
        );
        var sha256 = NormalizeFingerprint(latest?.ArtifactSha256);
        if (latest is null
            || !string.Equals(latest.ProjectName, projectName, StringComparison.OrdinalIgnoreCase)
            || !IsVersion(latest.AppVersion)
            || !IsBuild(latest.AppBuildVersion)
            || !IsTrustedHttpsUrl(latest.ArtifactUrl)
            || latest.ArtifactSize is not > 0
            || sha256 is null
            || signingFingerprint is null
            || !AndroidPackagePattern().IsMatch(packageName))
        {
            logger.LogWarning(
                "pos-handheld Android update metadata incomplete project={ProjectName}",
                projectName
            );
            return null;
        }

        var updateAvailable = IsUpdateAvailable(
            request.Version,
            request.Build,
            latest.AppVersion,
            latest.AppBuildVersion
        );
        if (!updateAvailable)
        {
            return NoNativeDecision("Android");
        }

        var required = configuration.AndroidRequired
            || IsBelowMinimum(
                request.Version,
                request.Build,
                configuration.AndroidMinimumSupportedVersion,
                configuration.AndroidMinimumSupportedBuild
            );
        return new PosHandheldNativeDecisionDto
        {
            State = required ? AppUpdateStates.Required : AppUpdateStates.Optional,
            PolicyVersion = NormalizePolicyVersion(),
            Platform = "Android",
            Required = required,
            LatestVersion = latest.AppVersion,
            LatestBuild = latest.AppBuildVersion,
            MinimumSupportedVersion = NormalizeOptional(
                configuration.AndroidMinimumSupportedVersion
            ),
            Distribution = "apk",
            DownloadUrl = latest.ArtifactUrl,
            FileSize = latest.ArtifactSize,
            Sha256 = sha256,
            PackageName = packageName,
            SigningCertificateSha256 = signingFingerprint,
            ReleaseMessage = NormalizeOptional(configuration.ReleaseMessage),
        };
    }

    private PosHandheldNativeDecisionDto? GetIosDecision(
        PosHandheldNativeDecisionRequest request
    )
    {
        var configuration = policyOptions.Value;
        var distribution = Normalize(configuration.IosDistribution).ToLowerInvariant();
        var downloadUrl = Normalize(configuration.IosDownloadUrl);
        var bundleIdentifier = Normalize(configuration.IosBundleIdentifier);
        var appStoreId = Normalize(configuration.IosAppStoreId);
        if (!IsVersion(configuration.IosLatestVersion)
            || !IsBuild(configuration.IosLatestBuild)
            || distribution is not ("app-store" or "testflight")
            || !string.Equals(
                bundleIdentifier,
                PosHandheldIosUpdateIdentity.BundleIdentifier,
                StringComparison.Ordinal
            )
            || !PosHandheldIosUpdateIdentity.IsValidAppStoreId(appStoreId)
            || !PosHandheldIosUpdateIdentity.IsValidDistributionUrl(
                downloadUrl,
                distribution,
                appStoreId
            )
            || (distribution == "testflight"
                && PosHandheldIosUpdateIdentity.CanProduceRequiredDecision(configuration)))
        {
            logger.LogWarning("pos-handheld iOS update identity metadata unsafe or incomplete");
            return null;
        }

        var updateAvailable = IsUpdateAvailable(
            request.Version,
            request.Build,
            configuration.IosLatestVersion,
            configuration.IosLatestBuild
        );
        if (!updateAvailable)
        {
            return NoNativeDecision("iOS");
        }

        var required = configuration.IosRequired
            || IsBelowMinimum(
                request.Version,
                request.Build,
                configuration.IosMinimumSupportedVersion,
                configuration.IosMinimumSupportedBuild
            );
        if (distribution == "testflight" && required)
        {
            logger.LogWarning("pos-handheld required TestFlight decision rejected");
            return null;
        }

        return new PosHandheldNativeDecisionDto
        {
            State = required ? AppUpdateStates.Required : AppUpdateStates.Optional,
            PolicyVersion = NormalizePolicyVersion(),
            Platform = "iOS",
            Required = required,
            LatestVersion = Normalize(configuration.IosLatestVersion),
            LatestBuild = Normalize(configuration.IosLatestBuild),
            MinimumSupportedVersion = NormalizeOptional(
                configuration.IosMinimumSupportedVersion
            ),
            Distribution = distribution,
            DownloadUrl = downloadUrl,
            BundleIdentifier = bundleIdentifier,
            AppStoreId = appStoreId,
            ReleaseMessage = NormalizeOptional(configuration.ReleaseMessage),
        };
    }

    private bool CanEvaluate(
        string? storeCode,
        string platform,
        out string projectName
    )
    {
        var configuredProjectName = Normalize(policyOptions.Value.EasProjectName);
        projectName = configuredProjectName;
        return Normalize(storeCode).Length > 0
            && platform is "iOS" or "Android"
            && configuredProjectName.Length > 0
            && easOptions.Value.ProjectAppKeys.Any(mapping =>
                string.Equals(
                    mapping.Key.Trim(),
                    configuredProjectName,
                    StringComparison.OrdinalIgnoreCase
                )
                && MobileAppKeys.TryNormalize(mapping.Value, out var appKey)
                && appKey == MobileAppKeys.PosHandheld
            );
    }

    private string NormalizePolicyVersion()
    {
        var version = Normalize(policyOptions.Value.PolicyVersion);
        return version.Length == 0 || version == AppUpdateStates.None
            ? "1"
            : version;
    }

    private static bool IsUpdateAvailable(
        string? currentVersion,
        string? currentBuild,
        string? latestVersion,
        string? latestBuild
    )
    {
        var versionComparison = CompareVersions(latestVersion, currentVersion);
        return versionComparison > 0
            || versionComparison == 0
                && ParseBuild(latestBuild) > ParseBuild(currentBuild);
    }

    private static bool IsBelowMinimum(
        string? currentVersion,
        string? currentBuild,
        string? minimumVersion,
        int? minimumBuild
    )
    {
        var normalizedMinimum = NormalizeOptional(minimumVersion);
        if (normalizedMinimum != null
            && CompareVersions(currentVersion, normalizedMinimum) < 0)
        {
            return true;
        }

        return minimumBuild is > 0 && ParseBuild(currentBuild) < minimumBuild.Value;
    }

    private static int CompareVersions(string? left, string? right)
    {
        return TryParseVersion(left, out var leftVersion)
            && TryParseVersion(right, out var rightVersion)
                ? leftVersion.CompareTo(rightVersion)
                : string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal)
                    ? 0
                    : TryParseVersion(left, out _) ? 1 : -1;
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        var normalized = Normalize(value).TrimStart('v', 'V');
        return Version.TryParse(normalized, out version!);
    }

    private static bool IsVersion(string? value) => TryParseVersion(value, out _);

    private static bool IsBuild(string? value) => ParseBuild(value) > 0;

    private static bool IsCanonicalCurrentBuild(string? value)
    {
        if (value is not { Length: > 0 and <= 16 } || value[0] == '0')
        {
            return false;
        }

        // 当前 build 必须按原始请求逐字符校验；latest/minimum 继续沿用既有 ParseBuild 行为。
        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return long.TryParse(value, out var build)
            && build is > 0 and <= JavaScriptSafeIntegerMax;
    }

    private static long ParseBuild(string? value)
    {
        var normalized = Normalize(value);
        // 原生更新 build 会跨服务传递，必须同时保持规范数字格式和 JavaScript 精确整数范围。
        return BuildPattern().IsMatch(normalized)
            && long.TryParse(normalized, out var build)
            && build <= JavaScriptSafeIntegerMax
                ? build
                : -1;
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();

    private static string? NormalizeOptional(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private static string? NormalizeFingerprint(string? value)
    {
        var normalized = Normalize(value).Replace(":", string.Empty).ToLowerInvariant();
        return Sha256Pattern().IsMatch(normalized) ? normalized : null;
    }

    private static string NormalizePlatform(string? value)
    {
        if (string.Equals(value, "iOS", StringComparison.Ordinal))
        {
            return "iOS";
        }

        return string.Equals(value, "Android", StringComparison.Ordinal)
            ? "Android"
            : string.Empty;
    }

    private static bool IsTrustedHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo);

    private static PosHandheldNativeDecisionDto NoNativeDecision(string platform) =>
        new()
        {
            State = AppUpdateStates.None,
            PolicyVersion = AppUpdateStates.None,
            Platform = platform,
            Required = false,
        };

    private static PosHandheldOtaDecisionDto NoOtaDecision(
        string platform,
        string? projectName,
        string? channel,
        string? runtimeVersion) =>
        new()
        {
            State = AppUpdateStates.None,
            PolicyVersion = AppUpdateStates.None,
            AppKey = MobileAppKeys.PosHandheld,
            ProjectName = NormalizeOptional(projectName),
            Platform = platform,
            Required = false,
            Channel = NormalizeOptional(channel),
            RuntimeVersion = NormalizeOptional(runtimeVersion),
        };

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9_]*(?:\\.[a-zA-Z][a-zA-Z0-9_]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex AndroidPackagePattern();

    [GeneratedRegex("^[1-9]\\d{0,15}$", RegexOptions.CultureInvariant)]
    private static partial Regex BuildPattern();

}
