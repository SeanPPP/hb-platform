using System.Net;
using System.Text;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Advertisements;

namespace Hbpos.Client.Tests;

public sealed class AdvertisementMediaCacheServiceTests
{
    [Fact]
    public async Task CacheAsync_downloads_remote_media_to_local_file_and_reuses_existing_file()
    {
        var payload = Encoding.UTF8.GetBytes("advertisement-video");
        var directory = Path.Combine(Path.GetTempPath(), $"hbpos-ad-cache-{Guid.NewGuid():N}");
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal("https://cdn.example.com/ads/2026/ad-1.mp4", request.RequestUri?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
        });
        var service = new AdvertisementMediaCacheService(
            new HttpClient(handler),
            new FixedAdvertisementMediaCacheDirectoryProvider(directory));
        var advertisement = CreateVideoAdvertisement(payload.Length);

        try
        {
            var first = await service.CacheAsync([advertisement]);
            var cached = Assert.Single(first);
            var cachedUri = new Uri(cached.MediaUrl);
            var cachedPath = cachedUri.LocalPath;

            Assert.Equal(Uri.UriSchemeFile, cachedUri.Scheme);
            Assert.True(File.Exists(cachedPath));
            Assert.Equal(payload, await File.ReadAllBytesAsync(cachedPath));
            Assert.Equal(1, handler.CallCount);

            var second = await service.CacheAsync([advertisement]);

            Assert.Equal(cached.MediaUrl, Assert.Single(second).MediaUrl);
            Assert.Equal(1, handler.CallCount);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CacheAsync_removes_cached_files_not_used_by_current_advertisements()
    {
        var payload = Encoding.UTF8.GetBytes("advertisement-video");
        var directory = Path.Combine(Path.GetTempPath(), $"hbpos-ad-cache-{Guid.NewGuid():N}");
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });
        var service = new AdvertisementMediaCacheService(
            new HttpClient(handler),
            new FixedAdvertisementMediaCacheDirectoryProvider(directory));

        try
        {
            var first = Assert.Single(await service.CacheAsync([CreateVideoAdvertisement("ad-1", payload.Length)]));
            var firstPath = new Uri(first.MediaUrl).LocalPath;
            Assert.True(File.Exists(firstPath));

            var second = Assert.Single(await service.CacheAsync([CreateVideoAdvertisement("ad-2", payload.Length)]));
            var secondPath = new Uri(second.MediaUrl).LocalPath;

            Assert.False(File.Exists(firstPath));
            Assert.True(File.Exists(secondPath));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CacheAsync_cleans_cache_directory_when_current_advertisements_are_empty()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hbpos-ad-cache-{Guid.NewGuid():N}");
        var staleFile = Path.Combine(directory, "stale.mp4");
        var staleDownload = Path.Combine(directory, "stale.mp4.download");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(staleFile, "old");
        await File.WriteAllTextAsync(staleDownload, "partial");
        var service = new AdvertisementMediaCacheService(
            new HttpClient(new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound))),
            new FixedAdvertisementMediaCacheDirectoryProvider(directory));

        try
        {
            var result = await service.CacheAsync([]);

            Assert.Empty(result);
            Assert.False(File.Exists(staleFile));
            Assert.False(File.Exists(staleDownload));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CacheAsync_returns_current_advertisements_when_stale_file_delete_fails()
    {
        var payload = Encoding.UTF8.GetBytes("advertisement-video");
        var directory = Path.Combine(Path.GetTempPath(), $"hbpos-ad-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var lockedFile = Path.Combine(directory, "locked.mp4");
        await File.WriteAllTextAsync(lockedFile, "locked");
        FileStream? locked = new(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None);
        var service = new AdvertisementMediaCacheService(
            new HttpClient(new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            })),
            new FixedAdvertisementMediaCacheDirectoryProvider(directory));

        try
        {
            var result = await service.CacheAsync([CreateVideoAdvertisement("ad-1", payload.Length)]);

            Assert.Single(result);
            Assert.Equal("ad-1", result[0].Id);
        }
        finally
        {
            if (locked is not null)
            {
                await locked.DisposeAsync();
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static AdvertisementPlaybackItemDto CreateVideoAdvertisement(long fileSize)
    {
        return CreateVideoAdvertisement("ad-1", fileSize);
    }

    private static AdvertisementPlaybackItemDto CreateVideoAdvertisement(string id, long fileSize)
    {
        return new AdvertisementPlaybackItemDto(
            id,
            "Video Ad",
            null,
            "video",
            $"https://cdn.example.com/ads/2026/{id}.mp4",
            null,
            $"ads/2026/{id}.mp4",
            $"{id}.mp4",
            "video/mp4",
            fileSize,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(5),
            1);
    }

    private sealed class FixedAdvertisementMediaCacheDirectoryProvider(string path) : IAdvertisementMediaCacheDirectoryProvider
    {
        public string GetCacheDirectory() => path;
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(handler(request, cancellationToken));
        }
    }
}
