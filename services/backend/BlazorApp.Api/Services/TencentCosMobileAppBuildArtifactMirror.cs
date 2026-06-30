using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Models.HBweb;

namespace BlazorApp.Api.Services
{
    public class TencentCosMobileAppBuildArtifactMirror : IMobileAppBuildArtifactMirror
    {
        private const string ApkContentType = "application/vnd.android.package-archive";
        private const long MaxApkBytes = 300L * 1024 * 1024;
        private const int MaxRedirects = 5;
        private readonly HttpClient _httpClient;
        private readonly TencentCloudUploadService _uploadService;
        private readonly ILogger<TencentCosMobileAppBuildArtifactMirror> _logger;

        public TencentCosMobileAppBuildArtifactMirror(
            HttpClient httpClient,
            TencentCloudUploadService uploadService,
            ILogger<TencentCosMobileAppBuildArtifactMirror> logger
        )
        {
            _httpClient = httpClient;
            _uploadService = uploadService;
            _logger = logger;
        }

        public async Task<MobileAppBuildArtifactMirrorResult> MirrorAsync(
            MobileAppBuild build,
            CancellationToken cancellationToken = default
        )
        {
            using var response = await GetArtifactResponseAsync(build.ArtifactUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // CDN 或 EAS artifact 传播可能短暂返回 403/429/5xx；这类失败允许 worker 重试，不能直接拉黑 latest。
                throw new MobileAppBuildArtifactMirrorException(
                    $"APK 下载地址返回 HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                    isDownloadUnsafe: false
                );
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (IsRejectedApkContentType(mediaType))
            {
                throw new MobileAppBuildArtifactMirrorException(
                    $"APK 下载地址返回了异常文件类型: {mediaType}",
                    isDownloadUnsafe: true
                );
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength == null)
            {
                throw new MobileAppBuildArtifactMirrorException(
                    "APK 下载地址缺少 Content-Length，无法校验文件大小",
                    isDownloadUnsafe: true
                );
            }
            if (contentLength <= 0)
            {
                throw new MobileAppBuildArtifactMirrorException(
                    "APK 下载地址返回空文件",
                    isDownloadUnsafe: true
                );
            }
            if (contentLength > MaxApkBytes)
            {
                throw new MobileAppBuildArtifactMirrorException(
                    $"APK 文件超过 {MaxApkBytes / 1024 / 1024}MB 限制",
                    isDownloadUnsafe: true
                );
            }

            var objectKey = BuildObjectKey(build);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var upload = await _uploadService.UploadStreamAsync(
                objectKey,
                ApkContentType,
                stream,
                contentLength,
                cancellationToken
            );

            if (!upload.Success || upload.Data == null || string.IsNullOrWhiteSpace(upload.Data.DownloadUrl))
            {
                throw new InvalidOperationException(upload.Message ?? "上传 APK 到腾讯云 COS 失败");
            }

            _logger.LogInformation(
                "APK 已镜像到腾讯云 COS，EasBuildId: {EasBuildId}, ObjectKey: {ObjectKey}",
                build.EasBuildId,
                objectKey
            );

            return new MobileAppBuildArtifactMirrorResult
            {
                ArtifactUrl = upload.Data.DownloadUrl,
                ObjectKey = upload.Data.ObjectKey,
                MirroredAt = DateTime.UtcNow,
            };
        }

        private async Task<HttpResponseMessage> GetArtifactResponseAsync(
            string artifactUrl,
            CancellationToken cancellationToken
        )
        {
            if (!Uri.TryCreate(artifactUrl, UriKind.Absolute, out var nextUri))
            {
                throw new MobileAppBuildArtifactMirrorException(
                    "APK 下载地址不是有效 URL",
                    isDownloadUnsafe: true
                );
            }

            for (var redirectCount = 0; redirectCount <= MaxRedirects; redirectCount++)
            {
                EnsureAllowedArtifactUri(nextUri);
                using var request = new HttpRequestMessage(HttpMethod.Get, nextUri);
                var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken
                );

                if (!IsRedirectStatusCode(response.StatusCode))
                {
                    return response;
                }

                var location = response.Headers.Location;
                response.Dispose();
                if (location == null)
                {
                    throw new MobileAppBuildArtifactMirrorException(
                        "APK 下载地址返回重定向但缺少 Location",
                        isDownloadUnsafe: true
                    );
                }

                var redirectUri = location.IsAbsoluteUri ? location : new Uri(nextUri, location);
                EnsureAllowedArtifactUri(redirectUri);
                nextUri = redirectUri;
            }

            throw new MobileAppBuildArtifactMirrorException(
                $"APK 下载地址重定向次数超过 {MaxRedirects} 次",
                isDownloadUnsafe: true
            );
        }

        private static void EnsureAllowedArtifactUri(Uri uri)
        {
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !IsAllowedArtifactHost(uri.Host))
            {
                throw new MobileAppBuildArtifactMirrorException(
                    $"不允许的 APK 下载域名: {uri.Host}",
                    isDownloadUnsafe: true
                );
            }
        }

        public static bool IsAllowedArtifactHost(string host)
        {
            var normalized = host.Trim().ToLowerInvariant();
            // EAS 初始 artifact 使用 expo.dev，实际文件可能经 CDN 或云存储域名跳转。
            return normalized == "expo.dev"
                || normalized.EndsWith(".expo.dev", StringComparison.Ordinal)
                // EAS 2026 构建产物会从 expo.dev 跳转到 wf-artifacts.eascdn.net。
                || normalized == "eascdn.net"
                || normalized.EndsWith(".eascdn.net", StringComparison.Ordinal)
                || normalized == "storage.googleapis.com"
                || normalized.EndsWith(".googleapis.com", StringComparison.Ordinal)
                || normalized.EndsWith(".cloudfront.net", StringComparison.Ordinal)
                || normalized.EndsWith(".amazonaws.com", StringComparison.Ordinal);
        }

        private static bool IsRedirectStatusCode(System.Net.HttpStatusCode statusCode)
        {
            var code = (int)statusCode;
            return code is >= 300 and <= 399;
        }

        private static string BuildObjectKey(MobileAppBuild build)
        {
            // COS 对象路径只使用 profile 和 buildId，确保同一 EAS build 重试时幂等覆盖同一个 APK。
            var profile = NormalizeObjectKeySegment(build.BuildProfile, "production");
            var buildId = NormalizeObjectKeySegment(build.EasBuildId, Guid.NewGuid().ToString("N"));
            return $"mobile-app-builds/{profile}/{buildId}.apk";
        }

        private static string NormalizeObjectKeySegment(string? value, string fallback)
        {
            var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            var chars = source
                .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-')
                .ToArray();
            var normalized = new string(chars).Trim('-', '.', '_');
            return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized.ToLowerInvariant();
        }

        private static bool IsRejectedApkContentType(string? mediaType)
        {
            if (string.IsNullOrWhiteSpace(mediaType))
            {
                return false;
            }

            var normalized = mediaType.Trim().ToLowerInvariant();
            return normalized.StartsWith("text/")
                || normalized is "application/json" or "application/xml" or "application/xhtml+xml";
        }
    }
}
