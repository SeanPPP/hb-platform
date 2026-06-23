using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using BlazorApp.Api.Models;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlazorApp.Api.Services
{
    public class TencentCloudUploadService
    {
        private readonly TencentCloudSettings _settings;
        private readonly ILogger<TencentCloudUploadService> _logger;
        private readonly HttpClient _httpClient;
        private readonly TimeProvider _timeProvider;
        private const int DefaultChunkSize = 40 * 1024 * 1024;

        public TencentCloudUploadService(
            IOptions<TencentCloudSettings> settings,
            ILogger<TencentCloudUploadService> logger,
            HttpClient httpClient,
            TimeProvider? timeProvider = null
        )
        {
            _settings = settings.Value;
            _logger = logger;
            _httpClient = httpClient;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        private string GeneratePublicUrl(string objectKey)
        {
            return $"https://{_settings.BucketName}.cos.{_settings.Region}.myqcloud.com/{objectKey}";
        }

        private string ExtractUploadIdFromResponse(string response)
        {
            try
            {
                var doc = XDocument.Parse(response);
                var uploadIdElement = doc.Descendants("UploadId").FirstOrDefault();
                return uploadIdElement?.Value ?? string.Empty;
            }
            catch
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    response,
                    "<UploadId>(.*?)</UploadId>"
                );
                return match.Success ? match.Groups[1].Value : string.Empty;
            }
        }

        private static string HmacSha1(string key, string data)
        {
            using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToHexString(hash).ToLower();
        }

        private static string Sha1Hex(string input)
        {
            using var sha1 = SHA1.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha1.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLower();
        }

        #region 前端直传签名方法

        public DirectUploadSignature GetDirectUploadSignature(
            string objectKey,
            string contentType,
            long expiresInSeconds = 3600
        )
        {
            var url = GeneratePresignedPutUrl(objectKey, contentType, expiresInSeconds);
            return new DirectUploadSignature
            {
                Url = url,
                ObjectKey = objectKey,
                Headers = new Dictionary<string, string> { ["Content-Type"] = contentType },
            };
        }

        public List<PresignedUrlItem> GenerateBatchPresignedPutUrls(
            List<PresignedFileInfo> files,
            string directory = "YW200",
            int maxUrls = 1000,
            string? bucketName = null,
            string? region = null
        )
        {
            var bucket = bucketName ?? _settings.BucketName;
            var reg = region ?? _settings.Region;
            var results = new List<PresignedUrlItem>();
            var count = 0;

            foreach (var file in files)
            {
                if (count >= maxUrls)
                {
                    results.Add(new PresignedUrlItem
                    {
                        FileName = file.FileName,
                        FileSize = file.FileSize,
                        Error = $"超出最大文件数量限制（{maxUrls}个）"
                    });
                    continue;
                }

                try
                {
                    // 统一转为 jpg 后缀
                    var baseName = Path.GetFileNameWithoutExtension(file.FileName);
                    var objectKey = $"{directory}/{baseName}.jpg";
                    var host = $"{bucket}.cos.{reg}.myqcloud.com";
                    var url = GeneratePresignedPutUrl(objectKey, "image/jpeg", 7200, bucket, reg);
                    var downloadUrl = $"https://{host}/{objectKey}";

                    results.Add(new PresignedUrlItem
                    {
                        ObjectKey = objectKey,
                        PresignedUrl = url,
                        DownloadUrl = downloadUrl,
                        FileName = file.FileName,
                        FileSize = file.FileSize,
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new PresignedUrlItem
                    {
                        FileName = file.FileName,
                        FileSize = file.FileSize,
                        Error = ex.Message
                    });
                }

                count++;
            }

            return results;
        }

        public async Task<ApiResponse<MultipartUploadInit>> InitiateMultipartUploadForClientAsync(
            string objectKey,
            string contentType,
            long fileSize,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                var uploadId = await InitiateMultipartUploadAsync(
                    objectKey,
                    contentType,
                    cancellationToken
                );
                var chunkSize = DefaultChunkSize;
                var totalChunks = (int)Math.Ceiling((double)fileSize / chunkSize);

                return ApiResponse<MultipartUploadInit>.OK(
                    new MultipartUploadInit
                    {
                        UploadId = uploadId,
                        ObjectKey = objectKey,
                        ChunkSize = chunkSize,
                        TotalChunks = totalChunks,
                    },
                    "分块上传初始化成功"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化分块上传失败，ObjectKey: {ObjectKey}", objectKey);
                return ApiResponse<MultipartUploadInit>.Error(
                    "初始化分块上传失败",
                    "INIT_MULTIPART_FAILED",
                    ex.Message
                );
            }
        }

        public PartUploadSignature GetPartUploadSignature(
            string objectKey,
            string uploadId,
            int partNumber,
            int expiresInSeconds = 3600
        )
        {
            var url = GeneratePresignedUrl(
                objectKey,
                "PUT",
                expiresInSeconds,
                uploadId,
                partNumber
            );
            return new PartUploadSignature { Url = url, PartNumber = partNumber };
        }

        public async Task<ApiResponse<UploadResult>> CompleteMultipartUploadForClientAsync(
            string objectKey,
            string uploadId,
            List<PartETag> parts,
            long fileSize,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                var etagList = parts
                    .OrderBy(p => p.PartNumber)
                    .Select(p => (p.PartNumber, p.ETag))
                    .ToList();

                await CompleteMultipartUploadAsync(
                    uploadId,
                    objectKey,
                    etagList,
                    cancellationToken
                );

                var downloadUrl = GeneratePublicUrl(objectKey);
                return ApiResponse<UploadResult>.OK(
                    new UploadResult
                    {
                        ObjectKey = objectKey,
                        DownloadUrl = downloadUrl,
                        FileSize = fileSize,
                    },
                    "文件上传成功"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "完成分块上传失败，ObjectKey: {ObjectKey}, UploadId: {UploadId}",
                    objectKey,
                    uploadId
                );
                return ApiResponse<UploadResult>.Error(
                    "完成分块上传失败",
                    "COMPLETE_MULTIPART_FAILED",
                    ex.Message
                );
            }
        }

        public async Task<ApiResponse<UploadResult>> UploadStreamAsync(
            string objectKey,
            string contentType,
            Stream content,
            long? fileSize = null,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_settings.SecretId)
                    || string.IsNullOrWhiteSpace(_settings.SecretKey)
                    || string.IsNullOrWhiteSpace(_settings.BucketName)
                    || string.IsNullOrWhiteSpace(_settings.Region))
                {
                    return ApiResponse<UploadResult>.Error("腾讯云主桶配置不完整", "COS_NOT_CONFIGURED");
                }

                var url = GeneratePresignedPutUrl(objectKey, contentType, 3600);
                using var request = new HttpRequestMessage(HttpMethod.Put, url);
                request.Content = new StreamContent(content);
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                if (fileSize is > 0)
                {
                    request.Content.Headers.ContentLength = fileSize.Value;
                }

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var downloadUrl = GeneratePublicUrl(objectKey);
                return ApiResponse<UploadResult>.OK(
                    new UploadResult
                    {
                        ObjectKey = objectKey,
                        DownloadUrl = downloadUrl,
                        FileSize = fileSize ?? (content.CanSeek ? content.Length : 0),
                    },
                    "文件上传成功"
                );
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 关键位置：后台任务停机取消必须向上传递，不能被包装成 COS_UPLOAD_FAILED 后回写为镜像失败。
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "服务端上传文件到 COS 失败，ObjectKey: {ObjectKey}", objectKey);
                return ApiResponse<UploadResult>.Error(
                    "服务端上传文件到 COS 失败",
                    "COS_UPLOAD_FAILED",
                    ex.Message
                );
            }
        }

        private string GeneratePresignedPutUrl(
            string objectKey,
            string contentType,
            long expiresInSeconds,
            string? bucketName = null,
            string? region = null
        )
        {
            var bucket = bucketName ?? _settings.BucketName;
            var regionValue = region ?? _settings.Region;
            var secretId = _settings.SecretId;
            var secretKey = _settings.SecretKey;

            var host = $"{bucket}.cos.{regionValue}.myqcloud.com";
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var startTimestamp = new DateTimeOffset(now).ToUnixTimeSeconds();
            var expiredTimestamp = new DateTimeOffset(
                now.AddSeconds(expiresInSeconds)
            ).ToUnixTimeSeconds();

            var keyTime = $"{startTimestamp};{expiredTimestamp}";
            var path = BuildCosCanonicalPath(objectKey);
            var (headerList, httpHeaders) = BuildCosHeaderSignatureParts(
                new Dictionary<string, string>
                {
                    ["content-type"] = contentType,
                    ["host"] = host,
                }
            );
            var authorization = GenerateCosAuthorization(
                "put",
                path,
                "",
                httpHeaders,
                keyTime,
                secretId,
                secretKey,
                headerList,
                ""
            );

            return $"https://{host}{BuildCosUrlPath(objectKey)}?{BuildCosAuthorizationQuery(authorization)}";
        }

        #endregion

        #region 内部辅助方法

        private async Task<string> InitiateMultipartUploadAsync(
            string objectKey,
            string contentType,
            CancellationToken cancellationToken
        )
        {
            var bucket = _settings.BucketName;
            var region = _settings.Region;
            var host = $"{bucket}.cos.{region}.myqcloud.com";
            var url = GeneratePresignedUrl(objectKey, "POST", 3600, multipart: true);

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Host", host);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var uploadId = ExtractUploadIdFromResponse(responseBody);

            _logger.LogInformation(
                "初始化分块上传成功，uploadId: {UploadId}, objectKey: {ObjectKey}",
                uploadId,
                objectKey
            );

            return uploadId;
        }

        private async Task CompleteMultipartUploadAsync(
            string uploadId,
            string objectKey,
            List<(int PartNumber, string Etag)> etags,
            CancellationToken cancellationToken
        )
        {
            var url = GeneratePresignedUrl(
                objectKey,
                "POST",
                3600,
                uploadId: uploadId,
                complete: true
            );

            var partsXml = new StringBuilder();
            partsXml.Append("<CompleteMultipartUpload>");
            foreach (var (partNumber, etag) in etags.OrderBy(e => e.PartNumber))
            {
                partsXml.Append(
                    $"<Part><PartNumber>{partNumber}</PartNumber><ETag>{etag}</ETag></Part>"
                );
            }
            partsXml.Append("</CompleteMultipartUpload>");

            var content = new StringContent(partsXml.ToString(), Encoding.UTF8, "application/xml");
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        private string GeneratePresignedUrl(
            string objectKey,
            string method,
            int expiresInSeconds,
            string? uploadId = null,
            int partNumber = 0,
            bool multipart = false,
            bool complete = false
        )
        {
            var bucket = _settings.BucketName;
            var region = _settings.Region;
            var secretId = _settings.SecretId;
            var secretKey = _settings.SecretKey;

            var host = $"{bucket}.cos.{region}.myqcloud.com";
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var startTimestamp = new DateTimeOffset(now).ToUnixTimeSeconds();
            var expiredTimestamp = new DateTimeOffset(
                now.AddSeconds(expiresInSeconds)
            ).ToUnixTimeSeconds();
            var keyTime = $"{startTimestamp};{expiredTimestamp}";
            var operationParameters = new List<(string Key, string Value)>();

            if (multipart)
            {
                operationParameters.Add(("uploads", ""));
            }
            else if (!string.IsNullOrEmpty(uploadId))
            {
                if (partNumber > 0)
                {
                    operationParameters.Add(("partNumber", partNumber.ToString()));
                    operationParameters.Add(("uploadId", uploadId));
                }
                else
                {
                    operationParameters.Add(("uploadId", uploadId));
                }
            }

            var path = BuildCosCanonicalPath(objectKey);
            var canonicalParameters = BuildCosCanonicalQueryParameters(operationParameters);
            var urlParamList = BuildCosQueryParameterList(operationParameters);
            var (headerList, httpHeaders) = BuildCosHeaderSignatureParts(
                new Dictionary<string, string> { ["host"] = host }
            );
            var authorization = GenerateCosAuthorization(
                method,
                path,
                canonicalParameters,
                httpHeaders,
                keyTime,
                secretId,
                secretKey,
                headerList,
                urlParamList
            );
            var queryParts = new List<string>();
            var operationQuery = BuildCosActualQueryParameters(operationParameters);
            if (!string.IsNullOrEmpty(operationQuery))
            {
                queryParts.Add(operationQuery);
            }
            queryParts.Add(BuildCosAuthorizationQuery(authorization));

            return $"https://{host}{BuildCosUrlPath(objectKey)}?{string.Join("&", queryParts)}";
        }

        private static string GenerateCosAuthorization(
            string method,
            string canonicalPath,
            string canonicalParameters,
            string canonicalHeaders,
            string keyTime,
            string secretId,
            string secretKey,
            string headerList,
            string urlParamList
        )
        {
            // 腾讯云 COS XML API 签名只支持 sha1；这里和官方 HttpString 格式保持一致。
            var httpString =
                $"{method.ToLowerInvariant()}\n{canonicalPath}\n{canonicalParameters}\n{canonicalHeaders}\n";
            var stringToSign = $"sha1\n{keyTime}\n{Sha1Hex(httpString)}\n";
            var signKey = HmacSha1(secretKey, keyTime);
            var signature = HmacSha1(signKey, stringToSign);

            return string.Join(
                "&",
                "q-sign-algorithm=sha1",
                $"q-ak={secretId}",
                $"q-sign-time={keyTime}",
                $"q-key-time={keyTime}",
                $"q-header-list={headerList}",
                $"q-url-param-list={urlParamList}",
                $"q-signature={signature}"
            );
        }

        private static string BuildCosAuthorizationQuery(string authorization)
        {
            return string.Join(
                "&",
                authorization
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part =>
                    {
                        var index = part.IndexOf('=');
                        if (index < 0)
                        {
                            return CosUrlEncode(part);
                        }

                        var key = part[..index];
                        var value = part[(index + 1)..];
                        return $"{CosUrlEncode(key)}={CosUrlEncode(value)}";
                    })
            );
        }

        private static (string HeaderList, string HttpHeaders) BuildCosHeaderSignatureParts(
            IReadOnlyDictionary<string, string> headers
        )
        {
            var normalized = headers
                .Select(pair => new
                {
                    Key = CosUrlEncode(pair.Key.Trim().ToLowerInvariant()),
                    Value = CosUrlEncode(pair.Value.Trim()),
                })
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToList();

            return (
                string.Join(";", normalized.Select(pair => pair.Key)),
                string.Join("&", normalized.Select(pair => $"{pair.Key}={pair.Value}"))
            );
        }

        private static string BuildCosCanonicalQueryParameters(
            IReadOnlyCollection<(string Key, string Value)> parameters
        )
        {
            if (parameters.Count == 0)
            {
                return "";
            }

            return string.Join(
                "&",
                parameters
                    .Select(pair => new
                    {
                        Key = CosUrlEncode(pair.Key.ToLowerInvariant()),
                        Value = CosUrlEncode(pair.Value),
                    })
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}={pair.Value}")
            );
        }

        private static string BuildCosQueryParameterList(
            IReadOnlyCollection<(string Key, string Value)> parameters
        )
        {
            return parameters.Count == 0
                ? ""
                : string.Join(
                    ";",
                    parameters
                        .Select(pair => CosUrlEncode(pair.Key.ToLowerInvariant()))
                        .Order(StringComparer.Ordinal)
                );
        }

        private static string BuildCosActualQueryParameters(
            IReadOnlyCollection<(string Key, string Value)> parameters
        )
        {
            return parameters.Count == 0
                ? ""
                : string.Join(
                    "&",
                    parameters.Select(pair => $"{CosUrlEncode(pair.Key)}={CosUrlEncode(pair.Value)}")
                );
        }

        private static string BuildCosCanonicalPath(string objectKey)
        {
            return "/" + objectKey.TrimStart('/');
        }

        private static string BuildCosUrlPath(string objectKey)
        {
            return "/"
                + string.Join(
                    "/",
                    objectKey
                        .TrimStart('/')
                        .Split('/', StringSplitOptions.None)
                        .Select(CosUrlEncode)
                );
        }

        private static string CosUrlEncode(string value)
        {
            return Uri.EscapeDataString(value);
        }

        #endregion
    }
}
