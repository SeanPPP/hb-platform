using BlazorApp.Api.Models;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class FileUploadController : ControllerBase
    {
        private readonly TencentCloudUploadService _uploadService;
        private readonly TencentCloudSettings _cosSettings;
        private readonly ILogger<FileUploadController> _logger;

        public FileUploadController(
            TencentCloudUploadService uploadService,
            IOptions<TencentCloudSettings> cosSettings,
            ILogger<FileUploadController> logger
        )
        {
            _uploadService = uploadService;
            _cosSettings = cosSettings.Value;
            _logger = logger;
        }

        [HttpGet("test")]
        public IActionResult TestUploadPermission()
        {
            try
            {
                return Ok(ApiResponse<object>.CreateSuccess("上传权限验证通过"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "权限测试失败");
                return StatusCode(
                    500,
                    ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        #region 前端直传签名接口

        [HttpPost("direct-upload-signature")]
        public ActionResult<ApiResponse<DirectUploadSignature>> GetDirectUploadSignature(
            [FromBody] DirectUploadRequest request
        )
        {
            try
            {
                if (string.IsNullOrEmpty(request.FileName))
                {
                    return BadRequest(
                        ApiResponse<DirectUploadSignature>.Error("文件名不能为空", "INVALID_REQUEST")
                    );
                }

                var objectKey =
                    request.ObjectKey
                    ?? $"PDA/{Path.GetFileNameWithoutExtension(request.FileName)}_{DateTime.Now:yyMMddHHmmss}{Path.GetExtension(request.FileName)}";

                var signature = _uploadService.GetDirectUploadSignature(
                    objectKey,
                    request.ContentType,
                    request.FileSize
                );

                _logger.LogInformation(
                    "生成直传签名，ObjectKey: {ObjectKey}, FileSize: {FileSize}",
                    objectKey,
                    request.FileSize
                );

                return Ok(ApiResponse<DirectUploadSignature>.OK(signature, "签名生成成功"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成直传签名失败");
                return StatusCode(
                    500,
                    ApiResponse<DirectUploadSignature>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        /// <summary>
        /// 批量生成COS直传签名URL（前端直传模式）
        /// </summary>
        [HttpPost("batch-presigned-urls")]
        public ActionResult<ApiResponse<BatchPresignedUrlResult>> GetBatchPresignedUrls(
            [FromBody] BatchPresignedUrlRequest request
        )
        {
            try
            {
                if (request.Files == null || request.Files.Count == 0)
                {
                    return BadRequest(
                        ApiResponse<BatchPresignedUrlResult>.Error("文件列表不能为空", "INVALID_REQUEST")
                    );
                }

                // 限制单次最大1000个文件
                const int MaxFilesPerBatch = 1000;
                if (request.Files.Count > MaxFilesPerBatch)
                {
                    return BadRequest(
                        ApiResponse<BatchPresignedUrlResult>.Error(
                            $"单次最多上传 {MaxFilesPerBatch} 个文件，当前提交了 {request.Files.Count} 个",
                            "TOO_MANY_FILES"
                        )
                    );
                }

                // 限制单文件大小 10MB
                const long MaxFileSizeBytes = 10 * 1024 * 1024; // const long is fine for compile-time constants
                var oversizedFiles = request.Files
                    .Where(f => f.FileSize > MaxFileSizeBytes)
                    .Select(f => f.FileName)
                    .ToList();
                if (oversizedFiles.Any())
                {
                    return BadRequest(
                        ApiResponse<BatchPresignedUrlResult>.Error(
                            $"以下文件超过10MB限制: {string.Join(", ", oversizedFiles)}",
                            "FILE_TOO_LARGE"
                        )
                    );
                }

                var results = _uploadService.GenerateBatchPresignedPutUrls(
                    request.Files,
                    directory: "YW200",
                    maxUrls: 1000,
                    bucketName: _cosSettings.ImageBucketName,
                    region: _cosSettings.ImageRegion
                );

                var successCount = results.Count(r => string.IsNullOrEmpty(r.Error));
                var failCount = results.Count - successCount;

                _logger.LogInformation(
                    "批量生成Presigned URL: 总数 {Total}, 成功 {Success}, 失败 {Fail}",
                    results.Count, successCount, failCount
                );

                return Ok(ApiResponse<BatchPresignedUrlResult>.OK(
                    new BatchPresignedUrlResult
                    {
                        Results = results,
                        SuccessCount = successCount,
                        FailCount = failCount,
                        Message = failCount == 0
                            ? $"全部成功，共 {successCount} 个"
                            : $"成功 {successCount} 个，失败 {failCount} 个"
                    },
                    "批量签名生成完成"
                ));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量生成Presigned URL失败");
                return StatusCode(
                    500,
                    ApiResponse<BatchPresignedUrlResult>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        [HttpPost("initiate-multipart")]
        public async Task<ActionResult<ApiResponse<MultipartUploadInit>>> InitiateMultipartUpload(
            [FromBody] DirectUploadRequest request
        )
        {
            try
            {
                if (string.IsNullOrEmpty(request.FileName))
                {
                    return BadRequest(
                        ApiResponse<MultipartUploadInit>.Error("文件名不能为空", "INVALID_REQUEST")
                    );
                }

                var objectKey =
                    request.ObjectKey
                    ?? $"PDA/{Path.GetFileNameWithoutExtension(request.FileName)}_{DateTime.Now:yyMMddHHmmss}{Path.GetExtension(request.FileName)}";

                var result = await _uploadService.InitiateMultipartUploadForClientAsync(
                    objectKey,
                    request.ContentType,
                    request.FileSize
                );

                _logger.LogInformation(
                    "初始化分片上传，ObjectKey: {ObjectKey}, UploadId: {UploadId}, TotalChunks: {TotalChunks}",
                    result.Data?.ObjectKey,
                    result.Data?.UploadId,
                    result.Data?.TotalChunks
                );

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化分片上传失败");
                return StatusCode(
                    500,
                    ApiResponse<MultipartUploadInit>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        [HttpPost("part-upload-signature")]
        public ActionResult<ApiResponse<PartUploadSignature>> GetPartUploadSignature(
            [FromBody] PartUploadRequest request
        )
        {
            try
            {
                if (
                    string.IsNullOrEmpty(request.ObjectKey)
                    || string.IsNullOrEmpty(request.UploadId)
                )
                {
                    return BadRequest(
                        ApiResponse<PartUploadSignature>.Error("参数不完整", "INVALID_REQUEST")
                    );
                }

                var signature = _uploadService.GetPartUploadSignature(
                    request.ObjectKey,
                    request.UploadId,
                    request.PartNumber
                );

                return Ok(ApiResponse<PartUploadSignature>.OK(signature, "分片签名生成成功"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成分片签名失败，PartNumber: {PartNumber}", request.PartNumber);
                return StatusCode(
                    500,
                    ApiResponse<PartUploadSignature>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        [HttpPost("complete-multipart")]
        public async Task<ActionResult<ApiResponse<UploadResult>>> CompleteMultipartUpload(
            [FromBody] CompleteMultipartRequest request
        )
        {
            try
            {
                if (
                    string.IsNullOrEmpty(request.ObjectKey)
                    || string.IsNullOrEmpty(request.UploadId)
                    || request.Parts == null
                    || request.Parts.Count == 0
                )
                {
                    return BadRequest(
                        ApiResponse<UploadResult>.Error("参数不完整", "INVALID_REQUEST")
                    );
                }

                var result = await _uploadService.CompleteMultipartUploadForClientAsync(
                    request.ObjectKey,
                    request.UploadId,
                    request.Parts,
                    request.FileSize
                );

                _logger.LogInformation(
                    "完成分片上传，ObjectKey: {ObjectKey}, Parts: {PartsCount}, FileSize: {FileSize}",
                    result.Data?.ObjectKey,
                    request.Parts.Count,
                    request.FileSize
                );

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "完成分片上传失败，ObjectKey: {ObjectKey}", request.ObjectKey);
                return StatusCode(
                    500,
                    ApiResponse<UploadResult>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        #endregion
    }
}
