using System.Security.Cryptography;
using System.Text;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class MobileAppBuildServiceTests : IDisposable
{
    private const string Secret = "test-eas-secret";
    private const string OtaGroup1 = "11111111-1111-1111-8111-111111111111";
    private const string OtaGroupRepeat = "22222222-2222-2222-8222-222222222222";
    private const string OtaGroupBefore = "33333333-3333-3333-8333-333333333333";
    private const string OtaProdOld = "44444444-4444-4444-8444-444444444444";
    private const string OtaProdNew = "55555555-5555-5555-8555-555555555555";
    private const string OtaProdRuntime2 = "66666666-6666-6666-8666-666666666666";
    private const string OtaPreviewNew = "77777777-7777-7777-8777-777777777777";
    private const string OtaGroupRollback = "88888888-8888-8888-8888-888888888888";
    private const string OtaNewerThanApk = "99999999-9999-9999-8999-999999999999";
    private readonly string _dbPath;
    private readonly ISqlSugarClient _db;

    public MobileAppBuildServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"mobile-app-build-{Guid.NewGuid():N}.db");
        _db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = $"DataSource={_dbPath}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        _db.CodeFirst.InitTables<MobileAppBuild, MobileAppOtaUpdate>();
    }

    [Fact]
    public async Task EasWebhook_签名正确_AndroidFinished写入构建记录()
    {
        var body = CreatePayload(easBuildId: "build-1", completedAt: "2026-06-15T01:00:00Z");
        var controller = CreateController(body, CreateSignature(body));

        var result = await controller.EasWebhook();

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse<MobileAppBuildWebhookResultDto>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("saved", response.Data!.Action);

        var saved = await _db.Queryable<MobileAppBuild>().SingleAsync();
        Assert.Equal("build-1", saved.EasBuildId);
        Assert.Equal("hotbargain", saved.AccountName);
        Assert.Equal("hb-mobile", saved.ProjectName);
        Assert.Equal("android", saved.Platform);
        Assert.Equal("finished", saved.Status);
        Assert.Equal("production", saved.BuildProfile);
        Assert.Equal("https://expo.dev/artifacts/eas/build-1.apk", saved.ArtifactUrl);
    }

    [Fact]
    public async Task EasWebhook_镜像到Cos成功_保存Cos地址且下载优先Cos()
    {
        var cosUrl = "https://hb-sales-2019-1300114625.cos.ap-sydney.myqcloud.com/mobile-app-builds/production/build-cos.apk";
        var mirror = FakeMobileAppBuildArtifactMirror.Success(
            cosUrl,
            "mobile-app-builds/production/build-cos.apk",
            new DateTime(2026, 6, 23, 1, 0, 0, DateTimeKind.Utc)
        );
        var body = CreatePayload(
            easBuildId: "build-cos",
            artifactUrl: "https://expo.dev/artifacts/eas/build-cos.apk",
            expirationDate: "2000-01-01T00:00:00Z"
        );
        var controller = CreateController(body, CreateSignature(body));

        var result = await controller.EasWebhook();

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse<MobileAppBuildWebhookResultDto>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("saved", response.Data!.Action);
        Assert.Equal(0, mirror.Calls);

        var saved = await _db.Queryable<MobileAppBuild>().SingleAsync();
        Assert.Null(saved.CosArtifactUrl);
        Assert.Equal(MobileAppBuildService.CosMirrorStatusPending, saved.CosMirrorStatus);
        Assert.Null(saved.CosMirrorError);

        var service = CreateService();
        var job = await service.ClaimNextCosMirrorJobAsync(
            new DateTime(2026, 6, 23, 0, 0, 0, DateTimeKind.Utc),
            3,
            TimeSpan.FromMinutes(30)
        );
        Assert.NotNull(job);
        var mirrored = await mirror.MirrorAsync(job!);
        await service.CompleteCosMirrorSuccessAsync(job!, mirrored);

        saved = await _db.Queryable<MobileAppBuild>().SingleAsync();
        Assert.Equal(1, mirror.Calls);
        Assert.Equal(cosUrl, saved.CosArtifactUrl);
        Assert.Equal("mobile-app-builds/production/build-cos.apk", saved.CosObjectKey);
        Assert.Equal(new DateTime(2026, 6, 23, 1, 0, 0, DateTimeKind.Utc), saved.CosMirroredAt);
        Assert.Equal(MobileAppBuildService.CosMirrorStatusSucceeded, saved.CosMirrorStatus);
        Assert.Equal(1, saved.CosMirrorAttempts);
        Assert.Null(saved.CosMirrorError);

        var latest = await service.GetLatestAsync("production");
        Assert.True(latest.Success);
        Assert.NotNull(latest.Data);
        Assert.Equal(cosUrl, latest.Data!.ArtifactUrl);
        Assert.Equal(cosUrl, latest.Data.CosArtifactUrl);

        var downloadResult = await CreateController("{}", CreateSignature("{}")).AndroidLatestDownload("production");
        var redirect = Assert.IsType<RedirectResult>(downloadResult);
        Assert.Equal(cosUrl, redirect.Url);
    }

    [Fact]
    public async Task EasWebhook_重复Build且Cos已存在_不重复镜像()
    {
        var cosUrl = "https://hb-sales-2019-1300114625.cos.ap-sydney.myqcloud.com/mobile-app-builds/production/build-repeat-cos.apk";
        var mirror = FakeMobileAppBuildArtifactMirror.Success(
            cosUrl,
            "mobile-app-builds/production/build-repeat-cos.apk",
            new DateTime(2026, 6, 23, 1, 0, 0, DateTimeKind.Utc)
        );
        var body = CreatePayload(
            easBuildId: "build-repeat-cos",
            artifactUrl: "https://expo.dev/artifacts/eas/build-repeat-cos.apk"
        );
        var service = CreateService();

        await service.HandleEasWebhookAsync(body);
        var job = await service.ClaimNextCosMirrorJobAsync(
            new DateTime(2026, 6, 23, 0, 0, 0, DateTimeKind.Utc),
            3,
            TimeSpan.FromMinutes(30)
        );
        Assert.NotNull(job);
        await service.CompleteCosMirrorSuccessAsync(job!, await mirror.MirrorAsync(job!));
        await service.HandleEasWebhookAsync(body);

        var nextJob = await service.ClaimNextCosMirrorJobAsync(
            new DateTime(2026, 6, 23, 0, 10, 0, DateTimeKind.Utc),
            3,
            TimeSpan.FromMinutes(30)
        );

        Assert.Equal(1, mirror.Calls);
        Assert.Null(nextJob);
        var saved = await _db.Queryable<MobileAppBuild>().SingleAsync();
        Assert.Equal(cosUrl, saved.CosArtifactUrl);
        Assert.Null(saved.CosMirrorError);
    }

    [Fact]
    public async Task EasWebhook_镜像到Cos失败_仍保存Eas地址并记录错误()
    {
        var mirror = FakeMobileAppBuildArtifactMirror.Failure(new InvalidOperationException("COS unavailable"));
        var body = CreatePayload(
            easBuildId: "build-cos-failed",
            artifactUrl: "https://expo.dev/artifacts/eas/build-cos-failed.apk"
        );
        var controller = CreateController(body, CreateSignature(body));

        var result = await controller.EasWebhook();

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse<MobileAppBuildWebhookResultDto>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("saved", response.Data!.Action);

        var service = CreateService();
        var job = await service.ClaimNextCosMirrorJobAsync(
            new DateTime(2026, 6, 23, 0, 0, 0, DateTimeKind.Utc),
            3,
            TimeSpan.FromMinutes(30)
        );
        Assert.NotNull(job);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mirror.MirrorAsync(job!)
        );
        await service.CompleteCosMirrorFailureAsync(job!, exception);

        var saved = await _db.Queryable<MobileAppBuild>().SingleAsync();
        Assert.Equal("https://expo.dev/artifacts/eas/build-cos-failed.apk", saved.ArtifactUrl);
        Assert.Null(saved.CosArtifactUrl);
        Assert.Null(saved.CosObjectKey);
        Assert.Null(saved.CosMirroredAt);
        Assert.Equal(MobileAppBuildService.CosMirrorStatusFailed, saved.CosMirrorStatus);
        Assert.Equal(1, saved.CosMirrorAttempts);
        Assert.Contains("COS unavailable", saved.CosMirrorError);

        var latest = await service.GetLatestAsync("production");
        Assert.Equal("https://expo.dev/artifacts/eas/build-cos-failed.apk", latest.Data!.ArtifactUrl);
    }

    [Fact]
    public async Task EasWebhook_Artifact不是允许域名_忽略且不入库()
    {
        var body = CreatePayload(
            easBuildId: "build-untrusted-artifact",
            artifactUrl: "https://example.com/build.apk"
        );
        var controller = CreateController(body, CreateSignature(body));

        var result = await controller.EasWebhook();

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse<MobileAppBuildWebhookResultDto>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("ignored", response.Data!.Action);
        Assert.Equal("artifact_url_not_allowed", response.Data.Reason);
        Assert.Equal(0, await _db.Queryable<MobileAppBuild>().CountAsync());
    }

    [Fact]
    public async Task EasWebhook_镜像判定Artifact不安全_不作为Latest下载()
    {
        var mirror = FakeMobileAppBuildArtifactMirror.Failure(
            new MobileAppBuildArtifactMirrorException("APK 下载地址缺少 Content-Length", isDownloadUnsafe: true)
        );
        var body = CreatePayload(
            easBuildId: "build-unsafe-artifact",
            artifactUrl: "https://expo.dev/artifacts/eas/build-unsafe-artifact.apk"
        );
        var controller = CreateController(body, CreateSignature(body));

        var result = await controller.EasWebhook();

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse<MobileAppBuildWebhookResultDto>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("saved", response.Data!.Action);

        var service = CreateService();
        var job = await service.ClaimNextCosMirrorJobAsync(
            new DateTime(2026, 6, 23, 0, 0, 0, DateTimeKind.Utc),
            3,
            TimeSpan.FromMinutes(30)
        );
        Assert.NotNull(job);
        var exception = await Assert.ThrowsAsync<MobileAppBuildArtifactMirrorException>(() =>
            mirror.MirrorAsync(job!)
        );
        await service.CompleteCosMirrorFailureAsync(job!, exception);

        var saved = await _db.Queryable<MobileAppBuild>().SingleAsync();
        Assert.Null(saved.CosArtifactUrl);
        Assert.Equal(MobileAppBuildService.CosMirrorStatusUnsafe, saved.CosMirrorStatus);
        Assert.Equal(1, saved.CosMirrorAttempts);
        Assert.StartsWith("UNSAFE_ARTIFACT:", saved.CosMirrorError);

        var retryJob = await service.ClaimNextCosMirrorJobAsync(
            new DateTime(2026, 6, 23, 0, 10, 0, DateTimeKind.Utc),
            3,
            TimeSpan.FromMinutes(30)
        );
        Assert.Null(retryJob);

        var latest = await service.GetLatestAsync("production");
        Assert.True(latest.Success);
        Assert.Null(latest.Data);

        var download = await CreateController("{}", CreateSignature("{}")).AndroidLatestDownload("production");
        Assert.IsType<NotFoundObjectResult>(download);
    }

    [Fact]
    public async Task Cos镜像Unsafe即使存在Cos地址_也不作为Latest下载()
    {
        var service = CreateService();
        await _db.Insertable(
            new MobileAppBuild
            {
                Id = Guid.NewGuid(),
                EasBuildId = "build-unsafe-with-cos",
                AccountName = "hotbargain",
                ProjectName = "hb-mobile",
                Platform = "android",
                Status = "finished",
                BuildProfile = "production",
                ArtifactUrl = "https://expo.dev/artifacts/eas/build-unsafe-with-cos.apk",
                CosArtifactUrl = "https://hb-sales-2019-1300114625.cos.ap-sydney.myqcloud.com/mobile-app-builds/production/build-unsafe-with-cos.apk",
                CosObjectKey = "mobile-app-builds/production/build-unsafe-with-cos.apk",
                CosMirrorStatus = MobileAppBuildService.CosMirrorStatusUnsafe,
                CosMirrorError = "UNSAFE_ARTIFACT: bad content",
                CompletedAt = new DateTime(2026, 6, 23, 1, 0, 0, DateTimeKind.Utc),
                ExpirationDate = new DateTime(2099, 7, 15, 0, 0, 0, DateTimeKind.Utc),
                ReceivedAt = new DateTime(2026, 6, 23, 1, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 6, 23, 1, 0, 0, DateTimeKind.Utc),
            }
        ).ExecuteCommandAsync();

        var latest = await service.GetLatestAsync("production");
        var bound = await service.GetByBuildIdAsync("build-unsafe-with-cos", "production");
        var latestDownload = await CreateController("{}", CreateSignature("{}")).AndroidLatestDownload("production");
        var boundDownload = await CreateController("{}", CreateSignature("{}")).AndroidBuildDownload(
            "build-unsafe-with-cos",
            "production"
        );

        Assert.True(latest.Success);
        Assert.Null(latest.Data);
        Assert.True(bound.Success);
        Assert.Null(bound.Data);
        Assert.IsType<NotFoundObjectResult>(latestDownload);
        Assert.IsType<NotFoundObjectResult>(boundDownload);
    }

    [Fact]
    public async Task Cos镜像Artifact临时失败_保留EasFallback且允许重试()
    {
        var service = CreateService();
        await service.HandleEasWebhookAsync(
            CreatePayload(
                easBuildId: "build-transient-artifact",
                artifactUrl: "https://expo.dev/artifacts/eas/build-transient-artifact.apk"
            )
        );
        var now = new DateTime(2026, 6, 23, 0, 0, 0, DateTimeKind.Utc);

        var first = await service.ClaimNextCosMirrorJobAsync(now, 3, TimeSpan.FromMinutes(30));
        Assert.NotNull(first);
        await service.CompleteCosMirrorFailureAsync(
            first!,
            new MobileAppBuildArtifactMirrorException(
                "APK 下载地址返回 HTTP 403 Forbidden",
                isDownloadUnsafe: false
            )
        );

        var saved = await _db.Queryable<MobileAppBuild>().SingleAsync();
        Assert.Equal(MobileAppBuildService.CosMirrorStatusFailed, saved.CosMirrorStatus);
        Assert.Contains("HTTP 403", saved.CosMirrorError);

        var latest = await service.GetLatestAsync("production");
        Assert.True(latest.Success);
        Assert.Equal("build-transient-artifact", latest.Data!.EasBuildId);
        Assert.Equal("https://expo.dev/artifacts/eas/build-transient-artifact.apk", latest.Data.ArtifactUrl);

        var retry = await service.ClaimNextCosMirrorJobAsync(
            now.AddMinutes(1),
            3,
            TimeSpan.FromMinutes(30)
        );
        Assert.NotNull(retry);
        Assert.Equal(2, retry!.CosMirrorAttempts);
    }

    [Fact]
    public async Task Cos镜像普通失败_三次内可重试超过后不再认领()
    {
        var service = CreateService();
        await service.HandleEasWebhookAsync(
            CreatePayload(
                easBuildId: "build-retry",
                artifactUrl: "https://expo.dev/artifacts/eas/build-retry.apk"
            )
        );
        var now = new DateTime(2026, 6, 23, 0, 0, 0, DateTimeKind.Utc);

        var first = await service.ClaimNextCosMirrorJobAsync(now, 3, TimeSpan.FromMinutes(30));
        Assert.NotNull(first);
        await service.CompleteCosMirrorFailureAsync(first!, new InvalidOperationException("first failed"));

        var second = await service.ClaimNextCosMirrorJobAsync(now.AddMinutes(1), 3, TimeSpan.FromMinutes(30));
        Assert.NotNull(second);
        Assert.Equal(2, second!.CosMirrorAttempts);
        await service.CompleteCosMirrorFailureAsync(second, new HttpRequestException("second failed"));

        var third = await service.ClaimNextCosMirrorJobAsync(now.AddMinutes(2), 3, TimeSpan.FromMinutes(30));
        Assert.NotNull(third);
        Assert.Equal(3, third!.CosMirrorAttempts);
        await service.CompleteCosMirrorFailureAsync(third, new TimeoutException("third failed"));

        var exhausted = await service.ClaimNextCosMirrorJobAsync(now.AddMinutes(3), 3, TimeSpan.FromMinutes(30));
        var saved = await _db.Queryable<MobileAppBuild>().SingleAsync();
        Assert.Null(exhausted);
        Assert.Equal(MobileAppBuildService.CosMirrorStatusFailed, saved.CosMirrorStatus);
        Assert.Equal(3, saved.CosMirrorAttempts);
        Assert.Contains("third failed", saved.CosMirrorError);

        var latest = await service.GetLatestAsync("production");
        Assert.Equal("https://expo.dev/artifacts/eas/build-retry.apk", latest.Data!.ArtifactUrl);
    }

    [Fact]
    public async Task Cos镜像Running超过三十分钟_可重新认领()
    {
        var service = CreateService();
        await service.HandleEasWebhookAsync(
            CreatePayload(
                easBuildId: "build-stale-running",
                artifactUrl: "https://expo.dev/artifacts/eas/build-stale-running.apk"
            )
        );
        var now = new DateTime(2026, 6, 23, 0, 0, 0, DateTimeKind.Utc);

        var first = await service.ClaimNextCosMirrorJobAsync(now, 3, TimeSpan.FromMinutes(30));
        Assert.NotNull(first);

        var tooSoon = await service.ClaimNextCosMirrorJobAsync(
            now.AddMinutes(10),
            3,
            TimeSpan.FromMinutes(30)
        );
        var stale = await service.ClaimNextCosMirrorJobAsync(
            now.AddMinutes(31),
            3,
            TimeSpan.FromMinutes(30)
        );

        Assert.Null(tooSoon);
        Assert.NotNull(stale);
        Assert.Equal(2, stale!.CosMirrorAttempts);
        Assert.Equal(MobileAppBuildService.CosMirrorStatusRunning, stale.CosMirrorStatus);
    }

    [Fact]
    public async Task Cos镜像失败回写_已有成功Cos地址时_不覆盖下载地址()
    {
        var service = CreateService();
        var buildId = Guid.NewGuid();
        var artifactUrl = "https://expo.dev/artifacts/eas/build-concurrent.apk";
        var cosUrl = "https://hb-sales-2019-1300114625.cos.ap-sydney.myqcloud.com/mobile-app-builds/production/build-concurrent.apk";
        await _db.Insertable(
            new MobileAppBuild
            {
                Id = buildId,
                EasBuildId = "build-concurrent",
                AccountName = "hotbargain",
                ProjectName = "hb-mobile",
                Platform = "android",
                Status = "finished",
                BuildProfile = "production",
                ArtifactUrl = artifactUrl,
                CosArtifactUrl = cosUrl,
                CosObjectKey = "mobile-app-builds/production/build-concurrent.apk",
                CosMirroredAt = new DateTime(2026, 6, 23, 1, 0, 0, DateTimeKind.Utc),
                CosMirrorStatus = MobileAppBuildService.CosMirrorStatusSucceeded,
                CompletedAt = new DateTime(2026, 6, 23, 1, 0, 0, DateTimeKind.Utc),
                ExpirationDate = new DateTime(2099, 7, 15, 0, 0, 0, DateTimeKind.Utc),
                ReceivedAt = new DateTime(2026, 6, 23, 1, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 6, 23, 1, 0, 0, DateTimeKind.Utc),
            }
        ).ExecuteCommandAsync();

        // 模拟另一个并发 webhook 持有的旧实体：它没有 COS 字段，但镜像失败后尝试回写错误。
        await service.CompleteCosMirrorFailureAsync(
            new MobileAppBuild
            {
                Id = buildId,
                EasBuildId = "build-concurrent",
                ArtifactUrl = artifactUrl,
            },
            new HttpRequestException("COS unavailable")
        );

        var saved = await _db.Queryable<MobileAppBuild>().SingleAsync(x => x.Id == buildId);
        Assert.Equal(cosUrl, saved.CosArtifactUrl);
        Assert.Equal("mobile-app-builds/production/build-concurrent.apk", saved.CosObjectKey);
        Assert.Null(saved.CosMirrorError);
    }

    [Fact]
    public async Task EasWebhook_签名基于原始字节_带Bom请求仍可通过()
    {
        var body = CreatePayload(easBuildId: "build-bom");
        var bodyBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(body)).ToArray();
        var controller = CreateController(bodyBytes, CreateSignature(bodyBytes));

        var result = await controller.EasWebhook();

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse<MobileAppBuildWebhookResultDto>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("saved", response.Data!.Action);
        Assert.Equal(1, await _db.Queryable<MobileAppBuild>().CountAsync());
    }

    [Fact]
    public async Task EasWebhook_签名错误_返回Unauthorized且不入库()
    {
        var body = CreatePayload(easBuildId: "build-wrong-signature");
        var controller = CreateController(body, "sha1=bad");

        var result = await controller.EasWebhook();

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(0, await _db.Queryable<MobileAppBuild>().CountAsync());
    }

    [Fact]
    public async Task EasWebhook_请求体超限_直接拒绝且不验签不入库()
    {
        var bodyBytes = new byte[256 * 1024 + 1];
        var controller = CreateController(bodyBytes, "sha1=bad");
        controller.ControllerContext.HttpContext.Request.ContentLength = bodyBytes.Length;

        var result = await controller.EasWebhook();

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, status.StatusCode);
        var response = Assert.IsType<ApiResponse<object>>(status.Value);
        Assert.False(response.Success);
        Assert.Equal("WEBHOOK_BODY_TOO_LARGE", response.Code);
        Assert.Equal(0, await _db.Queryable<MobileAppBuild>().CountAsync());
    }

    [Fact]
    public async Task EasWebhook_签名正确但Json无效_返回Ok并忽略避免重试()
    {
        const string body = "{ invalid json";
        var controller = CreateController(body, CreateSignature(body));

        var result = await controller.EasWebhook();

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse<MobileAppBuildWebhookResultDto>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("ignored", response.Data!.Action);
        Assert.Equal("invalid_webhook_json", response.Data.Reason);
        Assert.Equal(0, await _db.Queryable<MobileAppBuild>().CountAsync());
    }

    [Fact]
    public async Task EasWebhook_真实Metadata结构_保存版本Profile和Git信息()
    {
        var service = CreateService();
        var body = """
        {
          "id": "build-metadata",
          "accountName": "hotbargain",
          "projectName": "hb-mobile",
          "platform": "android",
          "status": "finished",
          "artifacts": {
            "buildUrl": "https://expo.dev/artifacts/eas/build-metadata.apk"
          },
          "buildDetailsPageUrl": "https://expo.dev/accounts/hotbargain/projects/hb-mobile/builds/build-metadata",
          "metadata": {
            "appName": "Hot Bargain",
            "appVersion": "3.0.0",
            "appBuildVersion": "99",
            "buildProfile": "production",
            "distribution": "store",
            "gitCommitHash": "abcdef123456",
            "gitCommitMessage": "发布 Android APK",
            "runtimeVersion": "3.0.0",
            "channel": "production"
          },
          "createdAt": "2026-06-15T00:50:00Z",
          "completedAt": "2026-06-15T01:00:00Z",
          "expirationDate": "2026-07-15T00:00:00Z"
        }
        """;

        var result = await service.HandleEasWebhookAsync(body);

        Assert.True(result.Success);
        var saved = await _db.Queryable<MobileAppBuild>().SingleAsync();
        Assert.Equal("3.0.0", saved.AppVersion);
        Assert.Equal("99", saved.AppBuildVersion);
        Assert.Equal("production", saved.BuildProfile);
        Assert.Equal("abcdef123456", saved.GitCommitHash);
        Assert.Equal("production", saved.Channel);
    }

    [Theory]
    [InlineData("ios", "finished", "https://expo.dev/artifacts/eas/ios.tar.gz")]
    [InlineData("android", "errored", "https://expo.dev/artifacts/eas/fail.apk")]
    [InlineData("android", "finished", "")]
    [InlineData("android", "finished", "http://expo.dev/artifacts/eas/insecure.apk")]
    public async Task EasWebhook_非目标构建_返回Ok但忽略(string platform, string status, string artifactUrl)
    {
        var body = CreatePayload(
            easBuildId: $"ignored-{platform}-{status}",
            platform: platform,
            status: status,
            artifactUrl: artifactUrl
        );
        var controller = CreateController(body, CreateSignature(body));

        var result = await controller.EasWebhook();

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse<MobileAppBuildWebhookResultDto>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("ignored", response.Data!.Action);
        Assert.Equal(0, await _db.Queryable<MobileAppBuild>().CountAsync());
    }

    [Fact]
    public async Task EasWebhook_AcceptedProfiles未配置_使用PreviewProduction默认值()
    {
        var service = new MobileAppBuildService(
            _db,
            Options.Create(
                new EasWebhookOptions
                {
                    Secret = Secret,
                    AllowedAccountName = "hotbargain",
                    AllowedProjectName = "hb-mobile",
                    AcceptedProfiles = [],
                }
            ),
            NullLogger<MobileAppBuildService>.Instance
        );

        var result = await service.HandleEasWebhookAsync(
            CreatePayload(easBuildId: "build-default-profile", profile: "preview")
        );

        Assert.True(result.Success);
        Assert.Equal("saved", result.Data!.Action);
        var saved = await _db.Queryable<MobileAppBuild>().SingleAsync();
        Assert.Equal("preview", saved.BuildProfile);
    }

    [Fact]
    public async Task EasWebhook_重复EasBuildId_更新已有记录不重复插入()
    {
        var service = CreateService();
        await service.HandleEasWebhookAsync(
            CreatePayload(easBuildId: "build-repeat", artifactUrl: "https://expo.dev/old.apk")
        );

        var result = await service.HandleEasWebhookAsync(
            CreatePayload(easBuildId: "build-repeat", artifactUrl: "https://expo.dev/new.apk")
        );

        Assert.True(result.Success);
        Assert.Equal("updated", result.Data!.Action);
        Assert.Equal(1, await _db.Queryable<MobileAppBuild>().CountAsync());
        var saved = await _db.Queryable<MobileAppBuild>().SingleAsync();
        Assert.Equal("https://expo.dev/new.apk", saved.ArtifactUrl);
    }

    [Fact]
    public async Task GetLatestAsync_返回指定Profile最新Android成功构建()
    {
        var service = CreateService();
        await service.HandleEasWebhookAsync(
            CreatePayload(
                easBuildId: "older",
                artifactUrl: "https://expo.dev/older.apk",
                completedAt: "2026-06-15T01:00:00Z"
            )
        );
        await service.HandleEasWebhookAsync(
            CreatePayload(
                easBuildId: "newer",
                artifactUrl: "https://expo.dev/newer.apk",
                completedAt: "2026-06-15T02:00:00Z"
            )
        );
        await service.HandleEasWebhookAsync(
            CreatePayload(
                easBuildId: "preview",
                profile: "preview",
                artifactUrl: "https://expo.dev/preview.apk",
                completedAt: "2026-06-15T03:00:00Z"
            )
        );

        var latest = await service.GetLatestAsync("production");

        Assert.True(latest.Success);
        Assert.NotNull(latest.Data);
        Assert.Equal("newer", latest.Data!.EasBuildId);
        Assert.Equal("https://expo.dev/newer.apk", latest.Data.ArtifactUrl);
    }

    [Fact]
    public async Task AndroidLatest_允许未登录读取最新Apk元数据()
    {
        var method = typeof(MobileAppBuildsController).GetMethod(nameof(MobileAppBuildsController.AndroidLatest));
        Assert.NotNull(method);
        Assert.Contains(
            method!.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true),
            attribute => attribute is AllowAnonymousAttribute
        );

        var service = CreateService();
        await service.HandleEasWebhookAsync(
            CreatePayload(
                easBuildId: "anonymous-latest",
                artifactUrl: "https://expo.dev/anonymous-latest.apk"
            )
        );
        await service.HandleEasWebhookAsync(
            CreatePayload(
                easBuildId: "anonymous-preview",
                profile: "preview",
                artifactUrl: "https://expo.dev/anonymous-preview.apk",
                completedAt: "2026-06-15T03:00:00Z"
            )
        );
        var controller = CreateController("{}", CreateSignature("{}"));

        var result = await controller.AndroidLatest("production");

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse<MobileAppBuildPublicDto?>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("anonymous-latest", response.Data!.EasBuildId);

        var publicFields = typeof(MobileAppBuildPublicDto)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();
        Assert.Equal(
            [
                nameof(MobileAppBuildPublicDto.AppBuildVersion),
                nameof(MobileAppBuildPublicDto.AppVersion),
                nameof(MobileAppBuildPublicDto.ArtifactUrl),
                nameof(MobileAppBuildPublicDto.BuildProfile),
                nameof(MobileAppBuildPublicDto.CosArtifactUrl),
                nameof(MobileAppBuildPublicDto.EasBuildId),
            ],
            publicFields
        );
        Assert.Null(response.Data.CosArtifactUrl);

        var previewResult = await controller.AndroidLatest("preview");
        var previewOk = Assert.IsType<OkObjectResult>(previewResult);
        var previewResponse = Assert.IsType<ApiResponse<MobileAppBuildPublicDto?>>(previewOk.Value);
        Assert.True(previewResponse.Success);
        Assert.Equal("anonymous-preview", previewResponse.Data!.EasBuildId);
        Assert.Equal("preview", previewResponse.Data.BuildProfile);

        var internalResult = await controller.AndroidLatest("development");
        var internalOk = Assert.IsType<OkObjectResult>(internalResult);
        var internalResponse = Assert.IsType<ApiResponse<MobileAppBuildPublicDto?>>(internalOk.Value);
        Assert.True(internalResponse.Success);
        Assert.Null(internalResponse.Data);
    }

    [Fact]
    public async Task AndroidLatestDownload_跳转到最新未过期Apk地址()
    {
        var service = CreateService();
        await service.HandleEasWebhookAsync(
            CreatePayload(
                easBuildId: "download-prod",
                artifactUrl: "https://expo.dev/download-prod.apk"
            )
        );
        await service.HandleEasWebhookAsync(
            CreatePayload(
                easBuildId: "download-preview",
                profile: "preview",
                artifactUrl: "https://expo.dev/download-preview.apk",
                completedAt: "2026-06-15T03:00:00Z"
            )
        );
        var controller = CreateController("{}", CreateSignature("{}"));

        var productionResult = await controller.AndroidLatestDownload("production");
        var previewResult = await controller.AndroidLatestDownload("preview");
        var internalResult = await controller.AndroidLatestDownload("development");

        var productionRedirect = Assert.IsType<RedirectResult>(productionResult);
        Assert.Equal("https://expo.dev/download-prod.apk", productionRedirect.Url);

        var previewRedirect = Assert.IsType<RedirectResult>(previewResult);
        Assert.Equal("https://expo.dev/download-preview.apk", previewRedirect.Url);

        Assert.IsType<NotFoundObjectResult>(internalResult);
    }

    [Fact]
    public async Task AndroidBuildDownload_Latest已变化时仍跳转指定Build()
    {
        var service = CreateService();
        await service.HandleEasWebhookAsync(
            CreatePayload(
                easBuildId: "download-bound-old",
                artifactUrl: "https://expo.dev/download-bound-old.apk",
                completedAt: "2026-06-15T01:00:00Z"
            )
        );
        await service.HandleEasWebhookAsync(
            CreatePayload(
                easBuildId: "download-bound-new",
                artifactUrl: "https://expo.dev/download-bound-new.apk",
                completedAt: "2026-06-15T02:00:00Z"
            )
        );
        var controller = CreateController("{}", CreateSignature("{}"));

        var boundResult = await controller.AndroidBuildDownload("download-bound-old", "production");
        var latestResult = await controller.AndroidLatestDownload("production");

        var boundRedirect = Assert.IsType<RedirectResult>(boundResult);
        Assert.Equal("https://expo.dev/download-bound-old.apk", boundRedirect.Url);

        var latestRedirect = Assert.IsType<RedirectResult>(latestResult);
        Assert.Equal("https://expo.dev/download-bound-new.apk", latestRedirect.Url);
    }

    [Fact]
    public async Task AndroidLatestDownload_没有可用Apk时返回NotFound()
    {
        var service = CreateService();
        await service.HandleEasWebhookAsync(
            CreatePayload(
                easBuildId: "expired-download",
                artifactUrl: "https://expo.dev/expired-download.apk",
                expirationDate: "2000-01-01T00:00:00Z"
            )
        );
        var controller = CreateController("{}", CreateSignature("{}"));

        var result = await controller.AndroidLatestDownload("production");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetLatestAsync_忽略已过期Apk地址()
    {
        var service = CreateService();
        await service.HandleEasWebhookAsync(
            CreatePayload(
                easBuildId: "expired",
                artifactUrl: "https://expo.dev/expired.apk",
                completedAt: "2026-06-15T03:00:00Z",
                expirationDate: "2000-01-01T00:00:00Z"
            )
        );

        var latest = await service.GetLatestAsync("production");

        Assert.True(latest.Success);
        Assert.Null(latest.Data);
    }

    [Fact]
    public async Task GetHistoryAsync_按Profile筛选并返回分页()
    {
        var service = CreateService();
        await service.HandleEasWebhookAsync(
            CreatePayload(
                easBuildId: "prod-old",
                artifactUrl: "https://expo.dev/prod-old.apk",
                completedAt: "2026-06-15T01:00:00Z"
            )
        );
        await service.HandleEasWebhookAsync(
            CreatePayload(
                easBuildId: "prod-new",
                artifactUrl: "https://expo.dev/prod-new.apk",
                completedAt: "2026-06-15T02:00:00Z"
            )
        );
        await service.HandleEasWebhookAsync(
            CreatePayload(
                easBuildId: "preview-new",
                profile: "preview",
                artifactUrl: "https://expo.dev/preview-new.apk",
                completedAt: "2026-06-15T03:00:00Z"
            )
        );

        var firstPage = await service.GetHistoryAsync(
            new MobileAppBuildQueryDto
            {
                Profile = "production",
                Page = 1,
                PageSize = 1,
            }
        );
        var secondPage = await service.GetHistoryAsync(
            new MobileAppBuildQueryDto
            {
                Profile = "production",
                Page = 2,
                PageSize = 1,
            }
        );
        var defaultProfilePage = await service.GetHistoryAsync(
            new MobileAppBuildQueryDto
            {
                Page = 1,
                PageSize = 10,
            }
        );

        Assert.True(firstPage.Success);
        Assert.NotNull(firstPage.Data);
        Assert.Equal(2, firstPage.Data!.Total);
        Assert.Equal(1, firstPage.Data.Page);
        Assert.Equal(1, firstPage.Data.PageSize);
        Assert.Single(firstPage.Data.Items!);
        Assert.Equal("prod-new", firstPage.Data.Items![0].EasBuildId);
        Assert.DoesNotContain(firstPage.Data.Items!, item => item.BuildProfile == "preview");

        Assert.True(secondPage.Success);
        Assert.NotNull(secondPage.Data);
        Assert.Equal(2, secondPage.Data!.Total);
        Assert.Single(secondPage.Data.Items!);
        Assert.Equal("prod-old", secondPage.Data.Items![0].EasBuildId);

        Assert.True(defaultProfilePage.Success);
        Assert.NotNull(defaultProfilePage.Data);
        Assert.Equal(2, defaultProfilePage.Data!.Total);
        Assert.DoesNotContain(defaultProfilePage.Data.Items!, item => item.BuildProfile == "preview");
    }

    [Fact]
    public async Task UpsertOtaUpdateAsync_新增Ota记录并归一化字段()
    {
        var service = CreateService();
        var publishedAt = new DateTime(2026, 6, 22, 1, 2, 3, DateTimeKind.Utc);

        var result = await service.UpsertOtaUpdateAsync(
            new MobileAppOtaUpdateUpsertDto
            {
                UpdateGroupId = OtaGroup1,
                AndroidUpdateId = "android-update-1",
                Channel = " production ",
                Branch = " main ",
                Platform = " ANDROID ",
                RuntimeVersion = " 3.0.0 ",
                Message = "发布 OTA",
                GitCommitHash = "abc123",
                DashboardUrl = $"https://expo.dev/updates/{OtaGroup1}",
                PublishedAt = publishedAt,
            }
        );

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(OtaGroup1, result.Data!.UpdateGroupId);
        Assert.Equal("android", result.Data.Platform);
        Assert.Equal("production", result.Data.Channel);
        Assert.Equal("main", result.Data.Branch);
        Assert.Equal("3.0.0", result.Data.RuntimeVersion);
        Assert.Equal($"https://expo.dev/updates/{OtaGroup1}", result.Data.DashboardUrl);
        Assert.Equal(publishedAt, result.Data.PublishedAt);

        var saved = await _db.Queryable<MobileAppOtaUpdate>().SingleAsync();
        Assert.Equal(OtaGroup1, saved.UpdateGroupId);
        Assert.Equal("android-update-1", saved.AndroidUpdateId);
        Assert.Equal("android", saved.Platform);
        Assert.Equal($"https://expo.dev/updates/{OtaGroup1}", saved.DashboardUrl);
        Assert.False(saved.IsRollback);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdA==")]
    [InlineData("http://expo.dev/updates/insecure")]
    public async Task UpsertOtaUpdateAsync_非HttpsDashboardUrl_清空链接但保留记录(string dashboardUrl)
    {
        var service = CreateService();

        var result = await service.UpsertOtaUpdateAsync(
            new MobileAppOtaUpdateUpsertDto
            {
                UpdateGroupId = OtaGroupBefore,
                AndroidUpdateId = "android-update-unsafe-url",
                Channel = "production",
                Platform = "android",
                RuntimeVersion = "3.0.0",
                DashboardUrl = dashboardUrl,
            }
        );

        Assert.True(result.Success);
        Assert.Null(result.Data!.DashboardUrl);
        var saved = await _db.Queryable<MobileAppOtaUpdate>().SingleAsync();
        Assert.Equal(OtaGroupBefore, saved.UpdateGroupId);
        Assert.Null(saved.DashboardUrl);
    }

    [Fact]
    public async Task UpsertOtaUpdateAsync_UpdateGroupId不是Uuid_返回错误且不入库()
    {
        var service = CreateService();

        var result = await service.UpsertOtaUpdateAsync(
            new MobileAppOtaUpdateUpsertDto
            {
                UpdateGroupId = "bad-group-id",
                Channel = "production",
                RuntimeVersion = "3.0.0",
            }
        );

        Assert.False(result.Success);
        Assert.Equal("INVALID_UPDATE_GROUP_ID", result.Code);
        Assert.Equal(0, await _db.Queryable<MobileAppOtaUpdate>().CountAsync());
    }

    [Fact]
    public async Task UpsertOtaUpdateAsync_相同Group和Platform幂等更新()
    {
        var service = CreateService();
        await service.UpsertOtaUpdateAsync(
            new MobileAppOtaUpdateUpsertDto
            {
                UpdateGroupId = OtaGroupRepeat,
                AndroidUpdateId = "android-old",
                Channel = "production",
                Branch = "main",
                Platform = "android",
                RuntimeVersion = "3.0.0",
                Message = "旧 OTA",
                PublishedAt = new DateTime(2026, 6, 22, 1, 0, 0, DateTimeKind.Utc),
            }
        );
        var first = await _db.Queryable<MobileAppOtaUpdate>().SingleAsync();

        var result = await service.UpsertOtaUpdateAsync(
            new MobileAppOtaUpdateUpsertDto
            {
                UpdateGroupId = OtaGroupRepeat,
                AndroidUpdateId = "android-new",
                Channel = "production",
                Branch = "release",
                Platform = "ANDROID",
                RuntimeVersion = "3.0.1",
                Message = "回撤 OTA",
                GitCommitHash = "def456",
                PublishedAt = new DateTime(2026, 6, 22, 2, 0, 0, DateTimeKind.Utc),
                IsRollback = true,
                RollbackOfGroupId = OtaGroupBefore,
            }
        );

        Assert.True(result.Success);
        Assert.Equal(1, await _db.Queryable<MobileAppOtaUpdate>().CountAsync());
        var saved = await _db.Queryable<MobileAppOtaUpdate>().SingleAsync();
        Assert.Equal(first.Id, saved.Id);
        Assert.Equal("android-new", saved.AndroidUpdateId);
        Assert.Equal("release", saved.Branch);
        Assert.Equal("3.0.1", saved.RuntimeVersion);
        Assert.True(saved.IsRollback);
        Assert.Equal(OtaGroupBefore, saved.RollbackOfGroupId);
    }

    [Fact]
    public async Task GetOtaUpdatesAsync_按Channel和Runtime分页过滤()
    {
        var service = CreateService();
        await service.UpsertOtaUpdateAsync(
            CreateOtaUpdate(
                OtaProdOld,
                channel: "production",
                runtimeVersion: "3.0.0",
                publishedAt: new DateTime(2026, 6, 22, 1, 0, 0, DateTimeKind.Utc)
            )
        );
        await service.UpsertOtaUpdateAsync(
            CreateOtaUpdate(
                OtaProdNew,
                channel: "production",
                runtimeVersion: "3.0.0",
                publishedAt: new DateTime(2026, 6, 22, 2, 0, 0, DateTimeKind.Utc)
            )
        );
        await service.UpsertOtaUpdateAsync(
            CreateOtaUpdate(
                OtaProdRuntime2,
                channel: "production",
                runtimeVersion: "4.0.0",
                publishedAt: new DateTime(2026, 6, 22, 3, 0, 0, DateTimeKind.Utc)
            )
        );
        await service.UpsertOtaUpdateAsync(
            CreateOtaUpdate(
                OtaPreviewNew,
                channel: "preview",
                runtimeVersion: "3.0.0",
                publishedAt: new DateTime(2026, 6, 22, 4, 0, 0, DateTimeKind.Utc)
            )
        );

        var firstPage = await service.GetOtaUpdatesAsync(
            new MobileAppOtaUpdateQueryDto
            {
                Channel = " production ",
                RuntimeVersion = " 3.0.0 ",
                Page = 1,
                PageSize = 1,
            }
        );
        var secondPage = await service.GetOtaUpdatesAsync(
            new MobileAppOtaUpdateQueryDto
            {
                Channel = "production",
                RuntimeVersion = "3.0.0",
                Page = 2,
                PageSize = 1,
            }
        );
        var defaultChannel = await service.GetOtaUpdatesAsync(
            new MobileAppOtaUpdateQueryDto { Page = 1, PageSize = 1000 }
        );

        Assert.True(firstPage.Success);
        Assert.Equal(2, firstPage.Data!.Total);
        Assert.Equal(1, firstPage.Data.PageSize);
        Assert.Single(firstPage.Data.Items!);
        Assert.Equal(OtaProdNew, firstPage.Data.Items![0].UpdateGroupId);

        Assert.True(secondPage.Success);
        Assert.Equal(OtaProdOld, Assert.Single(secondPage.Data!.Items!).UpdateGroupId);

        Assert.True(defaultChannel.Success);
        Assert.Equal(3, defaultChannel.Data!.Total);
        Assert.Equal(100, defaultChannel.Data.PageSize);
        Assert.DoesNotContain(defaultChannel.Data.Items!, item => item.Channel == "preview");
    }

    [Fact]
    public async Task CreateOtaRollbackCommandAsync_只生成回退命令不执行外部命令()
    {
        var service = CreateService();

        var result = await service.CreateOtaRollbackCommandAsync(
            OtaGroupRollback,
            new MobileAppOtaRollbackCommandDto { Message = "版本有问题" }
        );

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(OtaGroupRollback, result.Data!.UpdateGroupId);
        Assert.Equal("android", result.Data.Platform);
        Assert.Equal(
            $"npx eas-cli@latest update:rollback '{OtaGroupRollback}' -p 'android' -m '回退 OTA：版本有问题' --non-interactive",
            result.Data.Command
        );
        Assert.Equal(0, await _db.Queryable<MobileAppOtaUpdate>().CountAsync());
    }

    [Fact]
    public async Task CreateOtaRollbackCommandAsync_拒绝恶意GroupId()
    {
        var service = CreateService();

        var result = await service.CreateOtaRollbackCommandAsync(
            "not-a-uuid$(touch /tmp/pwn)",
            new MobileAppOtaRollbackCommandDto { Message = "版本有问题" }
        );

        Assert.False(result.Success);
        Assert.Equal("INVALID_UPDATE_GROUP_ID", result.Code);
        Assert.Equal(0, await _db.Queryable<MobileAppOtaUpdate>().CountAsync());
    }

    [Fact]
    public async Task CreateOtaRollbackCommandAsync_命令参数使用单引号避免Shell展开()
    {
        var service = CreateService();

        var result = await service.CreateOtaRollbackCommandAsync(
            OtaGroupRollback,
            new MobileAppOtaRollbackCommandDto
            {
                Message = "坏版本 $(touch /tmp/pwn) 'quote'",
            }
        );

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Contains("$(touch /tmp/pwn)", result.Data!.Command);
        Assert.Contains("'\"'\"'", result.Data.Command);
        Assert.DoesNotContain("--message \"", result.Data.Command);
    }

    [Fact]
    public async Task OtaUpdates_不影响ApkLatest查询()
    {
        var service = CreateService();
        await service.HandleEasWebhookAsync(
            CreatePayload(
                easBuildId: "apk-latest",
                artifactUrl: "https://expo.dev/apk-latest.apk",
                completedAt: "2026-06-15T01:00:00Z"
            )
        );
        await service.UpsertOtaUpdateAsync(
            CreateOtaUpdate(
                OtaNewerThanApk,
                channel: "production",
                runtimeVersion: "3.0.0",
                publishedAt: new DateTime(2026, 6, 22, 2, 0, 0, DateTimeKind.Utc)
            )
        );

        var latest = await service.GetLatestAsync("production");

        Assert.True(latest.Success);
        Assert.Equal("apk-latest", latest.Data!.EasBuildId);
        Assert.Equal("https://expo.dev/apk-latest.apk", latest.Data.ArtifactUrl);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbPath))
        {
            SqliteTempFileCleanup.DeleteIfExists(_dbPath);
        }
    }

    private MobileAppBuildsController CreateController(string body, string signature)
    {
        return CreateController(Encoding.UTF8.GetBytes(body), signature);
    }

    private MobileAppBuildsController CreateController(byte[] bodyBytes, string signature)
    {
        var controller = new MobileAppBuildsController(
            CreateService(),
            Options.Create(CreateOptions()),
            NullLogger<MobileAppBuildsController>.Instance
        );
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Request.Headers["expo-signature"] = signature;
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private MobileAppBuildService CreateService()
    {
        return new MobileAppBuildService(
            _db,
            Options.Create(CreateOptions()),
            NullLogger<MobileAppBuildService>.Instance
        );
    }

    private sealed class FakeMobileAppBuildArtifactMirror : IMobileAppBuildArtifactMirror
    {
        private readonly MobileAppBuildArtifactMirrorResult? _result;
        private readonly Exception? _exception;

        private FakeMobileAppBuildArtifactMirror(
            MobileAppBuildArtifactMirrorResult? result,
            Exception? exception
        )
        {
            _result = result;
            _exception = exception;
        }

        public int Calls { get; private set; }

        public static FakeMobileAppBuildArtifactMirror Success(
            string artifactUrl,
            string objectKey,
            DateTime mirroredAt
        )
        {
            return new FakeMobileAppBuildArtifactMirror(
                new MobileAppBuildArtifactMirrorResult
                {
                    ArtifactUrl = artifactUrl,
                    ObjectKey = objectKey,
                    MirroredAt = mirroredAt,
                },
                null
            );
        }

        public static FakeMobileAppBuildArtifactMirror Failure(Exception exception)
        {
            return new FakeMobileAppBuildArtifactMirror(null, exception);
        }

        public Task<MobileAppBuildArtifactMirrorResult> MirrorAsync(
            MobileAppBuild build,
            CancellationToken cancellationToken = default
        )
        {
            Calls += 1;
            if (_exception != null)
            {
                throw _exception;
            }

            return Task.FromResult(_result!);
        }
    }

    private static EasWebhookOptions CreateOptions()
    {
        return new EasWebhookOptions
        {
            Secret = Secret,
            AllowedAccountName = "hotbargain",
            AllowedProjectName = "hb-mobile",
            AcceptedProfiles = ["preview", "production"],
        };
    }

    private static string CreatePayload(
        string easBuildId,
        string platform = "android",
        string status = "finished",
        string profile = "production",
        string artifactUrl = "https://expo.dev/artifacts/eas/build-1.apk",
        string completedAt = "2026-06-15T01:00:00Z",
        string expirationDate = "2099-07-15T00:00:00Z"
    )
    {
        return $$"""
        {
          "id": "{{easBuildId}}",
          "accountName": "hotbargain",
          "projectName": "hb-mobile",
          "appName": "Hot Bargain",
          "platform": "{{platform}}",
          "status": "{{status}}",
          "buildProfile": "{{profile}}",
          "distribution": "store",
          "channel": "production",
          "runtimeVersion": "1.0.0",
          "appVersion": "2.3.4",
          "appBuildVersion": "56",
          "artifacts": {
            "buildUrl": "{{artifactUrl}}"
          },
          "buildDetailsPageUrl": "https://expo.dev/accounts/hotbargain/projects/hb-mobile/builds/{{easBuildId}}",
          "gitCommitHash": "abc123",
          "gitCommitMessage": "发布 Android APK",
          "createdAt": "2026-06-15T00:50:00Z",
          "completedAt": "{{completedAt}}",
          "expirationDate": "{{expirationDate}}"
        }
        """;
    }

    private static MobileAppOtaUpdateUpsertDto CreateOtaUpdate(
        string updateGroupId,
        string channel,
        string runtimeVersion,
        DateTime publishedAt
    )
    {
        return new MobileAppOtaUpdateUpsertDto
        {
            UpdateGroupId = updateGroupId,
            AndroidUpdateId = $"{updateGroupId}-android",
            Channel = channel,
            Branch = "main",
            Platform = "android",
            RuntimeVersion = runtimeVersion,
            Message = $"发布 {updateGroupId}",
            GitCommitHash = "abc123",
            DashboardUrl = $"https://expo.dev/updates/{updateGroupId}",
            PublishedAt = publishedAt,
        };
    }

    private static string CreateSignature(string body)
    {
        return CreateSignature(Encoding.UTF8.GetBytes(body));
    }

    private static string CreateSignature(byte[] bodyBytes)
    {
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(Secret));
        var hash = hmac.ComputeHash(bodyBytes);
        return "sha1=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
