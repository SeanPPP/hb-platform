using AutoMapper;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.POSM;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
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

        [Fact]
        public async Task ReportRuntimeStatus_UpdatesAuthorizedDeviceOnly()
        {
            var service = new Mock<IDeviceRegistrationService>();
            service
                .Setup(x => x.ValidateDeviceAuthCodeAsync("HW-001", "AUTH-001"))
                .ReturnsAsync(true);
            service
                .Setup(x => x.UpdateRuntimeStatusAsync("HW-001", true, "CASHIER-1", "Alice"))
                .ReturnsAsync(true);
            var controller = new DeviceRegistrationController(
                service.Object,
                Mock.Of<ILogger<DeviceRegistrationController>>(),
                Mock.Of<IMapper>(),
                Mock.Of<IStoreService>()
            );
            controller.ControllerContext.HttpContext = new DefaultHttpContext();
            controller.Request.Headers.Authorization = "Bearer AUTH-001";
            controller.Request.Headers["X-HBPOS-Hardware-Id"] = "HW-001";

            var result = await controller.ReportRuntimeStatus(
                new DeviceRuntimeStatusUpdateDto
                {
                    IsOnline = true,
                    CurrentCashierId = "CASHIER-1",
                    CurrentCashierName = "Alice",
                }
            );

            var ok = Assert.IsType<OkObjectResult>(result);
            var successProperty = ok.Value!.GetType().GetProperty("success");
            Assert.True((bool)successProperty!.GetValue(ok.Value)!);
            service.Verify(
                x => x.UpdateRuntimeStatusAsync("HW-001", true, "CASHIER-1", "Alice"),
                Times.Once
            );
        }

        [Fact]
        public async Task ReportRuntimeStatus_ReturnsUnauthorized_WhenDeviceAuthIsInvalid()
        {
            var service = new Mock<IDeviceRegistrationService>();
            service
                .Setup(x => x.ValidateDeviceAuthCodeAsync("HW-001", "WRONG"))
                .ReturnsAsync(false);
            var controller = new DeviceRegistrationController(
                service.Object,
                Mock.Of<ILogger<DeviceRegistrationController>>(),
                Mock.Of<IMapper>(),
                Mock.Of<IStoreService>()
            );
            controller.ControllerContext.HttpContext = new DefaultHttpContext();
            controller.Request.Headers.Authorization = "Bearer WRONG";
            controller.Request.Headers["X-HBPOS-Hardware-Id"] = "HW-001";

            var result = await controller.ReportRuntimeStatus(
                new DeviceRuntimeStatusUpdateDto
                {
                    IsOnline = true,
                    CurrentCashierId = "CASHIER-1",
                    CurrentCashierName = "Alice",
                }
            );

            Assert.IsType<UnauthorizedObjectResult>(result);
            service.Verify(
                x => x.UpdateRuntimeStatusAsync(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>()),
                Times.Never
            );
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

        [Fact]
        public async Task GetDevicesPagedAsync_FiltersByDeviceSystem()
        {
            await _db.Insertable(new[]
            {
                CreateDevice("hbmobile-android", "PDA_1004_1430", "1004", "Android"),
                CreateDevice("hbmobile-ios", "PDA_1004_1431", "1004", "iOS"),
                CreateDevice("hbmobile-windows", "PDA_1004_1432", "1004", "Windows"),
            }).ExecuteCommandAsync();
            var service = CreateService();

            var (devices, total) = await service.GetDevicesPagedAsync(
                page: 1,
                pageSize: 20,
                deviceSystem: "iOS"
            );

            var device = Assert.Single(devices);
            Assert.Equal(1, total);
            Assert.Equal("hbmobile-ios", device.设备硬件识别码);
            Assert.Equal("iOS", device.设备系统);
        }

        [Fact]
        public async Task UpdateRuntimeStatusAsync_KeepsCashierLoginTimeForSameCashierAndClearsWhenEmpty()
        {
            await _db.Insertable(new POSM_设备注册信息表
            {
                设备硬件识别码 = "HW-001",
                系统设备编号 = "POS-001",
                设备授权码 = "AUTH-001",
                设备状态 = (int)DeviceStatus.启用,
                设备类型 = "POS",
                设备系统 = "Windows",
                分店代码 = "S001",
                创建时间 = DateTime.UtcNow,
                最后修改时间 = new DateTime(2026, 1, 1, 9, 0, 0),
            }).ExecuteCommandAsync();
            var firstNow = new DateTime(2026, 7, 1, 10, 0, 0);
            var secondNow = new DateTime(2026, 7, 1, 10, 1, 0);

            var firstResult = await CreateService(firstNow).UpdateRuntimeStatusAsync(
                "HW-001",
                true,
                "CASHIER-1",
                "Alice"
            );
            var secondResult = await CreateService(secondNow).UpdateRuntimeStatusAsync(
                "HW-001",
                true,
                "CASHIER-1",
                "Alice"
            );

            var device = await _db.Queryable<POSM_设备注册信息表>()
                .FirstAsync(item => item.设备硬件识别码 == "HW-001");
            Assert.True(firstResult);
            Assert.True(secondResult);
            Assert.True(device.是否在线);
            Assert.Equal(secondNow, device.最后心跳时间);
            Assert.Equal("CASHIER-1", device.当前收银员ID);
            Assert.Equal("Alice", device.当前收银员姓名);
            Assert.Equal(firstNow, device.收银员登录时间);
            Assert.Equal(new DateTime(2026, 1, 1, 9, 0, 0), device.最后修改时间);

            var clearResult = await CreateService(secondNow.AddMinutes(1)).UpdateRuntimeStatusAsync(
                "HW-001",
                false,
                null,
                null
            );

            device = await _db.Queryable<POSM_设备注册信息表>()
                .FirstAsync(item => item.设备硬件识别码 == "HW-001");
            Assert.True(clearResult);
            Assert.False(device.是否在线);
            Assert.Null(device.当前收银员ID);
            Assert.Null(device.当前收银员姓名);
            Assert.Null(device.收银员登录时间);
        }

        [Fact]
        public async Task RegisterDeviceAsync_GeneratesPdaStoreTimeNumber_ForNewDevice()
        {
            var service = CreateService(new DateTime(2026, 1, 1, 14, 30, 0));

            var device = await service.RegisterDeviceAsync(
                "hbmobile-new",
                "Mobile",
                "Android",
                "1004"
            );

            Assert.Equal("PDA_1004_1430", device.系统设备编号);
        }

        [Fact]
        public async Task RegisterDeviceAsync_UsesNextMinute_WhenSameStoreTimeAlreadyExists()
        {
            await InsertDeviceAsync("hbmobile-existing", "PDA_1004_1430", "1004");
            var service = CreateService(new DateTime(2026, 1, 1, 14, 30, 0));

            var device = await service.RegisterDeviceAsync(
                "hbmobile-new",
                "Mobile",
                "Android",
                "1004"
            );

            Assert.Equal("PDA_1004_1431", device.系统设备编号);
        }

        [Fact]
        public async Task RegisterDeviceAsync_AllowsSameMinute_ForDifferentStores()
        {
            await InsertDeviceAsync("hbmobile-existing", "PDA_1005_1430", "1005");
            var service = CreateService(new DateTime(2026, 1, 1, 14, 30, 0));

            var device = await service.RegisterDeviceAsync(
                "hbmobile-new",
                "Mobile",
                "Android",
                "1004"
            );

            Assert.Equal("PDA_1004_1430", device.系统设备编号);
        }

        [Fact]
        public async Task RegisterDeviceAsync_Throws_WhenAllStoreMinutesAreUsed()
        {
            var rows = Enumerable.Range(0, 1440)
                .Select(offset =>
                {
                    var hhmm = new DateTime(2026, 1, 1).AddMinutes(offset).ToString("HHmm");
                    return CreateDevice($"hbmobile-existing-{hhmm}", $"PDA_1004_{hhmm}", "1004");
                })
                .ToList();
            await _db.Insertable(rows).ExecuteCommandAsync();
            var service = CreateService(new DateTime(2026, 1, 1, 14, 30, 0));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.RegisterDeviceAsync("hbmobile-new", "Mobile", "Android", "1004")
            );

            Assert.Contains("HHMM", ex.Message);
            Assert.Contains("1004", ex.Message);
        }

        [Fact]
        public async Task RegisterDeviceAsync_Throws_WhenStoreCodeIsMissing()
        {
            var service = CreateService(new DateTime(2026, 1, 1, 14, 30, 0));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.RegisterDeviceAsync("hbmobile-new", "Mobile", "Android", " ")
            );

            Assert.Contains("分店代码", ex.Message);
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

        private async Task InsertDeviceAsync(
            string hardwareId,
            string systemDeviceNumber,
            string storeCode
        )
        {
            await _db.Insertable(CreateDevice(hardwareId, systemDeviceNumber, storeCode))
                .ExecuteCommandAsync();
        }

        private static POSM_设备注册信息表 CreateDevice(
            string hardwareId,
            string systemDeviceNumber,
            string storeCode,
            string deviceSystem = "Android"
        )
        {
            return new POSM_设备注册信息表
            {
                设备硬件识别码 = hardwareId,
                系统设备编号 = systemDeviceNumber,
                设备授权码 = "AUTH-001",
                设备状态 = (int)DeviceStatus.启用,
                设备类型 = "Mobile",
                设备系统 = deviceSystem,
                分店代码 = storeCode,
                创建时间 = DateTime.UtcNow,
            };
        }

        private DeviceRegistrationService CreateService(DateTime? now = null)
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
                NullLogger<DeviceRegistrationService>.Instance,
                now.HasValue ? () => now.Value : null
            );
        }
    }
}
