using System.Text.RegularExpressions;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services;

public sealed class IosAppStoreReleaseService(
    ISqlSugarClient db,
    IAppleAppStoreLookupClient lookupClient,
    IOptions<AppUpdatePolicyOptions> options,
    ILogger<IosAppStoreReleaseService> logger
) : IIosAppStoreReleaseService
{
    private static readonly Regex StoreIdPattern = new("^\\d{6,20}$", RegexOptions.Compiled);
    private static readonly Regex StorefrontPattern = new("^[a-z]{2}$", RegexOptions.Compiled);
    private static readonly Regex BuildPattern = new("^[0-9A-Za-z._-]{1,64}$", RegexOptions.Compiled);

    public async Task<ApiResponse<List<IosAppStoreReleaseDto>>> GetAsync(
        IosAppStoreReleaseQuery query
    )
    {
        var app = NormalizeOptional(query.App)?.ToLowerInvariant();
        var storefront = NormalizeStorefront(query.Storefront);
        var queryable = db.Queryable<IosAppStoreRelease>().Where(item => !item.IsDeleted);
        if (app is not null)
        {
            queryable = queryable.Where(item => item.App == app);
        }

        if (storefront is not null)
        {
            queryable = queryable.Where(item => item.Storefront == storefront);
        }

        var rows = await queryable
            .OrderByDescending(item => item.AppleVerifiedAtUtc)
            .Take(200)
            .ToListAsync();
        return ApiResponse<List<IosAppStoreReleaseDto>>.OK(rows.Select(Map).ToList());
    }

    public async Task<ApiResponse<IosAppStoreReleaseDto>> CreateAsync(
        IosAppStoreReleaseCreateRequest request,
        string currentUser,
        CancellationToken cancellationToken = default
    )
    {
        var app = NormalizeApp(request.App);
        if (app is null)
        {
            return Error(
                "APP_STORE_APP_INVALID",
                "App 必须是 mobile-ios、pos-ipad 或 pos-handheld"
            );
        }

        var appStoreId = NormalizeOptional(request.AppStoreId);
        if (appStoreId is null || !StoreIdPattern.IsMatch(appStoreId))
        {
            return Error("APP_STORE_ID_INVALID", "App Store ID 无效");
        }

        var storefront = NormalizeStorefront(request.Storefront);
        if (storefront is null)
        {
            return Error("APP_STORE_STOREFRONT_INVALID", "Storefront 必须是两位国家或地区代码");
        }

        var buildNumber = NormalizeOptional(request.BuildNumber);
        if (buildNumber is null || !BuildPattern.IsMatch(buildNumber))
        {
            return Error("APP_STORE_BUILD_INVALID", "Build number 无效");
        }

        // Mobile 原生策略按 Int32 build 比较；Apple Lookup 不提供 CFBundleVersion，
        // 因此这里只校验管理员登记值的整数范围，不把它误当成 Apple 核验结果。
        if (
            app == AppUpdateApps.MobileIos
            && !PosIpadEffectiveVersion.TryParseBuild(buildNumber, out _)
        )
        {
            return Error(
                "APP_STORE_BUILD_INVALID",
                "Mobile iOS Build number 必须是 0 到 Int32.MaxValue 的整数"
            );
        }

        // 手持客户端会把 build 作为 JavaScript 安全整数比较；登记入口必须使用同一规范，
        // 避免写入成功后又被候选目录静默过滤。
        if (
            app == AppUpdateApps.PosHandheld
            && !PosHandheldIosUpdateIdentity.IsValidBuildNumber(buildNumber)
        )
        {
            return Error("APP_STORE_BUILD_INVALID", "手持 POS Build number 无效");
        }

        var lookup = await lookupClient.LookupAsync(appStoreId, storefront, cancellationToken);
        var lookedUpAppStoreId = NormalizeOptional(lookup?.AppStoreId);
        if (
            lookup is null
            || !string.Equals(lookedUpAppStoreId, appStoreId, StringComparison.Ordinal)
        )
        {
            return Error("APPLE_LOOKUP_FAILED", "Apple Lookup 未返回匹配的 App");
        }

        var bundleIdentifier = NormalizeOptional(lookup.BundleIdentifier);
        var expectedBundle = app switch
        {
            AppUpdateApps.MobileIos => options.Value.MobileIosBundleIdentifier,
            AppUpdateApps.PosIpad => options.Value.PosIpadBundleIdentifier,
            AppUpdateApps.PosHandheld => options.Value.PosHandheldBundleIdentifier,
            _ => string.Empty,
        };
        if (!string.Equals(bundleIdentifier, expectedBundle, StringComparison.Ordinal))
        {
            return Error("APP_STORE_BUNDLE_MISMATCH", "Apple 返回的 Bundle Identifier 与目标 App 不匹配");
        }

        var version = NormalizeOptional(lookup.Version);
        if (!AppMarketingVersion.TryParse(version, out _))
        {
            return Error("APP_STORE_VERSION_INVALID", "Apple 返回的营销版本无效");
        }

        if (!TryNormalizeAppleUrl(lookup.AppStoreUrl, out var appStoreUrl))
        {
            return Error("APP_STORE_URL_INVALID", "Apple 返回的 App Store URL 无效");
        }

        var existing = await db.Queryable<IosAppStoreRelease>()
            .FirstAsync(item =>
                item.App == app
                && item.Storefront == storefront
                && item.Version == version
                && item.BuildNumber == buildNumber
                && !item.IsDeleted
            );
        if (existing is not null)
        {
            return IsSameImmutableFacts(
                existing,
                app,
                appStoreId,
                bundleIdentifier!,
                version!,
                buildNumber,
                storefront,
                appStoreUrl
            )
                ? ApiResponse<IosAppStoreReleaseDto>.OK(Map(existing), "发布事实已登记")
                : Error("APP_STORE_RELEASE_CONFLICT", "唯一发布键已登记不同的不可变事实");
        }

        var now = DateTime.UtcNow;
        var entity = new IosAppStoreRelease
        {
            Id = Guid.NewGuid(),
            App = app,
            AppStoreId = appStoreId,
            BundleIdentifier = bundleIdentifier!,
            Version = version!,
            BuildNumber = buildNumber,
            Storefront = storefront,
            AppStoreUrl = appStoreUrl,
            AppleVerifiedAtUtc = now,
            CreatedAt = now,
            CreatedBy = NormalizeOptional(currentUser) ?? "System",
            UpdatedAt = null,
            IsDeleted = false,
        };

        try
        {
            await db.Insertable(entity).ExecuteCommandAsync();
        }
        catch (Exception ex) when (IsUniqueConflict(ex))
        {
            logger.LogInformation(ex, "App Store 发布事实并发重复登记，转为读取既有记录");
            existing = await db.Queryable<IosAppStoreRelease>()
                .FirstAsync(item =>
                    item.App == app
                    && item.Storefront == storefront
                    && item.Version == version
                    && item.BuildNumber == buildNumber
                    && !item.IsDeleted
                );
            if (existing is not null)
            {
                return IsSameImmutableFacts(
                    existing,
                    app,
                    appStoreId,
                    bundleIdentifier!,
                    version!,
                    buildNumber,
                    storefront,
                    appStoreUrl
                )
                    ? ApiResponse<IosAppStoreReleaseDto>.OK(Map(existing), "发布事实已登记")
                    : Error("APP_STORE_RELEASE_CONFLICT", "唯一发布键已登记不同的不可变事实");
            }

            throw;
        }

        return ApiResponse<IosAppStoreReleaseDto>.OK(Map(entity), "App Store 发布事实登记成功");
    }

    private static string? NormalizeApp(string? value)
    {
        var normalized = NormalizeOptional(value)?.ToLowerInvariant();
        return normalized
                is AppUpdateApps.MobileIos
                    or AppUpdateApps.PosIpad
                    or AppUpdateApps.PosHandheld
            ? normalized
            : null;
    }

    private static string? NormalizeStorefront(string? value)
    {
        var normalized = NormalizeOptional(value)?.ToLowerInvariant() ?? "au";
        return StorefrontPattern.IsMatch(normalized) ? normalized : null;
    }

    private static bool TryNormalizeAppleUrl(string value, out string normalized)
    {
        normalized = string.Empty;
        if (
            !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || (uri.Host != "apps.apple.com" && uri.Host != "itunes.apple.com")
            || !string.IsNullOrEmpty(uri.UserInfo)
        )
        {
            return false;
        }

        normalized = uri.ToString();
        return true;
    }

    private static bool IsUniqueConflict(Exception ex) =>
        ex.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase);

    private static bool IsSameImmutableFacts(
        IosAppStoreRelease existing,
        string app,
        string appStoreId,
        string bundleIdentifier,
        string version,
        string buildNumber,
        string storefront,
        string appStoreUrl
    ) =>
        string.Equals(existing.App, app, StringComparison.Ordinal)
        && string.Equals(existing.AppStoreId, appStoreId, StringComparison.Ordinal)
        && string.Equals(existing.BundleIdentifier, bundleIdentifier, StringComparison.Ordinal)
        && string.Equals(existing.Version, version, StringComparison.Ordinal)
        && string.Equals(existing.BuildNumber, buildNumber, StringComparison.Ordinal)
        && string.Equals(existing.Storefront, storefront, StringComparison.Ordinal)
        && string.Equals(existing.AppStoreUrl, appStoreUrl, StringComparison.Ordinal);

    private static IosAppStoreReleaseDto Map(IosAppStoreRelease item) =>
        new()
        {
            Id = item.Id,
            App = item.App,
            AppStoreId = item.AppStoreId,
            BundleIdentifier = item.BundleIdentifier,
            Version = item.Version,
            BuildNumber = item.BuildNumber,
            Storefront = item.Storefront,
            AppStoreUrl = item.AppStoreUrl,
            AppleVerifiedAtUtc = item.AppleVerifiedAtUtc,
            CreatedAt = item.CreatedAt,
            CreatedBy = item.CreatedBy,
        };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ApiResponse<IosAppStoreReleaseDto> Error(string code, string message) =>
        ApiResponse<IosAppStoreReleaseDto>.Error(message, code);
}
