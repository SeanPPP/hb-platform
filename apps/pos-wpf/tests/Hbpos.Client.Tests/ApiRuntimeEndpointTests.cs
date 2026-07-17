using System.Net;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class ApiRuntimeEndpointTests
{
    [Fact]
    public void Switch_normalizes_address_and_increments_version_only_for_real_change()
    {
        var state = new ApiRuntimeEndpointState("https://old.example.com/pos-api");

        var unchanged = state.Switch(" HTTPS://OLD.EXAMPLE.COM/pos-api/ ");
        var changed = state.Switch("https://new.example.com/root");

        Assert.False(unchanged);
        Assert.True(changed);
        Assert.Equal("https://new.example.com/root/", state.CurrentAddress.AbsoluteUri);
        Assert.Equal(1, state.Version);
    }

    [Fact]
    public async Task Handler_rewrites_startup_api_request_to_current_endpoint()
    {
        var state = new ApiRuntimeEndpointState("https://old.example.com/pos-api/");
        state.Switch("https://new.example.com/other-base/");
        var capture = new CaptureHandler();
        var client = new HttpClient(new ApiRuntimeEndpointHandler(state) { InnerHandler = capture });

        await client.GetAsync("https://old.example.com/pos-api/api/v1/catalog");

        Assert.Equal("https://new.example.com/other-base/api/v1/catalog", capture.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task Handler_does_not_rewrite_external_absolute_request()
    {
        var state = new ApiRuntimeEndpointState("https://old.example.com/pos-api/");
        state.Switch("https://new.example.com/pos-api/");
        var capture = new CaptureHandler();
        var client = new HttpClient(new ApiRuntimeEndpointHandler(state) { InnerHandler = capture });

        await client.GetAsync("https://cdn.example.com/image.png");

        Assert.Equal("https://cdn.example.com/image.png", capture.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task Switch_cancels_request_started_on_previous_endpoint_generation()
    {
        var state = new ApiRuntimeEndpointState("https://old.example.com/pos-api/");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var endpointClient = new HttpClient(new ApiRuntimeEndpointHandler(state)
        {
            InnerHandler = new WaitingTerminalHandler(started)
        });

        var request = endpointClient.GetAsync("https://old.example.com/pos-api/api/v1/catalog");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        state.Switch("https://new.example.com/pos-api/");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            request.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task BeginTransition_waits_until_previous_internal_request_releases_lease()
    {
        var state = new ApiRuntimeEndpointState("https://old.example.com/pos-api/");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new HttpClient(new ApiRuntimeEndpointHandler(state)
        {
            InnerHandler = new DrainControlledHandler(started, complete)
        });
        var request = client.GetAsync("https://old.example.com/pos-api/api/v1/catalog");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var begin = state.BeginTransitionAsync("https://new.example.com/pos-api/", CancellationToken.None);
        await Task.Delay(50);

        Assert.False(begin.IsCompleted);
        complete.SetResult(new HttpResponseMessage(HttpStatusCode.OK));
        using var response = await request;
        var transition = await begin.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("https://old.example.com/pos-api/", state.CurrentAddress.AbsoluteUri);

        state.Commit(transition);
        Assert.Equal("https://new.example.com/pos-api/", state.CurrentAddress.AbsoluteUri);
    }

    [Fact]
    public async Task Handler_rejects_new_internal_request_while_transition_is_open()
    {
        var state = new ApiRuntimeEndpointState("https://old.example.com/pos-api/");
        var transition = await state.BeginTransitionAsync(
            "https://new.example.com/pos-api/",
            CancellationToken.None);
        var client = new HttpClient(new ApiRuntimeEndpointHandler(state)
        {
            InnerHandler = new CaptureHandler()
        });

        await Assert.ThrowsAsync<ApiEndpointTransitionException>(() =>
            client.GetAsync("https://old.example.com/pos-api/api/v1/catalog"));

        state.Abort(transition);
        using var response = await client.GetAsync("https://old.example.com/pos-api/api/v1/catalog");
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task New_internal_clients_follow_round_trip_switch_from_a_to_b_to_a()
    {
        var state = new ApiRuntimeEndpointState("https://a.example.com/pos-api/");
        var firstCapture = new CaptureHandler();
        var firstClient = new HttpClient(new ApiRuntimeEndpointHandler(state) { InnerHandler = firstCapture });

        var toB = await state.BeginTransitionAsync("https://b.example.com/pos-api/", CancellationToken.None);
        state.Commit(toB);
        await firstClient.GetAsync("https://a.example.com/pos-api/api/v1/catalog");

        var toA = await state.BeginTransitionAsync("https://a.example.com/pos-api/", CancellationToken.None);
        state.Commit(toA);
        var newCapture = new CaptureHandler();
        var newClient = new HttpClient(new ApiRuntimeEndpointHandler(state) { InnerHandler = newCapture });
        await newClient.GetAsync("https://a.example.com/pos-api/api/v1/catalog");

        Assert.Equal("https://b.example.com/pos-api/api/v1/catalog", firstCapture.RequestUri?.AbsoluteUri);
        Assert.Equal("https://a.example.com/pos-api/api/v1/catalog", newCapture.RequestUri?.AbsoluteUri);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class WaitingTerminalHandler(TaskCompletionSource started) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("请求只能通过端点切换取消。");
        }
    }

    private sealed class DrainControlledHandler(
        TaskCompletionSource started,
        TaskCompletionSource<HttpResponseMessage> complete) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            started.TrySetResult();
            // 模拟不可立即响应取消的底层调用，transition 必须等待 handler lease 真正退出。
            return await complete.Task;
        }
    }

}
