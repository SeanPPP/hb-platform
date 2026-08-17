using System.Reflection;
using BlazorApp.Api.Authentication;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class PosHandheldUpdateDecisionServiceTests : IDisposable
{
    private const string OtaGroup = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
    private readonly string dbPath;
    private readonly ISqlSugarClient db;
    private readonly EasWebhookOptions easOptions;
    private readonly PosHandheldUpdatePolicyOptions policyOptions;

    public PosHandheldUpdateDecisionServiceTests()
    {
        dbPath = Path.Combine(Path.GetTempPath(), $"pos-handheld-policy-{Guid.NewGuid():N}.db");
        db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = $"DataSource={dbPath}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        db.CodeFirst.InitTables<
            MobileAppBuild,
            MobileAppOtaUpdate,
            IosAppStoreRelease,
            PosHandheldUpdatePolicy,
            PosHandheldUpdatePolicyRevision
        >();
        easOptions = new EasWebhookOptions
        {
            AllowedAccountName = "hotbargain",
            AllowedProjectName = "hb-mobile",
            ProjectAppKeys = new Dictionary<string, string>
            {
                ["hb-mobile"] = MobileAppKeys.Mobile,
                ["hb-pos-handheld"] = MobileAppKeys.PosHandheld,
            },
        };
        policyOptions = new PosHandheldUpdatePolicyOptions
        {
            Enabled = true,
            PolicyVersion = "12",
            EasProjectName = "hb-pos-handheld",
            AndroidProfile = "production",
            AndroidMinimumSupportedVersion = "1.5.0",
            AndroidMinimumSupportedBuild = 150,
            AndroidPackageName = "com.hbweb.poshandheld",
            AndroidSigningCertificateSha256 = new string('b', 64),
            IosLatestVersion = "3.0.0",
            IosLatestBuild = "300",
            IosMinimumSupportedVersion = null,
            IosMinimumSupportedBuild = null,
            IosDistribution = "testflight",
            IosDownloadUrl = "https://testflight.apple.com/join/AbCdEf12",
            IosBundleIdentifier = "com.hbweb.poshandheld",
            IosAppStoreId = "123456789",
            OtaRequired = false,
            ReleaseMessage = "handheld release",
        };
    }

    [Fact]
    public async Task Android_native_decision_uses_only_pos_handheld_build_and_verification_metadata()
    {
        await InsertBuildAsync(
            MobileAppKeys.Mobile,
            "hb-mobile",
            "mobile-newer",
            "9.0.0",
            "900",
            "https://downloads.example/mobile.apk",
            new DateTime(2026, 8, 10, 4, 0, 0, DateTimeKind.Utc)
        );
        await InsertBuildAsync(
            MobileAppKeys.PosHandheld,
            "hb-pos-handheld",
            "handheld-200",
            "2.0.0",
            "200",
            "https://downloads.example/handheld-200.apk",
            new DateTime(2026, 8, 10, 3, 0, 0, DateTimeKind.Utc)
        );
        var service = CreateDecisionService();

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "Android",
                Version = "1.0.0",
                Build = "100",
            }
        );

        Assert.NotNull(decision);
        Assert.Equal(AppUpdateStates.Required, decision.State);
        Assert.True(decision.Required);
        Assert.Equal("Android", decision.Platform);
        Assert.Equal("2.0.0", decision.LatestVersion);
        Assert.Equal("200", decision.LatestBuild);
        Assert.Equal("https://downloads.example/handheld-200.apk", decision.DownloadUrl);
        Assert.Equal(2048, decision.FileSize);
        Assert.Equal(new string('a', 64), decision.Sha256);
        Assert.Equal("com.hbweb.poshandheld", decision.PackageName);
        Assert.Equal(new string('b', 64), decision.SigningCertificateSha256);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("00")]
    [InlineData("01")]
    [InlineData("1.2")]
    [InlineData(" 1 ")]
    [InlineData("1 ")]
    [InlineData("\t1")]
    [InlineData("9007199254740992")]
    [InlineData("10000000000000000")]
    public async Task Native_decision_rejects_invalid_current_build_without_treating_it_as_old(
        string? build)
    {
        var service = CreateDecisionService();

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "iOS",
                Version = "1.0.0",
                Build = build,
            }
        );

        Assert.Null(decision);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("9007199254740991")]
    public async Task Native_decision_accepts_canonical_current_build(string build)
    {
        var service = CreateDecisionService();

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "iOS",
                Version = "3.0.0",
                Build = build,
            }
        );

        Assert.NotNull(decision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData(" ios ")]
    [InlineData("ios")]
    [InlineData("IOS")]
    [InlineData(" android ")]
    [InlineData("android")]
    [InlineData("ANDROID")]
    public async Task Native_decision_rejects_noncanonical_handheld_platform_even_when_disabled(
        string? platform)
    {
        policyOptions.Enabled = false;
        var service = CreateDecisionService();

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = platform,
                Version = "1.0.0",
                Build = "100",
            }
        );

        Assert.Null(decision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData(" ios ")]
    [InlineData("ios")]
    [InlineData("IOS")]
    [InlineData(" android ")]
    [InlineData("android")]
    [InlineData("ANDROID")]
    public async Task Ota_decision_rejects_noncanonical_handheld_platform_even_when_disabled(
        string? platform)
    {
        policyOptions.Enabled = false;
        var service = CreateDecisionService();

        var decision = await service.GetOtaDecisionAsync(
            new PosHandheldOtaDecisionRequest
            {
                StoreCode = "0247",
                Platform = platform,
                RuntimeVersion = "1.0.0",
            }
        );

        Assert.Null(decision);
    }

    [Fact]
    public async Task Ios_optional_testflight_decision_returns_identity_without_using_android_builds()
    {
        await InsertBuildAsync(
            MobileAppKeys.PosHandheld,
            "hb-pos-handheld",
            "android-only",
            "99.0.0",
            "9900",
            "https://downloads.example/android-only.apk",
            new DateTime(2026, 8, 10, 5, 0, 0, DateTimeKind.Utc)
        );
        var service = CreateDecisionService();

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "iOS",
                Version = "1.0.0",
                Build = "100",
            }
        );

        Assert.NotNull(decision);
        Assert.Equal(AppUpdateStates.Optional, decision.State);
        Assert.False(decision.Required);
        Assert.Equal("iOS", decision.Platform);
        Assert.Equal("3.0.0", decision.LatestVersion);
        Assert.Equal("300", decision.LatestBuild);
        Assert.Equal("testflight", decision.Distribution);
        Assert.Equal("https://testflight.apple.com/join/AbCdEf12", decision.DownloadUrl);
        Assert.Equal("com.hbweb.poshandheld", decision.BundleIdentifier);
        Assert.Equal("123456789", decision.AppStoreId);
        Assert.Null(decision.PackageName);
    }

    [Fact]
    public async Task Ios_app_store_decision_requires_exact_id_segment_and_frozen_bundle()
    {
        policyOptions.IosDistribution = "app-store";
        policyOptions.IosDownloadUrl =
            "https://apps.apple.com/au/app/hb-pos-handheld/id123456789";
        policyOptions.IosRequired = true;
        var service = CreateDecisionService();

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "iOS",
                Version = "1.0.0",
                Build = "100",
            }
        );

        Assert.NotNull(decision);
        Assert.Equal(AppUpdateStates.Required, decision.State);
        Assert.True(decision.Required);
        Assert.Equal("123456789", decision.AppStoreId);
        Assert.Equal("com.hbweb.poshandheld", decision.BundleIdentifier);
    }

    [Theory]
    [InlineData("https://apps.apple.com/au/app/id987654321", "com.hbweb.poshandheld", "123456789")]
    [InlineData("https://apps.apple.com/au/app/id0123456789", "com.hbweb.poshandheld", "123456789")]
    [InlineData("https://apps.apple.com/au/app/product-id123456789", "com.hbweb.poshandheld", "123456789")]
    [InlineData("https://apps.apple.com/au/app/hb-pos-handheld", "com.hbweb.poshandheld", "123456789")]
    [InlineData("https://apps.apple.com/au/app/id123456789/reviews", "com.hbweb.poshandheld", "123456789")]
    [InlineData("https://apps.apple.com/au/app/id123456789", "com.example.other-handheld", "123456789")]
    [InlineData("https://apps.apple.com/au/app/id123456789", "com.hbweb.poshandheld", "12345abc")]
    [InlineData("https://apps.apple.com/au/app/id1234", "com.hbweb.poshandheld", "1234")]
    [InlineData("https://apps.apple.com/au/app/id123456789012345678901", "com.hbweb.poshandheld", "123456789012345678901")]
    public async Task Ios_identity_mismatch_is_unavailable(
        string downloadUrl,
        string bundleIdentifier,
        string appStoreId)
    {
        policyOptions.IosDistribution = "app-store";
        policyOptions.IosDownloadUrl = downloadUrl;
        policyOptions.IosBundleIdentifier = bundleIdentifier;
        policyOptions.IosAppStoreId = appStoreId;
        var service = CreateDecisionService();

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "iOS",
                Version = "1.0.0",
                Build = "100",
            }
        );

        Assert.Null(decision);
    }

    [Theory]
    [InlineData(true, null, null)]
    [InlineData(false, "2.0.0", null)]
    [InlineData(false, null, 200)]
    public async Task Ios_required_testflight_configuration_is_unavailable(
        bool explicitlyRequired,
        string? minimumVersion,
        int? minimumBuild)
    {
        policyOptions.IosRequired = explicitlyRequired;
        policyOptions.IosMinimumSupportedVersion = minimumVersion;
        policyOptions.IosMinimumSupportedBuild = minimumBuild;
        var service = CreateDecisionService();

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "iOS",
                Version = "1.0.0",
                Build = "100",
            }
        );

        Assert.Null(decision);
    }

    [Fact]
    public async Task Ios_noncanonical_testflight_url_is_unavailable()
    {
        policyOptions.IosDownloadUrl = "https://testflight.apple.com/not-join/AbCdEf12";
        var service = CreateDecisionService();

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "iOS",
                Version = "1.0.0",
                Build = "100",
            }
        );

        Assert.Null(decision);
    }

    [Fact]
    public async Task Android_missing_or_incomplete_release_metadata_is_unavailable()
    {
        var service = CreateDecisionService();

        var missing = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "Android",
                Version = "1.0.0",
                Build = "100",
            }
        );

        await InsertBuildAsync(
            MobileAppKeys.PosHandheld,
            "wrong-project",
            "handheld-200",
            "2.0.0",
            "200",
            "https://downloads.example/handheld-200.apk",
            new DateTime(2026, 8, 10, 3, 0, 0, DateTimeKind.Utc)
        );
        var wrongProject = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "Android",
                Version = "1.0.0",
                Build = "100",
            }
        );

        Assert.Null(missing);
        Assert.Null(wrongProject);
    }

    [Fact]
    public async Task Required_ota_empty_query_is_unavailable()
    {
        policyOptions.OtaRequired = true;
        var buildService = new Mock<IMobileAppBuildService>(MockBehavior.Strict);
        buildService
            .Setup(service => service.GetOtaUpdatesAsync(It.IsAny<MobileAppOtaUpdateQueryDto>()))
            .ReturnsAsync(
                ApiResponse<PagedResult<MobileAppOtaUpdateDto>>.OK(
                    new PagedResult<MobileAppOtaUpdateDto>()
                )
            );
        var service = new PosHandheldUpdateDecisionService(
            buildService.Object,
            Options.Create(policyOptions),
            Options.Create(easOptions),
            NullLogger<PosHandheldUpdateDecisionService>.Instance
        );
        var request = new PosHandheldOtaDecisionRequest
        {
            StoreCode = "0247",
            Platform = "Android",
            RuntimeVersion = "1.0.0",
        };

        var decision = await service.GetOtaDecisionAsync(request);

        Assert.Null(decision);
    }

    [Fact]
    public async Task Optional_ota_empty_query_is_none()
    {
        var buildService = new Mock<IMobileAppBuildService>(MockBehavior.Strict);
        buildService
            .Setup(service => service.GetOtaUpdatesAsync(It.IsAny<MobileAppOtaUpdateQueryDto>()))
            .ReturnsAsync(
                ApiResponse<PagedResult<MobileAppOtaUpdateDto>>.OK(
                    new PagedResult<MobileAppOtaUpdateDto>()
                )
            );
        var service = new PosHandheldUpdateDecisionService(
            buildService.Object,
            Options.Create(policyOptions),
            Options.Create(easOptions),
            NullLogger<PosHandheldUpdateDecisionService>.Instance
        );

        var decision = await service.GetOtaDecisionAsync(
            new PosHandheldOtaDecisionRequest
            {
                StoreCode = "0247",
                Platform = "Android",
                RuntimeVersion = "1.0.0",
            }
        );

        Assert.NotNull(decision);
        Assert.Equal(AppUpdateStates.None, decision.State);
    }

    [Fact]
    public async Task Enabled_policy_with_invalid_scope_or_runtime_is_unavailable()
    {
        var service = CreateDecisionService();

        var missingStore = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "",
                Platform = "iOS",
                Version = "1.0.0",
                Build = "100",
            }
        );
        var missingRuntime = await service.GetOtaDecisionAsync(
            new PosHandheldOtaDecisionRequest
            {
                StoreCode = "0247",
                Platform = "Android",
                RuntimeVersion = "",
            }
        );

        Assert.Null(missingStore);
        Assert.Null(missingRuntime);
    }

    [Fact]
    public async Task Disabled_policy_keeps_none_decision_unchanged()
    {
        policyOptions.Enabled = false;
        var service = CreateDecisionService();

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "iOS",
                Version = "1.0.0",
                Build = "100",
            }
        );

        Assert.NotNull(decision);
        Assert.Equal(AppUpdateStates.None, decision.State);
        Assert.Equal(AppUpdateStates.None, decision.PolicyVersion);
        Assert.False(decision.Required);
    }

    [Theory]
    [InlineData("app-store", false, null, null, "https://apps.apple.com/au/app/id987654321", "com.hbweb.poshandheld", "123456789")]
    [InlineData("app-store", false, null, null, "https://apps.apple.com/au/app/id123456789", "com.example.other-handheld", "123456789")]
    [InlineData("app-store", false, null, null, "https://apps.apple.com/au/app/id123456789", "com.hbweb.poshandheld", "not-numeric")]
    [InlineData("testflight", true, null, null, "https://testflight.apple.com/join/AbCdEf12", "com.hbweb.poshandheld", "123456789")]
    [InlineData("testflight", false, "2.0.0", null, "https://testflight.apple.com/join/AbCdEf12", "com.hbweb.poshandheld", "123456789")]
    public void Options_validator_rejects_unsafe_ios_identity_or_required_testflight(
        string distribution,
        bool required,
        string? minimumVersion,
        int? minimumBuild,
        string downloadUrl,
        string bundleIdentifier,
        string appStoreId)
    {
        policyOptions.IosDistribution = distribution;
        policyOptions.IosRequired = required;
        policyOptions.IosMinimumSupportedVersion = minimumVersion;
        policyOptions.IosMinimumSupportedBuild = minimumBuild;
        policyOptions.IosDownloadUrl = downloadUrl;
        policyOptions.IosBundleIdentifier = bundleIdentifier;
        policyOptions.IosAppStoreId = appStoreId;

        Assert.True(ValidatePolicyOptions(policyOptions).Failed);
    }

    [Theory]
    [InlineData("app-store", "https://apps.apple.com/au/app/hb-pos/id123456789")]
    [InlineData("testflight", "https://testflight.apple.com/join/AbCdEf12")]
    public void Options_validator_accepts_bound_app_store_and_optional_testflight(
        string distribution,
        string downloadUrl)
    {
        policyOptions.IosDistribution = distribution;
        policyOptions.IosDownloadUrl = downloadUrl;

        Assert.True(ValidatePolicyOptions(policyOptions).Succeeded);
    }

    [Fact]
    public async Task Managed_disabled_native_lane_does_not_fallback_to_legacy_policy()
    {
        var policyService = CreatePolicyService();
        var saved = await policyService.SetLaneAsync(
            PosHandheldUpdateLanes.IosNative,
            new PosHandheldUpdatePolicyRequest
            {
                ExpectedPolicyVersion = 0,
                Enabled = false,
            },
            "admin"
        );
        var service = CreateDecisionService(managedPolicyService: policyService);

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "iOS",
                Version = "1.0.0",
                Build = "100",
            }
        );

        Assert.True(saved.Success);
        Assert.NotNull(decision);
        Assert.Equal(AppUpdateStates.None, decision.State);
        Assert.Equal(AppUpdateStates.None, decision.PolicyVersion);
    }

    [Fact]
    public async Task Managed_android_policy_pins_selected_build_when_newer_build_arrives()
    {
        var selected = await InsertBuildAsync(
            MobileAppKeys.PosHandheld,
            "hb-pos-handheld",
            "handheld-selected",
            "2.0.0",
            "200",
            "https://downloads.example/handheld-selected.apk",
            new DateTime(2026, 8, 10, 3, 0, 0, DateTimeKind.Utc)
        );
        var policyService = CreatePolicyService();
        var saved = await policyService.SetLaneAsync(
            PosHandheldUpdateLanes.AndroidNative,
            new PosHandheldUpdatePolicyRequest
            {
                ExpectedPolicyVersion = 0,
                Enabled = true,
                CandidateId = selected.Id,
                MinimumSupportedVersion = "1.5.0",
                MinimumSupportedBuildNumber = 150,
            },
            "admin"
        );
        await InsertBuildAsync(
            MobileAppKeys.PosHandheld,
            "hb-pos-handheld",
            "handheld-newer",
            "3.0.0",
            "300",
            "https://downloads.example/handheld-newer.apk",
            new DateTime(2026, 8, 10, 4, 0, 0, DateTimeKind.Utc)
        );
        var service = CreateDecisionService(managedPolicyService: policyService);

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "Android",
                Version = "1.0.0",
                Build = "100",
            }
        );

        Assert.True(saved.Success);
        Assert.NotNull(decision);
        Assert.Equal("2.0.0", decision.LatestVersion);
        Assert.Equal("200", decision.LatestBuild);
        Assert.Equal("1", decision.PolicyVersion);
        Assert.Equal(AppUpdateStates.Required, decision.State);
    }

    [Fact]
    public async Task Managed_android_required_policy_fails_closed_when_signer_trust_root_drifts()
    {
        var selected = await InsertBuildAsync(
            MobileAppKeys.PosHandheld,
            "hb-pos-handheld",
            "handheld-signer-bound",
            "2.0.0",
            "200",
            "https://downloads.example/handheld-signer-bound.apk",
            new DateTime(2026, 8, 10, 3, 0, 0, DateTimeKind.Utc)
        );
        var policyService = CreatePolicyService();
        var saved = await policyService.SetLaneAsync(
            PosHandheldUpdateLanes.AndroidNative,
            new PosHandheldUpdatePolicyRequest
            {
                ExpectedPolicyVersion = 0,
                Enabled = true,
                Required = true,
                CandidateId = selected.Id,
            },
            "admin"
        );
        policyOptions.AndroidSigningCertificateSha256 = new string('c', 64);
        var service = CreateDecisionService(managedPolicyService: policyService);

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "Android",
                Version = "1.0.0",
                Build = "100",
            }
        );

        Assert.True(saved.Success);
        Assert.Null(decision);
    }

    [Fact]
    public async Task Managed_ota_required_policy_fails_closed_after_new_head_arrives()
    {
        var buildService = CreateBuildService();
        var first = await buildService.UpsertOtaUpdateAsync(
            CreateOta("hb-pos-handheld", "handheld-first", "android")
        );
        var policyService = CreatePolicyService();
        var saved = await policyService.SetLaneAsync(
            PosHandheldUpdateLanes.AndroidOta,
            new PosHandheldUpdatePolicyRequest
            {
                ExpectedPolicyVersion = 0,
                Enabled = true,
                Required = true,
                CandidateId = first.Data!.Id,
            },
            "admin"
        );
        var next = CreateOta("hb-pos-handheld", "handheld-next", "android");
        next.PublishedAt = new DateTime(2026, 8, 10, 5, 0, 0, DateTimeKind.Utc);
        await buildService.UpsertOtaUpdateAsync(next);
        var service = CreateDecisionService(buildService, policyService);

        var decision = await service.GetOtaDecisionAsync(
            new PosHandheldOtaDecisionRequest
            {
                StoreCode = "0247",
                Platform = "Android",
                RuntimeVersion = "1.0.0",
                CurrentUpdateId = "old",
            }
        );

        Assert.True(saved.Success);
        Assert.Null(decision);
    }

    [Fact]
    public async Task Managed_ios_app_store_policy_uses_verified_candidate_and_minimums()
    {
        var release = new IosAppStoreRelease
        {
            Id = Guid.NewGuid(),
            App = AppUpdateApps.PosHandheld,
            AppStoreId = "123456789",
            BundleIdentifier = "com.hbweb.poshandheld",
            Version = "3.0.0",
            BuildNumber = "300",
            Storefront = "au",
            AppStoreUrl = "https://apps.apple.com/au/app/id123456789",
            AppleVerifiedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false,
        };
        await db.Insertable(release).ExecuteCommandAsync();
        var policyService = CreatePolicyService();
        var saved = await policyService.SetLaneAsync(
            PosHandheldUpdateLanes.IosNative,
            new PosHandheldUpdatePolicyRequest
            {
                ExpectedPolicyVersion = 0,
                Enabled = true,
                CandidateId = release.Id,
                MinimumSupportedVersion = "2.0.0",
                MinimumSupportedBuildNumber = 200,
                ReleaseMessage = "App Store 新版",
            },
            "admin"
        );
        var service = CreateDecisionService(managedPolicyService: policyService);

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "iOS",
                Version = "1.0.0",
                Build = "100",
            }
        );

        Assert.True(saved.Success);
        Assert.NotNull(decision);
        Assert.Equal(AppUpdateStates.Required, decision.State);
        Assert.Equal("app-store", decision.Distribution);
        Assert.Equal("3.0.0", decision.LatestVersion);
        Assert.Equal("300", decision.LatestBuild);
        Assert.Equal("2.0.0", decision.MinimumSupportedVersion);
        Assert.Equal("App Store 新版", decision.ReleaseMessage);
    }

    [Fact]
    public async Task Managed_native_build门槛无法由较旧营销版本候选兑现时fail_closed()
    {
        var release = new IosAppStoreRelease
        {
            Id = Guid.NewGuid(),
            App = AppUpdateApps.PosHandheld,
            AppStoreId = "123456789",
            BundleIdentifier = "com.hbweb.poshandheld",
            Version = "2.0.0",
            BuildNumber = "200",
            Storefront = "au",
            AppStoreUrl = "https://apps.apple.com/au/app/id123456789",
            AppleVerifiedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false,
        };
        await db.Insertable(release).ExecuteCommandAsync();
        var policyService = CreatePolicyService();
        var saved = await policyService.SetLaneAsync(
            PosHandheldUpdateLanes.IosNative,
            new PosHandheldUpdatePolicyRequest
            {
                ExpectedPolicyVersion = 0,
                Enabled = true,
                CandidateId = release.Id,
                MinimumSupportedBuildNumber = 150,
            },
            "admin"
        );
        var service = CreateDecisionService(managedPolicyService: policyService);

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "iOS",
                Version = "3.0.0",
                Build = "100",
            }
        );

        Assert.True(saved.Success);
        Assert.Null(decision);
    }

    [Fact]
    public async Task Missing_managed_lane_keeps_legacy_configuration_fallback()
    {
        var service = CreateDecisionService(managedPolicyService: CreatePolicyService());

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "iOS",
                Version = "1.0.0",
                Build = "100",
            }
        );

        Assert.NotNull(decision);
        Assert.Equal(AppUpdateStates.Optional, decision.State);
        Assert.Equal("12", decision.PolicyVersion);
        Assert.Equal("testflight", decision.Distribution);
    }

    [Fact]
    public void Options_validator_does_not_accept_disabled_example_as_enabled_production_policy()
    {
        var example = new PosHandheldUpdatePolicyOptions
        {
            Enabled = true,
            PolicyVersion = "none",
            EasProjectName = "your-pos-handheld-project",
            IosDistribution = "app-store",
            IosDownloadUrl = null,
            IosBundleIdentifier = "com.hbweb.poshandheld",
            IosAppStoreId = null,
        };

        Assert.True(ValidatePolicyOptions(example).Failed);
    }

    [Theory]
    [InlineData("policy-version")]
    [InlineData("eas-project")]
    [InlineData("android-profile")]
    [InlineData("android-package")]
    [InlineData("android-signing")]
    [InlineData("ios-latest-version")]
    [InlineData("ios-latest-build")]
    [InlineData("ota-channel")]
    public void Options_validator_rejects_incomplete_enabled_release_contract(string field)
    {
        switch (field)
        {
            case "policy-version":
                policyOptions.PolicyVersion = "none";
                break;
            case "eas-project":
                policyOptions.EasProjectName = " ";
                break;
            case "android-profile":
                policyOptions.AndroidProfile = "android-internal";
                break;
            case "android-package":
                policyOptions.AndroidPackageName = "com.example.other";
                break;
            case "android-signing":
                policyOptions.AndroidSigningCertificateSha256 = "not-a-sha256";
                break;
            case "ios-latest-version":
                policyOptions.IosLatestVersion = null;
                break;
            case "ios-latest-build":
                policyOptions.IosLatestBuild = null;
                break;
            case "ota-channel":
                policyOptions.OtaChannel = "production";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field));
        }

        Assert.True(ValidatePolicyOptions(policyOptions).Failed);
    }

    [Theory]
    [InlineData("ios-latest")]
    [InlineData("ios-minimum")]
    [InlineData("android-minimum")]
    public void Options_validator_rejects_zero_build_configuration(string field)
    {
        switch (field)
        {
            case "ios-latest":
                policyOptions.IosLatestBuild = "0";
                break;
            case "ios-minimum":
                policyOptions.IosMinimumSupportedBuild = 0;
                break;
            case "android-minimum":
                policyOptions.AndroidMinimumSupportedBuild = 0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field));
        }

        Assert.True(ValidatePolicyOptions(policyOptions).Failed);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("00")]
    [InlineData("01")]
    [InlineData("9007199254740992")]
    [InlineData("10000000000000000")]
    public void Options_validator_rejects_noncanonical_or_unsafe_ios_latest_build(
        string latestBuild)
    {
        policyOptions.IosLatestBuild = latestBuild;

        Assert.True(ValidatePolicyOptions(policyOptions).Failed);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("9007199254740991")]
    public void Options_validator_accepts_canonical_javascript_safe_ios_latest_build(
        string latestBuild)
    {
        policyOptions.IosLatestBuild = latestBuild;

        Assert.True(ValidatePolicyOptions(policyOptions).Succeeded);
    }

    [Fact]
    public async Task Android_native_decision_rejects_zero_build_response()
    {
        await InsertBuildAsync(
            MobileAppKeys.PosHandheld,
            "hb-pos-handheld",
            "handheld-zero",
            "2.0.0",
            "0",
            "https://downloads.example/handheld-zero.apk",
            new DateTime(2026, 8, 10, 3, 0, 0, DateTimeKind.Utc)
        );
        var service = CreateDecisionService();

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "Android",
                Version = "1.0.0",
                Build = "100",
            }
        );

        Assert.Null(decision);
    }

    [Theory]
    [InlineData("00")]
    [InlineData("01")]
    [InlineData("9007199254740992")]
    [InlineData("10000000000000000")]
    public async Task Android_native_decision_rejects_noncanonical_or_unsafe_latest_build(
        string latestBuild)
    {
        await InsertBuildAsync(
            MobileAppKeys.PosHandheld,
            "hb-pos-handheld",
            "handheld-invalid-build",
            "2.0.0",
            latestBuild,
            "https://downloads.example/handheld-invalid-build.apk",
            new DateTime(2026, 8, 10, 3, 0, 0, DateTimeKind.Utc)
        );
        var service = CreateDecisionService();

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "Android",
                Version = "1.0.0",
                Build = "100",
            }
        );

        Assert.Null(decision);
    }

    [Fact]
    public async Task Android_native_decision_compares_javascript_safe_integer_latest_build()
    {
        const string latestBuild = "9007199254740991";
        await InsertBuildAsync(
            MobileAppKeys.PosHandheld,
            "hb-pos-handheld",
            "handheld-safe-build",
            "2.0.0",
            latestBuild,
            "https://downloads.example/handheld-safe-build.apk",
            new DateTime(2026, 8, 10, 3, 0, 0, DateTimeKind.Utc)
        );
        var service = CreateDecisionService();

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "Android",
                Version = "2.0.0",
                Build = "200",
            }
        );

        Assert.NotNull(decision);
        Assert.Equal(AppUpdateStates.Optional, decision.State);
        Assert.Equal(latestBuild, decision.LatestBuild);
    }

    [Fact]
    public async Task Ios_native_decision_compares_javascript_safe_integer_latest_build()
    {
        const string latestBuild = "9007199254740991";
        policyOptions.IosLatestBuild = latestBuild;
        var service = CreateDecisionService();

        var decision = await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "iOS",
                Version = "3.0.0",
                Build = "1",
            }
        );

        Assert.NotNull(decision);
        Assert.Equal(AppUpdateStates.Optional, decision.State);
        Assert.Equal(latestBuild, decision.LatestBuild);
    }

    [Fact]
    public async Task Ota_default_channel_matches_pos_handheld_publish_contract()
    {
        const string expectedChannel = "pos-handheld-production";
        var options = new PosHandheldUpdatePolicyOptions
        {
            Enabled = true,
            EasProjectName = "hb-pos-handheld",
        };
        MobileAppOtaUpdateQueryDto? capturedQuery = null;
        var buildService = new Mock<IMobileAppBuildService>(MockBehavior.Strict);
        buildService
            .Setup(service => service.GetOtaUpdatesAsync(It.IsAny<MobileAppOtaUpdateQueryDto>()))
            .Callback<MobileAppOtaUpdateQueryDto>(query => capturedQuery = query)
            .ReturnsAsync(
                ApiResponse<PagedResult<MobileAppOtaUpdateDto>>.OK(
                    new PagedResult<MobileAppOtaUpdateDto>()
                )
            );
        var service = new PosHandheldUpdateDecisionService(
            buildService.Object,
            Options.Create(options),
            Options.Create(easOptions),
            NullLogger<PosHandheldUpdateDecisionService>.Instance
        );

        await service.GetOtaDecisionAsync(
            new PosHandheldOtaDecisionRequest
            {
                StoreCode = "0247",
                Platform = "Android",
                RuntimeVersion = "1.0.0",
            }
        );

        Assert.Equal(expectedChannel, options.OtaChannel);
        Assert.Equal(
            expectedChannel,
            Assert.IsType<MobileAppOtaUpdateQueryDto>(capturedQuery).Channel
        );
    }

    [Fact]
    public async Task Android_default_profile_matches_pos_handheld_eas_release_contract()
    {
        var options = new PosHandheldUpdatePolicyOptions
        {
            Enabled = true,
            EasProjectName = "hb-pos-handheld",
        };
        string? capturedProfile = null;
        var buildService = new Mock<IMobileAppBuildService>(MockBehavior.Strict);
        buildService
            .Setup(service =>
                service.GetLatestAsync(MobileAppKeys.PosHandheld, It.IsAny<string>())
            )
            .Callback<string, string>((_, profile) => capturedProfile = profile)
            .ReturnsAsync(ApiResponse<MobileAppBuildDto?>.OK(null));
        var service = new PosHandheldUpdateDecisionService(
            buildService.Object,
            Options.Create(options),
            Options.Create(easOptions),
            NullLogger<PosHandheldUpdateDecisionService>.Instance
        );

        await service.GetNativeDecisionAsync(
            new PosHandheldNativeDecisionRequest
            {
                StoreCode = "0247",
                Platform = "Android",
                Version = "1.0.0",
                Build = "100",
            }
        );

        Assert.Equal("production", options.AndroidProfile);
        Assert.Equal("production", capturedProfile);
    }

    [Fact]
    public async Task Ota_decision_filters_app_project_platform_channel_and_runtime()
    {
        var buildService = CreateBuildService();
        await buildService.UpsertOtaUpdateAsync(
            CreateOta("hb-mobile", "mobile-update", "android")
        );
        await buildService.UpsertOtaUpdateAsync(
            CreateOta("hb-pos-handheld", "handheld-update", "android")
        );
        await buildService.UpsertOtaUpdateAsync(
            CreateOta("hb-pos-handheld", "handheld-ios-update", "ios")
        );
        var service = CreateDecisionService(buildService);

        var decision = await service.GetOtaDecisionAsync(
            new PosHandheldOtaDecisionRequest
            {
                StoreCode = "0247",
                Platform = "Android",
                RuntimeVersion = "1.0.0",
                CurrentUpdateId = "old-update",
            }
        );

        Assert.NotNull(decision);
        Assert.Equal(AppUpdateStates.Optional, decision.State);
        Assert.False(decision.Required);
        Assert.Equal(MobileAppKeys.PosHandheld, decision.AppKey);
        Assert.Equal("hb-pos-handheld", decision.ProjectName);
        Assert.Equal("Android", decision.Platform);
        Assert.Equal("pos-handheld-production", decision.Channel);
        Assert.Equal("1.0.0", decision.RuntimeVersion);
        Assert.Equal("handheld-update", decision.UpdateId);
        Assert.Equal(OtaGroup, decision.UpdateGroupId);
    }

    [Fact]
    public void Internal_controller_has_independent_service_token_route()
    {
        var route = typeof(InternalPosHandheldAppUpdateDecisionsController)
            .GetCustomAttribute<RouteAttribute>();
        var authorize = typeof(InternalPosHandheldAppUpdateDecisionsController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal("api/internal/app-update-decisions/pos-handheld", route!.Template);
        Assert.Equal(
            ServiceApiTokenAuthenticationDefaults.AuthenticationScheme,
            authorize!.AuthenticationSchemes
        );
        Assert.Equal(ServiceApiScopes.ReadAppUpdateDecisions, authorize.Policy);
    }

    [Fact]
    public async Task Internal_controller_returns_service_unavailable_for_unevaluable_decisions()
    {
        var decisionService = new Mock<IPosHandheldUpdateDecisionService>(MockBehavior.Strict);
        decisionService
            .Setup(service => service.GetNativeDecisionAsync(
                It.IsAny<PosHandheldNativeDecisionRequest>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync((PosHandheldNativeDecisionDto)null!);
        decisionService
            .Setup(service => service.GetOtaDecisionAsync(
                It.IsAny<PosHandheldOtaDecisionRequest>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync((PosHandheldOtaDecisionDto)null!);
        var controller = new InternalPosHandheldAppUpdateDecisionsController(
            decisionService.Object
        );

        var native = await controller.Native(
            new PosHandheldNativeDecisionRequest(),
            CancellationToken.None
        );
        var ota = await controller.Ota(
            new PosHandheldOtaDecisionRequest(),
            CancellationToken.None
        );

        Assert.Equal(
            StatusCodes.Status503ServiceUnavailable,
            Assert.IsType<ObjectResult>(native).StatusCode
        );
        Assert.Equal(
            StatusCodes.Status503ServiceUnavailable,
            Assert.IsType<ObjectResult>(ota).StatusCode
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

    private MobileAppBuildService CreateBuildService() =>
        new(
            db,
            Options.Create(easOptions),
            NullLogger<MobileAppBuildService>.Instance
        );

    private PosHandheldUpdateDecisionService CreateDecisionService(
        MobileAppBuildService? buildService = null,
        IPosHandheldUpdatePolicyService? managedPolicyService = null) =>
        new(
            buildService ?? CreateBuildService(),
            Options.Create(policyOptions),
            Options.Create(easOptions),
            NullLogger<PosHandheldUpdateDecisionService>.Instance,
            managedPolicyService
        );

    private PosHandheldUpdatePolicyService CreatePolicyService() =>
        new(
            db,
            Options.Create(policyOptions),
            Options.Create(easOptions),
            NullLogger<PosHandheldUpdatePolicyService>.Instance
        );

    private static ValidateOptionsResult ValidatePolicyOptions(
        PosHandheldUpdatePolicyOptions options) =>
        new PosHandheldUpdatePolicyOptionsValidator().Validate(null, options);

    private async Task<MobileAppBuild> InsertBuildAsync(
        string appKey,
        string projectName,
        string buildId,
        string version,
        string buildVersion,
        string downloadUrl,
        DateTime completedAt)
    {
        var entity = new MobileAppBuild
            {
                Id = Guid.NewGuid(),
                AppKey = appKey,
                EasBuildId = buildId,
                AccountName = "hotbargain",
                ProjectName = projectName,
                Platform = "android",
                Status = "finished",
                BuildProfile = "production",
                AppVersion = version,
                AppBuildVersion = buildVersion,
                ArtifactUrl = downloadUrl,
                CosArtifactUrl = downloadUrl,
                ArtifactSha256 = new string('a', 64),
                ArtifactSize = 2048,
                CosMirrorStatus = MobileAppBuildService.CosMirrorStatusSucceeded,
                CompletedAt = completedAt,
                ExpirationDate = new DateTime(2099, 8, 10, 0, 0, 0, DateTimeKind.Utc),
                ReceivedAt = completedAt,
                CreatedAt = completedAt,
            };
        await db.Insertable(entity).ExecuteCommandAsync();
        return entity;
    }

    private static MobileAppOtaUpdateUpsertDto CreateOta(
        string projectName,
        string updateId,
        string platform) =>
        new()
        {
            ProjectName = projectName,
            UpdateGroupId = OtaGroup,
            UpdateId = updateId,
            AndroidUpdateId = platform == "android" ? updateId : null,
            Channel = "pos-handheld-production",
            Platform = platform,
            RuntimeVersion = "1.0.0",
            PublishedAt = new DateTime(2026, 8, 10, 4, 0, 0, DateTimeKind.Utc),
        };
}
