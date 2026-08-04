using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hbpos.Client.Wpf.Converters;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

[Collection(ProductThumbnailImageSourceConverterTestCollection.Name)]
public sealed class ProductThumbnailImageSourceConverterTests
{
    private const string OnePixelPngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==";

    [Fact]
    public void Convert_returns_bitmap_for_data_image_base64()
    {
        var converter = new ProductThumbnailImageSourceConverter();

        var result = converter.Convert(
            $"data:image/png;base64,{OnePixelPngBase64}",
            typeof(BitmapSource),
            null,
            CultureInfo.InvariantCulture);

        var image = Assert.IsType<BitmapImage>(result);
        Assert.Equal(72, image.PixelWidth);
        Assert.Equal(72, image.PixelHeight);
    }

    [Fact]
    public void Convert_decodes_larger_data_image_to_thumbnail_width()
    {
        var converter = new ProductThumbnailImageSourceConverter();

        var result = converter.Convert(
            CreatePngDataUri(144, 144),
            typeof(BitmapSource),
            null,
            CultureInfo.InvariantCulture);

        var image = Assert.IsType<BitmapImage>(result);
        Assert.Equal(72, image.PixelWidth);
        Assert.Equal(72, image.PixelHeight);
    }

    [Fact]
    public void Convert_rejects_data_image_before_decoding_when_base64_payload_exceeds_limit()
    {
        using var byteLimit = ProductThumbnailImageSourceConverter.UseImageInputByteLimitForTests(12);
        var payload = Convert.ToBase64String(new byte[13]);
        var converter = new ProductThumbnailImageSourceConverter();

        var logs = CaptureProductImageLogs(() =>
            Assert.Null(converter.Convert($"data:image/png;base64,{payload}", typeof(BitmapSource), null, CultureInfo.InvariantCulture)));

        var line = Assert.Single(logs);
        Assert.Contains("reason=data-payload-too-large", line);
        Assert.DoesNotContain(payload, line);
    }

    [Fact]
    public void Convert_rejects_data_image_after_decoding_when_bytes_exceed_limit()
    {
        using var byteLimit = ProductThumbnailImageSourceConverter.UseImageInputByteLimitForTests(11);
        var payload = Convert.ToBase64String(new byte[12]);
        var converter = new ProductThumbnailImageSourceConverter();

        var logs = CaptureProductImageLogs(() =>
            Assert.Null(converter.Convert($"data:image/png;base64,{payload}", typeof(BitmapSource), null, CultureInfo.InvariantCulture)));

        var line = Assert.Single(logs);
        Assert.Contains("reason=data-decoded-too-large", line);
        Assert.DoesNotContain(payload, line);
    }

    [Fact]
    public async Task ReadRemoteImageContent_rejects_response_when_content_length_exceeds_limit()
    {
        using var byteLimit = ProductThumbnailImageSourceConverter.UseImageInputByteLimitForTests(8);
        using var content = new ByteArrayContent(new byte[9]);

        await Assert.ThrowsAnyAsync<IOException>(
            () => ProductThumbnailImageSourceConverter.ReadRemoteImageContentForTestsAsync(content));
    }

    [Fact]
    public async Task ReadRemoteImageContent_rejects_stream_when_content_length_is_missing_and_limit_is_exceeded()
    {
        using var byteLimit = ProductThumbnailImageSourceConverter.UseImageInputByteLimitForTests(8);
        using var content = new UnknownLengthByteArrayContent(new byte[9]);
        Assert.Null(content.Headers.ContentLength);

        await Assert.ThrowsAnyAsync<IOException>(
            () => ProductThumbnailImageSourceConverter.ReadRemoteImageContentForTestsAsync(content));
    }

    [Fact]
    public void Convert_returns_bitmap_for_absolute_file_path()
    {
        var converter = new ProductThumbnailImageSourceConverter();
        var filePath = CreateTempImageFile();

        try
        {
            var result = converter.Convert(filePath, typeof(BitmapSource), null, CultureInfo.InvariantCulture);

            Assert.IsType<BitmapImage>(result);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Convert_returns_bitmap_for_file_uri()
    {
        var converter = new ProductThumbnailImageSourceConverter();
        var filePath = CreateTempImageFile();

        try
        {
            var result = converter.Convert(new Uri(filePath).AbsoluteUri, typeof(BitmapSource), null, CultureInfo.InvariantCulture);

            Assert.IsType<BitmapImage>(result);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Convert_returns_null_for_missing_absolute_file_path()
    {
        var converter = new ProductThumbnailImageSourceConverter();
        var filePath = Path.Combine(Path.GetTempPath(), $"hbpos-thumbnail-missing-{Guid.NewGuid():N}.png");

        var result = converter.Convert(filePath, typeof(BitmapSource), null, CultureInfo.InvariantCulture);

        Assert.Null(result);
    }

    [Fact]
    public void Convert_returns_bitmap_for_http_uri()
    {
        var converter = new ProductThumbnailImageSourceConverter();
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests(
            (_, _) => Task.FromResult(OnePixelPngBytes()));

        var result = converter.Convert(
            $"https://cdn.example.test/images/{Guid.NewGuid():N}/product.png",
            typeof(BitmapSource),
            null,
            CultureInfo.InvariantCulture);

        var image = Assert.IsType<BitmapImage>(result);
        Assert.Equal(72, image.PixelWidth);
        Assert.Equal(72, image.PixelHeight);
    }

    [Fact]
    public void Convert_logs_remote_uri_diagnostics_when_url_contains_unescaped_hash()
    {
        var converter = new ProductThumbnailImageSourceConverter();
        var imageUrl = $"https://cdn.example.test/images/{Guid.NewGuid():N}/#0065-6759#XRU.jpg";
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests(
            (_, _) => Task.FromResult(OnePixelPngBytes()));

        var logs = CaptureProductImageLogs(() => converter.Convert(imageUrl, typeof(BitmapSource), null, CultureInfo.InvariantCulture));

        var line = logs.Single(item => item.Contains("image uri parsed", StringComparison.Ordinal));
        Assert.Contains("image uri parsed", line);
        Assert.Contains("sourceKind=http", line);
        Assert.Contains("containsUnescapedHash=true", line);
        Assert.Contains("hasFragment=true", line);
        Assert.Contains("resolvedUri=\"https://cdn.example.test/images/", line);
    }

    [Fact]
    public void Convert_escapes_unescaped_hash_in_http_image_file_name()
    {
        var converter = new ProductThumbnailImageSourceConverter();
        Uri? requestedUri = null;
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests((uri, _) =>
        {
            requestedUri = uri;
            return Task.FromResult(OnePixelPngBytes());
        });

        var result = converter.Convert(
            "https://cdn.example.test/images/#0065-6759#XRU.jpg",
            typeof(BitmapSource),
            null,
            CultureInfo.InvariantCulture);

        Assert.IsType<BitmapImage>(result);
        Assert.NotNull(requestedUri);
        Assert.Empty(requestedUri.Fragment);
        Assert.Equal(
            "https://cdn.example.test/images/%230065-6759%23XRU.jpg",
            requestedUri.AbsoluteUri);
    }

    [Fact]
    public void Convert_logs_missing_local_file_once()
    {
        var converter = new ProductThumbnailImageSourceConverter();
        var filePath = Path.Combine(Path.GetTempPath(), $"hbpos-thumbnail-missing-{Guid.NewGuid():N}.png");

        var logs = CaptureProductImageLogs(() =>
        {
            converter.Convert(filePath, typeof(BitmapSource), null, CultureInfo.InvariantCulture);
            converter.Convert(filePath, typeof(BitmapSource), null, CultureInfo.InvariantCulture);
        });

        var line = Assert.Single(logs);
        Assert.Contains("image request rejected", line);
        Assert.Contains("reason=file-missing", line);
        Assert.Contains("sourceKind=file", line);
    }

    [Fact]
    public void Convert_logs_invalid_data_image_without_full_payload()
    {
        var converter = new ProductThumbnailImageSourceConverter();
        var payload = $"not-base64-{Guid.NewGuid():N}";

        var logs = CaptureProductImageLogs(() =>
            converter.Convert($"data:image/png;base64,{payload}", typeof(BitmapSource), null, CultureInfo.InvariantCulture));

        var line = Assert.Single(logs);
        Assert.Contains("reason=invalid-data-base64", line);
        Assert.Contains("sourceKind=data", line);
        Assert.Contains("dataLength=", line);
        Assert.DoesNotContain(payload, line);
    }

    [Fact]
    public void Convert_logs_rejected_unsupported_source_once()
    {
        var converter = new ProductThumbnailImageSourceConverter();
        var source = $"unsupported://image/{Guid.NewGuid():N}";

        var logs = CaptureProductImageLogs(() =>
        {
            converter.Convert(source, typeof(BitmapSource), null, CultureInfo.InvariantCulture);
            converter.Convert(source, typeof(BitmapSource), null, CultureInfo.InvariantCulture);
        });

        var line = Assert.Single(logs);
        Assert.Contains("image request rejected", line);
        Assert.Contains("reason=unsupported-uri-scheme", line);
        Assert.Contains("sourceKind=unsupported", line);
    }

    [Fact]
    public void Convert_evicts_oldest_diagnostic_when_diagnostic_cache_reaches_capacity()
    {
        using var cacheLimits = ProductThumbnailImageSourceConverter.UseCacheLimitsForTests(2, 2, 2);
        var converter = new ProductThumbnailImageSourceConverter();
        var sourceTexts = Enumerable.Range(0, 3)
            .Select(index => $"unsupported://image/{Guid.NewGuid():N}/{index}")
            .ToArray();

        var logs = CaptureProductImageLogs(() =>
        {
            foreach (var sourceText in sourceTexts)
            {
                Assert.Null(converter.Convert(sourceText, typeof(BitmapSource), null, CultureInfo.InvariantCulture));
            }

            // 第一键重新记录会按 FIFO 淘汰第二键，因此先验证第二键仍受抑制。
            Assert.Null(converter.Convert(sourceTexts[1], typeof(BitmapSource), null, CultureInfo.InvariantCulture));
            Assert.Null(converter.Convert(sourceTexts[0], typeof(BitmapSource), null, CultureInfo.InvariantCulture));
        });

        Assert.Equal(2, ProductThumbnailImageSourceConverter.GetCacheCountsForTests().DiagnosticCount);
        Assert.Equal(2, logs.Count(line => line.Contains(sourceTexts[0], StringComparison.Ordinal)));
        Assert.Equal(1, logs.Count(line => line.Contains(sourceTexts[1], StringComparison.Ordinal)));
        Assert.Equal(1, logs.Count(line => line.Contains(sourceTexts[2], StringComparison.Ordinal)));
    }

    [Fact]
    public void Convert_downloads_http_bitmap_once_and_caches_result()
    {
        ClearImageCacheForTests();
        var converter = new ProductThumbnailImageSourceConverter();
        var imageUrl = $"https://cdn.example.test/images/{Guid.NewGuid():N}/product.png";
        var loadCount = 0;
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests((_, _) =>
        {
            loadCount++;
            return Task.FromResult(OnePixelPngBytes());
        });

        var first = converter.Convert(imageUrl, typeof(BitmapSource), null, CultureInfo.InvariantCulture);
        var second = converter.Convert(imageUrl, typeof(BitmapSource), null, CultureInfo.InvariantCulture);

        var firstImage = Assert.IsType<BitmapImage>(first);
        var secondImage = Assert.IsType<BitmapImage>(second);
        Assert.True(firstImage.IsFrozen);
        Assert.True(secondImage.IsFrozen);
        Assert.Same(firstImage, secondImage);
        Assert.Equal(1, loadCount);
    }

    [Fact]
    public void Convert_evicts_oldest_image_when_cache_reaches_capacity()
    {
        using var cacheLimits = ProductThumbnailImageSourceConverter.UseCacheLimitsForTests(2, 2, 2);
        var converter = new ProductThumbnailImageSourceConverter();
        var imageUrls = Enumerable.Range(0, 3)
            .Select(index => $"https://cdn.example.test/images/{Guid.NewGuid():N}/{index}.png")
            .ToArray();
        var loadCount = 0;
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests((_, _) =>
        {
            loadCount++;
            return Task.FromResult(OnePixelPngBytes());
        });

        foreach (var imageUrl in imageUrls)
        {
            Assert.IsType<BitmapImage>(converter.Convert(imageUrl, typeof(BitmapSource), null, CultureInfo.InvariantCulture));
        }

        Assert.Equal(2, ProductThumbnailImageSourceConverter.GetCacheCountsForTests().ImageCount);
        Assert.IsType<BitmapImage>(converter.Convert(imageUrls[1], typeof(BitmapSource), null, CultureInfo.InvariantCulture));
        Assert.Equal(3, loadCount);

        Assert.IsType<BitmapImage>(converter.Convert(imageUrls[0], typeof(BitmapSource), null, CultureInfo.InvariantCulture));
        Assert.Equal(4, loadCount);
        Assert.Equal(2, ProductThumbnailImageSourceConverter.GetCacheCountsForTests().ImageCount);
    }

    [Fact]
    public void Convert_hashes_long_static_image_cache_keys()
    {
        using var cacheLimits = ProductThumbnailImageSourceConverter.UseCacheLimitsForTests(2, 2, 2);
        var converter = new ProductThumbnailImageSourceConverter();
        var sourceText = $"data:image/png;base64,{new string(' ', 513)}{OnePixelPngBase64}";

        Assert.IsType<BitmapImage>(converter.Convert(sourceText, typeof(BitmapSource), null, CultureInfo.InvariantCulture));

        var cacheKey = Assert.Single(ProductThumbnailImageSourceConverter.GetImageCacheKeysForTests());
        Assert.StartsWith("sha256:", cacheKey, StringComparison.Ordinal);
        Assert.InRange(cacheKey.Length, 1, 80);
    }

    [Fact]
    public async Task AsyncIsEnabled_false_defers_source_load_until_reenabled()
    {
        var sourceText = $"data:image/png;base64,{OnePixelPngBase64}";
        await ProductThumbnailImageSourceConverter.PreloadAsync([sourceText]);
        var imageBrush = new ImageBrush();

        ProductThumbnailImageSourceConverter.SetAsyncIsEnabled(imageBrush, false);
        ProductThumbnailImageSourceConverter.SetAsyncSourceText(imageBrush, sourceText);

        Assert.Null(imageBrush.ImageSource);

        ProductThumbnailImageSourceConverter.SetAsyncIsEnabled(imageBrush, true);

        Assert.IsType<BitmapImage>(imageBrush.ImageSource);
    }

    [Fact]
    public async Task AsyncIsEnabled_false_clears_existing_async_source()
    {
        var sourceText = $"data:image/png;base64,{OnePixelPngBase64}";
        await ProductThumbnailImageSourceConverter.PreloadAsync([sourceText]);
        var imageBrush = new ImageBrush();

        ProductThumbnailImageSourceConverter.SetAsyncSourceText(imageBrush, sourceText);
        Assert.IsType<BitmapImage>(imageBrush.ImageSource);

        ProductThumbnailImageSourceConverter.SetAsyncIsEnabled(imageBrush, false);

        Assert.Null(imageBrush.ImageSource);
    }

    [Fact]
    public void Convert_returns_bitmap_for_pack_uri()
    {
        var converter = new ProductThumbnailImageSourceConverter();

        var result = converter.Convert(
            "pack://application:,,,/Hbpos.Client.Wpf;component/Resources/AppIcon.ico",
            typeof(BitmapSource),
            null,
            CultureInfo.InvariantCulture);

        var image = Assert.IsType<BitmapImage>(result);
        Assert.True(image.PixelWidth > 0);
        Assert.True(image.PixelHeight > 0);
    }

    [Fact]
    public void Convert_resolves_root_relative_path_with_default_api_base_url()
    {
        var converter = new ProductThumbnailImageSourceConverter();
        Uri? requestedUri = null;
        using var apiBaseAddress = ProductThumbnailImageSourceConverter.UseApiBaseAddressProviderForTests(
            () => new Uri("http://localhost:5159/"));
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests((uri, _) =>
        {
            requestedUri = uri;
            return Task.FromResult(OnePixelPngBytes());
        });

        var result = converter.Convert("/images/product.png", typeof(BitmapSource), null, CultureInfo.InvariantCulture);

        Assert.IsType<BitmapImage>(result);
        Assert.Equal("http://localhost:5159/images/product.png", requestedUri?.AbsoluteUri);
    }

    [Fact]
    public void Convert_resolves_root_relative_path_with_api_base_url()
    {
        var converter = new ProductThumbnailImageSourceConverter();
        Uri? requestedUri = null;
        using var apiBaseAddress = ProductThumbnailImageSourceConverter.UseApiBaseAddressProviderForTests(
            () => new Uri("https://cdn.example.test/tenant-a/"));
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests((uri, _) =>
        {
            requestedUri = uri;
            return Task.FromResult(OnePixelPngBytes());
        });

        var result = converter.Convert("/images/product.png", typeof(BitmapSource), null, CultureInfo.InvariantCulture);

        Assert.IsType<BitmapImage>(result);
        Assert.Equal("https://cdn.example.test/tenant-a/images/product.png", requestedUri?.AbsoluteUri);
    }

    [Fact]
    public void Convert_resolves_relative_path_without_leading_slash_with_api_base_url()
    {
        var converter = new ProductThumbnailImageSourceConverter();
        Uri? requestedUri = null;
        using var apiBaseAddress = ProductThumbnailImageSourceConverter.UseApiBaseAddressProviderForTests(
            () => new Uri("https://cdn.example.test/tenant-a/"));
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests((uri, _) =>
        {
            requestedUri = uri;
            return Task.FromResult(OnePixelPngBytes());
        });

        var result = converter.Convert("images/product.png", typeof(BitmapSource), null, CultureInfo.InvariantCulture);

        Assert.IsType<BitmapImage>(result);
        Assert.Equal("https://cdn.example.test/tenant-a/images/product.png", requestedUri?.AbsoluteUri);
    }

    [Fact]
    public void Convert_resolves_relative_path_from_current_runtime_endpoint_after_switch()
    {
        var state = new ApiRuntimeEndpointState("https://first.example.test/pos-api/");
        var converter = new ProductThumbnailImageSourceConverter();
        var requestedUris = new List<string>();
        using var endpoint = ProductThumbnailImageSourceConverter.UseApiBaseAddressProviderForTests(
            () => state.CurrentAddress);
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests((uri, _) =>
        {
            requestedUris.Add(uri.AbsoluteUri);
            return Task.FromResult(OnePixelPngBytes());
        });

        converter.Convert("images/first.png", typeof(BitmapSource), null, CultureInfo.InvariantCulture);
        state.Switch("https://second.example.test/other-base/");
        converter.Convert("images/second.png", typeof(BitmapSource), null, CultureInfo.InvariantCulture);

        Assert.Equal(
            [
                "https://first.example.test/pos-api/images/first.png",
                "https://second.example.test/other-base/images/second.png"
            ],
            requestedUris);
    }

    [Fact]
    public void Convert_keeps_relative_image_cache_entries_isolated_across_endpoint_switches()
    {
        using var cacheLimits = ProductThumbnailImageSourceConverter.UseCacheLimitsForTests(2, 2, 2);
        var state = new ApiRuntimeEndpointState("https://first.example.test/pos-api/");
        var converter = new ProductThumbnailImageSourceConverter();
        var requestedUris = new List<string>();
        using var endpoint = ProductThumbnailImageSourceConverter.UseApiBaseAddressProviderForTests(
            () => state.CurrentAddress);
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests((uri, _) =>
        {
            requestedUris.Add(uri.AbsoluteUri);
            return Task.FromResult(OnePixelPngBytes());
        });

        converter.Convert("images/product.png", typeof(BitmapSource), null, CultureInfo.InvariantCulture);
        state.Switch("https://second.example.test/other-base/");
        converter.Convert("images/product.png", typeof(BitmapSource), null, CultureInfo.InvariantCulture);
        state.Switch("https://first.example.test/pos-api/");
        converter.Convert("images/product.png", typeof(BitmapSource), null, CultureInfo.InvariantCulture);

        Assert.Equal(
            [
                "https://first.example.test/pos-api/images/product.png",
                "https://second.example.test/other-base/images/product.png"
            ],
            requestedUris);
        Assert.Equal(2, ProductThumbnailImageSourceConverter.GetCacheCountsForTests().ImageCount);
    }

    [Fact]
    public void Convert_returns_null_for_invalid_data_image()
    {
        var converter = new ProductThumbnailImageSourceConverter();

        var result = converter.Convert("data:image/png;base64,not-base64", typeof(BitmapSource), null, CultureInfo.InvariantCulture);

        Assert.Null(result);
    }

    [Fact]
    public async Task PreloadAsync_warms_remote_image_cache_for_subsequent_convert_calls()
    {
        ClearImageCacheForTests();
        var converter = new ProductThumbnailImageSourceConverter();
        var imageUrl = $"https://cdn.example.test/images/{Guid.NewGuid():N}/product.png";
        var loadCount = 0;
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests((_, _) =>
        {
            loadCount++;
            return Task.FromResult(OnePixelPngBytes());
        });

        await ProductThumbnailImageSourceConverter.PreloadAsync([imageUrl]);

        Assert.Equal(1, loadCount);

        var first = converter.Convert(imageUrl, typeof(BitmapSource), null, CultureInfo.InvariantCulture);
        var second = converter.Convert(imageUrl, typeof(BitmapSource), null, CultureInfo.InvariantCulture);

        Assert.Same(first, second);
        Assert.IsType<BitmapImage>(first);
        Assert.Equal(1, loadCount);
    }

    [Fact]
    public async Task PreloadAsync_warms_remote_image_cache_for_async_attached_source()
    {
        ClearImageCacheForTests();
        var imageUrl = $"https://cdn.example.test/images/{Guid.NewGuid():N}/product.png";
        var loadCount = 0;
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests((_, _) =>
        {
            loadCount++;
            return Task.FromResult(OnePixelPngBytes());
        });

        await ProductThumbnailImageSourceConverter.PreloadAsync([imageUrl]);
        var imageBrush = new ImageBrush();
        ProductThumbnailImageSourceConverter.SetAsyncSourceText(imageBrush, imageUrl);

        Assert.IsType<BitmapImage>(imageBrush.ImageSource);
        Assert.Equal(1, loadCount);
    }

    [Fact]
    public void AsyncIsEnabled_false_cancels_inflight_remote_load()
    {
        ClearImageCacheForTests();
        var imageBrush = new ImageBrush();
        var imageUrl = $"https://cdn.example.test/images/{Guid.NewGuid():N}/product.png";
        using var loadStarted = new ManualResetEventSlim();
        using var loadCanceled = new ManualResetEventSlim();
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests((_, cancellationToken) =>
        {
            loadStarted.Set();
            cancellationToken.Register(() => loadCanceled.Set());
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                .ContinueWith(
                    _ => Array.Empty<byte>(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
        });

        ProductThumbnailImageSourceConverter.SetAsyncSourceText(imageBrush, imageUrl);
        Assert.True(loadStarted.Wait(TimeSpan.FromSeconds(3)));

        ProductThumbnailImageSourceConverter.SetAsyncIsEnabled(imageBrush, false);

        Assert.True(loadCanceled.Wait(TimeSpan.FromSeconds(3)));
        Assert.Null(imageBrush.ImageSource);
    }

    [Fact]
    public async Task PreloadAsync_skips_already_cached_remote_image()
    {
        ClearImageCacheForTests();
        var imageUrl = $"https://cdn.example.test/images/{Guid.NewGuid():N}/product.png";
        var loadCount = 0;
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests((_, _) =>
        {
            loadCount++;
            return Task.FromResult(OnePixelPngBytes());
        });

        var firstPreloadCount = await ProductThumbnailImageSourceConverter.PreloadAsync([imageUrl]);
        var secondPreloadCount = await ProductThumbnailImageSourceConverter.PreloadAsync([imageUrl]);

        Assert.Equal(1, firstPreloadCount);
        Assert.Equal(0, secondPreloadCount);
        Assert.Equal(1, loadCount);
    }

    [Fact]
    public async Task PreloadAsync_limits_background_read_and_decode_to_four_concurrent_images()
    {
        using var cacheLimits = ProductThumbnailImageSourceConverter.UseCacheLimitsForTests(2, 2, 2);
        var activeLoads = 0;
        var maxConcurrentLoads = 0;
        var imageUrls = Enumerable.Range(0, 9)
            .Select(index => $"https://cdn.example.test/images/{Guid.NewGuid():N}/{index}.png")
            .ToArray();
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests(async (_, cancellationToken) =>
        {
            var active = Interlocked.Increment(ref activeLoads);
            while (true)
            {
                var currentMaximum = Volatile.Read(ref maxConcurrentLoads);
                if (active <= currentMaximum || Interlocked.CompareExchange(ref maxConcurrentLoads, active, currentMaximum) == currentMaximum)
                {
                    break;
                }
            }

            try
            {
                await Task.Delay(30, cancellationToken);
                return OnePixelPngBytes();
            }
            finally
            {
                Interlocked.Decrement(ref activeLoads);
            }
        });

        var preloaded = await ProductThumbnailImageSourceConverter.PreloadAsync(imageUrls);

        Assert.Equal(imageUrls.Length, preloaded);
        Assert.InRange(maxConcurrentLoads, 1, 4);
        Assert.Equal(2, ProductThumbnailImageSourceConverter.GetCacheCountsForTests().ImageCount);
    }

    [Fact]
    public async Task PreloadAsync_defers_data_image_byte_decode_until_shared_gate_is_available()
    {
        ClearImageCacheForTests();
        var dataImage = CreatePngDataUri(32, 32, highEntropy: true);
        Assert.True(dataImage.Length > 512);
        var expectedCacheKey = $"sha256:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"72|{dataImage}")))}";
        var cacheKeyUtf8Chunks = new List<int>();
        using var remoteLoadsStarted = new ManualResetEventSlim();
        var startedCount = 0;
        var releaseRemoteLoads = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests(
            async (_, cancellationToken) =>
            {
                if (Interlocked.Increment(ref startedCount) == 4)
                {
                    remoteLoadsStarted.Set();
                }

                return await releaseRemoteLoads.Task.WaitAsync(cancellationToken);
            });
        var remotePreload = ProductThumbnailImageSourceConverter.PreloadAsync(
            Enumerable.Range(0, 4)
                .Select(index => $"https://cdn.example.test/images/{Guid.NewGuid():N}/{index}.png"));

        Assert.True(remoteLoadsStarted.Wait(TimeSpan.FromSeconds(3)));

        var dataDecodeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var dataDecode = ProductThumbnailImageSourceConverter.UseDataImageDecodeStartingForTests(
            () => dataDecodeStarted.TrySetResult());
        using var cacheKeyEncoding = ProductThumbnailImageSourceConverter.UseCacheKeyUtf8ChunkForTests(
            bytesUsed => cacheKeyUtf8Chunks.Add(bytesUsed));
        var dataPreload = ProductThumbnailImageSourceConverter.PreloadAsync(
            [dataImage]);

        try
        {
            await Task.Delay(100);
            Assert.False(dataDecodeStarted.Task.IsCompleted);
            Assert.NotEmpty(cacheKeyUtf8Chunks);
            Assert.All(cacheKeyUtf8Chunks, bytesUsed => Assert.InRange(bytesUsed, 1, 256));
        }
        finally
        {
            releaseRemoteLoads.TrySetResult(OnePixelPngBytes());
        }

        Assert.Equal(4, await remotePreload);
        await dataDecodeStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, await dataPreload);
        Assert.Contains(expectedCacheKey, ProductThumbnailImageSourceConverter.GetImageCacheKeysForTests());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreloadAsync_concurrent_success_and_failure_keeps_successful_image_cached(
        bool failureCompletesFirst)
    {
        ClearImageCacheForTests();
        var imageUrl = $"https://cdn.example.test/images/{Guid.NewGuid():N}/product.png";
        using var successStarted = new ManualResetEventSlim();
        using var failureStarted = new ManualResetEventSlim();
        var successRelease = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failureRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadCount = 0;
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests(
            async (_, cancellationToken) =>
            {
                if (Interlocked.Increment(ref loadCount) == 1)
                {
                    successStarted.Set();
                    return await successRelease.Task.WaitAsync(cancellationToken);
                }

                failureStarted.Set();
                await failureRelease.Task.WaitAsync(cancellationToken);
                throw new IOException("download failed");
            });

        var successPreload = ProductThumbnailImageSourceConverter.PreloadAsync([imageUrl]);
        Assert.True(successStarted.Wait(TimeSpan.FromSeconds(3)));
        var failurePreload = ProductThumbnailImageSourceConverter.PreloadAsync([imageUrl]);
        Assert.True(failureStarted.Wait(TimeSpan.FromSeconds(3)));

        if (failureCompletesFirst)
        {
            failureRelease.TrySetResult();
            Assert.Equal(0, await failurePreload);
            successRelease.TrySetResult(OnePixelPngBytes());
            Assert.Equal(1, await successPreload);
        }
        else
        {
            successRelease.TrySetResult(OnePixelPngBytes());
            Assert.Equal(1, await successPreload);
            failureRelease.TrySetResult();
            Assert.Equal(0, await failurePreload);
        }

        var cacheCounts = ProductThumbnailImageSourceConverter.GetCacheCountsForTests();
        Assert.Equal(1, cacheCounts.ImageCount);
        Assert.Equal(0, cacheCounts.FailedCount);

        var converter = new ProductThumbnailImageSourceConverter();
        Assert.IsType<BitmapImage>(converter.Convert(imageUrl, typeof(BitmapSource), null, CultureInfo.InvariantCulture));
        Assert.Equal(2, loadCount);
    }

#pragma warning disable xUnit1031 // 保持 ImageBrush 的创建线程，手动 dispatcher 帧和所有等待均有超时。
    [Fact]
    public void Async_data_image_byte_decode_waits_for_shared_background_gate()
    {
        ClearImageCacheForTests();
        var dataImage = CreatePngDataUri(32, 32, highEntropy: true);
        Assert.True(dataImage.Length > 512);
        using var remoteLoadsStarted = new ManualResetEventSlim();
        var startedCount = 0;
        var releaseRemoteLoads = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests(
            async (_, cancellationToken) =>
            {
                if (Interlocked.Increment(ref startedCount) == 4)
                {
                    remoteLoadsStarted.Set();
                }

                return await releaseRemoteLoads.Task.WaitAsync(cancellationToken);
            });
        var remotePreload = ProductThumbnailImageSourceConverter.PreloadAsync(
            Enumerable.Range(0, 4)
                .Select(index => $"https://cdn.example.test/images/{Guid.NewGuid():N}/{index}.png"));
        var imageBrush = new ImageBrush();
        var dataDecodeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var dataDecode = ProductThumbnailImageSourceConverter.UseDataImageDecodeStartingForTests(
            () => dataDecodeStarted.TrySetResult());

        try
        {
            try
            {
                Assert.True(remoteLoadsStarted.Wait(TimeSpan.FromSeconds(3)));
                ProductThumbnailImageSourceConverter.SetAsyncSourceText(
                    imageBrush,
                    dataImage);

                Task.Delay(100).GetAwaiter().GetResult();
                Assert.False(dataDecodeStarted.Task.IsCompleted);
                Assert.Null(imageBrush.ImageSource);
            }
            finally
            {
                releaseRemoteLoads.TrySetResult(OnePixelPngBytes());
            }

            Assert.Equal(4, remotePreload.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult());
            dataDecodeStarted.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();

            var frame = new System.Windows.Threading.DispatcherFrame();
            var imageApplyDeadline = DateTimeOffset.UtcNow.AddSeconds(3);
            var timer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background,
                imageBrush.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(10)
            };
            timer.Tick += (_, _) =>
            {
                if (imageBrush.ImageSource is not null || DateTimeOffset.UtcNow >= imageApplyDeadline)
                {
                    frame.Continue = false;
                }
            };
            timer.Start();
            try
            {
                System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
            finally
            {
                timer.Stop();
            }

            Assert.IsType<BitmapImage>(imageBrush.ImageSource);
        }
        finally
        {
            ProductThumbnailImageSourceConverter.SetAsyncIsEnabled(imageBrush, false);
        }
    }
#pragma warning restore xUnit1031

    [Fact]
    public async Task Async_local_file_read_waits_for_shared_background_gate()
    {
        ClearImageCacheForTests();
        var filePath = CreateTempImageFile();
        using var remoteLoadsStarted = new ManualResetEventSlim();
        var startedCount = 0;
        var releaseRemoteLoads = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fileMissing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var localImage = new ImageBrush();
        void HandleLog(string line)
        {
            if (line.Contains("[ProductImage]", StringComparison.Ordinal) &&
                line.Contains("reason=file-missing", StringComparison.Ordinal) &&
                line.Contains(filePath, StringComparison.OrdinalIgnoreCase))
            {
                fileMissing.TrySetResult();
            }
        }

        ConsoleLog.LineWritten += HandleLog;
        try
        {
            using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests(
                async (_, cancellationToken) =>
                {
                    if (Interlocked.Increment(ref startedCount) == 4)
                    {
                        remoteLoadsStarted.Set();
                    }

                    return await releaseRemoteLoads.Task.WaitAsync(cancellationToken);
                });
            var remotePreload = ProductThumbnailImageSourceConverter.PreloadAsync(
                Enumerable.Range(0, 4)
                    .Select(_ => $"https://cdn.example.test/images/{Guid.NewGuid():N}/product.png"));

            Assert.True(remoteLoadsStarted.Wait(TimeSpan.FromSeconds(3)));
            ProductThumbnailImageSourceConverter.SetAsyncSourceText(localImage, filePath);
            File.Delete(filePath);
            releaseRemoteLoads.TrySetResult(OnePixelPngBytes());

            await fileMissing.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await remotePreload;
        }
        finally
        {
            ConsoleLog.LineWritten -= HandleLog;
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task PreloadAsync_ignores_invalid_or_failed_images()
    {
        ClearImageCacheForTests();
        var failedUrl = $"https://cdn.example.test/images/{Guid.NewGuid():N}/missing.png";
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests((_, _) =>
            throw new IOException("download failed"));

        var preloadedCount = await ProductThumbnailImageSourceConverter.PreloadAsync(
            [null, string.Empty, "unsupported://image", failedUrl]);

        Assert.Equal(0, preloadedCount);
    }

    [Fact]
    public async Task PreloadAsync_evicts_oldest_failed_image_when_failure_cache_reaches_capacity()
    {
        using var cacheLimits = ProductThumbnailImageSourceConverter.UseCacheLimitsForTests(2, 2, 2);
        var imageUrls = Enumerable.Range(0, 3)
            .Select(index => $"https://cdn.example.test/images/{Guid.NewGuid():N}/missing-{index}.png")
            .ToArray();
        var loadCount = 0;
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests((_, _) =>
        {
            loadCount++;
            throw new IOException("download failed");
        });

        foreach (var imageUrl in imageUrls)
        {
            Assert.Equal(0, await ProductThumbnailImageSourceConverter.PreloadAsync([imageUrl]));
        }

        Assert.Equal(3, loadCount);
        Assert.Equal(2, ProductThumbnailImageSourceConverter.GetCacheCountsForTests().FailedCount);

        Assert.Equal(0, await ProductThumbnailImageSourceConverter.PreloadAsync([imageUrls[0]]));
        Assert.Equal(4, loadCount);
        Assert.Equal(2, ProductThumbnailImageSourceConverter.GetCacheCountsForTests().FailedCount);
    }

    [Fact]
    public async Task PreloadAsync_keeps_retried_expired_failure_when_evicting_oldest_failure()
    {
        using var cacheLimits = ProductThumbnailImageSourceConverter.UseCacheLimitsForTests(2, 2, 2);
        var expiredUrl = $"https://cdn.example.test/images/{Guid.NewGuid():N}/expired.png";
        var freshUrl = $"https://cdn.example.test/images/{Guid.NewGuid():N}/fresh.png";
        var newestUrl = $"https://cdn.example.test/images/{Guid.NewGuid():N}/newest.png";
        SetFailedCacheForTests(expiredUrl, DateTimeOffset.UtcNow.AddMinutes(-11));
        SetFailedCacheForTests(freshUrl, DateTimeOffset.UtcNow);

        var loadCount = 0;
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests((_, _) =>
        {
            loadCount++;
            throw new IOException("download failed");
        });

        Assert.Equal(0, await ProductThumbnailImageSourceConverter.PreloadAsync([expiredUrl]));
        Assert.Equal(1, loadCount);

        Assert.Equal(0, await ProductThumbnailImageSourceConverter.PreloadAsync([newestUrl]));
        Assert.Equal(2, loadCount);

        Assert.Equal(0, await ProductThumbnailImageSourceConverter.PreloadAsync([expiredUrl]));
        Assert.Equal(2, loadCount);

        Assert.Equal(0, await ProductThumbnailImageSourceConverter.PreloadAsync([freshUrl]));
        Assert.Equal(3, loadCount);
    }

    [Fact]
    public async Task PreloadAsync_remembers_failed_remote_image_and_does_not_retry_convert_or_async_load()
    {
        ClearImageCacheForTests();
        var failedUrl = $"https://cdn.example.test/images/{Guid.NewGuid():N}/missing.png";
        var loadCount = 0;
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests((_, _) =>
        {
            loadCount++;
            throw new IOException("download failed");
        });

        var preloadedCount = await ProductThumbnailImageSourceConverter.PreloadAsync([failedUrl]);

        Assert.Equal(0, preloadedCount);
        Assert.Equal(1, loadCount);

        var converter = new ProductThumbnailImageSourceConverter();
        Assert.Null(converter.Convert(failedUrl, typeof(BitmapSource), null, CultureInfo.InvariantCulture));
        Assert.Equal(1, loadCount);

        ProductThumbnailImageSourceConverter.SetAsyncSourceText(new ImageBrush(), failedUrl);
        Assert.Equal(1, loadCount);
    }

    [Fact]
    public async Task PreloadAsync_retries_failed_remote_image_after_failure_cache_expires()
    {
        ClearImageCacheForTests();
        var failedUrl = $"https://cdn.example.test/images/{Guid.NewGuid():N}/missing.png";
        var loadCount = 0;
        using var remoteImages = ProductThumbnailImageSourceConverter.UseRemoteImageBytesLoaderForTests((_, _) =>
        {
            loadCount++;
            return Task.FromResult(OnePixelPngBytes());
        });
        SetFailedCacheForTests(failedUrl, DateTimeOffset.UtcNow.AddMinutes(-11));

        var preloadedCount = await ProductThumbnailImageSourceConverter.PreloadAsync([failedUrl]);

        Assert.Equal(1, preloadedCount);
        Assert.Equal(1, loadCount);
    }

    private static string CreateTempImageFile()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"hbpos-thumbnail-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(filePath, OnePixelPngBytes());
        return filePath;
    }

    private static byte[] OnePixelPngBytes()
    {
        return Convert.FromBase64String(OnePixelPngBase64);
    }

    private static void ClearImageCacheForTests()
    {
        ProductThumbnailImageSourceConverter.ClearCachesForTests();
    }

    private static string CreatePngDataUri(int width, int height, bool highEntropy = false)
    {
        var pixels = new byte[width * height * 4];
        if (highEntropy)
        {
            var state = 17u;
            for (var index = 0; index < pixels.Length; index++)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                pixels[index] = (byte)(state >> 24);
            }
        }

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return $"data:image/png;base64,{Convert.ToBase64String(stream.ToArray())}";
    }

    private static void SetFailedCacheForTests(string sourceText, DateTimeOffset failedAt)
    {
        ProductThumbnailImageSourceConverter.SetFailedCacheEntryForTests(sourceText, failedAt);
    }

    private static List<string> CaptureProductImageLogs(Action action)
    {
        var lines = new List<string>();
        void Handler(string line)
        {
            if (line.Contains("[ProductImage]", StringComparison.Ordinal))
            {
                lines.Add(line);
            }
        }

        ConsoleLog.LineWritten += Handler;
        try
        {
            action();
        }
        finally
        {
            ConsoleLog.LineWritten -= Handler;
        }

        return lines;
    }

    private sealed class UnknownLengthByteArrayContent : HttpContent
    {
        private readonly byte[] _bytes;

        public UnknownLengthByteArrayContent(byte[] bytes)
        {
            _bytes = bytes;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(_bytes, 0, _bytes.Length);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            return Task.FromResult<Stream>(new MemoryStream(_bytes, writable: false));
        }
    }
}
