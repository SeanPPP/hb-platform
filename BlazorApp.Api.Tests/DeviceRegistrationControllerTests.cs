using AutoMapper;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.POSM;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public class DeviceRegistrationControllerTests
    {
        [Fact]
        public async Task RegisterDevice_ReturnsExistingEnabledDevice_WhenAlreadyRegistered()
        {
            var service = new Mock<IDeviceRegistrationService>();
            service
                .Setup(x =>
                    x.RegisterDeviceAsync(
                        "hbmobile-existing",
                        "Mobile",
                        "Android",
                        "S001"
                    )
                )
                .ReturnsAsync(
                    new POSM_设备注册信息表
                    {
                        ID = 12,
                        设备硬件识别码 = "hbmobile-existing",
                        系统设备编号 = "SYS-001",
                        设备授权码 = "AUTH-001",
                        设备状态 = 1,
                        设备类型 = "Mobile",
                        设备系统 = "Android",
                        分店代码 = "S001",
                    }
                );

            var mapper = new Mock<IMapper>();
            mapper
                .Setup(x => x.Map<DeviceRegistrationResponseDto>(It.IsAny<POSM_设备注册信息表>()))
                .Returns(
                    (POSM_设备注册信息表 device) =>
                        new DeviceRegistrationResponseDto
                        {
                            DeviceId = device.ID,
                            SystemDeviceNumber = device.系统设备编号 ?? string.Empty,
                            AuthCode = device.设备授权码 ?? string.Empty,
                            Status = device.设备状态,
                            StatusDescription = "启用",
                        }
                );
            var controller = new DeviceRegistrationController(
                service.Object,
                Mock.Of<ILogger<DeviceRegistrationController>>(),
                mapper.Object,
                Mock.Of<IStoreService>()
            );

            var result = await controller.RegisterDevice(
                new DeviceRegistrationRequestDto
                {
                    HardwareId = "hbmobile-existing",
                    DeviceType = "Mobile",
                    DeviceSystem = "Android",
                    StoreCode = "S001",
                }
            );

            var ok = Assert.IsType<OkObjectResult>(result);
            var dataProperty = ok.Value!.GetType().GetProperty("data");
            var data = Assert.IsType<DeviceRegistrationResponseDto>(
                dataProperty!.GetValue(ok.Value)
            );
            Assert.Equal("AUTH-001", data.AuthCode);
            Assert.Equal(1, data.Status);
        }

        [Fact]
        public async Task UnbindDevice_ReturnsOk_WhenDeviceAuthMatches()
        {
            var service = new Mock<IDeviceRegistrationService>();
            service
                .Setup(x => x.UnbindDeviceAsync("hbmobile-existing", "AUTH-001", "DeviceSelfService"))
                .ReturnsAsync(true);
            var controller = new DeviceRegistrationController(
                service.Object,
                Mock.Of<ILogger<DeviceRegistrationController>>(),
                Mock.Of<IMapper>(),
                Mock.Of<IStoreService>()
            );

            var result = await controller.UnbindDevice(
                new DeviceUnbindRequestDto
                {
                    HardwareId = "hbmobile-existing",
                    AuthCode = "AUTH-001",
                }
            );

            var ok = Assert.IsType<OkObjectResult>(result);
            var successProperty = ok.Value!.GetType().GetProperty("success");
            Assert.True((bool)successProperty!.GetValue(ok.Value)!);
            service.Verify(
                x => x.UnbindDeviceAsync("hbmobile-existing", "AUTH-001", "DeviceSelfService"),
                Times.Once
            );
        }

        [Fact]
        public async Task UnbindDevice_ReturnsBadRequest_WhenDeviceAuthDoesNotMatch()
        {
            var service = new Mock<IDeviceRegistrationService>();
            service
                .Setup(x => x.UnbindDeviceAsync("hbmobile-existing", "WRONG", "DeviceSelfService"))
                .ReturnsAsync(false);
            var controller = new DeviceRegistrationController(
                service.Object,
                Mock.Of<ILogger<DeviceRegistrationController>>(),
                Mock.Of<IMapper>(),
                Mock.Of<IStoreService>()
            );

            var result = await controller.UnbindDevice(
                new DeviceUnbindRequestDto
                {
                    HardwareId = "hbmobile-existing",
                    AuthCode = "WRONG",
                }
            );

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            var successProperty = badRequest.Value!.GetType().GetProperty("success");
            Assert.False((bool)successProperty!.GetValue(badRequest.Value)!);
        }

        [Fact]
        public async Task GetDeviceByHardwareId_IncludesStoreName_WhenDeviceHasStoreCode()
        {
            var service = new Mock<IDeviceRegistrationService>();
            service
                .Setup(x => x.GetDeviceByHardwareIdAsync("hbmobile-existing"))
                .ReturnsAsync(
                    new POSM_设备注册信息表
                    {
                        ID = 12,
                        设备硬件识别码 = "hbmobile-existing",
                        系统设备编号 = "SYS-001",
                        设备授权码 = "AUTH-001",
                        设备状态 = 1,
                        设备类型 = "Mobile",
                        设备系统 = "Android",
                        分店代码 = "1004",
                    }
                );

            var mapper = new Mock<IMapper>();
            mapper
                .Setup(x => x.Map<DeviceDataDto>(It.IsAny<POSM_设备注册信息表>()))
                .Returns(
                    (POSM_设备注册信息表 device) =>
                        new DeviceDataDto
                        {
                            Id = device.ID,
                            HardwareId = device.设备硬件识别码 ?? string.Empty,
                            SystemDeviceNumber = device.系统设备编号 ?? string.Empty,
                            AuthCode = device.设备授权码 ?? string.Empty,
                            Status = device.设备状态,
                            DeviceType = device.设备类型 ?? string.Empty,
                            DeviceSystem = device.设备系统 ?? string.Empty,
                            StoreCode = device.分店代码,
                        }
                );
            var storeService = new Mock<IStoreService>();
            storeService
                .Setup(x => x.GetStoreByCodeAsync("1004"))
                .ReturnsAsync(
                    ApiResponse<StoreDto>.OK(
                        new StoreDto
                        {
                            StoreCode = "1004",
                            StoreName = "Sunnybank",
                        }
                    )
                );
            var controller = new DeviceRegistrationController(
                service.Object,
                Mock.Of<ILogger<DeviceRegistrationController>>(),
                mapper.Object,
                storeService.Object
            );

            var result = await controller.GetDeviceByHardwareId("hbmobile-existing");

            var ok = Assert.IsType<OkObjectResult>(result);
            var dataProperty = ok.Value!.GetType().GetProperty("data");
            var data = Assert.IsType<DeviceDataDto>(dataProperty!.GetValue(ok.Value));
            Assert.Equal("1004", data.StoreCode);
            Assert.Equal("Sunnybank", data.StoreName);
        }
    }

    public sealed class DeviceRegistrationServiceTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _sqliteConnection;
        private readonly SqlSugarClient _db;

        public DeviceRegistrationServiceTests()
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

            _db.CodeFirst.InitTables<POSM_设备注册信息表>();
        }

        [Fact]
        public async Task UnbindDeviceAsync_MarksDeviceUnregisteredAndClearsAuthCode()
        {
            await _db.Insertable(new POSM_设备注册信息表
            {
                设备硬件识别码 = "hbmobile-existing",
                系统设备编号 = "SYS-001",
                设备授权码 = "AUTH-001",
                设备状态 = (int)DeviceStatus.启用,
                设备类型 = "Mobile",
                设备系统 = "Android",
                分店代码 = "S001",
                创建时间 = DateTime.UtcNow,
            }).ExecuteCommandAsync();
            var service = CreateService();

            var result = await service.UnbindDeviceAsync(
                "hbmobile-existing",
                "AUTH-001",
                "DeviceSelfService"
            );

            Assert.True(result);
            var device = await _db.Queryable<POSM_设备注册信息表>()
                .FirstAsync(item => item.设备硬件识别码 == "hbmobile-existing");
            Assert.Equal((int)DeviceStatus.未注册, device.设备状态);
            Assert.Equal(string.Empty, device.设备授权码);
            Assert.Equal("DeviceSelfService", device.最后修改人);
            Assert.NotNull(device.最后修改时间);
        }

        [Fact]
        public async Task UnbindDeviceAsync_DoesNotChangeDevice_WhenAuthCodeDoesNotMatch()
        {
            await _db.Insertable(new POSM_设备注册信息表
            {
                设备硬件识别码 = "hbmobile-existing",
                系统设备编号 = "SYS-001",
                设备授权码 = "AUTH-001",
                设备状态 = (int)DeviceStatus.启用,
                设备类型 = "Mobile",
                设备系统 = "Android",
                分店代码 = "S001",
                创建时间 = DateTime.UtcNow,
            }).ExecuteCommandAsync();
            var service = CreateService();

            var result = await service.UnbindDeviceAsync(
                "hbmobile-existing",
                "WRONG",
                "DeviceSelfService"
            );

            Assert.False(result);
            var device = await _db.Queryable<POSM_设备注册信息表>()
                .FirstAsync(item => item.设备硬件识别码 == "hbmobile-existing");
            Assert.Equal((int)DeviceStatus.启用, device.设备状态);
            Assert.Equal("AUTH-001", device.设备授权码);
            Assert.Null(device.最后修改人);
        }

        public void Dispose()
        {
            _db.Dispose();
            _sqliteConnection.Dispose();

            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }

        private DeviceRegistrationService CreateService()
        {
            var context = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
                typeof(POSMSqlSugarContext)
            );
            var dbField = typeof(POSMSqlSugarContext).GetField(
                "_db",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            dbField!.SetValue(context, _db);

            return new DeviceRegistrationService(
                context,
                NullLogger<DeviceRegistrationService>.Instance
            );
        }
    }
}
