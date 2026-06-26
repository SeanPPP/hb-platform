using System.Net;
using System.Net.Http.Json;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.AppUpdates;
using Hbpos.Contracts.Common;

namespace Hbpos.Client.Tests;

public sealed class AppUpdateApiClientTests
{
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
        });

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
        });

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

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
