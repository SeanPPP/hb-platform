using System.Net;
using BlazorApp.Api.Models;
using BlazorApp.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class TencentCloudUploadServiceTests
{
    [Fact]
    public void GetDirectUploadSignature_使用CosXmlSha1查询签名()
    {
        var service = CreateService(new CaptureHandler());

        var signature = service.GetDirectUploadSignature(
            "mobile-app-builds/production/build-123.apk",
            "application/vnd.android.package-archive",
            900
        );

        var uri = new Uri(signature.Url);
        var decodedQuery = Uri.UnescapeDataString(uri.Query.TrimStart('?'));

        Assert.Equal(
            "/mobile-app-builds/production/build-123.apk",
            uri.AbsolutePath
        );
        Assert.Equal(
            "https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/mobile-app-builds/production/build-123.apk?q-sign-algorithm=sha1&q-ak=secret-id&q-sign-time=1782172800%3B1782173700&q-key-time=1782172800%3B1782173700&q-header-list=content-type%3Bhost&q-url-param-list=&q-signature=8f1d72f7dee4b67d038564b34d71299f8a5ddf52",
            signature.Url
        );
        Assert.Contains("q-sign-algorithm=sha1", decodedQuery);
        Assert.Contains("q-sign-time=1782172800;1782173700", decodedQuery);
        Assert.Contains("q-key-time=1782172800;1782173700", decodedQuery);
        Assert.Contains("q-header-list=content-type;host", decodedQuery);
        Assert.Contains("q-url-param-list=", decodedQuery);
        Assert.Contains("q-signature=8f1d72f7dee4b67d038564b34d71299f8a5ddf52", decodedQuery);
        Assert.DoesNotContain("authorization=", decodedQuery);
        Assert.DoesNotContain("sha256", decodedQuery);
        Assert.Equal(
            "application/vnd.android.package-archive",
            signature.Headers["Content-Type"]
        );
    }

    [Fact]
    public async Task UploadStreamAsync_构造CosPut请求并带文件长度()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler);
        await using var content = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await service.UploadStreamAsync(
            "mobile-app-builds/production/build-123.apk",
            "application/vnd.android.package-archive",
            content,
            3,
            CancellationToken.None
        );

        Assert.True(result.Success);
        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Put, handler.Request!.Method);
        Assert.Equal(3, handler.Request.Content!.Headers.ContentLength);
        Assert.Equal(
            "application/vnd.android.package-archive",
            handler.Request.Content.Headers.ContentType!.MediaType
        );
        Assert.Contains(
            "q-sign-algorithm=sha1",
            Uri.UnescapeDataString(handler.Request.RequestUri!.Query)
        );
    }

    [Fact]
    public async Task UploadStreamAsync_请求取消时透传取消异常()
    {
        var handler = new CanceledHandler();
        var service = CreateService(handler);
        await using var content = new MemoryStream(new byte[] { 1, 2, 3 });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.UploadStreamAsync(
                "mobile-app-builds/production/build-canceled.apk",
                "application/vnd.android.package-archive",
                content,
                3,
                cts.Token
            )
        );
    }

    [Fact]
    public async Task DownloadObjectBytesAsync_超过限制时拒绝且不无界读取()
    {
        var handler = new ByteResponseHandler(new byte[1025]);
        var service = CreateService(handler);

        var result = await service.DownloadObjectBytesAsync("employee/image.jpg", 1024);

        Assert.False(result.Success);
        Assert.Equal("COS_OBJECT_TOO_LARGE", result.Code);
        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
    }

    [Fact]
    public async Task DeleteObjectAsync_使用签名Delete请求()
    {
        var handler = new ByteResponseHandler(Array.Empty<byte>());
        var service = CreateService(handler);

        var result = await service.DeleteObjectAsync("employee/image.jpg");

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Delete, handler.Request!.Method);
        Assert.Contains("q-sign-algorithm=sha1", handler.Request.RequestUri!.Query);
    }

    [Fact]
    public void GetDirectUploadSignature_私有Acl和归属元数据参与签名()
    {
        var service = CreateService(new CaptureHandler());
        var signature = service.GetDirectUploadSignature(
            "employee-profiles/user-a/identity/token.jpg",
            "image/jpeg",
            900,
            new Dictionary<string, string>
            {
                ["x-cos-acl"] = "private",
                ["x-cos-meta-owner"] = "user-a",
            }
        );

        Assert.Equal("private", signature.Headers["x-cos-acl"]);
        Assert.Equal("user-a", signature.Headers["x-cos-meta-owner"]);
        var query = Uri.UnescapeDataString(new Uri(signature.Url).Query);
        Assert.Contains("x-cos-acl", query);
        Assert.Contains("x-cos-meta-owner", query);
    }

    [Fact]
    public void GetSignedDownloadUrl_使用五分钟短期签名()
    {
        var service = CreateService(new CaptureHandler());

        var url = service.GetSignedDownloadUrl(
            "employee-profiles/user-a/identity/token.jpg",
            300
        );

        Assert.Contains("q-sign-time=1782172800%3B1782173100", url);
        var signed = service.GetSignedDownload(
            "employee-profiles/user-a/identity/token.jpg",
            300
        );
        Assert.Equal(new DateTime(2026, 6, 23, 0, 5, 0, DateTimeKind.Utc), signed.ExpiresAtUtc);
    }

    private static TencentCloudUploadService CreateService(HttpMessageHandler handler)
    {
        return new TencentCloudUploadService(
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
            new HttpClient(handler),
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 23, 0, 0, 0, TimeSpan.Zero))
        );
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class CanceledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class ByteResponseHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes;
        public HttpRequestMessage? Request { get; private set; }

        public ByteResponseHandler(byte[] bytes)
        {
            _bytes = bytes;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Request = request;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_bytes),
                }
            );
        }
    }
}
