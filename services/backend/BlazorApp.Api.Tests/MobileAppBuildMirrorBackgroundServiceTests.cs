using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class MobileAppBuildMirrorBackgroundServiceTests
{
    [Fact]
    public async Task ProcessOneAsync_宿主取消时不写失败状态()
    {
        var queue = new FakeMirrorQueue
        {
            ClaimedJob = new MobileAppBuild
            {
                Id = Guid.NewGuid(),
                EasBuildId = "build-cancel",
                ArtifactUrl = "https://expo.dev/artifacts/eas/build-cancel.apk",
            },
        };
        var mirror = new FakeArtifactMirror { ThrowWhenTokenCancelled = true };
        using var host = CreateHost(queue, mirror);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            host.Service.ProcessOneAsync(cts.Token)
        );

        Assert.Equal(1, queue.ClaimCalls);
        Assert.Equal(1, mirror.Calls);
        Assert.Equal(0, queue.SuccessCalls);
        Assert.Equal(0, queue.FailureCalls);
    }

    [Fact]
    public async Task ProcessOneAsync_只依赖镜像队列接口无需解析ConcreteService()
    {
        var queue = new FakeMirrorQueue();
        var mirror = new FakeArtifactMirror();
        using var host = CreateHost(queue, mirror);

        var processed = await host.Service.ProcessOneAsync(CancellationToken.None);

        Assert.False(processed);
        Assert.Equal(1, queue.ClaimCalls);
        Assert.Equal(0, mirror.Calls);
    }

    private static TestHost CreateHost(
        FakeMirrorQueue queue,
        FakeArtifactMirror mirror
    )
    {
        var services = new ServiceCollection();
        services.AddScoped<IMobileAppBuildMirrorQueue>(_ => queue);
        services.AddScoped<IMobileAppBuildArtifactMirror>(_ => mirror);
        var provider = services.BuildServiceProvider();

        var service = new MobileAppBuildMirrorBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MobileAppBuildMirrorBackgroundService>.Instance
        );

        return new TestHost(provider, service);
    }

    private sealed class TestHost : IDisposable
    {
        private readonly ServiceProvider _provider;

        public TestHost(
            ServiceProvider provider,
            MobileAppBuildMirrorBackgroundService service
        )
        {
            _provider = provider;
            Service = service;
        }

        public MobileAppBuildMirrorBackgroundService Service { get; }

        public void Dispose()
        {
            _provider.Dispose();
        }
    }

    private sealed class FakeMirrorQueue : IMobileAppBuildMirrorQueue
    {
        public MobileAppBuild? ClaimedJob { get; init; }

        public int ClaimCalls { get; private set; }

        public int SuccessCalls { get; private set; }

        public int FailureCalls { get; private set; }

        public Task<MobileAppBuild?> ClaimNextCosMirrorJobAsync(
            DateTime now,
            int maxAttempts,
            TimeSpan staleRunningAfter
        )
        {
            ClaimCalls++;
            return Task.FromResult(ClaimedJob);
        }

        public Task CompleteCosMirrorSuccessAsync(
            MobileAppBuild entity,
            MobileAppBuildArtifactMirrorResult mirror
        )
        {
            SuccessCalls++;
            return Task.CompletedTask;
        }

        public Task CompleteCosMirrorFailureAsync(MobileAppBuild entity, Exception exception)
        {
            FailureCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeArtifactMirror : IMobileAppBuildArtifactMirror
    {
        public bool ThrowWhenTokenCancelled { get; init; }

        public int Calls { get; private set; }

        public Task<MobileAppBuildArtifactMirrorResult> MirrorAsync(
            MobileAppBuild build,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            if (ThrowWhenTokenCancelled)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Task.FromResult(
                new MobileAppBuildArtifactMirrorResult
                {
                    ArtifactUrl = "https://cos.example.com/mobile-app-builds/production/build.apk",
                    ObjectKey = "mobile-app-builds/production/build.apk",
                    MirroredAt = DateTime.UtcNow,
                }
            );
        }
    }
}
