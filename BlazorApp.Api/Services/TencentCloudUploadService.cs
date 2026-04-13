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
        private const int DefaultChunkSize = 40 * 1024 * 1024;

        public TencentCloudUploadService(
            IOptions<TencentCloudSettings> settings,
            ILogger<TencentCloudUploadService> logger,
            HttpClient httpClient
        )
        {
            _settings = settings.Value;
            _logger = logger;
            _httpClient = httpClient;
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

        private string HmacSha256(string key, string data)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToHexString(hash).ToLower();
        }

        private string Sha256Hex(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha256.ComputeHash(bytes);
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

            var host = $"{bucket}.cos.{region}.myqcloud.com";
            var now = DateTime.UtcNow;
            var startTimestamp = new DateTimeOffset(now).ToUnixTimeSeconds();
            var expiredTimestamp = new DateTimeOffset(
                now.AddSeconds(expiresInSeconds)
            ).ToUnixTimeSeconds();

            var keyTime = $"{startTimestamp};{expiredTimestamp}";
            var signKey = HmacSha256(secretKey, keyTime);

            var httpString = "put";
            var httpUri = $"/{objectKey}";
            var httpUriString = Uri.EscapeDataString(httpUri);

            var headerString = "content-type;host";
            var formatString = $"{httpString}\n{httpUriString}\n\n{headerString}\n";
            var formatStringSha256 = Sha256Hex(formatString);
            var stringToSign = $"sha256\n{keyTime}\n{formatStringSha256}\n";
            var signature = HmacSha256(signKey, stringToSign);

            var authorization =
                $"q-sign-algorithm=sha256&q-ak={secretId}&q-sign-time={keyTime}&q-key-time={keyTime}&q-header-list=content-type;host&q-url-param-list=&q-signature={signature}";

            return $"https://{host}/{objectKey}?authorization={Uri.EscapeDataString(authorization)}";
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
            var now = DateTime.UtcNow;
            var startTimestamp = new DateTimeOffset(now).ToUnixTimeSeconds();
            var expiredTimestamp = new DateTimeOffset(
                now.AddSeconds(expiresInSeconds)
            ).ToUnixTimeSeconds();

            var keyTime = $"{startTimestamp};{expiredTimestamp}";
            var signKey = HmacSha256(secretKey, keyTime);

            var httpString = method.ToLower();
            var httpUri = $"/{objectKey}";
            var queryString = "";

            if (multipart)
            {
                queryString = "uploads=";
            }
            else if (!string.IsNullOrEmpty(uploadId))
            {
                if (partNumber > 0)
                {
                    queryString =
                        $"partNumber={partNumber}&uploadId={Uri.EscapeDataString(uploadId)}";
                }
                else
                {
                    queryString = $"uploadId={Uri.EscapeDataString(uploadId)}";
                }
            }

            var headerString = "";
            var formatString = $"{httpString}\n{Uri.EscapeDataString(httpUri)}\n{queryString}\n{headerString}\n";
            var formatStringSha256 = Sha256Hex(formatString);
            var stringToSign = $"sha256\n{keyTime}\n{formatStringSha256}\n";
            var signature = HmacSha256(signKey, stringToSign);

            var authorization =
                $"q-sign-algorithm=sha256&q-ak={secretId}&q-sign-time={keyTime}&q-key-time={keyTime}&q-header-list=&q-url-param-list={(string.IsNullOrEmpty(queryString) ? "" : Uri.EscapeDataString(queryString.Split('=')[0]))}&q-signature={signature}";

            if (string.IsNullOrEmpty(queryString))
            {
                return $"https://{host}/{objectKey}?authorization={Uri.EscapeDataString(authorization)}";
            }
            return $"https://{host}/{objectKey}?{queryString}&authorization={Uri.EscapeDataString(authorization)}";
        }

        #endregion
    }
}
