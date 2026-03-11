using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class FileUploadController : ControllerBase
    {
        private readonly TencentCloudUploadService _uploadService;
        private readonly ILogger<FileUploadController> _logger;

        public FileUploadController(
            TencentCloudUploadService uploadService,
            ILogger<FileUploadController> logger
        )
        {
            _uploadService = uploadService;
            _logger = logger;
        }

        [HttpPost("upload")]
        [RequestSizeLimit(2147483648L)]
        public async Task<IActionResult> UploadFile(
            IFormFile file,
            [FromForm] string? objectKey = null,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest(ApiResponse<UploadResult>.Error("请选择文件", "NO_FILE"));
                }

                _logger.LogInformation(
                    "开始上传文件，FileName: {FileName}, FileSize: {FileSize}MB, ObjectKey: {ObjectKey}",
                    file.FileName,
                    file.Length / (1024.0 * 1024.0),
                    objectKey ?? "(自动生成)"
                );

                var progress = new Progress<UploadProgress>();
                var fileStream = file.OpenReadStream();

                // 确保使用传入的 objectKey（包含目录路径）
                var result = await _uploadService.UploadFileAsync(
                    fileStream,
                    file.FileName,
                    file.ContentType,
                    objectKey, // 直接使用前端传入的 objectKey
                    progress,
                    cancellationToken
                );

                _logger.LogInformation(
                    "=== 文件上传成功调试 ===\nObjectKey: {ObjectKey}\nDownloadUrl: {DownloadUrl}\nFileSize: {FileSize}\n=====================",
                    result.Data?.ObjectKey,
                    result.Data?.DownloadUrl,
                    result.Data?.FileSize
                );

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "文件上传失败，ObjectKey: {ObjectKey}", objectKey);
                return StatusCode(
                    500,
                    ApiResponse<UploadResult>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
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
                    request.Parts
                );

                _logger.LogInformation(
                    "完成分片上传，ObjectKey: {ObjectKey}, Parts: {PartsCount}",
                    result.Data?.ObjectKey,
                    request.Parts.Count
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
