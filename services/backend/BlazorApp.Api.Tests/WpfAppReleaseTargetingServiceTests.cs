using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class WpfAppReleaseTargetingServiceTests : IDisposable
{
    private readonly string _mainDbPath = Path.Combine(Path.GetTempPath(), $"wpf-target-main-{Guid.NewGuid():N}.db");
    private readonly string _posmDbPath = Path.Combine(Path.GetTempPath(), $"wpf-target-posm-{Guid.NewGuid():N}.db");
    private readonly ISqlSugarClient _mainDb;
    private readonly ISqlSugarClient _posmDb;

    public WpfAppReleaseTargetingServiceTests()
    {
        _mainDb = CreateSqliteClient(_mainDbPath);
        _posmDb = CreateSqliteClient(_posmDbPath);
        _mainDb.CodeFirst.InitTables<WpfAppRelease, WpfUpdatePolicy, WpfUpdatePolicyTarget, Store>();
        _posmDb.CodeFirst.InitTables<POSM_设备注册信息表>();
    }

    [Fact]
    public async Task CheckUpdateAsync_仅接受精确硬件标识与匹配授权码的设备身份()
    {
        var service = CreateService();
        await CreateReleaseAsync("1.0.0");
        await CreateReleaseAsync("1.2.0");
        await _posmDb.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = "hardware-1",
            系统设备编号 = "system-old",
            分店代码 = "BRI",
            设备类型 = "POS",
            设备系统 = "Windows",
            设备状态 = (int)DeviceStatus.启用,
            设备授权码 = "auth-1",
            创建时间 = DateTime.UtcNow.AddMinutes(-1),
        }).ExecuteCommandAsync();
        await _posmDb.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = "hardware-1",
            系统设备编号 = "system-1",
            分店代码 = "BRI",
            设备类型 = "POS",
            设备系统 = "Windows",
            设备状态 = (int)DeviceStatus.启用,
            设备授权码 = "auth-1",
            创建时间 = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        var device = await _posmDb.Queryable<POSM_设备注册信息表>()
            .OrderByDescending(item => item.ID)
            .FirstAsync();
        var policy = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.2.0",
                MinimumSupportedVersion = "1.0.0",
                TargetScope = "devices",
                TargetDeviceRegistrationIds = [device.ID],
            },
            "admin"
        );
        Assert.True(policy.Success);

        var exactMatch = await service.CheckUpdateAsync("production", "1.0.0", "hardware-1", "auth-1");
        var systemNumberAlias = await service.CheckUpdateAsync("production", "1.0.0", "system-1", "auth-1");
        var wrongAuthCode = await service.CheckUpdateAsync("production", "1.0.0", "hardware-1", "wrong-auth");

        Assert.True(exactMatch.Success);
        Assert.True(exactMatch.Data!.UpdateAvailable);
        Assert.False(systemNumberAlias.Data!.UpdateAvailable);
        Assert.False(wrongAuthCode.Data!.UpdateAvailable);
    }

    [Fact]
    public async Task CheckUpdateAsync_最新登记换码或禁用后旧记录不得继续命中()
    {
        var service = CreateService();
        await CreateReleaseAsync("1.0.0");
        await CreateReleaseAsync("1.2.0");
        await _posmDb.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = "hardware-rotated",
            系统设备编号 = "POS-OLD-AUTH",
            设备类型 = "POS",
            设备系统 = "Windows",
            设备状态 = (int)DeviceStatus.启用,
            设备授权码 = "old-auth",
            创建时间 = DateTime.UtcNow.AddMinutes(-2),
        }).ExecuteCommandAsync();
        var oldAuthDevice = await _posmDb.Queryable<POSM_设备注册信息表>().SingleAsync();
        var oldAuthPolicy = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.2.0",
                MinimumSupportedVersion = "1.0.0",
                TargetScope = "devices",
                TargetDeviceRegistrationIds = [oldAuthDevice.ID],
            },
            "admin"
        );
        Assert.True(oldAuthPolicy.Success);
        await _posmDb.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = "hardware-rotated",
            系统设备编号 = "POS-NEW-AUTH",
            设备类型 = "POS",
            设备系统 = "Windows",
            设备状态 = (int)DeviceStatus.启用,
            设备授权码 = "new-auth",
            创建时间 = DateTime.UtcNow,
        }).ExecuteCommandAsync();

        var staleAuth = await service.CheckUpdateAsync(
            "production",
            "1.0.0",
            "hardware-rotated",
            "old-auth"
        );

        Assert.True(staleAuth.Success);
        Assert.False(staleAuth.Data!.UpdateAvailable);

        await _posmDb.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = "hardware-disabled",
            系统设备编号 = "POS-OLD-ENABLED",
            设备类型 = "POS",
            设备系统 = "Windows",
            设备状态 = (int)DeviceStatus.启用,
            设备授权码 = "same-auth",
            创建时间 = DateTime.UtcNow.AddMinutes(-2),
        }).ExecuteCommandAsync();
        var oldEnabledDevice = await _posmDb.Queryable<POSM_设备注册信息表>()
            .Where(device => device.设备硬件识别码 == "hardware-disabled")
            .SingleAsync();
        var oldEnabledPolicy = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.2.0",
                MinimumSupportedVersion = "1.0.0",
                TargetScope = "devices",
                TargetDeviceRegistrationIds = [oldEnabledDevice.ID],
            },
            "admin"
        );
        Assert.True(oldEnabledPolicy.Success);
        await _posmDb.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = "hardware-disabled",
            系统设备编号 = "POS-NEW-DISABLED",
            设备类型 = "POS",
            设备系统 = "Windows",
            设备状态 = (int)DeviceStatus.禁用,
            设备授权码 = "same-auth",
            创建时间 = DateTime.UtcNow,
        }).ExecuteCommandAsync();

        var staleEnabled = await service.CheckUpdateAsync(
            "production",
            "1.0.0",
            "hardware-disabled",
            "same-auth"
        );

        Assert.True(staleEnabled.Success);
        Assert.False(staleEnabled.Data!.UpdateAvailable);
    }

    [Fact]
    public async Task GetDeviceOptionsAsync_不泄露授权码或完整硬件标识()
    {
        var service = CreateService();
        await _mainDb.Insertable(new Store
        {
            StoreGUID = "store-brisbane",
            StoreCode = "BRI",
            StoreName = "Brisbane",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        await _posmDb.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = "hardware-secret-must-not-leak",
            系统设备编号 = "POS-BRI-01",
            分店代码 = "BRI",
            设备类型 = "POS",
            设备系统 = "Windows",
            设备状态 = (int)DeviceStatus.启用,
            设备授权码 = "auth-secret-must-not-leak",
            备注 = "Front counter",
            创建时间 = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        await _posmDb.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = "disabled-hardware",
            系统设备编号 = "POS-BRI-DISABLED",
            分店代码 = "BRI",
            设备类型 = "POS",
            设备系统 = "Windows",
            设备状态 = (int)DeviceStatus.禁用,
            设备授权码 = "disabled-auth",
            创建时间 = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        await _posmDb.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = "mobile-hardware",
            系统设备编号 = "MOBILE-BRI-01",
            分店代码 = "BRI",
            设备类型 = "Mobile",
            设备系统 = "Android",
            设备状态 = (int)DeviceStatus.启用,
            设备授权码 = "mobile-auth",
            创建时间 = DateTime.UtcNow,
        }).ExecuteCommandAsync();

        var result = await service.GetDeviceOptionsAsync(1, 20, "POS-BRI");
        var item = Assert.Single(result.Data!.Items!);
        var json = JsonSerializer.Serialize(result);

        Assert.Equal("POS-BRI-01", item.SystemDeviceNumber);
        Assert.Equal("Brisbane", item.StoreName);
        Assert.Equal("Front counter", item.Remarks);
        Assert.DoesNotContain("hardware-secret-must-not-leak", json, StringComparison.Ordinal);
        Assert.DoesNotContain("auth-secret-must-not-leak", json, StringComparison.Ordinal);
        Assert.Null(typeof(WpfUpdateTargetDeviceOptionDto).GetProperty("HardwareId"));
        Assert.Null(typeof(WpfUpdateTargetDeviceOptionDto).GetProperty("AuthCode"));
        Assert.Null(typeof(WpfUpdateTargetDeviceOptionDto).GetProperty("Status"));
    }

    [Fact]
    public async Task 设备目标_只列出并接受同一硬件的最新登记且排除空硬件标识()
    {
        var service = CreateService();
        await CreateReleaseAsync("1.0.0");
        await _posmDb.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = "hardware-reregistered",
            系统设备编号 = "POS-OLD",
            设备类型 = "POS",
            设备系统 = "Windows",
            设备状态 = (int)DeviceStatus.启用,
            设备授权码 = "old-auth",
            创建时间 = DateTime.UtcNow.AddMinutes(-2),
        }).ExecuteCommandAsync();
        var oldDevice = await _posmDb.Queryable<POSM_设备注册信息表>().SingleAsync();
        await _posmDb.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = "hardware-reregistered",
            系统设备编号 = "POS-NEW",
            设备类型 = "POS",
            设备系统 = "Windows",
            设备状态 = (int)DeviceStatus.启用,
            设备授权码 = "new-auth",
            创建时间 = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        await _posmDb.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = "   ",
            系统设备编号 = "POS-NO-HARDWARE",
            设备类型 = "POS",
            设备系统 = "Windows",
            设备状态 = (int)DeviceStatus.启用,
            设备授权码 = "blank-auth",
            创建时间 = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        await _posmDb.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = "hardware-other",
            系统设备编号 = "POS-OTHER",
            设备类型 = "POS",
            设备系统 = "Windows",
            设备状态 = (int)DeviceStatus.启用,
            设备授权码 = "other-auth",
            创建时间 = DateTime.UtcNow.AddMinutes(-1),
        }).ExecuteCommandAsync();
        var newDevice = await _posmDb.Queryable<POSM_设备注册信息表>()
            .Where(device => device.系统设备编号 == "POS-NEW")
            .SingleAsync();
        var blankHardwareDevice = await _posmDb.Queryable<POSM_设备注册信息表>()
            .Where(device => device.系统设备编号 == "POS-NO-HARDWARE")
            .SingleAsync();

        var deviceOptionSql = new List<string>();
        _posmDb.Aop.OnLogExecuting = (sql, _) => deviceOptionSql.Add(sql);
        var firstPage = await service.GetDeviceOptionsAsync(1, 1, null);
        var secondPage = await service.GetDeviceOptionsAsync(2, 1, null);
        var firstPageOption = Assert.Single(firstPage.Data!.Items!);
        var secondPageOption = Assert.Single(secondPage.Data!.Items!);
        Assert.Contains(deviceOptionSql, sql =>
            sql.Contains("NOT", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("EXISTS", StringComparison.OrdinalIgnoreCase));
        var oldTarget = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.0.0",
                MinimumSupportedVersion = "1.0.0",
                TargetScope = "devices",
                TargetDeviceRegistrationIds = [oldDevice.ID],
            },
            "admin"
        );
        var blankHardwareTarget = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.0.0",
                MinimumSupportedVersion = "1.0.0",
                TargetScope = "devices",
                TargetDeviceRegistrationIds = [blankHardwareDevice.ID],
            },
            "admin"
        );
        var latestTarget = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.0.0",
                MinimumSupportedVersion = "1.0.0",
                TargetScope = "devices",
                TargetDeviceRegistrationIds = [newDevice.ID],
            },
            "admin"
        );

        Assert.Equal(newDevice.ID, firstPageOption.DeviceRegistrationId);
        Assert.Equal("POS-OTHER", secondPageOption.SystemDeviceNumber);
        Assert.Equal(2, firstPage.Data.Total);
        Assert.Equal(2, secondPage.Data.Total);
        Assert.False(oldTarget.Success);
        Assert.Equal("TARGET_DEVICES_INVALID", oldTarget.Code);
        Assert.False(blankHardwareTarget.Success);
        Assert.Equal("TARGET_DEVICES_INVALID", blankHardwareTarget.Code);
        Assert.True(latestTarget.Success);
    }

    [Fact]
    public async Task 策略与发布只读响应_补全设备安全摘要且保留已禁用目标()
    {
        var releaseService = new WpfAppReleaseService(
            _mainDb,
            NullLogger<WpfAppReleaseService>.Instance,
            _posmDb
        );
        var targetingService = new WpfAppReleaseTargetingService(
            releaseService,
            CreateContext<SqlSugarContext>(_mainDb),
            CreateContext<POSMSqlSugarContext>(_posmDb)
        );
        await CreateReleaseAsync("1.0.0");
        await CreateReleaseAsync("1.2.0");
        await _mainDb.Insertable(new Store
        {
            StoreGUID = "store-brisbane",
            StoreCode = "BRI",
            StoreName = "Brisbane",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        await _posmDb.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = "hardware-secret-must-not-leak-from-summary",
            系统设备编号 = "POS-BRI-02",
            分店代码 = "BRI",
            设备类型 = "POS",
            设备系统 = "Windows",
            设备状态 = (int)DeviceStatus.启用,
            设备授权码 = "auth-secret-must-not-leak-from-summary",
            备注 = "Back counter",
            创建时间 = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        var device = await _posmDb.Queryable<POSM_设备注册信息表>().SingleAsync();

        var saved = await targetingService.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.2.0",
                MinimumSupportedVersion = "1.0.0",
                TargetScope = "devices",
                TargetDeviceRegistrationIds = [device.ID],
            },
            "admin"
        );

        Assert.True(saved.Success);
        var savedSummary = Assert.Single(saved.Data!.TargetDeviceSummaries);
        Assert.Equal(device.ID, savedSummary.DeviceRegistrationId);
        Assert.Equal("POS-BRI-02", savedSummary.SystemDeviceNumber);
        Assert.Equal("BRI", savedSummary.StoreCode);
        Assert.Equal("Brisbane", savedSummary.StoreName);
        Assert.Equal("Back counter", savedSummary.Remarks);

        device.设备状态 = (int)DeviceStatus.禁用;
        await _posmDb.Updateable(device).ExecuteCommandAsync();
        var listed = await releaseService.GetReleasesAsync(
            new WpfAppReleaseQuery { Channel = "production" }
        );
        var listJson = JsonSerializer.Serialize(listed);
        var listSummary = Assert.Single(listed.Data!.Items!.First().TargetDeviceSummaries);
        Assert.Equal(device.ID, listSummary.DeviceRegistrationId);
        Assert.Equal("POS-BRI-02", listSummary.SystemDeviceNumber);
        Assert.Equal("Brisbane", listSummary.StoreName);
        Assert.DoesNotContain("hardware-secret-must-not-leak-from-summary", listJson, StringComparison.Ordinal);
        Assert.DoesNotContain("auth-secret-must-not-leak-from-summary", listJson, StringComparison.Ordinal);
        Assert.Null(typeof(WpfUpdateTargetDeviceSummaryDto).GetProperty("HardwareId"));
        Assert.Null(typeof(WpfUpdateTargetDeviceSummaryDto).GetProperty("AuthCode"));
        Assert.Null(typeof(WpfUpdateTargetDeviceSummaryDto).GetProperty("Status"));
    }

    [Fact]
    public async Task SetPolicyAsync_拒绝非活动分店或非启用WindowsPos设备且不写策略()
    {
        var service = CreateService();
        await CreateReleaseAsync("1.0.0");
        await _mainDb.Insertable(new Store
        {
            StoreGUID = "inactive-store",
            StoreCode = "OLD",
            StoreName = "Old store",
            IsActive = false,
            CreatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        await _posmDb.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = "disabled-device",
            系统设备编号 = "POS-OLD-01",
            设备类型 = "POS",
            设备系统 = "Windows",
            设备状态 = (int)DeviceStatus.禁用,
            设备授权码 = "auth",
            创建时间 = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        var disabledDevice = await _posmDb.Queryable<POSM_设备注册信息表>().SingleAsync();

        var invalidStore = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.0.0",
                MinimumSupportedVersion = "1.0.0",
                TargetScope = "stores",
                TargetStoreGuids = ["inactive-store"],
            },
            "admin"
        );
        var invalidDevice = await service.SetPolicyAsync(
            new WpfUpdatePolicyRequest
            {
                Channel = "production",
                TargetVersion = "1.0.0",
                MinimumSupportedVersion = "1.0.0",
                TargetScope = "devices",
                TargetDeviceRegistrationIds = [disabledDevice.ID],
            },
            "admin"
        );

        Assert.False(invalidStore.Success);
        Assert.Equal("TARGET_STORES_INVALID", invalidStore.Code);
        Assert.False(invalidDevice.Success);
        Assert.Equal("TARGET_DEVICES_INVALID", invalidDevice.Code);
        Assert.Empty(await _mainDb.Queryable<WpfUpdatePolicy>().ToListAsync());
    }

    private WpfAppReleaseTargetingService CreateService()
    {
        return new WpfAppReleaseTargetingService(
            new WpfAppReleaseService(_mainDb, NullLogger<WpfAppReleaseService>.Instance),
            CreateContext<SqlSugarContext>(_mainDb),
            CreateContext<POSMSqlSugarContext>(_posmDb)
        );
    }

    private async Task CreateReleaseAsync(string version)
    {
        var releaseService = new WpfAppReleaseService(_mainDb, NullLogger<WpfAppReleaseService>.Instance);
        var result = await releaseService.CreateReleaseAsync(
            new WpfAppReleaseCreateRequest
            {
                Channel = "production",
                Version = version,
                FileName = $"hbpos-{version}.exe",
                FileSize = 100,
                Sha256 = new string('a', 64),
                DownloadUrl = $"https://example.test/wpf/hbpos-{version}.exe",
                InstallerType = "exe",
            },
            "tester"
        );
        Assert.True(result.Success);
    }

    private static ISqlSugarClient CreateSqliteClient(string path)
    {
        return new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = $"DataSource={path}",
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        });
    }

    private static TContext CreateContext<TContext>(ISqlSugarClient db)
        where TContext : class
    {
        var context = (TContext)RuntimeHelpers.GetUninitializedObject(typeof(TContext));
        typeof(TContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    public void Dispose()
    {
        _mainDb.Dispose();
        _posmDb.Dispose();
        if (File.Exists(_mainDbPath))
        {
            File.Delete(_mainDbPath);
        }

        if (File.Exists(_posmDbPath))
        {
            File.Delete(_posmDbPath);
        }
    }
}
