using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Hbpos.Contracts.Advertisements;

namespace Hbpos.Client.Wpf.Services;

public interface IAdvertisementMediaCache
{
    Task<IReadOnlyList<AdvertisementPlaybackItemDto>> CacheAsync(
        IReadOnlyList<AdvertisementPlaybackItemDto> advertisements,
        CancellationToken cancellationToken = default);
}

public interface IAdvertisementMediaCacheDirectoryProvider
{
    string GetCacheDirectory();
}

public sealed class AdvertisementMediaCacheDirectoryProvider : IAdvertisementMediaCacheDirectoryProvider
{
    public string GetCacheDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hbpos",
            "AdvertisementMedia");
    }
}

public sealed class AdvertisementMediaCacheService(
    HttpClient httpClient,
    IAdvertisementMediaCacheDirectoryProvider directoryProvider) : IAdvertisementMediaCache
{
    private const int CopyBufferSize = 81920;
    private const long MaximumAdvertisementBytes = 200L * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> ContentTypeExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp",
            ["image/gif"] = ".gif",
            ["video/mp4"] = ".mp4",
            ["video/webm"] = ".webm",
            ["video/quicktime"] = ".mov"
        };

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".gif",
        ".mp4",
        ".webm",
        ".mov"
    };

    public async Task<IReadOnlyList<AdvertisementPlaybackItemDto>> CacheAsync(
        IReadOnlyList<AdvertisementPlaybackItemDto> advertisements,
        CancellationToken cancellationToken = default)
    {
        var cached = new List<AdvertisementPlaybackItemDto>(advertisements.Count);
        var retainedFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var advertisement in advertisements)
        {
            var cachedAdvertisement = await CacheOneAsync(advertisement, cancellationToken).ConfigureAwait(false);
            cached.Add(cachedAdvertisement);
            if (TryCreateLocalCachePath(cachedAdvertisement.MediaUrl, out var retainedFilePath))
            {
                retainedFilePaths.Add(retainedFilePath);
            }
        }

        CleanupCacheDirectory(retainedFilePaths);
        return cached;
    }

    private async Task<AdvertisementPlaybackItemDto> CacheOneAsync(
        AdvertisementPlaybackItemDto advertisement,
        CancellationToken cancellationToken)
    {
        if (!TryCreateRemoteUri(advertisement.MediaUrl, out var mediaUri) ||
            advertisement.FileSize is <= 0 or > MaximumAdvertisementBytes)
        {
            return advertisement;
        }

        var directory = directoryProvider.GetCacheDirectory();
        var filePath = Path.Combine(directory, BuildCacheFileName(advertisement, mediaUri));
        if (File.Exists(filePath) && new FileInfo(filePath).Length == advertisement.FileSize)
        {
            return advertisement with { MediaUrl = new Uri(filePath).AbsoluteUri };
        }

        var tempFilePath = filePath + ".download";
        try
        {
            Directory.CreateDirectory(directory);
            DeleteIfExists(tempFilePath);

            using var response = await httpClient.GetAsync(
                mediaUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is long contentLength &&
                contentLength != advertisement.FileSize)
            {
                return advertisement;
            }

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(
                tempFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                useAsync: true))
            {
                var copiedBytes = await CopyExactSizeAsync(
                    source,
                    target,
                    advertisement.FileSize,
                    cancellationToken).ConfigureAwait(false);
                if (copiedBytes != advertisement.FileSize)
                {
                    DeleteIfExists(tempFilePath);
                    return advertisement;
                }
            }

            // 下载完成后再替换正式文件，避免 WPF 播放到半截素材。
            File.Move(tempFilePath, filePath, overwrite: true);
            return advertisement with { MediaUrl = new Uri(filePath).AbsoluteUri };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DeleteIfExists(tempFilePath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException or OperationCanceledException)
        {
            DeleteIfExists(tempFilePath);
            ConsoleLog.Write(
                "CustomerDisplay",
                $"advertisement media cache item failed id={advertisement.Id} uri={mediaUri} error={ex.GetType().Name}: {ex.Message}");
            return advertisement;
        }
    }

    private static bool TryCreateRemoteUri(string mediaUrl, out Uri uri)
    {
        return Uri.TryCreate(mediaUrl, UriKind.Absolute, out uri!) &&
            (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
    }

    private bool TryCreateLocalCachePath(string mediaUrl, out string filePath)
    {
        filePath = string.Empty;
        if (!Uri.TryCreate(mediaUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeFile)
        {
            return false;
        }

        var cacheDirectory = Path.GetFullPath(directoryProvider.GetCacheDirectory())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(uri.LocalPath);
        if (!candidate.StartsWith(cacheDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        filePath = candidate;
        return true;
    }

    private void CleanupCacheDirectory(IReadOnlySet<string> retainedFilePaths)
    {
        var directory = directoryProvider.GetCacheDirectory();
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(directory))
        {
            var fullPath = Path.GetFullPath(filePath);
            if (retainedFilePaths.Contains(fullPath))
            {
                continue;
            }

            try
            {
                // 当前有效列表之外的本地素材直接清掉，避免过期/下架广告继续占用磁盘。
                File.Delete(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ConsoleLog.Write(
                    "CustomerDisplay",
                    $"advertisement media cache cleanup failed path={fullPath} error={ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static string BuildCacheFileName(AdvertisementPlaybackItemDto advertisement, Uri mediaUri)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(advertisement.MediaUrl)));
        return $"{hash.ToLowerInvariant()}{ResolveExtension(advertisement, mediaUri)}";
    }

    private static string ResolveExtension(AdvertisementPlaybackItemDto advertisement, Uri mediaUri)
    {
        foreach (var candidate in new[]
        {
            Path.GetExtension(advertisement.ObjectKey),
            Path.GetExtension(advertisement.OriginalFileName),
            Path.GetExtension(mediaUri.LocalPath)
        })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && AllowedExtensions.Contains(candidate))
            {
                return candidate.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                    ? ".jpg"
                    : candidate.ToLowerInvariant();
            }
        }

        return ContentTypeExtensions.TryGetValue(advertisement.ContentType, out var extension)
            ? extension
            : ".bin";
    }

    private static async Task<long> CopyExactSizeAsync(
        Stream source,
        Stream target,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[CopyBufferSize];
        long copiedBytes = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return copiedBytes;
            }

            copiedBytes += read;
            if (copiedBytes > expectedSize)
            {
                return copiedBytes;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
