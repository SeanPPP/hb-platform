using System.Security.Cryptography;
using System.Text;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
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
        _db.CodeFirst.InitTables<MobileAppBuild>();
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

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
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
        string completedAt = "2026-06-15T01:00:00Z"
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
          "expirationDate": "2026-07-15T00:00:00Z"
        }
        """;
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
