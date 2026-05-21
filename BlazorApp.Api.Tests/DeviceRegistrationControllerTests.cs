using AutoMapper;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.POSM;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
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
                mapper.Object
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
    }
}
