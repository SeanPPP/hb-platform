using BlazorApp.Api.Controllers;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class MobileAppBuildAppKeyIsolationTests : IDisposable
{
    private const string SharedOtaGroup = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
    private readonly string dbPath;
    private readonly ISqlSugarClient db;

    public MobileAppBuildAppKeyIsolationTests()
    {
        dbPath = Path.Combine(Path.GetTempPath(), $"mobile-app-key-{Guid.NewGuid():N}.db");
        db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = $"DataSource={dbPath}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        db.CodeFirst.InitTables<MobileAppBuild, MobileAppOtaUpdate>();
    }

    [Fact]
    public async Task Webhook_derives_app_key_only_from_server_project_mapping()
    {
        var service = CreateService();

        var accepted = await service.HandleEasWebhookAsync(
            CreateBuildPayload(
                "handheld-build",
                "hb-pos-handheld",
                "2026-08-10T02:00:00Z",
                clientAppKey: MobileAppKeys.Mobile)
        );
        var rejected = await service.HandleEasWebhookAsync(
            CreateBuildPayload(
                "spoofed-build",
                "unmapped-project",
                "2026-08-10T03:00:00Z",
                clientAppKey: MobileAppKeys.PosHandheld)
        );

        Assert.Equal("saved", accepted.Data!.Action);
        Assert.Equal("ignored", rejected.Data!.Action);
        Assert.Equal("project_not_allowed", rejected.Data.Reason);
        var saved = await db.Queryable<MobileAppBuild>().SingleAsync();
        Assert.Equal(MobileAppKeys.PosHandheld, saved.AppKey);
        Assert.Equal("hb-pos-handheld", saved.ProjectName);
    }

    [Fact]
    public async Task Webhook_手持项目只接受Production且不影响移动端Preview()
    {
        var service = CreateService();

        var handheldPreview = await service.HandleEasWebhookAsync(
            CreateBuildPayload(
                "handheld-preview",
                "hb-pos-handheld",
                "2026-08-10T01:00:00Z",
                profile: "preview"
            )
        );
        var mobilePreview = await service.HandleEasWebhookAsync(
            CreateBuildPayload(
                "mobile-preview",
                "hb-mobile",
                "2026-08-10T02:00:00Z",
                profile: "preview"
            )
        );

        Assert.Equal("ignored", handheldPreview.Data!.Action);
        Assert.Equal("profile_not_accepted", handheldPreview.Data.Reason);
        Assert.Equal("saved", mobilePreview.Data!.Action);
        var saved = await db.Queryable<MobileAppBuild>().SingleAsync();
        Assert.Equal(MobileAppKeys.Mobile, saved.AppKey);
        Assert.Equal("mobile-preview", saved.EasBuildId);
    }

    [Fact]
    public async Task Same_profile_projects_do_not_cross_latest_build_id_or_history()
    {
        var service = CreateService();
        await service.HandleEasWebhookAsync(
            CreateBuildPayload("mobile-build", "hb-mobile", "2026-08-10T01:00:00Z")
        );
        await service.HandleEasWebhookAsync(
            CreateBuildPayload("handheld-build", "hb-pos-handheld", "2026-08-10T02:00:00Z")
        );

        var legacyLatest = await service.GetLatestAsync("production");
        var mobileLatest = await service.GetLatestAsync(MobileAppKeys.Mobile, "production");
        var handheldLatest = await service.GetLatestAsync(MobileAppKeys.PosHandheld, "production");
        var crossedDownload = await service.GetByBuildIdAsync(
            MobileAppKeys.Mobile,
            "handheld-build",
            "production"
        );
        var handheldDownload = await service.GetByBuildIdAsync(
            MobileAppKeys.PosHandheld,
            "handheld-build",
            "production"
        );
        var mobileHistory = await service.GetHistoryAsync(
            new MobileAppBuildQueryDto
            {
                AppKey = MobileAppKeys.Mobile,
                Profile = "production",
                PageSize = 20,
            }
        );
        var handheldHistory = await service.GetHistoryAsync(
            new MobileAppBuildQueryDto
            {
                AppKey = MobileAppKeys.PosHandheld,
                Profile = "production",
                PageSize = 20,
            }
        );

        Assert.Equal("mobile-build", legacyLatest.Data!.EasBuildId);
        Assert.Equal("mobile-build", mobileLatest.Data!.EasBuildId);
        Assert.Equal(MobileAppKeys.Mobile, mobileLatest.Data.AppKey);
        Assert.Equal("handheld-build", handheldLatest.Data!.EasBuildId);
        Assert.Equal(MobileAppKeys.PosHandheld, handheldLatest.Data.AppKey);
        Assert.Null(crossedDownload.Data);
        Assert.Equal("handheld-build", handheldDownload.Data!.EasBuildId);
        Assert.Equal(["mobile-build"], mobileHistory.Data!.Items!.Select(item => item.EasBuildId));
        Assert.Equal(["handheld-build"], handheldHistory.Data!.Items!.Select(item => item.EasBuildId));
    }

    [Fact]
    public async Task Latest_按受控AppKey隔离且非法键不回退Mobile()
    {
        var service = CreateService();
        await service.HandleEasWebhookAsync(
            CreateBuildPayload("mobile-build", "hb-mobile", "2026-08-10T01:00:00Z")
        );
        await service.HandleEasWebhookAsync(
            CreateBuildPayload("handheld-build", "hb-pos-handheld", "2026-08-10T02:00:00Z")
        );
        var controller = CreateController(service);

        var defaultLatest = Assert.IsType<OkObjectResult>(
            await controller.Latest(profile: "production")
        );
        var handheldLatest = Assert.IsType<OkObjectResult>(
            await controller.Latest(appKey: " POS-HANDHELD ", profile: "production")
        );
        var invalidLatest = Assert.IsType<OkObjectResult>(
            await controller.Latest(appKey: "unknown-app", profile: "production")
        );
        var blankLatest = Assert.IsType<OkObjectResult>(
            await controller.Latest(appKey: " ", profile: "production")
        );

        Assert.Equal(
            "mobile-build",
            Assert.IsType<ApiResponse<MobileAppBuildDto?>>(defaultLatest.Value).Data!.EasBuildId
        );
        Assert.Equal(
            "handheld-build",
            Assert.IsType<ApiResponse<MobileAppBuildDto?>>(handheldLatest.Value).Data!.EasBuildId
        );
        var invalidResponse = Assert.IsType<ApiResponse<MobileAppBuildDto?>>(invalidLatest.Value);
        Assert.True(invalidResponse.Success);
        Assert.Null(invalidResponse.Data);
        Assert.Null(Assert.IsType<ApiResponse<MobileAppBuildDto?>>(blankLatest.Value).Data);
    }

    [Fact]
    public async Task Same_ota_group_platform_is_isolated_by_server_mapped_app_key()
    {
        var service = CreateService();

        await service.UpsertOtaUpdateAsync(
            CreateOta("hb-mobile", "mobile-update")
        );
        await service.UpsertOtaUpdateAsync(
            CreateOta("hb-pos-handheld", "handheld-update")
        );

        var mobile = await service.GetOtaUpdatesAsync(
            new MobileAppOtaUpdateQueryDto
            {
                AppKey = MobileAppKeys.Mobile,
                Channel = "production",
                RuntimeVersion = "1.0.0",
            }
        );
        var handheld = await service.GetOtaUpdatesAsync(
            new MobileAppOtaUpdateQueryDto
            {
                AppKey = MobileAppKeys.PosHandheld,
                Channel = "production",
                RuntimeVersion = "1.0.0",
            }
        );

        Assert.Equal(2, await db.Queryable<MobileAppOtaUpdate>().CountAsync());
        var mobileItem = Assert.Single(mobile.Data!.Items!);
        var handheldItem = Assert.Single(handheld.Data!.Items!);
        Assert.Equal(MobileAppKeys.Mobile, mobileItem.AppKey);
        Assert.Equal("mobile-update", mobileItem.UpdateId);
        Assert.Equal(MobileAppKeys.PosHandheld, handheldItem.AppKey);
        Assert.Equal("handheld-update", handheldItem.UpdateId);
    }

    [Theory]
    [InlineData(" android ", "android")]
    [InlineData(" ANDROID ", "android")]
    [InlineData(" ios ", "ios")]
    [InlineData(" IOS ", "ios")]
    public async Task Ota_upsert_normalizes_only_supported_platforms(
        string submittedPlatform,
        string expectedPlatform)
    {
        var service = CreateService();

        var result = await service.UpsertOtaUpdateAsync(
            new MobileAppOtaUpdateUpsertDto
            {
                ProjectName = "hb-pos-handheld",
                UpdateGroupId = SharedOtaGroup,
                UpdateId = "handheld-update",
                AndroidUpdateId = "android-update",
                Channel = "pos-handheld-production",
                Platform = submittedPlatform,
                RuntimeVersion = "1.0.0",
            }
        );

        Assert.True(result.Success);
        Assert.Equal(expectedPlatform, result.Data!.Platform);
        Assert.Equal(
            expectedPlatform,
            (await db.Queryable<MobileAppOtaUpdate>().SingleAsync()).Platform
        );
    }

    [Theory]
    [InlineData("web")]
    [InlineData("watchOS")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Ota_upsert_rejects_unknown_platform_without_writing(string? platform)
    {
        var service = CreateService();

        var result = await service.UpsertOtaUpdateAsync(
            new MobileAppOtaUpdateUpsertDto
            {
                ProjectName = "hb-pos-handheld",
                UpdateGroupId = SharedOtaGroup,
                UpdateId = "dirty-update",
                Channel = "pos-handheld-production",
                Platform = platform,
                RuntimeVersion = "1.0.0",
            }
        );

        Assert.False(result.Success);
        Assert.Equal("INVALID_OTA_PLATFORM", result.ErrorCode);
        Assert.Equal(0, await db.Queryable<MobileAppOtaUpdate>().CountAsync());
    }

    [Fact]
    public async Task Ota_rollback_rejects_unknown_platform_without_generating_command()
    {
        var service = CreateService();

        var result = await service.CreateOtaRollbackCommandAsync(
            SharedOtaGroup,
            new MobileAppOtaRollbackCommandDto { Platform = "web" }
        );

        Assert.False(result.Success);
        Assert.Equal("INVALID_OTA_PLATFORM", result.ErrorCode);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Anonymous_mobile_and_pos_handheld_download_routes_cannot_cross_app_keys()
    {
        var service = CreateService();
        await service.HandleEasWebhookAsync(
            CreateBuildPayload("mobile-build", "hb-mobile", "2026-08-10T01:00:00Z")
        );
        await service.HandleEasWebhookAsync(
            CreateBuildPayload("handheld-build", "hb-pos-handheld", "2026-08-10T02:00:00Z")
        );
        var controller = CreateController(service);

        var mobileLatest = Assert.IsType<OkObjectResult>(
            await controller.AndroidLatest("production")
        );
        var handheldLatest = Assert.IsType<OkObjectResult>(
            await controller.PosHandheldAndroidLatest("production")
        );
        var crossed = await controller.AndroidBuildDownload("handheld-build", "production");
        var handheldDownload = await controller.PosHandheldAndroidBuildDownload(
            "handheld-build",
            "production"
        );

        Assert.Equal(
            "mobile-build",
            Assert.IsType<ApiResponse<MobileAppBuildPublicDto?>>(mobileLatest.Value).Data!.EasBuildId
        );
        Assert.Equal(
            "handheld-build",
            Assert.IsType<ApiResponse<MobileAppBuildPublicDto?>>(handheldLatest.Value).Data!.EasBuildId
        );
        Assert.IsType<NotFoundObjectResult>(crossed);
        Assert.Equal(
            "https://expo.dev/artifacts/eas/handheld-build.apk",
            Assert.IsType<RedirectResult>(handheldDownload).Url
        );
    }

    [Fact]
    public async Task Anonymous_android_internal_is_rejected_for_mobile_and_available_for_pos_handheld()
    {
        var service = CreateService();
        await service.HandleEasWebhookAsync(
            CreateBuildPayload(
                "mobile-internal",
                "hb-mobile",
                "2026-08-10T01:00:00Z",
                profile: "android-internal"
            )
        );
        await service.HandleEasWebhookAsync(
            CreateBuildPayload(
                "handheld-internal",
                "hb-pos-handheld",
                "2026-08-10T02:00:00Z",
                profile: "production"
            )
        );
        // 新 Webhook 只收 production；历史 android-internal 记录仍须保持公开下载兼容。
        var legacyHandheld = await db
            .Queryable<MobileAppBuild>()
            .SingleAsync(x =>
                x.AppKey == MobileAppKeys.PosHandheld && x.EasBuildId == "handheld-internal"
            );
        legacyHandheld.BuildProfile = "android-internal";
        await db.Updateable(legacyHandheld).ExecuteCommandAsync();
        var controller = CreateController(service);

        var mobileLatest = Assert.IsType<OkObjectResult>(
            await controller.AndroidLatest("ANDROID-INTERNAL")
        );
        var mobileLatestDownload = await controller.AndroidLatestDownload("android-internal");
        var mobileBuildDownload = await controller.AndroidBuildDownload(
            "mobile-internal",
            " android-internal "
        );
        var handheldLatest = Assert.IsType<OkObjectResult>(
            await controller.PosHandheldAndroidLatest(" android-internal ")
        );
        var handheldLatestDownload = await controller.PosHandheldAndroidLatestDownload(
            "ANDROID-INTERNAL"
        );
        var handheldBuildDownload = await controller.PosHandheldAndroidBuildDownload(
            "handheld-internal",
            "android-internal"
        );
        var handheldCrossedDownload = await controller.PosHandheldAndroidBuildDownload(
            "mobile-internal",
            "android-internal"
        );

        Assert.Null(
            Assert.IsType<ApiResponse<MobileAppBuildPublicDto?>>(mobileLatest.Value).Data
        );
        Assert.IsType<NotFoundObjectResult>(mobileLatestDownload);
        Assert.IsType<NotFoundObjectResult>(mobileBuildDownload);
        Assert.Equal(
            "handheld-internal",
            Assert.IsType<ApiResponse<MobileAppBuildPublicDto?>>(handheldLatest.Value)
                .Data!
                .EasBuildId
        );
        Assert.Equal(
            "https://expo.dev/artifacts/eas/handheld-internal.apk",
            Assert.IsType<RedirectResult>(handheldLatestDownload).Url
        );
        Assert.Equal(
            "https://expo.dev/artifacts/eas/handheld-internal.apk",
            Assert.IsType<RedirectResult>(handheldBuildDownload).Url
        );
        Assert.IsType<NotFoundObjectResult>(handheldCrossedDownload);
    }

    [Fact]
    public async Task Startup_migration_backfills_app_key_before_not_null_and_replaces_unique_indexes()
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(
                FindRepoRoot(),
                "services/backend/BlazorApp.Api/Data/StartupSchemaMigrator.cs"
            )
        );

        Assert.Contains("COL_LENGTH('MobileAppBuild', 'AppKey') IS NULL", source);
        Assert.Contains("COL_LENGTH('MobileAppOtaUpdate', 'AppKey') IS NULL", source);
        Assert.Contains("SET [AppKey] = ''mobile''", source);
        Assert.Contains("ALTER COLUMN [AppKey] nvarchar(80) NOT NULL", source);
        Assert.True(
            source.IndexOf("SET [AppKey] = ''mobile''", StringComparison.Ordinal)
            < source.IndexOf("ALTER COLUMN [AppKey] nvarchar(80) NOT NULL", StringComparison.Ordinal)
        );
        Assert.Contains("DROP INDEX [IX_MobileAppBuild_EasBuildId]", source);
        Assert.Contains("IX_MobileAppBuild_AppKey_EasBuildId", source);
        Assert.Contains("ON [MobileAppBuild]([AppKey], [EasBuildId])", source);
        Assert.Contains("DROP INDEX [IX_MobileAppOtaUpdate_Group_Platform]", source);
        Assert.Contains("IX_MobileAppOtaUpdate_AppKey_Group_Platform", source);
        Assert.Contains(
            "ON [MobileAppOtaUpdate]([AppKey], [UpdateGroupId], [Platform])",
            source
        );
    }

    public void Dispose()
    {
        db.Dispose();
        if (File.Exists(dbPath))
        {
            SqliteTempFileCleanup.DeleteIfExists(dbPath);
        }
    }

    private MobileAppBuildService CreateService() =>
        new(
            db,
            Options.Create(
                new EasWebhookOptions
                {
                    Secret = "test-secret",
                    AllowedAccountName = "hotbargain",
                    AllowedProjectName = "hb-mobile",
                    ProjectAppKeys = new Dictionary<string, string>
                    {
                        ["hb-mobile"] = MobileAppKeys.Mobile,
                        ["hb-pos-handheld"] = MobileAppKeys.PosHandheld,
                    },
                    AcceptedProfiles = ["preview", "production", "android-internal"],
                }
            ),
            NullLogger<MobileAppBuildService>.Instance
        );

    private static MobileAppBuildsController CreateController(MobileAppBuildService service) =>
        new(
            service,
            Options.Create(
                new EasWebhookOptions
                {
                    Secret = "test-secret",
                    AllowedAccountName = "hotbargain",
                    AllowedProjectName = "hb-mobile",
                    ProjectAppKeys = new Dictionary<string, string>
                    {
                        ["hb-mobile"] = MobileAppKeys.Mobile,
                        ["hb-pos-handheld"] = MobileAppKeys.PosHandheld,
                    },
                }
            ),
            NullLogger<MobileAppBuildsController>.Instance
        );

    private static MobileAppOtaUpdateUpsertDto CreateOta(string projectName, string updateId) =>
        new()
        {
            ProjectName = projectName,
            UpdateGroupId = SharedOtaGroup,
            UpdateId = updateId,
            AndroidUpdateId = updateId,
            Channel = "production",
            Platform = "android",
            RuntimeVersion = "1.0.0",
            PublishedAt = new DateTime(2026, 8, 10, 3, 0, 0, DateTimeKind.Utc),
        };

    private static string CreateBuildPayload(
        string buildId,
        string projectName,
        string completedAt,
        string? clientAppKey = null,
        string profile = "production") =>
        $$"""
        {
          "id": "{{buildId}}",
          "accountName": "hotbargain",
          "projectName": "{{projectName}}",
          "appKey": "{{clientAppKey ?? string.Empty}}",
          "platform": "android",
          "status": "finished",
          "buildProfile": "{{profile}}",
          "runtimeVersion": "1.0.0",
          "appVersion": "1.2.3",
          "appBuildVersion": "123",
          "artifacts": { "buildUrl": "https://expo.dev/artifacts/eas/{{buildId}}.apk" },
          "createdAt": "2026-08-10T00:00:00Z",
          "completedAt": "{{completedAt}}",
          "expirationDate": "2099-08-10T00:00:00Z"
        }
        """;

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "services", "backend")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到仓库根目录");
    }
}
