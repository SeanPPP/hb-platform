using System.Reflection;
using System.Security.Claims;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.POSM;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public class ReactLocationControllerTests
    {
        [Theory]
        [InlineData(nameof(ReactLocationController.Create))]
        [InlineData(nameof(ReactLocationController.Update))]
        [InlineData(nameof(ReactLocationController.Delete))]
        public void LocationMaintenance_允许WarehouseStaff进入账号授权(string methodName)
        {
            var method = typeof(ReactLocationController).GetMethod(methodName);

            var authorizeAttribute = Assert.Single(
                method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            ) as AuthorizeAttribute;
            Assert.Equal("Admin,WarehouseManager,WarehouseStaff", authorizeAttribute!.Roles);
            Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false));
        }

        [Fact]
        public async Task Create_WhenWarehouseStaff_CallsService()
        {
            var dto = new CreateLocationReactDto
            {
                LocationCode = "A-00-00-02",
                LocationType = 1,
                Status = 1,
            };
            var serviceMock = new Mock<ILocationReactService>();
            serviceMock
                .Setup(service => service.CreateAsync(dto))
                .ReturnsAsync(ApiResponse<LocationReactDto>.OK(new LocationReactDto { LocationGuid = "loc-2" }));

            var controller = CreateController(serviceMock.Object, roles: new[] { "WarehouseStaff" });

            var result = await controller.Create(dto);

            Assert.IsType<OkObjectResult>(result);
            serviceMock.Verify(service => service.CreateAsync(dto), Times.Once);
        }

        [Fact]
        public async Task Update_WhenWarehouseStaff_CallsService()
        {
            var dto = new UpdateLocationReactDto
            {
                LocationCode = "A-00-00-03",
                LocationType = 1,
                Status = 1,
            };
            var serviceMock = new Mock<ILocationReactService>();
            serviceMock
                .Setup(service => service.UpdateAsync("loc-3", dto))
                .ReturnsAsync(ApiResponse<LocationReactDto>.OK(new LocationReactDto { LocationGuid = "loc-3" }));

            var controller = CreateController(serviceMock.Object, roles: new[] { "WarehouseStaff" });

            var result = await controller.Update("loc-3", dto);

            Assert.IsType<OkObjectResult>(result);
            serviceMock.Verify(service => service.UpdateAsync("loc-3", dto), Times.Once);
        }

        [Fact]
        public async Task Delete_WhenWarehouseStaff_CallsService()
        {
            var serviceMock = new Mock<ILocationReactService>();
            serviceMock
                .Setup(service => service.DeleteAsync("loc-4"))
                .ReturnsAsync(ApiResponse<bool>.OK(true));

            var controller = CreateController(serviceMock.Object, roles: new[] { "WarehouseStaff" });

            var result = await controller.Delete("loc-4");

            Assert.IsType<OkObjectResult>(result);
            serviceMock.Verify(service => service.DeleteAsync("loc-4"), Times.Once);
        }

        [Fact]
        public async Task Create_WhenDeviceAuthorized_ReturnsUnauthorizedAndDoesNotCallService()
        {
            var dto = new CreateLocationReactDto
            {
                LocationCode = "A-00-00-04",
                LocationType = 1,
                Status = 1,
            };
            var serviceMock = new Mock<ILocationReactService>();
            var controller = CreateDeviceAuthorizedController(serviceMock.Object);

            var result = await controller.Create(dto);

            Assert.IsType<UnauthorizedObjectResult>(result);
            serviceMock.Verify(service => service.CreateAsync(It.IsAny<CreateLocationReactDto>()), Times.Never);
        }

        [Fact]
        public async Task Update_WhenDeviceAuthorized_ReturnsUnauthorizedAndDoesNotCallService()
        {
            var dto = new UpdateLocationReactDto
            {
                LocationCode = "A-00-00-05",
                LocationType = 1,
                Status = 1,
            };
            var serviceMock = new Mock<ILocationReactService>();
            var controller = CreateDeviceAuthorizedController(serviceMock.Object);

            var result = await controller.Update("loc-5", dto);

            Assert.IsType<UnauthorizedObjectResult>(result);
            serviceMock.Verify(
                service => service.UpdateAsync(It.IsAny<string>(), It.IsAny<UpdateLocationReactDto>()),
                Times.Never
            );
        }

        [Fact]
        public async Task Delete_WhenDeviceAuthorized_ReturnsUnauthorizedAndDoesNotCallService()
        {
            var serviceMock = new Mock<ILocationReactService>();
            var controller = CreateDeviceAuthorizedController(serviceMock.Object);

            var result = await controller.Delete("loc-6");

            Assert.IsType<UnauthorizedObjectResult>(result);
            serviceMock.Verify(service => service.DeleteAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void UnbindProduct_允许设备授权进入方法内校验()
        {
            var method = typeof(ReactLocationController).GetMethod(
                nameof(ReactLocationController.UnbindProduct)
            );

            Assert.Single(method!.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false));
            Assert.Empty(method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false));
        }

        [Fact]
        public async Task UnbindProduct_WhenWarehouseStaff_CallsUnbindService()
        {
            var serviceMock = new Mock<ILocationReactService>();
            serviceMock
                .Setup(service => service.UnbindProductAsync("loc-1", "product-1"))
                .ReturnsAsync(ApiResponse<LocationReactDto>.OK(new LocationReactDto { LocationGuid = "loc-1" }));

            var controller = CreateController(serviceMock.Object, roles: new[] { "WarehouseStaff" });

            var result = await controller.UnbindProduct("loc-1", "product-1");

            Assert.IsType<OkObjectResult>(result);
            serviceMock.Verify(
                service => service.UnbindProductAsync("loc-1", "product-1"),
                Times.Once
            );
        }

        [Fact]
        public async Task UnbindProduct_WhenDeviceAuthorized_CallsUnbindService()
        {
            var serviceMock = new Mock<ILocationReactService>();
            serviceMock
                .Setup(service => service.UnbindProductAsync("loc-1", "product-1"))
                .ReturnsAsync(ApiResponse<LocationReactDto>.OK(new LocationReactDto { LocationGuid = "loc-1" }));

            var deviceServiceMock = new Mock<IDeviceRegistrationService>();
            deviceServiceMock
                .Setup(service => service.ValidateDeviceAuthCodeAsync("device-1", "auth-1"))
                .ReturnsAsync(true);
            deviceServiceMock
                .Setup(service => service.GetDeviceByHardwareIdAsync("device-1"))
                .ReturnsAsync(new POSM_设备注册信息表 { 设备硬件识别码 = "device-1", 设备状态 = 1 });

            var controller = CreateController(
                serviceMock.Object,
                deviceService: deviceServiceMock.Object,
                roles: Array.Empty<string>()
            );
            controller.Request.Headers["X-Device-Id"] = "device-1";
            controller.Request.Headers["X-Auth-Code"] = "auth-1";

            var result = await controller.UnbindProduct("loc-1", "product-1");

            Assert.IsType<OkObjectResult>(result);
            serviceMock.Verify(
                service => service.UnbindProductAsync("loc-1", "product-1"),
                Times.Once
            );
        }

        private static ReactLocationController CreateController(
            ILocationReactService service,
            IDeviceRegistrationService? deviceService = null,
            string[]? roles = null
        )
        {
            roles ??= new[] { "Admin" };
            var httpContext = new DefaultHttpContext();
            if (roles.Length > 0)
            {
                var claims = roles.Select(role => new Claim(ClaimTypes.Role, role));
                httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
            }

            var controller = new ReactLocationController(
                service,
                Mock.Of<ILogger<ReactLocationController>>(),
                deviceService ?? Mock.Of<IDeviceRegistrationService>()
            );
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
        }

        private static ReactLocationController CreateDeviceAuthorizedController(
            ILocationReactService service
        )
        {
            var deviceServiceMock = new Mock<IDeviceRegistrationService>();
            deviceServiceMock
                .Setup(service => service.ValidateDeviceAuthCodeAsync("device-1", "auth-1"))
                .ReturnsAsync(true);
            deviceServiceMock
                .Setup(service => service.GetDeviceByHardwareIdAsync("device-1"))
                .ReturnsAsync(new POSM_设备注册信息表 { 设备硬件识别码 = "device-1", 设备状态 = 1 });

            var controller = CreateController(
                service,
                deviceService: deviceServiceMock.Object,
                roles: Array.Empty<string>()
            );
            controller.Request.Headers["X-Device-Id"] = "device-1";
            controller.Request.Headers["X-Auth-Code"] = "auth-1";
            return controller;
        }
    }
}
