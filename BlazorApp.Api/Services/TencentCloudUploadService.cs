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
        private const long DirectUploadThreshold = 50 * 1024 * 1024;
        private const int DefaultChunkSize = 10 * 1024 * 1024;
        private const int DefaultConcurrentUploads = 4;
        private const int MaxRetryCount = 3;

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

        public async Task<ApiResponse<UploadResult>> UploadFileAsync(
            Stream fileStream,
            string fileName,
            string contentType,
            string? objectKey = null,
            IProgress<UploadProgress>? progress = null,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                var fileSize = fileStream.Length;
                objectKey ??= GenerateObjectKey(fileName);

                if (fileSize <= DirectUploadThreshold)
                {
                    return await UploadDirectAsync(
                        fileStream,
                        objectKey,
                        contentType,
                        progress,
                        cancellationToken
                    );
                }
                else
                {
                    return await UploadMultipartAsync(
                        fileStream,
                        objectKey,
                        contentType,
                        fileSize,
                        progress,
                        cancellationToken
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "文件上传失败，FileName: {FileName}", fileName);
                return ApiResponse<UploadResult>.Error("文件上传失败", "UPLOAD_FAILED", ex.Message);
            }
        }

        private async Task<ApiResponse<UploadResult>> UploadDirectAsync(
            Stream fileStream,
            string objectKey,
            string contentType,
            IProgress<UploadProgress>? progress,
            CancellationToken cancellationToken
        )
        {
            try
            {
                var url = GeneratePresignedUrl(objectKey, "PUT", 3600);
                var fileSize = fileStream.Length;

                progress?.Report(new UploadProgress { UploadedBytes = 0, TotalBytes = fileSize });

                using var content = new StreamContent(fileStream);
                content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

                var response = await _httpClient.PutAsync(url, content, cancellationToken);
                response.EnsureSuccessStatusCode();

                progress?.Report(
                    new UploadProgress { UploadedBytes = fileSize, TotalBytes = fileSize }
                );

                var downloadUrl = GeneratePublicUrl(objectKey);
                _logger.LogInformation("文件直接上传成功，ObjectKey: {ObjectKey}", objectKey);

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
                _logger.LogError(ex, "直接上传失败");
                throw;
            }
        }

        private async Task<ApiResponse<UploadResult>> UploadMultipartAsync(
            Stream fileStream,
            string objectKey,
            string contentType,
            long fileSize,
            IProgress<UploadProgress>? progress,
            CancellationToken cancellationToken
        )
        {
            string? uploadId = null;
            var etags = new List<(int PartNumber, string Etag)>();
            int chunkSize = DefaultChunkSize;
            int totalChunks = (int)Math.Ceiling((double)fileSize / chunkSize);

            try
            {
                uploadId = await InitiateMultipartUploadAsync(
                    objectKey,
                    contentType,
                    cancellationToken
                );
                _logger.LogInformation(
                    "初始化分块上传成功，UploadId: {UploadId}, TotalChunks: {TotalChunks}",
                    uploadId,
                    totalChunks
                );

                var buffer = new byte[chunkSize];
                int partNumber = 1;
                long uploadedBytes = 0;

                int bytesRead;
                while ((bytesRead = await fileStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    var chunkData = new byte[bytesRead];
                    Array.Copy(buffer, chunkData, bytesRead);

                    for (int attempt = 1; attempt <= MaxRetryCount; attempt++)
                    {
                        try
                        {
                            var etag = await UploadPartAsync(
                                uploadId,
                                objectKey,
                                partNumber,
                                chunkData,
                                cancellationToken
                            );
                            etags.Add((partNumber, etag));
                            uploadedBytes += bytesRead;
                            progress?.Report(
                                new UploadProgress
                                {
                                    UploadedBytes = uploadedBytes,
                                    TotalBytes = fileSize,
                                }
                            );
                            break;
                        }
                        catch (Exception ex) when (attempt < MaxRetryCount)
                        {
                            _logger.LogWarning(
                                ex,
                                "分块上传失败，第 {Attempt} 次重试，PartNumber: {PartNumber}",
                                attempt,
                                partNumber
                            );
                            await Task.Delay(
                                TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                                cancellationToken
                            );
                        }
                    }

                    partNumber++;
                }

                await CompleteMultipartUploadAsync(uploadId, objectKey, etags, cancellationToken);

                var downloadUrl = GeneratePublicUrl(objectKey);
                _logger.LogInformation("分块上传完成，ObjectKey: {ObjectKey}", objectKey);

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
                _logger.LogError(ex, "分块上传失败，尝试清理");
                if (uploadId != null)
                {
                    try
                    {
                        await AbortMultipartUploadAsync(uploadId, objectKey, cancellationToken);
                    }
                    catch (Exception abortEx)
                    {
                        _logger.LogError(abortEx, "清理分块上传失败");
                    }
                }
                throw;
            }
        }

        private async Task<string> UploadPartAsync(
            string uploadId,
            string objectKey,
            int partNumber,
            byte[] data,
            CancellationToken cancellationToken
        )
        {
            var url = GeneratePresignedUrl(objectKey, "PUT", 3600, uploadId, partNumber);
            using var content = new ByteArrayContent(data);
            var response = await _httpClient.PutAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();
            var etag = response.Headers.GetValues("ETag").FirstOrDefault() ?? string.Empty;
            return etag;
        }

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

        private async Task AbortMultipartUploadAsync(
            string uploadId,
            string objectKey,
            CancellationToken cancellationToken
        )
        {
            var url = GeneratePresignedUrl(objectKey, "DELETE", 3600, uploadId: uploadId);
            var response = await _httpClient.DeleteAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
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

            var httpUriWithQuery = string.IsNullOrEmpty(queryString)
                ? httpUri
                : $"{httpUri}?{queryString}";
            var httpUriString = Uri.EscapeDataString(httpUri);

            var headerString = "";
            var formatString = $"{httpString}\n{httpUriString}\n{queryString}\n{headerString}\n";
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

        private string GeneratePublicUrl(string objectKey)
        {
            return $"https://{_settings.BucketName}.cos.{_settings.Region}.myqcloud.com/{objectKey}";
        }

        private string GenerateObjectKey(string fileName)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var extension = Path.GetExtension(fileName);
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            return $"{nameWithoutExtension}_{timestamp}{extension}";
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
                        FileSize = 0,
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
            long expiresInSeconds
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
    }
}
