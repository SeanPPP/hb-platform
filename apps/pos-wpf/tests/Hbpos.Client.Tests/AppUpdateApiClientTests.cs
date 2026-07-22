using System.Net;
using System.Net.Http.Json;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.AppUpdates;
using Hbpos.Contracts.Common;

namespace Hbpos.Client.Tests;

public sealed class AppUpdateApiClientTests
{
    private static readonly IAppUpdateDeviceCredentialProvider NoCredentials = new StaticCredentialsProvider(null);

    [Fact]
    public async Task CheckAsync_wrapped_error_returns_check_failed_contract()
    {
        var client = new AppUpdateApiClient(new HttpClient(new CapturingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(
                    ApiResult<AppUpdateCheckResponse>.Fail(
                        "TARGET_RELEASE_NOT_FOUND",
                        "Target release is disabled."))
            }))
        {
            BaseAddress = new Uri("https://pos.local/")
        }, NoCredentials);

        var result = await client.CheckAsync(new AppUpdateCheckRequest
        {
            CurrentVersion = "1.0.0",
            Channel = "preview"
        });

        Assert.True(result.CheckFailed);
        Assert.Equal("TARGET_RELEASE_NOT_FOUND", result.ErrorCode);
        Assert.Equal("Target release is disabled.", result.ErrorMessage);
        Assert.Equal("1.0.0", result.CurrentVersion);
    }

    [Fact]
    public async Task CheckAsync_http_failure_returns_check_failed_contract()
    {
        var client = new AppUpdateApiClient(new HttpClient(new CapturingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)))
        {
            BaseAddress = new Uri("https://pos.local/")
        }, NoCredentials);

        var result = await client.CheckAsync(new AppUpdateCheckRequest
        {
            CurrentVersion = "1.0.0",
            Channel = "preview"
        });

        Assert.True(result.CheckFailed);
        Assert.False(result.UpdateAvailable);
        Assert.Equal("LOCAL_APP_UPDATE_HTTP_ERROR", result.ErrorCode);
        Assert.Equal("1.0.0", result.CurrentVersion);
    }

    [Fact]
    public async Task CheckAsync_preview_channel_sends_device_identity_only_as_headers()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = new AppUpdateApiClient(
            new HttpClient(new CapturingHandler(request =>
            {
                capturedRequest = request;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(AppUpdateCheckResponse.NoUpdate("1.0.0"))
                };
            }))
            {
                BaseAddress = new Uri("https://pos.local/")
            },
            new StaticCredentialsProvider(new AppUpdateDeviceCredentials(
                "POS-001",
                "1002",
                "HW-001",
                "device-auth-secret")));

        await client.CheckAsync(new AppUpdateCheckRequest
        {
            CurrentVersion = "1.0.0",
            Channel = "preview"
        });

        Assert.NotNull(capturedRequest);
        Assert.Equal("Bearer", capturedRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("device-auth-secret", capturedRequest.Headers.Authorization?.Parameter);
        Assert.Equal("POS-001", capturedRequest.Headers.GetValues("X-HBPOS-Device-Code").Single());
        Assert.Equal("1002", capturedRequest.Headers.GetValues("X-HBPOS-Store-Code").Single());
        Assert.Equal("HW-001", capturedRequest.Headers.GetValues("X-HBPOS-Hardware-Id").Single());
        Assert.DoesNotContain("device-auth-secret", capturedRequest.RequestUri!.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("POS-001", capturedRequest.RequestUri.Query, StringComparison.Ordinal);
        Assert.Equal("?currentVersion=1.0.0&channel=preview", capturedRequest.RequestUri.Query);
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }

    private sealed class StaticCredentialsProvider(AppUpdateDeviceCredentials? credentials) : IAppUpdateDeviceCredentialProvider
    {
        public Task<AppUpdateDeviceCredentials?> GetCredentialsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(credentials);
    }
}
