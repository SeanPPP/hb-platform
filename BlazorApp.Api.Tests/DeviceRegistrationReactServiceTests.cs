using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class DeviceRegistrationReactServiceTests : IDisposable
{
    private readonly string _mainDbPath;
    private readonly string _posmDbPath;
    private readonly SqliteConnection _mainConnection;
    private readonly SqliteConnection _posmConnection;
    private readonly SqlSugarClient _mainDb;
    private readonly SqlSugarClient _posmDb;

    public DeviceRegistrationReactServiceTests()
    {
        _mainDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-main.db");
        _posmDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-posm.db");
        _mainConnection = new SqliteConnection($"Data Source={_mainDbPath}");
        _posmConnection = new SqliteConnection($"Data Source={_posmDbPath}");
        _mainConnection.Open();
        _posmConnection.Open();

        _mainDb = CreateClient(_mainConnection.ConnectionString);
        _posmDb = CreateClient(_posmConnection.ConnectionString);
        _mainDb.CodeFirst.InitTables<Store>();
        _posmDb.CodeFirst.InitTables<POSM_设备注册信息表>();
    }

    [Fact]
    public void DeviceRegistrationReactDtos_DoNotExposeAuthCode()
    {
        Assert.DoesNotContain(
            typeof(DeviceRegistrationListDto).GetProperties(BindingFlags.Instance | BindingFlags.Public),
            property => property.Name == "设备授权码"
        );
        Assert.DoesNotContain(
            typeof(DeviceRegistrationDetailDto).GetProperties(BindingFlags.Instance | BindingFlags.Public),
            property => property.Name == "设备授权码"
        );
        Assert.DoesNotContain(
            typeof(UpdateDeviceRegistrationDto).GetProperties(BindingFlags.Instance | BindingFlags.Public),
            property => property.Name == "设备状态"
        );
    }

    [Fact]
    public async Task UpdateAsync_UpdatesEditableFieldsWithoutChangingStatus()
    {
        await _posmDb.Insertable(new POSM_设备注册信息表
        {
            ID = 1,
            设备硬件识别码 = "hardware-001",
            系统设备编号 = "SYS-001",
            设备授权码 = "AUTH-SECRET",
            设备状态 = (int)DeviceStatus.启用,
            设备类型 = "Mobile",
            设备系统 = "Android",
            分店代码 = "1004",
            备注 = "old remark",
            创建时间 = DateTime.UtcNow,
            创建人 = "creator",
        }).ExecuteCommandAsync();
        await _mainDb.Insertable(new Store
        {
            StoreGUID = Guid.NewGuid().ToString("N"),
            StoreCode = "1004",
            StoreName = "Store 1004",
            IsActive = true,
        }).ExecuteCommandAsync();
        var service = CreateService();

        var result = await service.UpdateAsync(
            1,
            new UpdateDeviceRegistrationDto
            {
                设备类型 = "PDA",
                设备系统 = "iOS",
                备注 = "new remark",
            },
            "manager"
        );

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("PDA", result.Data!.设备类型);
        Assert.Equal("iOS", result.Data.设备系统);
        Assert.Equal("new remark", result.Data.备注);
        Assert.Equal((int)DeviceStatus.启用, result.Data.设备状态);

        var saved = await _posmDb.Queryable<POSM_设备注册信息表>()
            .SingleAsync(device => device.ID == 1);
        Assert.Equal((int)DeviceStatus.启用, saved.设备状态);
        Assert.Equal("AUTH-SECRET", saved.设备授权码);
        Assert.Equal("manager", saved.最后修改人);
        Assert.NotNull(saved.最后修改时间);
    }

    [Fact]
    public async Task UpdateAsync_RejectsInvalidEditableFieldValues()
    {
        await _posmDb.Insertable(new POSM_设备注册信息表
        {
                ID = 1,
            设备硬件识别码 = "hardware-002",
            系统设备编号 = "SYS-002",
            设备授权码 = "AUTH-SECRET-2",
            设备状态 = (int)DeviceStatus.启用,
            设备类型 = "Mobile",
            设备系统 = "Android",
            分店代码 = "1005",
            备注 = "old remark",
            创建时间 = DateTime.UtcNow,
            创建人 = "creator",
        }).ExecuteCommandAsync();
        var service = CreateService();

            var result = await service.UpdateAsync(
                1,
            new UpdateDeviceRegistrationDto
            {
                设备类型 = "Tablet",
                设备系统 = "Linux",
                备注 = "should not save",
            },
            "manager"
        );

        Assert.False(result.Success);
        var saved = await _posmDb.Queryable<POSM_设备注册信息表>()
            .SingleAsync(device => device.ID == 1);
        Assert.Equal("Mobile", saved.设备类型);
        Assert.Equal("Android", saved.设备系统);
        Assert.Equal("old remark", saved.备注);
        Assert.Null(saved.最后修改人);
        Assert.Null(saved.最后修改时间);
    }

    public void Dispose()
    {
        _mainDb.Dispose();
        _posmDb.Dispose();
        _mainConnection.Dispose();
        _posmConnection.Dispose();
        DeleteIfExists(_mainDbPath);
        DeleteIfExists(_posmDbPath);
    }

    private static SqlSugarClient CreateClient(string connectionString) =>
        new(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });

    private DeviceRegistrationReactService CreateService()
    {
        var mainContext = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(SqlSugarContext)
        );
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(mainContext, _mainDb);

        var posmContext = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(POSMSqlSugarContext)
        );
        typeof(POSMSqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(posmContext, _posmDb);

        return new DeviceRegistrationReactService(
            posmContext,
            mainContext,
            NullLogger<DeviceRegistrationReactService>.Instance
        );
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
