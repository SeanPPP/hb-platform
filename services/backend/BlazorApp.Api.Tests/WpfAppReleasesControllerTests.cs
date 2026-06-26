using BlazorApp.Api.Controllers;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class WpfAppReleasesControllerTests
{
    private const string AppUpdateKeyHeaderName = "X-HBPOS-App-Update-Key";

    [Fact]
    public async Task Check_returns_unauthorized_when_configured_key_is_missing()
    {
        var service = new CapturingWpfAppReleaseService();
        var controller = CreateController(service, "terminal-secret");

        var result = await controller.Check("production", "1.0.0");

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        var payload = Assert.IsType<ApiResponse<WpfUpdateCheckResponse>>(unauthorized.Value);
        Assert.False(payload.Success);
        Assert.Equal("APP_UPDATE_CHECK_UNAUTHORIZED", payload.Code);
        Assert.False(service.CheckCalled);
    }

    [Fact]
    public async Task Check_allows_request_with_matching_configured_key()
    {
        var service = new CapturingWpfAppReleaseService();
        var controller = CreateController(service, "terminal-secret");
        controller.HttpContext.Request.Headers[AppUpdateKeyHeaderName] = " terminal-secret ";

        var result = await controller.Check("preview", "1.0.0");

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<ApiResponse<WpfUpdateCheckResponse>>(ok.Value);
        Assert.True(payload.Success);
        Assert.True(service.CheckCalled);
        Assert.Equal("preview", service.Channel);
        Assert.Equal("1.0.0", service.CurrentVersion);
    }

    [Fact]
    public async Task Check_uses_first_non_blank_key_and_returns_unauthorized_when_header_missing()
    {
        var service = new CapturingWpfAppReleaseService();
        var controller = CreateController(
            service,
            wpfAppUpdateCheckApiKey: " ",
            appUpdateCheckApiKey: "fallback-secret"
        );

        var result = await controller.Check("production", "1.0.0");

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        var payload = Assert.IsType<ApiResponse<WpfUpdateCheckResponse>>(unauthorized.Value);
        Assert.False(payload.Success);
        Assert.Equal("APP_UPDATE_CHECK_UNAUTHORIZED", payload.Code);
        Assert.False(service.CheckCalled);
    }

    [Fact]
    public async Task Check_uses_first_non_blank_key_and_allows_matching_fallback_header()
    {
        var service = new CapturingWpfAppReleaseService();
        var controller = CreateController(
            service,
            wpfAppUpdateCheckApiKey: " ",
            appUpdateCheckApiKey: "fallback-secret"
        );
        controller.HttpContext.Request.Headers[AppUpdateKeyHeaderName] = " fallback-secret ";

        var result = await controller.Check("preview", "1.0.0");

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<ApiResponse<WpfUpdateCheckResponse>>(ok.Value);
        Assert.True(payload.Success);
        Assert.True(service.CheckCalled);
        Assert.Equal("preview", service.Channel);
        Assert.Equal("1.0.0", service.CurrentVersion);
    }

    [Fact]
    public async Task Check_returns_unauthorized_when_no_key_is_configured_outside_development()
    {
        var service = new CapturingWpfAppReleaseService();
        var controller = CreateController(service);

        var result = await controller.Check("production", "1.0.0");

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        var payload = Assert.IsType<ApiResponse<WpfUpdateCheckResponse>>(unauthorized.Value);
        Assert.False(payload.Success);
        Assert.Equal("APP_UPDATE_CHECK_UNAUTHORIZED", payload.Code);
        Assert.False(service.CheckCalled);
    }

    [Fact]
    public async Task Check_allows_request_without_key_in_development()
    {
        var service = new CapturingWpfAppReleaseService();
        var controller = CreateController(service, environmentName: Environments.Development);

        var result = await controller.Check("preview", "1.0.0");

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<ApiResponse<WpfUpdateCheckResponse>>(ok.Value);
        Assert.True(payload.Success);
        Assert.True(service.CheckCalled);
        Assert.Equal("preview", service.Channel);
        Assert.Equal("1.0.0", service.CurrentVersion);
    }

    private static WpfAppReleasesController CreateController(
        IWpfAppReleaseService service,
        string? wpfAppUpdateCheckApiKey = null,
        string? appUpdateCheckApiKey = null,
        string? environmentName = "Production")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WpfAppUpdate:CheckApiKey"] = wpfAppUpdateCheckApiKey,
                ["AppUpdate:CheckApiKey"] = appUpdateCheckApiKey,
            })
            .Build();
        return new WpfAppReleasesController(
            service,
            configuration,
            new TestHostEnvironment { EnvironmentName = environmentName ?? Environments.Production }
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "BlazorApp.Api.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CapturingWpfAppReleaseService : IWpfAppReleaseService
    {
        public bool CheckCalled { get; private set; }

        public string? Channel { get; private set; }

        public string? CurrentVersion { get; private set; }

        public Task<ApiResponse<WpfUpdateCheckResponse>> CheckUpdateAsync(
            string? channel,
            string? currentVersion)
        {
            CheckCalled = true;
            Channel = channel;
            CurrentVersion = currentVersion;
            return Task.FromResult(ApiResponse<WpfUpdateCheckResponse>.OK(new WpfUpdateCheckResponse
            {
                CurrentVersion = currentVersion ?? string.Empty
            }));
        }

        public Task<ApiResponse<PagedResult<WpfAppReleaseDto>>> GetReleasesAsync(
            WpfAppReleaseQuery query) =>
            throw new NotImplementedException();

        public Task<ApiResponse<WpfAppReleaseDto>> CreateReleaseAsync(
            WpfAppReleaseCreateRequest request,
            string currentUser) =>
            throw new NotImplementedException();

        public Task<ApiResponse<WpfAppReleaseDto>> UpdateReleaseAsync(
            Guid id,
            WpfAppReleaseUpdateRequest request,
            string currentUser) =>
            throw new NotImplementedException();

        public Task<ApiResponse<WpfUpdatePolicyDto>> SetPolicyAsync(
            WpfUpdatePolicyRequest request,
            string currentUser) =>
            throw new NotImplementedException();

        public Task<ApiResponse<WpfAppReleaseUploadInitResponse>> CreateUploadInitAsync(
            WpfAppReleaseUploadInitRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
