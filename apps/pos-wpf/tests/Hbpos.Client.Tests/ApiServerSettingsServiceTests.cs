using System.Net;
using System.Net.Http.Json;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Health;

namespace Hbpos.Client.Tests;

public sealed class ApiServerSettingsServiceTests
{
    [Fact]
    public void Release_api_base_address_is_https()
    {
        Assert.Equal("https://hotbargain.vip/pos-api/", ApiServerSettingsService.ReleaseApiBaseAddress);
    }

    [Theory]
    [InlineData(" https://api.example.com ", "https://api.example.com/")]
    [InlineData("https://api.example.com/store", "https://api.example.com/store/")]
    [InlineData("http://localhost:5159", "http://localhost:5159/")]
    [InlineData("http://127.0.0.1:5159", "http://127.0.0.1:5159/")]
    public void NormalizeAddress_normalizes_supported_server_addresses(string input, string expected)
    {
        Assert.Equal(expected, ApiServerSettingsService.NormalizeAddress(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/relative")]
    [InlineData("ftp://localhost/")]
    [InlineData("http://api.example.com/")]
    [InlineData("https://user:password@api.example.com/")]
    [InlineData("https://@api.example.com/")]
    [InlineData("https://api.example.com/?store=1")]
    [InlineData("https://api.example.com/#fragment")]
    public void NormalizeAddress_rejects_unsupported_server_addresses(string input)
    {
        Assert.Throws<ArgumentException>(() => ApiServerSettingsService.NormalizeAddress(input));
    }

    [Fact]
    public async Task TestConnectionAsync_calls_health_endpoint_and_accepts_online_response()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(ApiResult<HealthCheckResponse>.Ok(
                    new HealthCheckResponse(true, DateTimeOffset.UnixEpoch, "ok")))
            };
        });
        var service = CreateService(handler);

        var result = await service.TestConnectionAsync("https://api.example.com/store", CancellationToken.None);

        Assert.True(result);
        Assert.Equal(
            "https://api.example.com/store/api/v1/health",
            capturedRequest?.RequestUri?.AbsoluteUri);
        Assert.Equal(TimeSpan.FromSeconds(5), ApiServerSettingsService.ConnectionTimeout);
    }

    [Fact]
    public async Task TestConnectionAsync_rejects_unsuccessful_or_offline_response()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ApiResult<HealthCheckResponse>.Ok(
                new HealthCheckResponse(false, DateTimeOffset.UnixEpoch, "offline")))
        });
        var service = CreateService(handler);

        var result = await service.TestConnectionAsync("https://api.example.com", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public void SaveUserAddress_normalizes_before_writing_user_setting()
    {
        string? savedAddress = null;
        var service = CreateService(
            new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            saveAddress: value => savedAddress = value);

        service.SaveUserAddress(" https://api.example.com/store ");

        Assert.Equal("https://api.example.com/store/", savedAddress);
    }

    [Fact]
    public void GetCurrentAddress_returns_normalized_process_address()
    {
        var service = CreateService(
            new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            currentAddress: () => "https://api.example.com/store");

        Assert.Equal("https://api.example.com/store/", service.GetCurrentAddress());
    }

    [Fact]
    public void GetCurrentAddress_preserves_legacy_absolute_process_address()
    {
        var service = CreateService(
            new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            currentAddress: () => "http://10.0.0.5:5159");

        Assert.Equal("http://10.0.0.5:5159/", service.GetCurrentAddress());
    }

    [Fact]
    public async Task TestConnectionAsync_propagates_caller_cancellation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateService(new WaitingHttpMessageHandler(started));
        using var cancellation = new CancellationTokenSource();

        var testTask = service.TestConnectionAsync("https://api.example.com", cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => testTask);
    }

    [Fact]
    public async Task TestConnectionAsync_returns_false_after_internal_timeout()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateService(new WaitingHttpMessageHandler(started));

        var result = await service.TestConnectionAsync(
                "https://api.example.com",
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(8));

        Assert.False(result);
    }

    private static ApiServerSettingsService CreateService(
        HttpMessageHandler handler,
        Func<string>? currentAddress = null,
        Action<string>? saveAddress = null)
    {
        return new ApiServerSettingsService(
            new HttpClient(handler),
            currentAddress ?? (() => "http://localhost:5159/"),
            saveAddress ?? (_ => { }));
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }

    private sealed class WaitingHttpMessageHandler(TaskCompletionSource started) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("等待请求只能通过取消结束。");
        }
    }
}
