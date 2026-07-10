using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.POSM;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class MobileAppDeviceStatusServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;
    private readonly Mock<IDeviceRegistrationService> _deviceRegistrationService = new();
    private DateTime _now = new(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc);

    public MobileAppDeviceStatusServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"mobile-app-device-status-{Guid.NewGuid():N}.db");
        _sqliteConnection = new SqliteConnection($"Data Source={_dbPath}");
        _sqliteConnection.Open();

        _db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = _sqliteConnection.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            }
        );

        _db.CodeFirst.InitTables<MobileAppDeviceStatus, User>();
        _db.Ado.ExecuteCommand(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_MobileAppDeviceStatus_HardwareId ON MobileAppDeviceStatus(HardwareId)"
        );
        _deviceRegistrationService
            .Setup(service => service.GetDeviceByHardwareIdAsync(It.IsAny<string>()))
            .ReturnsAsync((POSM_设备注册信息表?)null);
    }

    [Fact]
    public async Task UpsertHeartbeat_同一HardwareId只保留当前快照并补充注册信息()
    {
        _deviceRegistrationService
            .Setup(service => service.GetDeviceByHardwareIdAsync("hbmobile-001"))
            .ReturnsAsync(
                new POSM_设备注册信息表
                {
                    ID = 88,
                    设备硬件识别码 = "hbmobile-001",
                    系统设备编号 = "SYS-001",
                    设备系统 = "Android",
                    分店代码 = "1004",
                }
            );
        var service = CreateService();
        var authContext = new MobileAppDeviceHeartbeatAuthContext(
            "bearer-device",
            "user-1",
            "sean",
            "Sean",
            true
        );

        await service.UpsertHeartbeatAsync(
            new MobileAppDeviceHeartbeatDto
            {
                HardwareId = " hbmobile-001 ",
                SystemDeviceNumber = "CLIENT-SYS",
                DeviceSystem = "iOS",
                Platform = "android",
                StoreCode = "9999",
                AppVersion = "1.0.0",
                RuntimeVersion = "1.0.0",
                Channel = "preview",
                UpdateId = "update-old",
            },
            authContext
        );
        _now = _now.AddMinutes(1);

        var response = await service.UpsertHeartbeatAsync(
            new MobileAppDeviceHeartbeatDto
            {
                HardwareId = "hbmobile-001",
                SystemDeviceNumber = "CLIENT-SYS-2",
                DeviceSystem = "iOS",
                Platform = "android",
                StoreCode = "9999",
                AppVersion = "1.0.1",
                AppBuildVersion = "12",
                RuntimeVersion = "1.0.1",
                Channel = "production",
                UpdateId = "update-new",
            },
            authContext
        );

        Assert.True(response.Success);
        Assert.Equal(1, await _db.Queryable<MobileAppDeviceStatus>().CountAsync());
        var saved = await _db.Queryable<MobileAppDeviceStatus>().SingleAsync();
        Assert.Equal("hbmobile-001", saved.HardwareId);
        Assert.Equal("SYS-001", saved.SystemDeviceNumber);
        Assert.Equal("Android", saved.DeviceSystem);
        Assert.Equal("1004", saved.StoreCode);
        Assert.Equal("1.0.1", saved.AppVersion);
        Assert.Equal("12", saved.AppBuildVersion);
        Assert.Equal("production", saved.Channel);
        Assert.Equal("update-new", saved.UpdateId);
        Assert.Equal("bearer-device", saved.LastAuthMode);
        Assert.Equal("user-1", saved.LastSeenUserGuid);
        Assert.Equal(88, saved.RegisteredDeviceId);
        Assert.True(response.Data!.IsOnline);
    }

    [Fact]
    public async Task UpsertHeartbeat_BearerOnly不能更新已注册设备快照()
    {
        _deviceRegistrationService
            .Setup(service => service.GetDeviceByHardwareIdAsync("hbmobile-registered"))
            .ReturnsAsync(
                new POSM_设备注册信息表
                {
                    ID = 91,
                    设备硬件识别码 = "hbmobile-registered",
                    系统设备编号 = "SYS-091",
                    设备系统 = "Android",
                    分店代码 = "1004",
                }
            );
        var service = CreateService();

        var response = await service.UpsertHeartbeatAsync(
            new MobileAppDeviceHeartbeatDto
            {
                HardwareId = "hbmobile-registered",
                StoreCode = "9999",
                DeviceSystem = "iOS",
            },
            new MobileAppDeviceHeartbeatAuthContext("bearer", "user-1", "staff", "Staff")
        );

        Assert.False(response.Success);
        Assert.Equal("REGISTERED_DEVICE_AUTH_REQUIRED", response.ErrorCode);
        Assert.Equal(0, await _db.Queryable<MobileAppDeviceStatus>().CountAsync());
    }

    [Fact]
    public async Task UpsertHeartbeat_注册设备查询失败_不降级为未注册快照()
    {
        _deviceRegistrationService
            .Setup(service => service.GetDeviceByHardwareIdAsync("hbmobile-posm-error"))
            .ThrowsAsync(new InvalidOperationException("POSM unavailable"));
        var service = CreateService();

        var response = await service.UpsertHeartbeatAsync(
            new MobileAppDeviceHeartbeatDto
            {
                HardwareId = "hbmobile-posm-error",
                StoreCode = "9999",
                DeviceSystem = "iOS",
            },
            new MobileAppDeviceHeartbeatAuthContext("bearer", "user-1", "staff", "Staff")
        );

        Assert.False(response.Success);
        Assert.Equal("REGISTERED_DEVICE_LOOKUP_FAILED", response.ErrorCode);
        Assert.Equal(0, await _db.Queryable<MobileAppDeviceStatus>().CountAsync());
    }

    [Fact]
    public async Task UpsertHeartbeat_首次并发同一HardwareId_最终只保留一条快照()
    {
        var service = CreateService();
        var authContext = new MobileAppDeviceHeartbeatAuthContext(
            "bearer",
            "user-1",
            "staff",
            "Staff"
        );

        await Task.WhenAll(
            service.UpsertHeartbeatAsync(
                new MobileAppDeviceHeartbeatDto
                {
                    HardwareId = "hbmobile-concurrent",
                    AppVersion = "1.0.1",
                    Platform = "android",
                },
                authContext
            ),
            service.UpsertHeartbeatAsync(
                new MobileAppDeviceHeartbeatDto
                {
                    HardwareId = "hbmobile-concurrent",
                    AppVersion = "1.0.2",
                    Platform = "android",
                },
                authContext
            )
        );

        Assert.Equal(1, await _db.Queryable<MobileAppDeviceStatus>()
            .Where(item => item.HardwareId == "hbmobile-concurrent")
            .CountAsync());
    }

    [Fact]
    public async Task GetSummary_按十五分钟在线窗口和设备系统计数()
    {
        await InsertStatusAsync("hw-android", "Android", "android", _now.AddMinutes(-1));
        await InsertStatusAsync("hw-ios", null, "ios", _now.AddMinutes(-16));
        await InsertStatusAsync("hw-unknown", null, "web", _now.AddMinutes(-60));
        var service = CreateService();

        var response = await service.GetSummaryAsync(new MobileAppDeviceStatusQueryDto());

        Assert.True(response.Success);
        Assert.Equal(3, response.Data!.Total);
        Assert.Equal(1, response.Data.Online);
        Assert.Equal(2, response.Data.Offline);
        Assert.Equal(1, response.Data.Android);
        Assert.Equal(1, response.Data.Ios);
        Assert.Equal(1, response.Data.UnknownSystem);
    }

    [Fact]
    public async Task GetPaged_默认按最后在线时间倒序分页()
    {
        await InsertStatusAsync("hw-old", "Android", "android", _now.AddMinutes(-10));
        await InsertStatusAsync("hw-new", "iOS", "ios", _now.AddMinutes(-2));
        var service = CreateService();

        var response = await service.GetPagedAsync(
            new MobileAppDeviceStatusQueryDto { Page = 1, PageSize = 1 }
        );

        Assert.True(response.Success);
        Assert.Equal(2, response.Data!.Total);
        Assert.Single(response.Data.Items!);
        Assert.Equal("hw-new", response.Data.Items![0].HardwareId);
    }

    [Fact]
    public async Task Heartbeat_无Bearer且无有效设备会话_返回401()
    {
        var controller = new MobileAppDeviceStatusController(
            CreateService(),
            _deviceRegistrationService.Object,
            CreateSqlSugarContext(_db),
            NullLogger<MobileAppDeviceStatusController>.Instance
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        var result = await controller.Heartbeat(
            new MobileAppDeviceHeartbeatDto { HardwareId = "hbmobile-unauthorized" }
        );

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();

        if (File.Exists(_dbPath))
        {
            SqliteTempFileCleanup.DeleteIfExists(_dbPath);
        }
    }

    private MobileAppDeviceStatusService CreateService()
    {
        return new MobileAppDeviceStatusService(
            _db,
            _deviceRegistrationService.Object,
            NullLogger<MobileAppDeviceStatusService>.Instance,
            () => _now
        );
    }

    private async Task InsertStatusAsync(
        string hardwareId,
        string? deviceSystem,
        string? platform,
        DateTime lastSeenAtUtc
    )
    {
        await _db.Insertable(new MobileAppDeviceStatus
        {
            Id = Guid.NewGuid(),
            HardwareId = hardwareId,
            DeviceSystem = deviceSystem,
            Platform = platform,
            LastSeenAtUtc = lastSeenAtUtc,
            LastAuthMode = "test",
            CreatedAt = lastSeenAtUtc,
        }).ExecuteCommandAsync();
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(SqlSugarContext)
        );

        var dbField = typeof(SqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, db);

        return context;
    }
}
