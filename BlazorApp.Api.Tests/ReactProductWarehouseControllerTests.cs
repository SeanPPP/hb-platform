using System;
using System.Threading.Tasks;
using AutoMapper;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Models;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public class ReactProductWarehouseControllerTests
    {
        [Fact]
        public async Task SetMobileProductLocation_WhenLocationIsInvalid_ReturnsBadRequest()
        {
            var serviceMock = new Mock<IProductWarehouseReactService>();
            serviceMock
                .Setup(service => service.SetMobileProductLocationAsync("P001", "LOC-404"))
                .ThrowsAsync(new InvalidOperationException("货位不存在"));

            var uploadService = new TencentCloudUploadService(
                Options.Create(new TencentCloudSettings()),
                Mock.Of<ILogger<TencentCloudUploadService>>(),
                new System.Net.Http.HttpClient()
            );

            var controller = new ReactProductWarehouseController(
                serviceMock.Object,
                Mock.Of<ILogger<ReactProductWarehouseController>>(),
                Mock.Of<IDeviceRegistrationService>(),
                Mock.Of<IMapper>(),
                uploadService
            );

            var result = await controller.SetMobileProductLocation(
                "P001",
                new SetWarehouseProductLocationDto { LocationGuid = "LOC-404" }
            );

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            var payload = badRequest.Value?.ToString() ?? string.Empty;
            Assert.Contains("货位不存在", payload);
        }
    }
}
