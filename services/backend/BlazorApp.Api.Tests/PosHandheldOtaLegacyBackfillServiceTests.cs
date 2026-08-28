using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class PosHandheldOtaLegacyBackfillServiceTests : IDisposable
{
    private readonly string dbPath = Path.Combine(
        Path.GetTempPath(),
        $"pos-handheld-ota-backfill-{Guid.NewGuid():N}.db"
    );
    private readonly ISqlSugarClient db;
    private readonly PosHandheldUpdatePolicyOptions policyOptions = new()
    {
        Enabled = true,
        PolicyVersion = "legacy-1",
        EasProjectName = "hb-pos-handheld",
        AndroidProfile = "production",
        AndroidPackageName = "com.hbweb.poshandheld",
        AndroidSigningCertificateSha256 = new string('a', 64),
        IosBundleIdentifier = "com.hbweb.poshandheld",
        OtaChannel = "pos-handheld-production",
    };
    private readonly EasWebhookOptions easOptions = new()
    {
        ProjectAppKeys = new Dictionary<string, string>
        {
            ["hb-pos-handheld"] = MobileAppKeys.PosHandheld,
        },
    };

    public PosHandheldOtaLegacyBackfillServiceTests()
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
            typeof(MobileAppBuild),
            typeof(MobileAppOtaUpdate),
            typeof(AppOtaRelease),
            typeof(IosAppStoreRelease),
            typeof(PosHandheldUpdatePolicy),
            typeof(PosHandheldUpdatePolicyRevision)
        );
    }

    [Fact]
    public async Task Prepare_active_target不再是runtime真实head时中止()
    {
        var selected = await SeedOtaAsync(DateTime.UtcNow.AddMinutes(-2));
        var policyService = CreatePolicyService();
        var saved = await policyService.SetLaneAsync(
            PosHandheldUpdateLanes.AndroidOta,
            new PosHandheldUpdatePolicyRequest
            {
                ExpectedPolicyVersion = 0,
                Enabled = true,
                CandidateId = selected.Id,
            },
            "admin"
        );
        await SeedOtaAsync(DateTime.UtcNow.AddMinutes(-1));

        var prepared = await CreateBackfillService().PrepareAsync();

        Assert.True(saved.Success);
        Assert.False(prepared.Success);
        var preview = Assert.IsType<PosHandheldOtaLegacyBackfillPreviewDto>(
            prepared.Details
        );
        Assert.Contains(preview.BlockingReasons, reason => reason.Contains("真实 head"));
        Assert.Empty(await db.Queryable<AppOtaRelease>().ToListAsync());
    }

    [Fact]
    public async Task Apply_锁内重验后保留原Id且重复prepare_apply幂等()
    {
        var selected = await SeedOtaAsync(DateTime.UtcNow.AddMinutes(-1));
        var saved = await CreatePolicyService().SetLaneAsync(
            PosHandheldUpdateLanes.AndroidOta,
            new PosHandheldUpdatePolicyRequest
            {
                ExpectedPolicyVersion = 0,
                Enabled = true,
                Required = true,
                CandidateId = selected.Id,
            },
            "admin"
        );
        var service = CreateBackfillService();
        var prepared = await service.PrepareAsync();

        var applied = await service.ApplyAsync(
            prepared.Data!.PreparationFingerprint,
            "migration-operator"
        );
        var preparedAgain = await service.PrepareAsync();
        var appliedAgain = await service.ApplyAsync(
            preparedAgain.Data!.PreparationFingerprint,
            "migration-operator"
        );
        var release = await db.Queryable<AppOtaRelease>().SingleAsync();

        Assert.True(saved.Success);
        Assert.True(prepared.Success);
        Assert.True(applied.Success);
        Assert.Equal(1, applied.Data!.Inserted);
        Assert.Equal(selected.Id, release.Id);
        Assert.True(release.Legacy);
        Assert.Equal("pos-handheld-production", release.ReleaseChannel);
        Assert.True(preparedAgain.Success);
        Assert.True(appliedAgain.Success);
        Assert.Equal(0, appliedAgain.Data!.Inserted);
        Assert.Equal(1, appliedAgain.Data.AlreadyBackfilled);
    }

    [Fact]
    public async Task Prepare_rollback来源缺失或不存在时中止()
    {
        var rollback = await SeedOtaAsync(DateTime.UtcNow.AddMinutes(-1));
        rollback.IsRollback = true;
        rollback.RollbackOfGroupId = null;
        await db.Updateable(rollback).ExecuteCommandAsync();

        var missing = await CreateBackfillService().PrepareAsync();

        rollback.RollbackOfGroupId = Guid.NewGuid().ToString("D");
        await db.Updateable(rollback).ExecuteCommandAsync();
        var unknown = await CreateBackfillService().PrepareAsync();

        Assert.False(missing.Success);
        var missingPreview = Assert.IsType<PosHandheldOtaLegacyBackfillPreviewDto>(
            missing.Details
        );
        Assert.Contains(
            missingPreview.BlockingReasons,
            reason => reason.Contains("rollback")
        );
        Assert.False(unknown.Success);
        var unknownPreview = Assert.IsType<PosHandheldOtaLegacyBackfillPreviewDto>(
            unknown.Details
        );
        Assert.Contains(
            unknownPreview.BlockingReasons,
            reason => reason.Contains("rollback")
        );
        Assert.Empty(await db.Queryable<AppOtaRelease>().ToListAsync());
    }

    private PosHandheldUpdatePolicyService CreatePolicyService() =>
        new(
            db,
            Options.Create(policyOptions),
            Options.Create(easOptions),
            NullLogger<PosHandheldUpdatePolicyService>.Instance
        );

    private PosHandheldOtaLegacyBackfillService CreateBackfillService() =>
        new(
            db,
            Options.Create(policyOptions),
            NullLogger<PosHandheldOtaLegacyBackfillService>.Instance
        );

    private async Task<MobileAppOtaUpdate> SeedOtaAsync(DateTime publishedAt)
    {
        var updateId = Guid.NewGuid().ToString();
        var entity = new MobileAppOtaUpdate
        {
            Id = Guid.NewGuid(),
            AppKey = MobileAppKeys.PosHandheld,
            ProjectName = "hb-pos-handheld",
            UpdateGroupId = Guid.NewGuid().ToString(),
            UpdateId = updateId,
            AndroidUpdateId = updateId,
            Channel = "pos-handheld-production",
            Branch = "production",
            Platform = "android",
            RuntimeVersion = "1.0.2",
            Message = "legacy update",
            PublishedAt = publishedAt,
            CreatedAt = publishedAt,
            IsDeleted = false,
        };
        await db.Insertable(entity).ExecuteCommandAsync();
        return entity;
    }

    public void Dispose()
    {
        db.Dispose();
        if (File.Exists(dbPath))
        {
            SqliteTempFileCleanup.DeleteIfExists(dbPath);
        }
    }
}
