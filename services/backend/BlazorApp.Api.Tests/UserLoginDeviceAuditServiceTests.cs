using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public sealed class UserLoginDeviceAuditServiceTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _sqliteConnection;
        private readonly SqlSugarClient _db;
        private readonly Mock<IDeviceRegistrationService> _deviceRegistrationService = new();

        public UserLoginDeviceAuditServiceTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
            _sqliteConnection = new SqliteConnection($"Data Source={_dbPath}");
            _sqliteConnection.Open();

            _db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = _sqliteConnection.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            });

            _db.CodeFirst.InitTables(typeof(UserLoginDeviceRecord));
            _deviceRegistrationService
                .Setup(service => service.GetDeviceByHardwareIdAsync(It.IsAny<string>()))
                .ReturnsAsync((POSM_设备注册信息表?)null);
        }

        [Fact]
        public async Task RecordAsync_WhenDeviceChanges_MarksDeviceSwitched()
        {
            var service = CreateService();
            var user = CreateUser();

            await service.RecordAsync(user, CreateInput("device-a"));
            var result = await service.RecordAsync(user, CreateInput("device-b"));

            Assert.True(result.IsDeviceSwitched);
            Assert.False(result.IsCommonDevice);

            var stored = await _db.Queryable<UserLoginDeviceRecord>()
                .OrderByDescending(item => item.LoginAtUtc)
                .FirstAsync();
            Assert.Equal("device-b", stored.HardwareId);
            Assert.True(stored.IsDeviceSwitched);
        }

        [Fact]
        public async Task RecordAsync_WhenSameDeviceLogsInThirdTime_MarksCommonDevice()
        {
            var service = CreateService();
            var user = CreateUser();

            var first = await service.RecordAsync(user, CreateInput("device-a"));
            var second = await service.RecordAsync(user, CreateInput("device-a"));
            var third = await service.RecordAsync(user, CreateInput("device-a"));

            Assert.False(first.IsCommonDevice);
            Assert.False(second.IsCommonDevice);
            Assert.True(third.IsCommonDevice);
            Assert.Equal(3, await _db.Queryable<UserLoginDeviceRecord>().CountAsync());
        }

        [Fact]
        public async Task RecordAsync_WhenDeviceIsEnabledInBackend_MarksCommonDeviceImmediately()
        {
            _deviceRegistrationService
                .Setup(service => service.GetDeviceByHardwareIdAsync("enabled-device"))
                .ReturnsAsync(new POSM_设备注册信息表
                {
                    设备硬件识别码 = "enabled-device",
                    系统设备编号 = "SYS-001",
                    设备系统 = "iOS",
                    设备状态 = 1,
                });
            var service = CreateService();

            var result = await service.RecordAsync(CreateUser(), CreateInput("enabled-device"));

            Assert.False(result.IsDeviceSwitched);
            Assert.True(result.IsCommonDevice);
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

        private UserLoginDeviceAuditService CreateService()
        {
            return new UserLoginDeviceAuditService(
                CreateSqlSugarContext(_db),
                _deviceRegistrationService.Object
            );
        }

        private static User CreateUser() => new()
        {
            UserGUID = "user-1",
            Username = "staff",
        };

        private static LoginDeviceAuditInput CreateInput(string hardwareId) => new(
            LoginSource: "AppLogin",
            HardwareId: hardwareId,
            SystemDeviceNumber: "SYS-001",
            DeviceSystem: "iOS",
            StoreCode: "BRI",
            LoginIp: "127.0.0.1",
            UserAgent: "unit-test",
            LocationLatitude: -27.4698,
            LocationLongitude: 153.0251,
            LocationAccuracy: 12.5,
            LocationCapturedAtUtc: DateTime.Parse("2026-05-18T00:00:00Z").ToUniversalTime()
        );

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
}
