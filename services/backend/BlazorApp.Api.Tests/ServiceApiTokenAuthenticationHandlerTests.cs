using System.Security.Claims;
using System.Text.Encodings.Web;
using BlazorApp.Api.Authentication;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ServiceApiTokenAuthenticationHandlerTests
{
    [Fact]
    public async Task AuthenticateAsync_有效ServiceToken生成专用ScopeClaims()
    {
        var token = "hbsvc_valid-token";
        var tokenId = Guid.NewGuid();
        var tokenService = new Mock<IServiceApiTokenService>();
        tokenService
            .Setup(service => service.ValidateAsync(token, "198.51.100.10"))
            .ReturnsAsync(new ServiceApiTokenValidationResult
            {
                Id = tokenId,
                Name = "OTA 发布",
                TokenPrefix = "hbsvc_valid-token",
                Scopes = new List<string> { Permissions.System.ManageAppDownloads },
                ExpiresAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                LastUsedAt = new DateTime(2026, 6, 30, 9, 0, 0, DateTimeKind.Utc),
            });

        var result = await AuthenticateAsync(token, tokenService);

        Assert.True(result.Succeeded);
        Assert.Equal(
            ServiceApiTokenAuthenticationDefaults.AuthenticationScheme,
            result.Principal!.Identity!.AuthenticationType
        );
        Assert.Equal(
            tokenId.ToString(),
            result.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
        );
        Assert.Equal(
            "true",
            result.Principal.FindFirst(ServiceApiTokenAuthenticationDefaults.TokenTypeClaim)?.Value
        );
        Assert.Equal(
            Permissions.System.ManageAppDownloads,
            result.Principal.FindFirst(ServiceApiTokenAuthenticationDefaults.ScopeClaim)?.Value
        );
        tokenService.Verify(service => service.ValidateAsync(token, "198.51.100.10"), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_普通BearerToken不走ServiceToken校验()
    {
        var tokenService = new Mock<IServiceApiTokenService>();

        var result = await AuthenticateAsync("regular-jwt-token", tokenService);

        Assert.True(result.None);
        tokenService.Verify(
            service => service.ValidateAsync(It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never
        );
    }

    [Fact]
    public async Task AuthenticateAsync_ServiceToken校验失败时认证失败()
    {
        var token = "hbsvc_revoked";
        var tokenService = new Mock<IServiceApiTokenService>();
        tokenService
            .Setup(service => service.ValidateAsync(token, "198.51.100.10"))
            .ReturnsAsync((ServiceApiTokenValidationResult?)null);

        var result = await AuthenticateAsync(token, tokenService);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    [Theory]
    [InlineData("Bearer hbsvc_token", true)]
    [InlineData("Bearer regular-jwt", false)]
    [InlineData("Basic hbsvc_token", false)]
    public void RequestHasServiceApiToken_只识别BearerServiceToken前缀(
        string authorization,
        bool expected
    )
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = authorization;

        Assert.Equal(
            expected,
            ServiceApiTokenAuthenticationDefaults.RequestHasServiceApiToken(context.Request)
        );
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(
        string token,
        Mock<IServiceApiTokenService> tokenService
    )
    {
        var clientIpResolver = new Mock<IClientIpResolver>();
        clientIpResolver
            .Setup(resolver => resolver.Resolve(It.IsAny<HttpContext>()))
            .Returns("198.51.100.10");
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {token}";
        var handler = new ServiceApiTokenAuthenticationHandler(
            new StaticOptionsMonitor<AuthenticationSchemeOptions>(new AuthenticationSchemeOptions()),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            tokenService.Object,
            clientIpResolver.Object
        );
        var scheme = new AuthenticationScheme(
            ServiceApiTokenAuthenticationDefaults.AuthenticationScheme,
            ServiceApiTokenAuthenticationDefaults.AuthenticationScheme,
            typeof(ServiceApiTokenAuthenticationHandler)
        );

        await ((IAuthenticationHandler)handler).InitializeAsync(scheme, context);
        return await ((IAuthenticationHandler)handler).AuthenticateAsync();
    }

    private sealed class StaticOptionsMonitor<TOptions>(TOptions currentValue)
        : IOptionsMonitor<TOptions>
        where TOptions : class
    {
        public TOptions CurrentValue => currentValue;

        public TOptions Get(string? name) => currentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
