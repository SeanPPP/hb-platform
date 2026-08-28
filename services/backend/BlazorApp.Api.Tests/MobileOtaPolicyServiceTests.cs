using BlazorApp.Api.Services;
using BlazorApp.Api.Controllers;
using System.Text.Json;
using System.Reflection;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class MobileOtaPolicyServiceTests : IDisposable
{
    private readonly string dbPath = Path.Combine(
        Path.GetTempPath(),
        $"mobile-ota-policy-{Guid.NewGuid():N}.db"
    );
    private readonly ISqlSugarClient db;

    public MobileOtaPolicyServiceTests()
    {
        db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = $"DataSource={dbPath}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        db.CodeFirst.InitTables(
            typeof(AppOtaRelease),
            typeof(MobileOtaPolicy),
            typeof(MobileOtaPolicyRevision),
            typeof(MobileAppOtaUpdate),
            typeof(PosHandheldUpdatePolicy),
            typeof(PosHandheldUpdatePolicyRevision)
        );
    }

    [Fact]
    public async Task 发布事实_完全一致重试幂等且字段漂移冲突()
    {
        var service = CreateReleaseService();
        var request = ReleaseRequest("production", "android", "01-release-android");

        var first = await service.RegisterAsync(request, "publisher");
        var repeated = await service.RegisterAsync(request, "publisher");
        var drifted = await service.RegisterAsync(
            request with { Message = "字段发生漂移" },
            "publisher"
        );

        Assert.True(first.Success);
        Assert.False(first.Data!.Idempotent);
        Assert.True(repeated.Success);
        Assert.True(repeated.Data!.Idempotent);
        Assert.Equal(first.Data.Release.Id, repeated.Data.Release.Id);
        Assert.False(drifted.Success);
        Assert.Equal(AppOtaReleaseErrorCodes.FactConflict, drifted.ErrorCode);
        Assert.Single(await db.Queryable<AppOtaRelease>().ToListAsync());
    }

    [Fact]
    public async Task 发布事实_四条Mobile_lane严格隔离且拒绝不受控channel()
    {
        var service = CreateReleaseService();

        foreach (var environment in new[] { "production", "preview" })
        {
            foreach (var platform in new[] { "android", "ios" })
            {
                var result = await service.RegisterAsync(
                    ReleaseRequest(environment, platform, $"{environment}-{platform}"),
                    "publisher"
                );
                Assert.True(result.Success, result.Message);
            }
        }

        var invalid = await service.RegisterAsync(
            ReleaseRequest("production", "ios", "invalid") with
            {
                ReleaseChannel = "production",
            },
            "publisher"
        );
        var previewIos = await service.ListAsync(
            new AppOtaReleaseQuery
            {
                AppKey = MobileAppKeys.Mobile,
                Environment = "preview",
                Platform = "ios",
            }
        );

        Assert.False(invalid.Success);
        Assert.Equal(AppOtaReleaseErrorCodes.IdentityInvalid, invalid.ErrorCode);
        Assert.Single(previewIos.Data!);
        Assert.Equal("preview", previewIos.Data![0].Environment);
        Assert.Equal("ios", previewIos.Data[0].Platform);
    }

    [Fact]
    public async Task 发布事实_并发完全一致登记收敛为单一事实()
    {
        var service = CreateReleaseService();
        var request = ReleaseRequest("preview", "ios", "concurrent");

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ =>
                service.RegisterAsync(request, "publisher")
            )
        );

        Assert.All(results, result => Assert.True(result.Success, result.Message));
        Assert.Equal(1, results.Count(result => !result.Data!.Idempotent));
        Assert.Equal(7, results.Count(result => result.Data!.Idempotent));
        Assert.Single(await db.Queryable<AppOtaRelease>().ToListAsync());
    }

    [Fact]
    public async Task Mobile策略_保存no_op版本冲突和revision均符合合同()
    {
        var release = (await CreateReleaseService().RegisterAsync(
            ReleaseRequest("production", "ios", "policy"),
            "publisher"
        )).Data!.Release;
        var service = CreatePolicyService();
        var request = new MobileOtaPolicyRequest
        {
            ExpectedPolicyVersion = 0,
            Enabled = true,
            Required = false,
            TargetReleaseId = release.Id,
            ReleaseMessage = " 可选更新 ",
        };

        var first = await service.SetAsync("production", "ios", request, "admin");
        var noOp = await service.SetAsync(
            "production",
            "ios",
            request with { ExpectedPolicyVersion = 1, ReleaseMessage = "可选更新" },
            "admin"
        );
        var conflict = await service.SetAsync(
            "production",
            "ios",
            request with { ExpectedPolicyVersion = 0, Required = true },
            "admin"
        );
        var revisions = await service.GetRevisionsAsync("production", "ios");

        Assert.True(first.Success);
        Assert.Equal(1, first.Data!.PolicyVersion);
        Assert.Equal(release.RuntimeVersion, first.Data.TargetRuntimeVersion);
        Assert.True(noOp.Success);
        Assert.Equal(1, noOp.Data!.PolicyVersion);
        Assert.False(conflict.Success);
        Assert.Equal(AppUpdatePolicyErrorCodes.VersionConflict, conflict.ErrorCode);
        Assert.Single(revisions.Data!);
        Assert.Contains("targetReleaseId", revisions.Data![0].SnapshotJson);
    }

    [Fact]
    public async Task Mobile决策_required目标损坏失败关闭_optional损坏返回可信none()
    {
        var release = (await CreateReleaseService().RegisterAsync(
            ReleaseRequest("preview", "android", "decision"),
            "publisher"
        )).Data!.Release;
        var service = CreatePolicyService();
        var optional = await service.SetAsync(
            "preview",
            "android",
            new MobileOtaPolicyRequest
            {
                ExpectedPolicyVersion = 0,
                Enabled = true,
                Required = false,
                TargetReleaseId = release.Id,
            },
            "admin"
        );
        await db.Deleteable<AppOtaRelease>().Where(item => item.Id == release.Id)
            .ExecuteCommandAsync();

        var optionalDecision = await service.GetDecisionAsync(
            new MobileOtaDecisionRequest
            {
                Platform = "android",
                ClientChannel = "preview",
                RuntimeVersion = "1.0.2",
            }
        );
        var replacement = (await CreateReleaseService().RegisterAsync(
            ReleaseRequest("preview", "android", "required"),
            "publisher"
        )).Data!.Release;
        var required = await service.SetAsync(
            "preview",
            "android",
            new MobileOtaPolicyRequest
            {
                ExpectedPolicyVersion = optional.Data!.PolicyVersion,
                Enabled = true,
                Required = true,
                TargetReleaseId = replacement.Id,
            },
            "admin"
        );
        await db.Deleteable<AppOtaRelease>().Where(item => item.Id == replacement.Id)
            .ExecuteCommandAsync();
        var requiredDecision = await service.GetDecisionAsync(
            new MobileOtaDecisionRequest
            {
                Platform = "android",
                ClientChannel = "preview",
                RuntimeVersion = "1.0.2",
            }
        );

        Assert.NotNull(optionalDecision);
        Assert.Equal(AppUpdateStates.None, optionalDecision!.State);
        Assert.Equal("1", optionalDecision.PolicyVersion);
        Assert.True(required.Success);
        Assert.Null(requiredDecision);
    }

    [Fact]
    public async Task Mobile决策_只向相同环境平台runtime返回目标且当前目标返回none()
    {
        var release = (await CreateReleaseService().RegisterAsync(
            ReleaseRequest("production", "ios", "target"),
            "publisher"
        )).Data!.Release;
        var service = CreatePolicyService();
        await service.SetAsync(
            "production",
            "ios",
            new MobileOtaPolicyRequest
            {
                ExpectedPolicyVersion = 0,
                Enabled = true,
                Required = true,
                TargetReleaseId = release.Id,
                ReleaseMessage = "必须更新",
            },
            "admin"
        );

        var available = await service.GetDecisionAsync(
            new MobileOtaDecisionRequest
            {
                Platform = "ios",
                ClientChannel = "production",
                RuntimeVersion = "1.0.2",
            }
        );
        var wrongRuntime = await service.GetDecisionAsync(
            new MobileOtaDecisionRequest
            {
                Platform = "ios",
                ClientChannel = "production",
                RuntimeVersion = "2.0.0",
            }
        );
        var current = await service.GetDecisionAsync(
            new MobileOtaDecisionRequest
            {
                Platform = "ios",
                ClientChannel = "production",
                RuntimeVersion = "1.0.2",
                CurrentUpdateId = release.UpdateId,
            }
        );
        var groupOnly = await service.GetDecisionAsync(
            new MobileOtaDecisionRequest
            {
                Platform = "ios",
                ClientChannel = "production",
                RuntimeVersion = "1.0.2",
                CurrentUpdateGroupId = release.UpdateGroupId,
            }
        );
        var idWithWrongGroup = await service.GetDecisionAsync(
            new MobileOtaDecisionRequest
            {
                Platform = "ios",
                ClientChannel = "production",
                RuntimeVersion = "1.0.2",
                CurrentUpdateId = release.UpdateId,
                CurrentUpdateGroupId = Guid.NewGuid().ToString(),
            }
        );

        Assert.Equal(AppUpdateStates.Required, available!.State);
        Assert.Equal(release.ReleaseChannel, available.ReleaseChannel);
        Assert.Equal(AppUpdateStates.None, wrongRuntime!.State);
        Assert.Null(wrongRuntime.ReleaseChannel);
        Assert.Equal(AppUpdateStates.None, current!.State);
        Assert.Equal(AppUpdateStates.Required, groupOnly!.State);
        Assert.Equal(AppUpdateStates.Required, idWithWrongGroup!.State);
    }

    [Theory]
    [InlineData("clientChannel", false)]
    [InlineData("releaseChannel", false)]
    [InlineData("easBranch", false)]
    [InlineData("factFingerprint", false)]
    [InlineData("factFingerprint", true)]
    public async Task Mobile决策_目标完整身份损坏时optional返回none_required失败关闭(
        string damagedField,
        bool required
    )
    {
        var release = (await CreateReleaseService().RegisterAsync(
            ReleaseRequest(
                "production",
                "android",
                $"identity-{damagedField.ToLowerInvariant()}-{(required ? "required" : "optional")}"
            ),
            "publisher"
        )).Data!.Release;
        var service = CreatePolicyService();
        var saved = await service.SetAsync(
            "production",
            "android",
            new MobileOtaPolicyRequest
            {
                ExpectedPolicyVersion = 0,
                Enabled = true,
                Required = required,
                TargetReleaseId = release.Id,
            },
            "admin"
        );
        var entity = await db.Queryable<AppOtaRelease>().SingleAsync(item =>
            item.Id == release.Id
        );
        switch (damagedField)
        {
            case "clientChannel":
                entity.ClientChannel = "preview";
                break;
            case "releaseChannel":
                entity.ReleaseChannel = "production";
                break;
            case "easBranch":
                entity.EasBranch = "mobile-production-android-release-other";
                break;
            case "factFingerprint":
                entity.FactFingerprint = new string('f', 64);
                break;
        }
        await db.Updateable(entity).ExecuteCommandAsync();

        var decision = await service.GetDecisionAsync(
            new MobileOtaDecisionRequest
            {
                Platform = "android",
                ClientChannel = "production",
                RuntimeVersion = "1.0.2",
            }
        );

        Assert.True(saved.Success, saved.Message);
        if (required)
        {
            Assert.Null(decision);
        }
        else
        {
            Assert.NotNull(decision);
            Assert.Equal(AppUpdateStates.None, decision!.State);
            Assert.Equal(saved.Data!.PolicyVersion.ToString(), decision.PolicyVersion);
        }
    }

    [Fact]
    public async Task Preflight_legacy_bootstrap需显式请求和服务端开启且覆盖Mobile与手持()
    {
        var service = CreateReleaseService();
        var request = new AppOtaReleasePreflightRequest
        {
            AppKey = MobileAppKeys.PosHandheld,
            Environment = "production",
            ClientChannel = "pos-handheld-production",
            ReleaseChannel = "pos-handheld-production",
            ProjectName = "hb-pos-handheld",
            Platform = "ios",
            RuntimeVersion = "1.0.2",
        };

        var implicitRequest = await service.PreflightAsync(request);
        var disabledBootstrap = await service.PreflightAsync(
            request with { BootstrapLegacyFixedChannel = true }
        );
        var enabledOptions = new EasWebhookOptions
        {
            ProjectAppKeys = new Dictionary<string, string>
            {
                ["hb-mobile"] = MobileAppKeys.Mobile,
                ["hb-pos-handheld"] = MobileAppKeys.PosHandheld,
            },
            AllowLegacyOtaBootstrapRegistration = true,
        };
        var enabledService = new AppOtaReleaseService(
            db,
            Options.Create(enabledOptions),
            NullLogger<AppOtaReleaseService>.Instance
        );
        var handheldBootstrap = await enabledService.PreflightAsync(
            request with { BootstrapLegacyFixedChannel = true }
        );
        var mobileBootstrap = await enabledService.PreflightAsync(
            new AppOtaReleasePreflightRequest
            {
                AppKey = MobileAppKeys.Mobile,
                Environment = "preview",
                ClientChannel = "preview",
                ReleaseChannel = "preview",
                EasBranch = "preview",
                ProjectName = "hb-mobile",
                Platform = "ios",
                RuntimeVersion = "1.0.2",
                BootstrapLegacyFixedChannel = true,
            }
        );

        Assert.False(implicitRequest.Success);
        Assert.False(disabledBootstrap.Success);
        Assert.Equal(
            AppOtaReleaseErrorCodes.LegacyEndpointMigrated,
            disabledBootstrap.ErrorCode
        );
        Assert.True(handheldBootstrap.Success, handheldBootstrap.Message);
        Assert.True(handheldBootstrap.Data!.Valid);
        Assert.True(mobileBootstrap.Success, mobileBootstrap.Message);
        Assert.True(mobileBootstrap.Data!.Valid);
    }

    [Fact]
    public async Task 旧通用登记端点_默认关闭且仅服务端开启后允许显式完整bootstrap()
    {
        var service = new MobileAppBuildService(
            db,
            Options.Create(
                new EasWebhookOptions
                {
                    ProjectAppKeys = new Dictionary<string, string>
                    {
                        ["hb-pos-handheld"] = MobileAppKeys.PosHandheld,
                    },
                }
            ),
            NullLogger<MobileAppBuildService>.Instance
        );
        var dto = new MobileAppOtaUpdateUpsertDto
        {
            ProjectName = "hb-pos-handheld",
            UpdateGroupId = Guid.NewGuid().ToString(),
            UpdateId = Guid.NewGuid().ToString(),
            Platform = "android",
            RuntimeVersion = "1.0.2",
            Channel = "pos-handheld-production-android-release-test",
        };

        var migrated = await service.UpsertOtaUpdateAsync(dto);
        var ordinaryFixed = await service.UpsertOtaUpdateAsync(
            new MobileAppOtaUpdateUpsertDto
            {
                ProjectName = "hb-pos-handheld",
                UpdateGroupId = Guid.NewGuid().ToString(),
                UpdateId = Guid.NewGuid().ToString(),
                Platform = "android",
                RuntimeVersion = "1.0.2",
                Channel = "pos-handheld-production",
                Branch = "pos-handheld-production",
            }
        );
        var disabledBootstrap = await service.UpsertOtaUpdateAsync(
            new MobileAppOtaUpdateUpsertDto
            {
                ProjectName = "hb-pos-handheld",
                UpdateGroupId = Guid.NewGuid().ToString(),
                UpdateId = Guid.NewGuid().ToString(),
                Platform = "android",
                RuntimeVersion = "1.0.2",
                Channel = "pos-handheld-production",
                Branch = "pos-handheld-production",
                BootstrapLegacyFixedChannel = true,
            }
        );
        var enabledService = new MobileAppBuildService(
            db,
            Options.Create(
                new EasWebhookOptions
                {
                    ProjectAppKeys = new Dictionary<string, string>
                    {
                        ["hb-pos-handheld"] = MobileAppKeys.PosHandheld,
                    },
                    AllowLegacyOtaBootstrapRegistration = true,
                }
            ),
            NullLogger<MobileAppBuildService>.Instance
        );
        var enabledBootstrap = await enabledService.UpsertOtaUpdateAsync(
            new MobileAppOtaUpdateUpsertDto
            {
                ProjectName = "hb-pos-handheld",
                UpdateGroupId = Guid.NewGuid().ToString(),
                UpdateId = Guid.NewGuid().ToString(),
                Platform = "android",
                RuntimeVersion = "1.0.2",
                Channel = "pos-handheld-production",
                Branch = "pos-handheld-production",
                BootstrapLegacyFixedChannel = true,
            }
        );

        Assert.False(migrated.Success);
        Assert.Equal(
            AppOtaReleaseErrorCodes.LegacyEndpointMigrated,
            migrated.ErrorCode
        );
        Assert.False(ordinaryFixed.Success);
        Assert.Equal(
            AppOtaReleaseErrorCodes.LegacyEndpointMigrated,
            ordinaryFixed.ErrorCode
        );
        Assert.False(disabledBootstrap.Success);
        Assert.Equal(
            AppOtaReleaseErrorCodes.LegacyEndpointMigrated,
            disabledBootstrap.ErrorCode
        );
        Assert.True(enabledBootstrap.Success, enabledBootstrap.Message);
    }

    [Fact]
    public async Task Preflight_配置projectId映射时必须精确匹配()
    {
        const string expectedProjectId = "3b37541e-6191-460d-9a57-fe6691e206cf";
        var service = new AppOtaReleaseService(
            db,
            Options.Create(
                new EasWebhookOptions
                {
                    ProjectAppKeys = new Dictionary<string, string>
                    {
                        ["hb-mobile"] = MobileAppKeys.Mobile,
                    },
                    ProjectIds = new Dictionary<string, string>
                    {
                        ["hb-mobile"] = expectedProjectId,
                    },
                }
            ),
            NullLogger<AppOtaReleaseService>.Instance
        );
        var request = new AppOtaReleasePreflightRequest
        {
            AppKey = MobileAppKeys.Mobile,
            Environment = "production",
            ClientChannel = "production",
            ReleaseChannel = "mobile-production-ios-release-project-id",
            EasBranch = "mobile-production-ios-release-project-id",
            ProjectName = "hb-mobile",
            EasProjectId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            Platform = "ios",
            RuntimeVersion = "1.0.2",
        };

        var wrong = await service.PreflightAsync(request);
        var matched = await service.PreflightAsync(
            request with { EasProjectId = expectedProjectId }
        );

        Assert.False(wrong.Success);
        Assert.Equal(AppOtaReleaseErrorCodes.IdentityInvalid, wrong.ErrorCode);
        Assert.True(matched.Success, matched.Message);
    }

    [Fact]
    public async Task 发布事实_preflight与register均拒绝branch和release_channel不一致()
    {
        var service = CreateReleaseService();
        var request = ReleaseRequest("production", "ios", "branch-mismatch");
        var preflight = await service.PreflightAsync(
            new AppOtaReleasePreflightRequest
            {
                ReleaseBatchId = request.ReleaseBatchId,
                AppKey = request.AppKey,
                Environment = request.Environment,
                ClientChannel = request.ClientChannel,
                ReleaseChannel = request.ReleaseChannel,
                EasBranch = "production",
                ProjectName = request.ProjectName,
                Platform = request.Platform,
                RuntimeVersion = request.RuntimeVersion,
            }
        );
        var registration = await service.RegisterAsync(
            request with { EasBranch = "production" },
            "publisher"
        );

        Assert.False(preflight.Success);
        Assert.Equal(AppOtaReleaseErrorCodes.IdentityInvalid, preflight.ErrorCode);
        Assert.False(registration.Success);
        Assert.Equal(AppOtaReleaseErrorCodes.IdentityInvalid, registration.ErrorCode);
        Assert.Empty(await db.Queryable<AppOtaRelease>().ToListAsync());
    }

    [Fact]
    public async Task 发布事实_在EAS前拒绝超长runtime_project且登记拒绝超长Dashboard()
    {
        var longProjectName = new string('p', 121);
        var service = new AppOtaReleaseService(
            db,
            Options.Create(
                new EasWebhookOptions
                {
                    ProjectAppKeys = new Dictionary<string, string>
                    {
                        ["hb-mobile"] = MobileAppKeys.Mobile,
                        [longProjectName] = MobileAppKeys.Mobile,
                    },
                }
            ),
            NullLogger<AppOtaReleaseService>.Instance
        );
        var runtimeRequest = ReleaseRequest(
            "production",
            "android",
            "runtime-too-long"
        ) with
        {
            RuntimeVersion = new string('r', 121),
        };
        var projectRequest = ReleaseRequest(
            "production",
            "android",
            "project-too-long"
        ) with
        {
            ProjectName = longProjectName,
        };
        var dashboardRequest = ReleaseRequest(
            "production",
            "android",
            "dashboard-too-long"
        ) with
        {
            DashboardUrl = $"https://example.com/{new string('a', 2048)}",
        };

        var runtimePreflight = await service.PreflightAsync(
            CreatePreflightRequest(runtimeRequest)
        );
        var projectPreflight = await service.PreflightAsync(
            CreatePreflightRequest(projectRequest)
        );
        var runtimeRegistration = await service.RegisterAsync(
            runtimeRequest,
            "publisher"
        );
        var projectRegistration = await service.RegisterAsync(
            projectRequest,
            "publisher"
        );
        var dashboardRegistration = await service.RegisterAsync(
            dashboardRequest,
            "publisher"
        );

        foreach (
            var result in new[]
            {
                runtimePreflight.Success,
                projectPreflight.Success,
                runtimeRegistration.Success,
                projectRegistration.Success,
                dashboardRegistration.Success,
            }
        )
        {
            Assert.False(result);
        }
        Assert.Equal(
            AppOtaReleaseErrorCodes.IdentityInvalid,
            runtimePreflight.ErrorCode
        );
        Assert.Equal(
            AppOtaReleaseErrorCodes.IdentityInvalid,
            projectPreflight.ErrorCode
        );
        Assert.Equal(
            AppOtaReleaseErrorCodes.IdentityInvalid,
            dashboardRegistration.ErrorCode
        );
        Assert.Empty(await db.Queryable<AppOtaRelease>().ToListAsync());
    }

    [Fact]
    public async Task Preflight_rollback来源必须存在且属于相同lane()
    {
        var service = CreateReleaseService();
        var source = (await service.RegisterAsync(
            ReleaseRequest("production", "android", "rollback-source"),
            "publisher"
        )).Data!.Release;
        var releaseChannel = "mobile-preview-android-release-rollback-target";
        var request = new AppOtaReleasePreflightRequest
        {
            AppKey = MobileAppKeys.Mobile,
            Environment = "preview",
            ClientChannel = "preview",
            ReleaseChannel = releaseChannel,
            EasBranch = releaseChannel,
            ProjectName = "hb-mobile",
            Platform = "android",
            RuntimeVersion = "1.0.2",
            RollbackOfReleaseId = source.Id,
        };

        var crossLane = await service.PreflightAsync(request);
        var missing = await service.PreflightAsync(
            request with { RollbackOfReleaseId = Guid.NewGuid() }
        );
        var sameLaneChannel = "mobile-production-android-release-rollback-target";
        var sameLane = await service.PreflightAsync(
            request with
            {
                Environment = "production",
                ClientChannel = "production",
                ReleaseChannel = sameLaneChannel,
                EasBranch = sameLaneChannel,
            }
        );

        Assert.False(crossLane.Success);
        Assert.Equal(AppOtaReleaseErrorCodes.IdentityInvalid, crossLane.ErrorCode);
        Assert.False(missing.Success);
        Assert.Equal(AppOtaReleaseErrorCodes.IdentityInvalid, missing.ErrorCode);
        Assert.True(sameLane.Success, sameLane.Message);
    }

    [Fact]
    public async Task Preflight_手持新release_channel默认关闭()
    {
        var service = CreateReleaseService();
        var releaseChannel = "pos-handheld-production-android-release-gated";

        var result = await service.PreflightAsync(
            new AppOtaReleasePreflightRequest
            {
                AppKey = MobileAppKeys.PosHandheld,
                Environment = "production",
                ClientChannel = "pos-handheld-production",
                ReleaseChannel = releaseChannel,
                EasBranch = releaseChannel,
                ProjectName = "hb-pos-handheld",
                Platform = "android",
                RuntimeVersion = "1.0.2",
            }
        );

        Assert.False(result.Success);
        Assert.Equal(
            AppOtaReleaseErrorCodes.PosHandheldMigrationNotReady,
            result.ErrorCode
        );
    }

    [Fact]
    public async Task Register_手持新事实不能绕过迁移门且已登记精确重试不受关门影响()
    {
        var closedService = CreateReleaseService();
        var releaseChannel = "pos-handheld-production-android-release-register-gate";
        var request = ReleaseRequest("production", "android", "register-gate") with
        {
            AppKey = MobileAppKeys.PosHandheld,
            ClientChannel = "pos-handheld-production",
            ReleaseChannel = releaseChannel,
            EasBranch = releaseChannel,
            ProjectName = "hb-pos-handheld",
        };

        var blocked = await closedService.RegisterAsync(request, "publisher");

        Assert.False(blocked.Success);
        Assert.Equal(
            AppOtaReleaseErrorCodes.PosHandheldMigrationNotReady,
            blocked.ErrorCode
        );

        var openService = CreatePosReleaseService(publishingEnabled: true);
        var registered = await openService.RegisterAsync(request, "publisher");
        var retriedAfterClose = await closedService.RegisterAsync(request, "publisher");
        var driftedAfterClose = await closedService.RegisterAsync(
            request with { Message = "字段发生漂移" },
            "publisher"
        );

        Assert.True(registered.Success, registered.Message);
        Assert.False(registered.Data!.Idempotent);
        Assert.True(retriedAfterClose.Success, retriedAfterClose.Message);
        Assert.True(retriedAfterClose.Data!.Idempotent);
        Assert.False(driftedAfterClose.Success);
        Assert.Equal(AppOtaReleaseErrorCodes.FactConflict, driftedAfterClose.ErrorCode);
        Assert.Single(await db.Queryable<AppOtaRelease>().ToListAsync());
    }

    [Fact]
    public async Task 手持新release激活后可继续发布但目标身份损坏时关闭()
    {
        var releaseService = CreatePosReleaseService(publishingEnabled: true);
        var firstRequest = CreatePosReleaseRequest("first");
        var first = await releaseService.RegisterAsync(firstRequest, "publisher");
        Assert.True(first.Success, first.Message);

        var policyService = new PosHandheldUpdatePolicyService(
            db,
            Options.Create(CreatePosPolicyOptions()),
            Options.Create(CreatePosEasOptions(publishingEnabled: true)),
            NullLogger<PosHandheldUpdatePolicyService>.Instance
        );
        var saved = await policyService.SetLaneAsync(
            PosHandheldUpdateLanes.AndroidOta,
            new PosHandheldUpdatePolicyRequest
            {
                ExpectedPolicyVersion = 0,
                Enabled = true,
                CandidateId = first.Data!.Release.Id,
            },
            "admin"
        );
        Assert.True(saved.Success, saved.Message);

        var secondRequest = CreatePosReleaseRequest("second");
        var preflight = await releaseService.PreflightAsync(
            CreatePreflightRequest(secondRequest)
        );
        var registered = await releaseService.RegisterAsync(secondRequest, "publisher");

        Assert.True(preflight.Success, preflight.Message);
        Assert.True(registered.Success, registered.Message);

        await db.Updateable<AppOtaRelease>()
            .SetColumns(item => item.EasBranch == "pos-handheld-production-android-release-tampered")
            .Where(item => item.Id == first.Data.Release.Id)
            .ExecuteCommandAsync();
        var thirdRequest = CreatePosReleaseRequest("third");
        var blockedPreflight = await releaseService.PreflightAsync(
            CreatePreflightRequest(thirdRequest)
        );
        var blockedRegister = await releaseService.RegisterAsync(
            thirdRequest,
            "publisher"
        );

        Assert.False(blockedPreflight.Success);
        Assert.Equal(
            AppOtaReleaseErrorCodes.PosHandheldMigrationNotReady,
            blockedPreflight.ErrorCode
        );
        Assert.False(blockedRegister.Success);
        Assert.Equal(
            AppOtaReleaseErrorCodes.PosHandheldMigrationNotReady,
            blockedRegister.ErrorCode
        );
    }

    [Fact]
    public async Task Preflight_手持开启后active_target必须先完成匹配legacy回填()
    {
        var publishedAt = DateTime.UtcNow.AddMinutes(-1);
        var updateId = Guid.NewGuid().ToString();
        var raw = new MobileAppOtaUpdate
        {
            Id = Guid.NewGuid(),
            AppKey = MobileAppKeys.PosHandheld,
            ProjectName = "hb-pos-handheld",
            UpdateGroupId = Guid.NewGuid().ToString(),
            UpdateId = updateId,
            AndroidUpdateId = updateId,
            Channel = "pos-handheld-production",
            Branch = "pos-handheld-production",
            Platform = "android",
            RuntimeVersion = "1.0.2",
            Message = "legacy bootstrap",
            PublishedAt = publishedAt,
            CreatedAt = publishedAt,
            IsDeleted = false,
        };
        await db.Insertable(raw).ExecuteCommandAsync();
        var policyOptions = new PosHandheldUpdatePolicyOptions
        {
            Enabled = true,
            EasProjectName = "hb-pos-handheld",
            OtaChannel = "pos-handheld-production",
        };
        var easOptions = new EasWebhookOptions
        {
            ProjectAppKeys = new Dictionary<string, string>
            {
                ["hb-pos-handheld"] = MobileAppKeys.PosHandheld,
            },
            PosHandheldReleaseChannelPublishingEnabled = true,
        };
        var saved = await new PosHandheldUpdatePolicyService(
            db,
            Options.Create(policyOptions),
            Options.Create(easOptions),
            NullLogger<PosHandheldUpdatePolicyService>.Instance
        ).SetLaneAsync(
            PosHandheldUpdateLanes.AndroidOta,
            new PosHandheldUpdatePolicyRequest
            {
                ExpectedPolicyVersion = 0,
                Enabled = true,
                CandidateId = raw.Id,
            },
            "admin"
        );
        var service = new AppOtaReleaseService(
            db,
            Options.Create(easOptions),
            NullLogger<AppOtaReleaseService>.Instance
        );
        var releaseChannel = "pos-handheld-production-android-release-ready";
        var request = new AppOtaReleasePreflightRequest
        {
            AppKey = MobileAppKeys.PosHandheld,
            Environment = "production",
            ClientChannel = "pos-handheld-production",
            ReleaseChannel = releaseChannel,
            EasBranch = releaseChannel,
            ProjectName = "hb-pos-handheld",
            Platform = "android",
            RuntimeVersion = "1.0.2",
        };

        var beforeBackfill = await service.PreflightAsync(request);
        var backfill = new PosHandheldOtaLegacyBackfillService(
            db,
            Options.Create(policyOptions),
            NullLogger<PosHandheldOtaLegacyBackfillService>.Instance
        );
        var prepared = await backfill.PrepareAsync();
        var applied = await backfill.ApplyAsync(
            prepared.Data!.PreparationFingerprint,
            "migration-operator"
        );
        var release = await db.Queryable<AppOtaRelease>().SingleAsync();
        var storedPolicy = await db.Queryable<PosHandheldUpdatePolicy>().SingleAsync();
        var releaseCandidate = PosHandheldUpdatePolicyService.MapOtaCandidate(
            release,
            PosHandheldUpdateLanes.AndroidOta,
            isCurrentHead: true
        );
        var afterBackfill = await service.PreflightAsync(request);

        Assert.True(saved.Success, saved.Message);
        Assert.False(beforeBackfill.Success);
        Assert.Equal(
            AppOtaReleaseErrorCodes.PosHandheldMigrationNotReady,
            beforeBackfill.ErrorCode
        );
        Assert.True(prepared.Success, prepared.Message);
        Assert.True(applied.Success, applied.Message);
        Assert.Equal(release.FactFingerprint, AppOtaReleaseService.ComputeFingerprint(release));
        Assert.NotNull(releaseCandidate);
        Assert.Equal(
            storedPolicy.CandidateFingerprint,
            PosHandheldUpdatePolicyService.ComputeCandidateFingerprint(releaseCandidate!)
        );
        Assert.True(afterBackfill.Success, afterBackfill.Message);
    }

    [Fact]
    public async Task Mobile决策成功JSON固定为十一字段()
    {
        var decision = await CreatePolicyService().GetDecisionAsync(
            new MobileOtaDecisionRequest
            {
                Platform = "Android",
                ClientChannel = "preview",
                RuntimeVersion = "1.0.2",
            }
        );
        var json = JsonSerializer.SerializeToElement(
            decision,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );

        Assert.Equal(11, json.EnumerateObject().Count());
        Assert.Equal("none", json.GetProperty("state").GetString());
        Assert.Equal("mobile", json.GetProperty("appKey").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("releaseChannel").ValueKind);
    }

    [Fact]
    public void Schema与DI_创建不可变事实和追加revision且不自动回填()
    {
        var backendRoot = FindBackendRoot();
        var migrator = File.ReadAllText(
            Path.Combine(backendRoot, "BlazorApp.Api", "Data", "AppUpdatePolicySchemaMigrator.cs")
        );
        var program = File.ReadAllText(
            Path.Combine(backendRoot, "BlazorApp.Api", "Program.cs")
        );

        Assert.Contains("CREATE TABLE [dbo].[AppOtaRelease]", migrator);
        Assert.Contains("UX_AppOtaRelease_App_Environment_Platform_UpdateId", migrator);
        Assert.Contains("CK_AppOtaRelease_RollbackPair", migrator);
        Assert.Contains("TR_AppOtaRelease_Immutable", migrator);
        Assert.Contains("CREATE TABLE [dbo].[MobileOtaPolicy]", migrator);
        Assert.Contains("TR_MobileOtaPolicyRevision_AppendOnly", migrator);
        Assert.DoesNotContain("INSERT INTO [dbo].[AppOtaRelease]", migrator);
        Assert.Contains(
            "Configure<EasWebhookOptions>(builder.Configuration.GetSection(\"EasWebhook\"))",
            program
        );
        Assert.Contains("AddScoped<IAppOtaReleaseService>", program);
        Assert.Contains("AddScoped<IMobileOtaPolicyService>", program);
        Assert.Contains("AddScoped<IPosHandheldOtaLegacyBackfillService>", program);
    }

    [Fact]
    public void EasWebhook示例配置_显式提供projectId并默认关闭迁移写入门()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    FindBackendRoot(),
                    "BlazorApp.Api",
                    "appsettings.EasWebhook.example.json"
                )
            )
        );
        var section = document.RootElement.GetProperty("EasWebhook");
        var projectIds = section.GetProperty("ProjectIds");

        Assert.True(Guid.TryParse(projectIds.GetProperty("hbweb-expo").GetString(), out _));
        Assert.True(Guid.TryParse(projectIds.GetProperty("hb-pos-handheld").GetString(), out _));
        Assert.False(section.GetProperty("AllowLegacyOtaBootstrapRegistration").GetBoolean());
        Assert.False(section.GetProperty("PosHandheldReleaseChannelPublishingEnabled").GetBoolean());
    }

    [Fact]
    public void Legacy回填管理API_仅管理员JWT且apply合同只含预检指纹()
    {
        var prepare = typeof(AppOtaReleasesController).GetMethod(
            "PreparePosHandheldLegacyBackfill"
        );
        var apply = typeof(AppOtaReleasesController).GetMethod(
            "ApplyPosHandheldLegacyBackfill"
        );

        Assert.NotNull(prepare);
        Assert.NotNull(apply);
        Assert.Equal(
            "pos-handheld-legacy-backfill/prepare",
            prepare!.GetCustomAttribute<HttpPostAttribute>()?.Template
        );
        Assert.Equal(
            "pos-handheld-legacy-backfill/apply",
            apply!.GetCustomAttribute<HttpPostAttribute>()?.Template
        );
        foreach (var method in new[] { prepare, apply })
        {
            var authorize = Assert.Single(
                method.GetCustomAttributes<AuthorizeAttribute>()
            );
            Assert.Equal(Permissions.System.ManageAppDownloads, authorize.Policy);
            Assert.True(string.IsNullOrEmpty(authorize.AuthenticationSchemes));
        }

        var applyRequestType = Assert.Single(apply.GetParameters()).ParameterType;
        var applyProperties = applyRequestType.GetProperties(
            BindingFlags.Public | BindingFlags.Instance
        );
        var property = Assert.Single(applyProperties);
        Assert.Equal("PreparationFingerprint", property.Name);
        Assert.Equal(typeof(string), property.PropertyType);
    }

    private AppOtaReleaseService CreateReleaseService() =>
        new(
            db,
            Options.Create(
                new EasWebhookOptions
                {
                    ProjectAppKeys = new Dictionary<string, string>
                    {
                        ["hb-mobile"] = MobileAppKeys.Mobile,
                        ["hb-pos-handheld"] = MobileAppKeys.PosHandheld,
                    },
                }
            ),
            NullLogger<AppOtaReleaseService>.Instance
        );

    private AppOtaReleaseService CreatePosReleaseService(bool publishingEnabled) =>
        new(
            db,
            Options.Create(CreatePosEasOptions(publishingEnabled)),
            NullLogger<AppOtaReleaseService>.Instance
        );

    private static EasWebhookOptions CreatePosEasOptions(bool publishingEnabled) =>
        new()
        {
            ProjectAppKeys = new Dictionary<string, string>
            {
                ["hb-pos-handheld"] = MobileAppKeys.PosHandheld,
            },
            PosHandheldReleaseChannelPublishingEnabled = publishingEnabled,
        };

    private static PosHandheldUpdatePolicyOptions CreatePosPolicyOptions() =>
        new()
        {
            Enabled = true,
            EasProjectName = "hb-pos-handheld",
            OtaChannel = "pos-handheld-production",
        };

    private static AppOtaReleaseRegisterRequest CreatePosReleaseRequest(
        string discriminator
    )
    {
        var releaseChannel =
            $"pos-handheld-production-android-release-{discriminator}";
        return new AppOtaReleaseRegisterRequest
        {
            ReleaseBatchId = Guid.NewGuid(),
            AppKey = MobileAppKeys.PosHandheld,
            Environment = "production",
            ClientChannel = "pos-handheld-production",
            ReleaseChannel = releaseChannel,
            EasBranch = releaseChannel,
            ProjectName = "hb-pos-handheld",
            Platform = "android",
            RuntimeVersion = "1.0.2",
            UpdateGroupId = Guid.NewGuid().ToString(),
            UpdateId = Guid.NewGuid().ToString(),
            Message = "手持 POS 发布",
            PublishedAtUtc = DateTime.UtcNow,
        };
    }

    private static AppOtaReleasePreflightRequest CreatePreflightRequest(
        AppOtaReleaseRegisterRequest request
    ) =>
        new()
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
            RollbackOfReleaseId = request.RollbackOfReleaseId,
        };

    private MobileOtaPolicyService CreatePolicyService() =>
        new(db, NullLogger<MobileOtaPolicyService>.Instance);

    private static AppOtaReleaseRegisterRequest ReleaseRequest(
        string environment,
        string platform,
        string discriminator
    )
    {
        var releaseChannel = $"mobile-{environment}-{platform}-release-{discriminator}";
        return new AppOtaReleaseRegisterRequest
        {
            ReleaseBatchId = Guid.NewGuid(),
            AppKey = MobileAppKeys.Mobile,
            Environment = environment,
            ClientChannel = environment,
            ReleaseChannel = releaseChannel,
            EasBranch = releaseChannel,
            ProjectName = "hb-mobile",
            Platform = platform,
            RuntimeVersion = "1.0.2",
            UpdateGroupId = Guid.NewGuid().ToString(),
            UpdateId = Guid.NewGuid().ToString(),
            Message = "发布说明",
            GitCommitHash = "01234567",
            DashboardUrl = "https://expo.dev/accounts/hb/projects/mobile/updates/test",
            PublishedAtUtc = DateTime.UtcNow,
        };
    }

    private static string FindBackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "BlazorApp.Api")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到 services/backend");
    }

    public void Dispose()
    {
        db.Dispose();
        if (File.Exists(dbPath))
        {
            File.Delete(dbPath);
        }
    }
}
