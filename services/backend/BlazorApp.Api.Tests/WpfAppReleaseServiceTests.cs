using System.Net;
using BlazorApp.Api.Services;
using BlazorApp.Api.Models;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class WpfAppReleaseServiceTests : IDisposable
{
    private const long MaxSupportedInstallerBytes = 512L * 1024 * 1024;
    private readonly string _dbPath;
    private readonly ISqlSugarClient _db;

    public WpfAppReleaseServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"wpf-app-release-{Guid.NewGuid():N}.db");
        _db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = $"DataSource={_dbPath}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        _db.CodeFirst.InitTables<WpfAppRelease, WpfUpdatePolicy>();
    }

    [Fact]
    public async Task CreateReleaseAsync_新增Wpf版本并归一化CosKey()
    {
        var service = CreateService();

        var result = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = " Production ",
                Version = " v1.2.3 ",
                FileName = " HB POS Setup 1.2.3.exe ",
                FileSize = 123456,
                Sha256 = new string('A', 64),
                DownloadUrl = "https://example.test/wpf/HB-POS-Setup-1.2.3.exe",
                InstallerType = " exe ",
                InstallerArguments = "/quiet",
                ReleaseNotes = "发布说明",
            },
            "tester"
        );

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("production", result.Data!.Channel);
        Assert.Equal("1.2.3", result.Data.Version);
        Assert.Equal("HB POS Setup 1.2.3.exe", result.Data.FileName);
        Assert.Equal("wpf-releases/production/1.2.3/HB-POS-Setup-1.2.3.exe", result.Data.CosObjectKey);
        Assert.Equal(new string('a', 64), result.Data.Sha256);
        Assert.Equal("https://example.test/wpf/HB-POS-Setup-1.2.3.exe", result.Data.DownloadUrl);
        Assert.True(result.Data.IsActive);

        var saved = await _db.Queryable<WpfAppRelease>().SingleAsync();
        Assert.Equal("tester", saved.CreatedBy);
        Assert.Equal("1.2.3", saved.Version);
        Assert.Equal("wpf-releases/production/1.2.3/HB-POS-Setup-1.2.3.exe", saved.CosObjectKey);
    }

    [Fact]
    public async Task CreateReleaseAsync_rejects_duplicate_version_when_prefix_differs()
    {
        var service = CreateService();
        var first = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = "v1.2.3",
                FileName = "hbpos-v1.2.3.exe",
                FileSize = 100,
                Sha256 = new string('a', 64),
                DownloadUrl = "https://example.test/wpf/hbpos-v1.2.3.exe",
                InstallerType = "exe",
            },
            "tester"
        );
        Assert.True(first.Success);

        var duplicate = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos-1.2.3.exe",
                FileSize = 100,
                Sha256 = new string('b', 64),
                DownloadUrl = "https://example.test/wpf/hbpos-1.2.3.exe",
                InstallerType = "exe",
            },
            "tester"
        );

        Assert.False(duplicate.Success);
        Assert.Equal("WPF_RELEASE_EXISTS", duplicate.Code);
        Assert.Equal(1, await _db.Queryable<WpfAppRelease>().CountAsync());
    }

    [Theory]
    [InlineData("", "https://example.test/wpf/hbpos.exe", "INVALID_SHA256")]
    [InlineData("abc", "https://example.test/wpf/hbpos.exe", "INVALID_SHA256")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "", "INVALID_DOWNLOAD_URL")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "wpf-releases/production/1.2.3/hbpos.exe", "INVALID_DOWNLOAD_URL")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "http://downloads.example.test/hbpos.exe", "INVALID_DOWNLOAD_URL")]
    public async Task CreateReleaseAsync_拒绝不可信校验或下载地址(
        string sha256,
        string downloadUrl,
        string expectedCode)
    {
        var service = CreateService();

        var result = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos.exe",
                FileSize = 100,
                Sha256 = sha256,
                DownloadUrl = downloadUrl,
                InstallerType = "exe",
            },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal(expectedCode, result.Code);
        Assert.Equal(0, await _db.Queryable<WpfAppRelease>().CountAsync());
    }

    [Theory]
    [InlineData("hbpos.zip", "zip")]
    [InlineData("hbpos.txt", "exe")]
    [InlineData("hbpos.exe", "msi")]
    public async Task CreateReleaseAsync_拒绝非Exe或Msi安装包(
        string fileName,
        string installerType)
    {
        var service = CreateService();

        var result = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = fileName,
                FileSize = 100,
                Sha256 = new string('a', 64),
                DownloadUrl = $"https://example.test/wpf/{fileName}",
                InstallerType = installerType,
            },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal("INVALID_INSTALLER_FILE", result.Code);
        Assert.Equal(0, await _db.Queryable<WpfAppRelease>().CountAsync());
    }

    [Theory]
    [InlineData("../hbpos.exe")]
    [InlineData("folder/hbpos.exe")]
    [InlineData("CON.exe")]
    [InlineData("CON.any.exe")]
    [InlineData("NUL.v1.msi")]
    [InlineData("hbpos?.exe")]
    [InlineData("hbpos:1.2.3.exe")]
    public async Task CreateReleaseAsync_rejects_dangerous_installer_file_names(
        string fileName)
    {
        var service = CreateService();

        var result = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = fileName,
                FileSize = 100,
                Sha256 = new string('a', 64),
                DownloadUrl = "https://example.test/wpf/hbpos.exe",
                InstallerType = fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ? "msi" : "exe",
            },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal("INVALID_INSTALLER_FILE", result.Code);
        Assert.Equal(0, await _db.Queryable<WpfAppRelease>().CountAsync());
    }

    [Fact]
    public async Task CreateReleaseAsync_拒绝超过512MB的安装包()
    {
        var service = CreateService();

        var result = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos.exe",
                FileSize = MaxSupportedInstallerBytes + 1,
                Sha256 = new string('a', 64),
                DownloadUrl = "https://example.test/wpf/hbpos.exe",
                InstallerType = "exe",
            },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal("FILE_TOO_LARGE", result.Code);
        Assert.Equal(0, await _db.Queryable<WpfAppRelease>().CountAsync());
    }

    [Theory]
    [InlineData("http://localhost:5000/wpf/hbpos.exe")]
    [InlineData("http://127.0.0.1:5000/wpf/hbpos.exe")]
    [InlineData("http://[::1]:5000/wpf/hbpos.exe")]
    public async Task CreateReleaseAsync_允许本地Loopback的Http下载地址(string downloadUrl)
    {
        var service = CreateService();

        var result = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos.exe",
                FileSize = 100,
                Sha256 = new string('a', 64),
                DownloadUrl = downloadUrl,
                InstallerType = "exe",
            },
            "tester"
        );

        Assert.True(result.Success);
        Assert.Equal(downloadUrl, result.Data!.DownloadUrl);
    }

    [Fact]
    public async Task CreateReleaseAsync_with_upload_service_accepts_matching_cos_download_url()
    {
        var service = CreateServiceWithUpload();

        var result = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos-1.2.3.exe",
                FileSize = 100,
                Sha256 = new string('a', 64),
                DownloadUrl = "https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/wpf-releases/production/1.2.3/hbpos-1.2.3.exe",
                CosObjectKey = "wpf-releases/production/other/hbpos.exe",
                InstallerType = "exe",
            },
            "tester"
        );

        Assert.True(result.Success);
        Assert.Equal(
            "wpf-releases/production/1.2.3/hbpos-1.2.3.exe",
            result.Data!.CosObjectKey
        );
    }

    [Fact]
    public async Task CreateReleaseAsync_with_upload_service_normalizes_signed_cos_download_url()
    {
        var service = CreateServiceWithUpload();
        const string expectedDownloadUrl =
            "https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/wpf-releases/production/1.2.3/hbpos-1.2.3.exe";

        var result = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos-1.2.3.exe",
                FileSize = 100,
                Sha256 = new string('a', 64),
                DownloadUrl =
                    $"{expectedDownloadUrl}?q-sign-algorithm=sha1&q-sign-time=1719400000%3B1719403600&q-key-time=1719400000%3B1719403600&q-url-param-list=&q-header-list=host&q-signature=test-signature#ignored-fragment",
                InstallerType = "exe",
            },
            "tester"
        );

        Assert.True(result.Success);
        Assert.Equal(expectedDownloadUrl, result.Data!.DownloadUrl);
        var saved = await _db.Queryable<WpfAppRelease>().SingleAsync();
        Assert.Equal(expectedDownloadUrl, saved.DownloadUrl);
    }

    [Fact]
    public async Task CreateReleaseAsync_with_upload_service_rejects_external_download_url()
    {
        var service = CreateServiceWithUpload();

        var result = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos-1.2.3.exe",
                FileSize = 100,
                Sha256 = new string('a', 64),
                DownloadUrl = "https://example.test/wpf-releases/production/1.2.3/hbpos-1.2.3.exe",
                InstallerType = "exe",
            },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal("INVALID_DOWNLOAD_URL", result.Code);
        Assert.Equal(0, await _db.Queryable<WpfAppRelease>().CountAsync());
    }

    [Fact]
    public async Task CreateReleaseAsync_with_upload_service_allows_loopback_download_url()
    {
        var service = CreateServiceWithUpload();

        var result = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos-1.2.3.exe",
                FileSize = 100,
                Sha256 = new string('a', 64),
                DownloadUrl = "http://localhost:5000/wpf-releases/production/1.2.3/hbpos-1.2.3.exe",
                InstallerType = "exe",
            },
            "tester"
        );

        Assert.True(result.Success);
        var saved = await _db.Queryable<WpfAppRelease>().SingleAsync();
        Assert.Equal(
            "http://localhost:5000/wpf-releases/production/1.2.3/hbpos-1.2.3.exe",
            saved.DownloadUrl
        );
    }

    [Fact]
    public async Task UpdateReleaseAsync_拒绝与文件扩展名不一致的安装器类型()
    {
        var service = CreateService();
        var created = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos-1.2.3.exe",
                FileSize = 100,
                Sha256 = new string('a', 64),
                DownloadUrl = "https://example.test/wpf/hbpos-1.2.3.exe",
                InstallerType = "exe",
            },
            "tester"
        );

        var result = await service.UpdateReleaseAsync(
            created.Data!.Id,
            new WpfAppReleaseUpdateRequest { InstallerType = "msi" },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal("INVALID_INSTALLER_FILE", result.Code);
        var saved = await _db.Queryable<WpfAppRelease>().SingleAsync();
        Assert.Equal("exe", saved.InstallerType);
    }

    [Fact]
    public async Task UpdateReleaseAsync_with_upload_service_allows_loopback_download_url()
    {
        var service = CreateServiceWithUpload();
        var created = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos-1.2.3.exe",
                FileSize = 100,
                Sha256 = new string('a', 64),
                DownloadUrl = "https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/wpf-releases/production/1.2.3/hbpos-1.2.3.exe",
                InstallerType = "exe",
            },
            "tester"
        );
        Assert.True(created.Success);

        var result = await service.UpdateReleaseAsync(
            created.Data!.Id,
            new WpfAppReleaseUpdateRequest
            {
                DownloadUrl = "http://localhost:5000/wpf-releases/production/1.2.3/hbpos-1.2.3.exe",
            },
            "tester"
        );

        Assert.True(result.Success);
        var saved = await _db.Queryable<WpfAppRelease>().SingleAsync();
        Assert.Equal(
            "http://localhost:5000/wpf-releases/production/1.2.3/hbpos-1.2.3.exe",
            saved.DownloadUrl
        );
    }

    [Fact]
    public async Task UpdateReleaseAsync_with_upload_service_normalizes_signed_cos_download_url()
    {
        var service = CreateServiceWithUpload();
        const string expectedDownloadUrl =
            "https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/wpf-releases/production/1.2.3/hbpos-1.2.3.exe";
        var created = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos-1.2.3.exe",
                FileSize = 100,
                Sha256 = new string('a', 64),
                DownloadUrl = expectedDownloadUrl,
                InstallerType = "exe",
            },
            "tester"
        );
        Assert.True(created.Success);

        var result = await service.UpdateReleaseAsync(
            created.Data!.Id,
            new WpfAppReleaseUpdateRequest
            {
                DownloadUrl =
                    $"{expectedDownloadUrl}?q-sign-algorithm=sha1&q-sign-time=1719400000%3B1719403600&q-key-time=1719400000%3B1719403600&q-url-param-list=&q-header-list=host&q-signature=test-signature#ignored-fragment",
            },
            "tester"
        );

        Assert.True(result.Success);
        Assert.Equal(expectedDownloadUrl, result.Data!.DownloadUrl);
        var saved = await _db.Queryable<WpfAppRelease>().SingleAsync();
        Assert.Equal(expectedDownloadUrl, saved.DownloadUrl);
    }

    [Fact]
    public async Task UpdateReleaseAsync_rejects_policy_referenced_release_when_cos_object_sha_mismatches()
    {
        var service = CreateServiceWithUpload();
        var created = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos-1.2.3.exe",
                FileSize = 100,
                Sha256 = new string('a', 64),
                DownloadUrl = "https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/wpf-releases/production/1.2.3/hbpos-1.2.3.exe",
                InstallerType = "exe",
            },
            "tester"
        );
        Assert.True(created.Success);

        var policyResult = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.2.3",
                MinimumSupportedVersion = "1.2.3",
                ForceUpdate = true,
            },
            "admin"
        );
        Assert.True(policyResult.Success);

        var result = await service.UpdateReleaseAsync(
            created.Data!.Id,
            new WpfAppReleaseUpdateRequest { Sha256 = new string('b', 64) },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal("COS_OBJECT_SHA256_MISMATCH", result.Code);
        var saved = await _db.Queryable<WpfAppRelease>().SingleAsync();
        Assert.Equal(new string('a', 64), saved.Sha256);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task SetPolicyAsync_可设置和回退目标版本()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");
        await CreateRelease("production", "1.1.0");
        await CreateRelease("production", "1.0.0");

        var upgrade = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.2.0",
                MinimumSupportedVersion = "1.0.0",
                ForceUpdate = false,
            },
            "admin"
        );
        var rollback = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.1.0",
                MinimumSupportedVersion = "1.0.0",
                ForceUpdate = true,
                RollbackConfirmed = true,
            },
            "admin"
        );

        Assert.True(upgrade.Success);
        Assert.True(rollback.Success);
        Assert.Equal("1.1.0", rollback.Data!.TargetVersion);
        Assert.True(rollback.Data.ForceUpdate);
        Assert.Equal(1, await _db.Queryable<WpfUpdatePolicy>().CountAsync());
    }

    [Fact]
    public async Task SetPolicyAsync_lower_target_without_confirmation_returns_rollback_required()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");
        await CreateRelease("production", "1.1.0");
        await SetPolicy("production", "1.2.0", "1.0.0", forceUpdate: false);

        var result = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.1.0",
                MinimumSupportedVersion = "1.0.0",
                ForceUpdate = true,
            },
            "admin"
        );

        Assert.False(result.Success);
        Assert.Equal("ROLLBACK_CONFIRMATION_REQUIRED", result.Code);
        var saved = await _db.Queryable<WpfUpdatePolicy>().SingleAsync();
        Assert.Equal("1.2.0", saved.TargetVersion);
    }

    [Fact]
    public async Task SetPolicyAsync_lower_than_latest_active_release_succeeds_without_confirmation_when_no_existing_policy()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");
        await CreateRelease("production", "1.1.0");
        await CreateRelease("production", "1.0.0");

        var result = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.1.0",
                MinimumSupportedVersion = "1.0.0",
                ForceUpdate = true,
            },
            "admin"
        );

        Assert.True(result.Success);
        Assert.Equal("1.1.0", result.Data!.TargetVersion);
    }

    [Fact]
    public async Task SetPolicyAsync_minimum_supported_version_above_target_returns_invalid_version_range()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");

        var result = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.2.0",
                MinimumSupportedVersion = "1.2.1",
                ForceUpdate = false,
            },
            "admin"
        );

        Assert.False(result.Success);
        Assert.Equal("INVALID_VERSION_RANGE", result.Code);
        Assert.Null(result.Data);
        Assert.Equal(0, await _db.Queryable<WpfUpdatePolicy>().CountAsync());
    }

    [Fact]
    public async Task SetPolicyAsync_allows_minimum_supported_version_equal_to_target()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");

        var result = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.2.0",
                MinimumSupportedVersion = "1.2.0",
                ForceUpdate = true,
            },
            "admin"
        );

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("1.2.0", result.Data!.TargetVersion);
        Assert.Equal("1.2.0", result.Data.MinimumSupportedVersion);
    }

    [Fact]
    public async Task SetPolicyAsync_can_find_release_created_with_prefixed_version_using_canonical_version()
    {
        var service = CreateService();
        await CreateRelease("production", "1.0.0");
        var created = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = "v1.2.3",
                FileName = "hbpos-v1.2.3.exe",
                FileSize = 100,
                Sha256 = new string('a', 64),
                DownloadUrl = "https://example.test/wpf/hbpos-v1.2.3.exe",
                InstallerType = "exe",
            },
            "tester"
        );
        Assert.True(created.Success);

        var policy = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.2.3",
                MinimumSupportedVersion = "1.0.0",
                ForceUpdate = true,
            },
            "admin"
        );
        var check = await service.CheckUpdateAsync("production", "1.2.2");

        Assert.True(policy.Success);
        Assert.Equal("1.2.3", policy.Data!.TargetVersion);
        Assert.True(check.Success);
        Assert.Equal("1.2.3", check.Data!.TargetVersion);
        Assert.Equal("1.2.3", (await _db.Queryable<WpfUpdatePolicy>().SingleAsync()).TargetVersion);
    }

    [Fact]
    public async Task GetReleasesAsync_返回当前目标和强制策略字段()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");
        await CreateRelease("production", "1.1.0");
        await SetPolicy("production", "1.2.0", "1.0.0", forceUpdate: true);

        var result = await service.GetReleasesAsync(
            new WpfAppReleaseQuery
            {
                Channel = "production",
                Page = 1,
                PageSize = 10,
            }
        );

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var data = result.Data!;
        Assert.NotNull(data.Items);
        var items = data.Items!;
        var current = Assert.Single(items, item => item.IsCurrent);
        Assert.Equal("1.2.0", current.Version);
        Assert.Equal("1.2.0", current.TargetVersion);
        Assert.Equal("1.0.0", current.MinimumSupportedVersion);
        Assert.True(current.ForceUpdate);
        Assert.All(
            items,
            item => Assert.Equal("1.2.0", item.TargetVersion)
        );
        var rollbackCandidate = Assert.Single(items, item => item.Version == "1.1.0");
        Assert.False(current.IsRollback);
        Assert.True(rollbackCandidate.IsRollback);
    }

    [Fact]
    public async Task GetReleasesAsync_无策略时不标记回退()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");
        await CreateRelease("production", "1.1.0");

        var result = await service.GetReleasesAsync(
            new WpfAppReleaseQuery
            {
                Channel = "production",
                Page = 1,
                PageSize = 10,
            }
        );

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var items = result.Data!.Items!;
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.False(item.IsRollback));
    }

    [Fact]
    public async Task GetReleasesAsync_忽略软删除版本和策略()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");
        await CreateRelease("production", "1.1.0");
        var deletedRelease = await _db.Queryable<WpfAppRelease>().SingleAsync(x => x.Version == "1.1.0");
        deletedRelease.IsDeleted = true;
        await _db.Updateable(deletedRelease).ExecuteCommandAsync();
        await _db.Insertable(new WpfUpdatePolicy
        {
            Id = Guid.NewGuid(),
            Channel = "production",
            TargetVersion = "1.2.0",
            ForceUpdate = true,
            IsDeleted = true,
            CreatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();

        var result = await service.GetReleasesAsync(new WpfAppReleaseQuery
        {
            Channel = "production",
            Page = 1,
            PageSize = 10,
            IncludeDisabled = true,
        });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items!);
        Assert.Equal("1.2.0", item.Version);
        Assert.False(item.IsCurrent);
        Assert.False(item.ForceUpdate);
    }

    [Fact]
    public async Task CheckUpdateAsync_忽略软删除策略()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");
        await _db.Insertable(new WpfUpdatePolicy
        {
            Id = Guid.NewGuid(),
            Channel = "production",
            TargetVersion = "1.2.0",
            ForceUpdate = true,
            IsDeleted = true,
            CreatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();

        var result = await service.CheckUpdateAsync("production", "1.0.0");

        Assert.True(result.Success);
        Assert.False(result.Data!.UpdateAvailable);
        Assert.Equal("1.0.0", result.Data.CurrentVersion);
        Assert.Null(result.Data.TargetVersion);
    }

    [Fact]
    public async Task SetPolicyAsync_拒绝软删除目标版本()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");
        var deletedRelease = await _db.Queryable<WpfAppRelease>().SingleAsync(x => x.Version == "1.2.0");
        deletedRelease.IsDeleted = true;
        await _db.Updateable(deletedRelease).ExecuteCommandAsync();

        var result = await service.SetPolicyAsync(new WpfUpdatePolicyRequest
        {
            Channel = "production",
            TargetVersion = "1.2.0",
            ForceUpdate = true,
        }, "admin");

        Assert.False(result.Success);
        Assert.Equal("TARGET_RELEASE_NOT_FOUND", result.Code);
        Assert.Equal(0, await _db.Queryable<WpfUpdatePolicy>().CountAsync());
    }

    [Fact]
    public async Task SetPolicyAsync_复用软删除策略避免唯一索引冲突()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");
        var deletedPolicyId = Guid.NewGuid();
        await _db.Insertable(new WpfUpdatePolicy
        {
            Id = deletedPolicyId,
            Channel = "production",
            TargetVersion = "1.0.0",
            ForceUpdate = false,
            IsDeleted = true,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            CreatedBy = "old-admin",
        }).ExecuteCommandAsync();

        var result = await service.SetPolicyAsync(new WpfUpdatePolicyRequest
        {
            Channel = "production",
            TargetVersion = "1.2.0",
            ForceUpdate = true,
        }, "admin");

        Assert.True(result.Success);
        Assert.Equal(deletedPolicyId, result.Data!.Id);
        var saved = Assert.Single(await _db.Queryable<WpfUpdatePolicy>().ToListAsync());
        Assert.Equal(deletedPolicyId, saved.Id);
        Assert.False(saved.IsDeleted);
        Assert.Equal("1.2.0", saved.TargetVersion);
        Assert.True(saved.ForceUpdate);
        Assert.Equal("admin", saved.UpdatedBy);
    }

    [Fact]
    public async Task CheckUpdateAsync_目标版本不同返回普通升级()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");
        await SetPolicy("production", "1.2.0", "1.0.0", forceUpdate: false);

        var result = await service.CheckUpdateAsync("production", "1.1.0");

        Assert.True(result.Success);
        Assert.True(result.Data!.UpdateAvailable);
        Assert.False(result.Data.ForceUpdate);
        Assert.False(result.Data.IsRollback);
        Assert.Equal("1.1.0", result.Data.CurrentVersion);
        Assert.Equal("1.2.0", result.Data.TargetVersion);
        Assert.Equal("1.0.0", result.Data.MinimumSupportedVersion);
        Assert.Equal("https://example.test/production/1.2.0.exe", result.Data.DownloadUrl);
    }

    [Fact]
    public async Task CheckUpdateAsync_策略强制升级时ForceUpdate为True()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");
        await SetPolicy("production", "1.2.0", "1.0.0", forceUpdate: true);

        var result = await service.CheckUpdateAsync("production", "1.1.0");

        Assert.True(result.Success);
        Assert.True(result.Data!.UpdateAvailable);
        Assert.True(result.Data.ForceUpdate);
    }

    [Fact]
    public async Task CheckUpdateAsync_当前版本低于最低支持版本时强制升级()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");
        await SetPolicy("production", "1.2.0", "1.1.0", forceUpdate: false);

        var result = await service.CheckUpdateAsync("production", "v1.0.9");

        Assert.True(result.Success);
        Assert.True(result.Data!.UpdateAvailable);
        Assert.True(result.Data.ForceUpdate);
    }

    [Fact]
    public async Task CheckUpdateAsync_当前版本高于目标版本返回回退()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");
        await SetPolicy("production", "1.2.0", "1.0.0", forceUpdate: false);

        var result = await service.CheckUpdateAsync("production", "1.3.0");

        Assert.True(result.Success);
        Assert.True(result.Data!.UpdateAvailable);
        Assert.True(result.Data.IsRollback);
        Assert.False(result.Data.ForceUpdate);
    }

    [Fact]
    public async Task CheckUpdateAsync_版本无法解析时返回可控错误且不强制更新()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");
        await SetPolicy("production", "1.2.0", "1.0.0", forceUpdate: true);

        var result = await service.CheckUpdateAsync("production", "bad-version");

        Assert.False(result.Success);
        Assert.Equal("INVALID_VERSION", result.Code);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task CheckUpdateAsync_version_segment_overflow_returns_invalid_version()
    {
        var service = CreateService();

        var result = await service.CheckUpdateAsync(
            "production",
            "999999999999999999999999999.1.0"
        );

        Assert.False(result.Success);
        Assert.Equal("INVALID_VERSION", result.Code);
        Assert.Null(result.Data);
    }

    [Fact]
    public void BuildCosObjectKey_按约定生成WpfReleaseKey并清理危险字符()
    {
        var key = WpfAppReleaseService.BuildCosObjectKey(
            " Preview ",
            "v2.0.0",
            "..\\HB POS Setup?.msi"
        );

        Assert.Equal("wpf-releases/preview/2.0.0/HB-POS-Setup-.msi", key);
    }

    [Fact]
    public async Task CreateUploadInitAsync_返回直传签名和公开下载地址()
    {
        var service = CreateServiceWithUpload();

        var result = await service.CreateUploadInitAsync(
            new WpfAppReleaseUploadInitRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos-1.2.3.msi",
                ContentType = "application/x-msi",
                FileSize = 1024,
                Sha256 = new string('a', 64),
            }
        );

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(
            "wpf-releases/production/1.2.3/hbpos-1.2.3.msi",
            result.Data!.ObjectKey
        );
        Assert.Equal(
            "https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/wpf-releases/production/1.2.3/hbpos-1.2.3.msi",
            result.Data.DownloadUrl
        );
        Assert.NotNull(result.Data.DirectUpload);
        Assert.StartsWith(result.Data.DownloadUrl!, result.Data.DirectUpload!.Url);
        Assert.Equal(new string('a', 64), result.Data.DirectUpload.Headers["x-cos-meta-sha256"]);
        Assert.Contains("x-cos-meta-sha256", Uri.UnescapeDataString(result.Data.DirectUpload.Url));
    }

    [Fact]
    public async Task CreateUploadInitAsync_重复版本已存在时拒绝签发直传地址()
    {
        await CreateRelease("production", "1.2.3");
        var service = CreateServiceWithUpload();

        var result = await service.CreateUploadInitAsync(
            new WpfAppReleaseUploadInitRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos-1.2.3.msi",
                ContentType = "application/x-msi",
                FileSize = 1024,
                Sha256 = new string('a', 64),
            }
        );

        Assert.False(result.Success);
        Assert.Equal("WPF_RELEASE_EXISTS", result.Code);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task CreateUploadInitAsync_拒绝超过512MB的安装包()
    {
        var service = CreateServiceWithUpload();

        var result = await service.CreateUploadInitAsync(
            new WpfAppReleaseUploadInitRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos-1.2.3.msi",
                ContentType = "application/x-msi",
                FileSize = MaxSupportedInstallerBytes + 1,
                Sha256 = new string('a', 64),
            }
        );

        Assert.False(result.Success);
        Assert.Equal("FILE_TOO_LARGE", result.Code);
        Assert.Null(result.Data);
    }

    [Theory]
    [InlineData("hbpos-1.2.3.zip")]
    [InlineData("hbpos-1.2.3.txt")]
    public async Task CreateUploadInitAsync_拒绝非Exe或Msi安装包(string fileName)
    {
        var service = CreateServiceWithUpload();

        var result = await service.CreateUploadInitAsync(
            new WpfAppReleaseUploadInitRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = fileName,
                ContentType = "application/octet-stream",
                FileSize = 1024,
                Sha256 = new string('a', 64),
            }
        );

        Assert.False(result.Success);
        Assert.Equal("INVALID_INSTALLER_FILE", result.Code);
    }

    [Theory]
    [InlineData("../hbpos.exe")]
    [InlineData("folder/hbpos.exe")]
    [InlineData("CON.exe")]
    [InlineData("CON.any.exe")]
    [InlineData("NUL.v1.msi")]
    [InlineData("hbpos?.exe")]
    [InlineData("hbpos:1.2.3.exe")]
    public async Task CreateUploadInitAsync_rejects_dangerous_installer_file_names(
        string fileName)
    {
        var service = CreateServiceWithUpload();

        var result = await service.CreateUploadInitAsync(
            new WpfAppReleaseUploadInitRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = fileName,
                ContentType = "application/octet-stream",
                FileSize = 1024,
                Sha256 = new string('a', 64),
            }
        );

        Assert.False(result.Success);
        Assert.Equal("INVALID_INSTALLER_FILE", result.Code);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task CreateUploadInitAsync_rejects_multipart_upload_contract()
    {
        var service = CreateServiceWithUpload();

        var result = await service.CreateUploadInitAsync(
            new WpfAppReleaseUploadInitRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos-1.2.3.exe",
                ContentType = "application/octet-stream",
                FileSize = 1024,
                Sha256 = new string('a', 64),
                Multipart = true,
            }
        );

        Assert.False(result.Success);
        Assert.Equal("WPF_MULTIPART_UPLOAD_UNSUPPORTED", result.Code);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task UpdateReleaseAsync_rejects_disabling_release_referenced_by_target_version()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");
        await SetPolicy("production", "1.2.0", "1.0.0", forceUpdate: true);
        var release = await _db.Queryable<WpfAppRelease>().SingleAsync(x => x.Version == "1.2.0");

        var result = await service.UpdateReleaseAsync(
            release.Id,
            new WpfAppReleaseUpdateRequest { IsActive = false },
            "admin"
        );

        Assert.False(result.Success);
        Assert.Equal("WPF_RELEASE_REFERENCED_BY_POLICY", result.Code);
        Assert.True((await _db.Queryable<WpfAppRelease>().SingleAsync(x => x.Id == release.Id)).IsActive);
    }

    [Fact]
    public async Task UpdateReleaseAsync_rejects_disabling_release_referenced_by_minimum_supported_version()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");
        await CreateRelease("production", "1.1.0");
        await SetPolicy("production", "1.2.0", "1.1.0", forceUpdate: false);
        var release = await _db.Queryable<WpfAppRelease>().SingleAsync(x => x.Version == "1.1.0");

        var result = await service.UpdateReleaseAsync(
            release.Id,
            new WpfAppReleaseUpdateRequest { IsActive = false },
            "admin"
        );

        Assert.False(result.Success);
        Assert.Equal("WPF_RELEASE_REFERENCED_BY_POLICY", result.Code);
        Assert.True((await _db.Queryable<WpfAppRelease>().SingleAsync(x => x.Id == release.Id)).IsActive);
    }

    [Fact]
    public async Task CreateUploadInitAsync_rejects_duplicate_version_when_prefix_differs()
    {
        await CreateRelease("production", "1.2.3");
        var service = CreateServiceWithUpload();

        var result = await service.CreateUploadInitAsync(
            new WpfAppReleaseUploadInitRequest
            {
                Channel = "production",
                Version = "v1.2.3",
                FileName = "hbpos-v1.2.3.msi",
                ContentType = "application/x-msi",
                FileSize = 1024,
                Sha256 = new string('a', 64),
            }
        );

        Assert.False(result.Success);
        Assert.Equal("WPF_RELEASE_EXISTS", result.Code);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task CreateUploadInitAsync_COS配置缺失时返回稳定错误且不返回上传地址()
    {
        var service = CreateServiceWithUpload(
            settings: new TencentCloudSettings()
        );

        var result = await service.CreateUploadInitAsync(
            new WpfAppReleaseUploadInitRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos-1.2.3.exe",
                ContentType = "application/octet-stream",
                FileSize = 1024,
            }
        );

        Assert.False(result.Success);
        Assert.Equal("COS_UPLOAD_SERVICE_NOT_CONFIGURED", result.Code);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task CreateUploadInitAsync_缺少Sha256时拒绝签发直传地址()
    {
        var service = CreateServiceWithUpload();

        var result = await service.CreateUploadInitAsync(new WpfAppReleaseUploadInitRequest
        {
            Channel = "production",
            Version = "1.2.3",
            FileName = "hbpos-1.2.3.exe",
            ContentType = "application/octet-stream",
            FileSize = 1024,
        });

        Assert.False(result.Success);
        Assert.Equal("INVALID_SHA256", result.Code);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task CreateReleaseAsync_with_upload_service_rejects_active_release_when_cos_object_missing()
    {
        var service = CreateServiceWithUpload(
            handler: CreateHttpHandler(
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            )
        );

        var result = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos-1.2.3.exe",
                FileSize = 100,
                Sha256 = new string('a', 64),
                DownloadUrl = "https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/wpf-releases/production/1.2.3/hbpos-1.2.3.exe",
                InstallerType = "exe",
            },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal("COS_OBJECT_NOT_FOUND", result.Code);
        Assert.Null(result.Data);
        Assert.Equal(0, await _db.Queryable<WpfAppRelease>().CountAsync());
    }

    [Fact]
    public async Task CreateReleaseAsync_with_upload_service_rejects_active_release_when_cos_object_size_mismatches()
    {
        var service = CreateServiceWithUpload(
            handler: CreateHttpHandler(
                _ =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    response.Content = new ByteArrayContent([]);
                    response.Content.Headers.ContentLength = 99;
                    response.Headers.TryAddWithoutValidation(
                        "x-cos-meta-sha256",
                        new string('a', 64)
                    );
                    return response;
                }
            )
        );

        var result = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos-1.2.3.exe",
                FileSize = 100,
                Sha256 = new string('a', 64),
                DownloadUrl = "https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/wpf-releases/production/1.2.3/hbpos-1.2.3.exe",
                InstallerType = "exe",
            },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal("COS_OBJECT_SIZE_MISMATCH", result.Code);
        Assert.Null(result.Data);
        Assert.Equal(0, await _db.Queryable<WpfAppRelease>().CountAsync());
    }

    [Fact]
    public async Task CreateReleaseAsync_with_upload_service_rejects_active_release_when_cos_object_sha_mismatches()
    {
        var service = CreateServiceWithUpload(
            handler: CreateHttpHandler(
                _ =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    response.Content = new ByteArrayContent([]);
                    response.Content.Headers.ContentLength = 100;
                    response.Headers.TryAddWithoutValidation(
                        "x-cos-meta-sha256",
                        new string('b', 64)
                    );
                    return response;
                }
            )
        );

        var result = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = "1.2.3",
                FileName = "hbpos-1.2.3.exe",
                FileSize = 100,
                Sha256 = new string('a', 64),
                DownloadUrl = "https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/wpf-releases/production/1.2.3/hbpos-1.2.3.exe",
                InstallerType = "exe",
            },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal("COS_OBJECT_SHA256_MISMATCH", result.Code);
        Assert.Null(result.Data);
        Assert.Equal(0, await _db.Queryable<WpfAppRelease>().CountAsync());
    }

    [Fact]
    public async Task SetPolicyAsync_rejects_target_release_when_cos_object_cannot_be_verified()
    {
        var service = CreateServiceWithUpload(
            handler: CreateHttpHandler(
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            )
        );
        await _db.Insertable(
            new WpfAppRelease
            {
                Id = Guid.NewGuid(),
                Channel = "production",
                Version = "1.2.0",
                FileName = "hbpos-1.2.0.exe",
                FileSize = 100,
                Sha256 = new string('a', 64),
                DownloadUrl = "https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/wpf-releases/production/1.2.0/hbpos-1.2.0.exe",
                CosObjectKey = "wpf-releases/production/1.2.0/hbpos-1.2.0.exe",
                InstallerType = "exe",
                IsActive = true,
                PublishedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "tester",
            }
        ).ExecuteCommandAsync();

        var result = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.2.0",
                ForceUpdate = true,
            },
            "admin"
        );

        Assert.False(result.Success);
        Assert.Equal("COS_OBJECT_NOT_FOUND", result.Code);
        Assert.Null(result.Data);
        Assert.Equal(0, await _db.Queryable<WpfUpdatePolicy>().CountAsync());
    }

    [Fact]
    public async Task SetPolicyAsync_最低支持版本未登记时拒绝写入策略()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");

        var result = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.2.0",
                MinimumSupportedVersion = "1.1.0",
                ForceUpdate = true,
            },
            "admin"
        );

        Assert.False(result.Success);
        Assert.Equal("MINIMUM_SUPPORTED_RELEASE_NOT_FOUND", result.Code);
        Assert.Null(result.Data);
        Assert.Equal(0, await _db.Queryable<WpfUpdatePolicy>().CountAsync());
    }

    [Fact]
    public async Task SetPolicyAsync_最低支持版本已禁用时拒绝写入策略()
    {
        var service = CreateService();
        await CreateRelease("production", "1.2.0");
        await CreateRelease("production", "1.1.0");
        var minimumRelease = await _db.Queryable<WpfAppRelease>().SingleAsync(x => x.Version == "1.1.0");
        minimumRelease.IsActive = false;
        await _db.Updateable(minimumRelease).ExecuteCommandAsync();

        var result = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.2.0",
                MinimumSupportedVersion = "1.1.0",
                ForceUpdate = true,
            },
            "admin"
        );

        Assert.False(result.Success);
        Assert.Equal("MINIMUM_SUPPORTED_RELEASE_NOT_FOUND", result.Code);
        Assert.Null(result.Data);
        Assert.Equal(0, await _db.Queryable<WpfUpdatePolicy>().CountAsync());
    }

    private WpfAppReleaseService CreateService()
    {
        return new WpfAppReleaseService(_db, NullLogger<WpfAppReleaseService>.Instance);
    }

    private WpfAppReleaseService CreateServiceWithUpload(
        TencentCloudSettings? settings = null,
        HttpMessageHandler? handler = null
    )
    {
        var uploadService = new TencentCloudUploadService(
            Options.Create(
                settings
                ?? new TencentCloudSettings
                {
                    SecretId = "secret-id",
                    SecretKey = "secret-key",
                    BucketName = "hb-sales-2019-1300114625",
                    Region = "ap-singapore",
                }
            ),
            NullLogger<TencentCloudUploadService>.Instance,
            new HttpClient(
                handler
                ?? CreateHttpHandler(_ => CreateCosMetadataResponse(100, new string('a', 64)))
            )
        );
        return new WpfAppReleaseService(
            _db,
            uploadService,
            NullLogger<WpfAppReleaseService>.Instance
        );
    }

    private static HttpMessageHandler CreateHttpHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder
    )
    {
        return new DelegatingTestHandler(
            request => Task.FromResult(responder(request))
        );
    }

    private static HttpResponseMessage CreateCosMetadataResponse(long fileSize, string sha256)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
        };
        response.Content.Headers.ContentLength = fileSize;
        response.Headers.TryAddWithoutValidation("x-cos-meta-sha256", sha256);
        return response;
    }

    private async Task CreateRelease(string channel, string version)
    {
        var service = CreateService();
        var result = await service.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = channel,
                Version = version,
                FileName = $"hbpos-{version}.exe",
                FileSize = 100,
                Sha256 = new string('a', 64),
                DownloadUrl = $"https://example.test/{channel}/{version}.exe",
                InstallerType = "exe",
                InstallerArguments = "/quiet",
                ReleaseNotes = $"release {version}",
            },
            "tester"
        );
        Assert.True(result.Success);
    }

    private async Task SetPolicy(
        string channel,
        string targetVersion,
        string minimumSupportedVersion,
        bool forceUpdate
    )
    {
        var service = CreateService();
        if (
            !string.IsNullOrWhiteSpace(minimumSupportedVersion)
            && await _db.Queryable<WpfAppRelease>()
                .CountAsync(x => x.Channel == channel && x.Version == minimumSupportedVersion) == 0
        )
        {
            await CreateRelease(channel, minimumSupportedVersion);
        }

        var result = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = channel,
                TargetVersion = targetVersion,
                MinimumSupportedVersion = minimumSupportedVersion,
                ForceUpdate = forceUpdate,
            },
            "tester"
        );
        Assert.True(result.Success);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbPath))
        {
            SqliteTempFileCleanup.DeleteIfExists(_dbPath);
        }
    }

    private sealed class DelegatingTestHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder
    ) : HttpMessageHandler
    {
        // 中文注释：单测通过可编程的 HttpMessageHandler 模拟 COS HEAD 响应，避免访问真实对象存储。
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return responder(request);
        }
    }
}
