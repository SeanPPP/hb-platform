using System.Net;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Models;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class TencentCosMobileAppBuildArtifactMirrorTests
{
    [Fact]
    public async Task MirrorAsync_Artifact缺少ContentLength_拒绝镜像且不上传()
    {
        var artifactHandler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new UnknownLengthContent([1, 2, 3, 4]),
            }
        );
        var uploadHandler = new CaptureHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var mirror = CreateMirror(artifactHandler, uploadHandler);

        var error = await Assert.ThrowsAsync<MobileAppBuildArtifactMirrorException>(() =>
            mirror.MirrorAsync(CreateBuild("https://expo.dev/artifacts/eas/build-no-length.apk"))
        );

        Assert.Contains("Content-Length", error.Message);
        Assert.True(error.IsDownloadUnsafe);
        Assert.Null(uploadHandler.Request);
    }

    [Fact]
    public async Task MirrorAsync_Artifact返回非成功状态_拒绝镜像但允许重试()
    {
        var artifactHandler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new ByteArrayContent([1, 2, 3, 4]),
            }
        );
        var uploadHandler = new CaptureHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var mirror = CreateMirror(artifactHandler, uploadHandler);

        var error = await Assert.ThrowsAsync<MobileAppBuildArtifactMirrorException>(() =>
            mirror.MirrorAsync(CreateBuild("https://expo.dev/artifacts/eas/build-forbidden.apk"))
        );

        Assert.Contains("HTTP 403", error.Message);
        Assert.False(error.IsDownloadUnsafe);
        Assert.Equal(1, artifactHandler.Calls);
        Assert.Null(uploadHandler.Request);
    }

    [Fact]
    public async Task MirrorAsync_Artifact不是允许域名_拒绝出站请求()
    {
        var artifactHandler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4]),
            }
        );
        var uploadHandler = new CaptureHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var mirror = CreateMirror(artifactHandler, uploadHandler);

        var error = await Assert.ThrowsAsync<MobileAppBuildArtifactMirrorException>(() =>
            mirror.MirrorAsync(CreateBuild("https://example.com/build.apk"))
        );

        Assert.Contains("不允许的 APK 下载域名", error.Message);
        Assert.True(error.IsDownloadUnsafe);
        Assert.Equal(0, artifactHandler.Calls);
        Assert.Null(uploadHandler.Request);
    }

    [Fact]
    public async Task MirrorAsync_Artifact重定向到非允许域名_拒绝继续下载()
    {
        var artifactHandler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://example.com/redirected.apk") },
            }
        );
        var uploadHandler = new CaptureHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var mirror = CreateMirror(artifactHandler, uploadHandler);

        var error = await Assert.ThrowsAsync<MobileAppBuildArtifactMirrorException>(() =>
            mirror.MirrorAsync(CreateBuild("https://expo.dev/artifacts/eas/build-redirect.apk"))
        );

        Assert.Contains("不允许的 APK 下载域名", error.Message);
        Assert.True(error.IsDownloadUnsafe);
        Assert.Equal(1, artifactHandler.Calls);
        Assert.Null(uploadHandler.Request);
    }

    [Fact]
    public async Task MirrorAsync_Artifact重定向到EasCdn_允许上传到Cos()
    {
        var artifactHandler = new StubHttpMessageHandler(request =>
            request.RequestUri?.Host == "expo.dev"
                ? new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers =
                    {
                        Location = new Uri("https://wf-artifacts.eascdn.net/build-123.apk"),
                    },
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3, 4]),
                }
        );
        var uploadHandler = new CaptureHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var mirror = CreateMirror(artifactHandler, uploadHandler);

        var result = await mirror.MirrorAsync(
            CreateBuild("https://expo.dev/artifacts/eas/build-redirect.apk")
        );

        Assert.Equal(2, artifactHandler.Calls);
        Assert.NotNull(uploadHandler.Request);
        Assert.Equal(HttpMethod.Put, uploadHandler.Request.Method);
        Assert.Equal(
            "hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com",
            uploadHandler.Request.RequestUri?.Host
        );
        Assert.Equal("mobile-app-builds/production/build-123.apk", result.ObjectKey);
    }

    private static TencentCosMobileAppBuildArtifactMirror CreateMirror(
        HttpMessageHandler artifactHandler,
        HttpMessageHandler uploadHandler
    )
    {
        var uploadService = new TencentCloudUploadService(
            Options.Create(
                new TencentCloudSettings
                {
                    SecretId = "secret-id",
                    SecretKey = "secret-key",
                    BucketName = "hb-sales-2019-1300114625",
                    Region = "ap-singapore",
                }
            ),
            NullLogger<TencentCloudUploadService>.Instance,
            new HttpClient(uploadHandler)
        );

        return new TencentCosMobileAppBuildArtifactMirror(
            new HttpClient(artifactHandler),
            uploadService,
            NullLogger<TencentCosMobileAppBuildArtifactMirror>.Instance
        );
    }

    private static MobileAppBuild CreateBuild(string artifactUrl)
    {
        return new MobileAppBuild
        {
            EasBuildId = "build-123",
            BuildProfile = "production",
            ArtifactUrl = artifactUrl,
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls += 1;
            return Task.FromResult(_responseFactory(request));
        }
    }

    private sealed class CaptureHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public CaptureHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Request = request;
            return Task.FromResult(_responseFactory(request));
        }
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _bytes;

        public UnknownLengthContent(byte[] bytes)
        {
            _bytes = bytes;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(_bytes).AsTask();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
