using System.Security.Cryptography;
using System.Text;
using BlazorApp.Api.Authentication;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/mobile-app-builds")]
    [Authorize]
    public class MobileAppBuildsController : ControllerBase
    {
        private const int MaxEasWebhookBodyBytes = 256 * 1024;
        private readonly IMobileAppBuildService _service;
        private readonly EasWebhookOptions _options;
        private readonly MobileAppBuildOptions _mobileAppBuildOptions;
        private readonly ILogger<MobileAppBuildsController> _logger;

        public MobileAppBuildsController(
            IMobileAppBuildService service,
            IOptions<EasWebhookOptions> options,
            ILogger<MobileAppBuildsController> logger,
            IOptions<MobileAppBuildOptions>? mobileAppBuildOptions = null
        )
        {
            _service = service;
            _options = options.Value;
            _mobileAppBuildOptions = mobileAppBuildOptions?.Value ?? new MobileAppBuildOptions();
            _logger = logger;
        }

        [HttpPost("eas-webhook")]
        [AllowAnonymous]
        [RequestSizeLimit(MaxEasWebhookBodyBytes)]
        public async Task<IActionResult> EasWebhook()
        {
            if (Request.ContentLength is > MaxEasWebhookBodyBytes)
            {
                _logger.LogWarning(
                    "EAS Webhook 请求体超过限制，ContentLength: {ContentLength}",
                    Request.ContentLength
                );
                return StatusCode(
                    StatusCodes.Status413PayloadTooLarge,
                    ApiResponse<object>.Error("请求体过大", "WEBHOOK_BODY_TOO_LARGE")
                );
            }

            using var memory = new MemoryStream();
            await Request.Body.CopyToAsync(memory);
            if (memory.Length > MaxEasWebhookBodyBytes)
            {
                _logger.LogWarning(
                    "EAS Webhook 读取后发现请求体超过限制，Length: {Length}",
                    memory.Length
                );
                return StatusCode(
                    StatusCodes.Status413PayloadTooLarge,
                    ApiResponse<object>.Error("请求体过大", "WEBHOOK_BODY_TOO_LARGE")
                );
            }

            var bodyBytes = memory.ToArray();

            if (!IsValidSignature(bodyBytes, Request.Headers["expo-signature"].FirstOrDefault()))
            {
                _logger.LogWarning("EAS Webhook 签名校验失败");
                return Unauthorized(ApiResponse<object>.Error("签名校验失败", "INVALID_SIGNATURE"));
            }

            // 签名必须基于原始字节；业务解析再按 UTF-8 解码，兼容带 BOM 的测试/代理请求。
            var body = Encoding.UTF8.GetString(bodyBytes).TrimStart('\uFEFF');
            var result = await _service.HandleEasWebhookAsync(body);
            return Ok(result);
        }

        [HttpGet("latest")]
        [Authorize(Policy = Permissions.System.ViewAppDownloads)]
        public async Task<IActionResult> Latest(
            [FromQuery] string? appKey = null,
            [FromQuery] string profile = "production"
        )
        {
            var normalizedAppKey = MobileAppKeys.Mobile;
            // appKey 未提供时保持 legacy mobile；已提供的值必须是受控分区键，避免误读 mobile 构建。
            if (appKey != null && !MobileAppKeys.TryNormalize(appKey, out normalizedAppKey))
            {
                return Ok(ApiResponse<MobileAppBuildDto?>.OK(null));
            }

            var result = await _service.GetLatestAsync(normalizedAppKey, profile);
            return Ok(result);
        }

        [HttpGet("android-latest")]
        [AllowAnonymous]
        public async Task<IActionResult> AndroidLatest(
            [FromQuery] string profile = "production",
            [FromQuery] string? integrity = null
        )
        {
            var normalizedProfile = NormalizePublicProfile(profile);
            if (
                normalizedProfile == null
                || !_mobileAppBuildOptions.PublicAndroidUpdatesEnabled
                || !string.Equals(integrity, "sha256-v1", StringComparison.Ordinal)
            )
            {
                // 旧端没有协商完整性能力时主动不投放，避免残缺 APK 继续触发系统安装循环。
                return Ok(ApiResponse<MobileAppBuildPublicDto?>.OK(null));
            }

            var result = await _service.GetLatestPublicMobileAndroidAsync(normalizedProfile);
            if (!result.Success)
            {
                return Ok(ApiResponse<MobileAppBuildPublicDto?>.Error(result.Message, result.ErrorCode, result.Details));
            }

            var latest = result.Data == null
                ? null
                : new MobileAppBuildPublicDto
                {
                    EasBuildId = result.Data.EasBuildId,
                    BuildProfile = result.Data.BuildProfile,
                    AppVersion = result.Data.AppVersion,
                    AppBuildVersion = result.Data.AppBuildVersion,
                    ArtifactUrl = result.Data.ArtifactUrl,
                    CosArtifactUrl = result.Data.CosArtifactUrl,
                    ArtifactSha256 = result.Data.ArtifactSha256,
                    ArtifactSize = result.Data.ArtifactSize,
                };

            return Ok(ApiResponse<MobileAppBuildPublicDto?>.OK(latest));
        }

        [HttpGet("android-latest/download")]
        [AllowAnonymous]
        public async Task<IActionResult> AndroidLatestDownload([FromQuery] string profile = "production")
        {
            var normalizedProfile = NormalizePublicProfile(profile);
            if (normalizedProfile == null)
            {
                // 匿名下载入口沿用同一精确白名单，不允许探测其他 development/internal 包。
                return NotFound(ApiResponse<object>.Error("未找到可下载的安装包", "APK_NOT_FOUND"));
            }

            var result = await _service.GetLatestAsync(normalizedProfile);
            if (!result.Success)
            {
                return BadRequest(ApiResponse<object>.Error(result.Message, result.ErrorCode, result.Details));
            }

            if (string.IsNullOrWhiteSpace(result.Data?.ArtifactUrl))
            {
                return NotFound(ApiResponse<object>.Error("未找到可下载的安装包", "APK_NOT_FOUND"));
            }

            // 每次点击都重新解析最新未过期地址，避免旧 OTA 弹窗里持有的 EAS artifact URL 过期。
            return Redirect(result.Data.ArtifactUrl);
        }

        [HttpGet("android/{easBuildId}/download")]
        [AllowAnonymous]
        public async Task<IActionResult> AndroidBuildDownload(
            string easBuildId,
            [FromQuery] string profile = "production"
        )
        {
            var normalizedProfile = NormalizePublicProfile(profile);
            if (
                normalizedProfile == null
                || !_mobileAppBuildOptions.PublicAndroidUpdatesEnabled
                || string.IsNullOrWhiteSpace(easBuildId)
            )
            {
                return NotFound(ApiResponse<object>.Error("未找到可下载的安装包", "APK_NOT_FOUND"));
            }

            var result = await _service.GetPublicMobileAndroidByBuildIdAsync(
                easBuildId,
                normalizedProfile
            );
            if (!result.Success)
            {
                return BadRequest(ApiResponse<object>.Error(result.Message, result.ErrorCode, result.Details));
            }

            if (string.IsNullOrWhiteSpace(result.Data?.CosArtifactUrl))
            {
                return NotFound(ApiResponse<object>.Error("未找到可下载的安装包", "APK_NOT_FOUND"));
            }

            // 绑定到已完成镜像的同一 build，拒绝 EAS 回退，避免元数据和下载字节不一致。
            return Redirect(result.Data.CosArtifactUrl);
        }

        [HttpGet("pos-handheld/android-latest")]
        [AllowAnonymous]
        public async Task<IActionResult> PosHandheldAndroidLatest(
            [FromQuery] string profile = "production"
        )
        {
            var normalizedProfile = NormalizePosHandheldPublicProfile(profile);
            if (normalizedProfile == null)
            {
                return Ok(ApiResponse<MobileAppBuildPublicDto?>.OK(null));
            }

            var result = await _service.GetLatestAsync(
                MobileAppKeys.PosHandheld,
                normalizedProfile
            );
            if (!result.Success)
            {
                return Ok(
                    ApiResponse<MobileAppBuildPublicDto?>.Error(
                        result.Message,
                        result.ErrorCode,
                        result.Details
                    )
                );
            }

            var latest = result.Data == null
                ? null
                : new MobileAppBuildPublicDto
                {
                    EasBuildId = result.Data.EasBuildId,
                    BuildProfile = result.Data.BuildProfile,
                    AppVersion = result.Data.AppVersion,
                    AppBuildVersion = result.Data.AppBuildVersion,
                    ArtifactUrl = result.Data.ArtifactUrl,
                    CosArtifactUrl = result.Data.CosArtifactUrl,
                };

            return Ok(ApiResponse<MobileAppBuildPublicDto?>.OK(latest));
        }

        [HttpGet("pos-handheld/android-latest/download")]
        [AllowAnonymous]
        public async Task<IActionResult> PosHandheldAndroidLatestDownload(
            [FromQuery] string profile = "production"
        )
        {
            var normalizedProfile = NormalizePosHandheldPublicProfile(profile);
            if (normalizedProfile == null)
            {
                return NotFound(
                    ApiResponse<object>.Error("未找到可下载的安装包", "APK_NOT_FOUND")
                );
            }

            var result = await _service.GetLatestAsync(
                MobileAppKeys.PosHandheld,
                normalizedProfile
            );
            if (!result.Success)
            {
                return BadRequest(
                    ApiResponse<object>.Error(result.Message, result.ErrorCode, result.Details)
                );
            }

            return string.IsNullOrWhiteSpace(result.Data?.ArtifactUrl)
                ? NotFound(ApiResponse<object>.Error("未找到可下载的安装包", "APK_NOT_FOUND"))
                : Redirect(result.Data.ArtifactUrl);
        }

        [HttpGet("pos-handheld/android/{easBuildId}/download")]
        [AllowAnonymous]
        public async Task<IActionResult> PosHandheldAndroidBuildDownload(
            string easBuildId,
            [FromQuery] string profile = "production"
        )
        {
            var normalizedProfile = NormalizePosHandheldPublicProfile(profile);
            if (normalizedProfile == null || string.IsNullOrWhiteSpace(easBuildId))
            {
                return NotFound(
                    ApiResponse<object>.Error("未找到可下载的安装包", "APK_NOT_FOUND")
                );
            }

            var result = await _service.GetByBuildIdAsync(
                MobileAppKeys.PosHandheld,
                easBuildId,
                normalizedProfile
            );
            if (!result.Success)
            {
                return BadRequest(
                    ApiResponse<object>.Error(result.Message, result.ErrorCode, result.Details)
                );
            }

            return string.IsNullOrWhiteSpace(result.Data?.ArtifactUrl)
                ? NotFound(ApiResponse<object>.Error("未找到可下载的安装包", "APK_NOT_FOUND"))
                : Redirect(result.Data.ArtifactUrl);
        }

        [HttpGet]
        [Authorize(Policy = Permissions.System.ViewAppDownloads)]
        public async Task<IActionResult> History([FromQuery] MobileAppBuildQueryDto query)
        {
            var result = await _service.GetHistoryAsync(query);
            return Ok(result);
        }

        [HttpGet("ota-updates")]
        [Authorize(Policy = Permissions.System.ViewAppDownloads)]
        public async Task<IActionResult> GetOtaUpdates([FromQuery] MobileAppOtaUpdateQueryDto query)
        {
            var result = await _service.GetOtaUpdatesAsync(query);
            return Ok(result);
        }

        [HttpPost("ota-updates")]
        [Authorize(
            AuthenticationSchemes = ServiceApiTokenAuthenticationDefaults.PolicyScheme,
            Policy = Permissions.System.ManageAppDownloads
        )]
        public async Task<IActionResult> UpsertOtaUpdate([FromBody] MobileAppOtaUpdateUpsertDto dto)
        {
            var result = await _service.UpsertOtaUpdateAsync(dto);
            return ToLegacyOtaMutationResult(result);
        }

        [HttpPost("ota-updates/{updateGroupId}/rollback-command")]
        [Authorize(Policy = Permissions.System.ManageAppDownloads)]
        public async Task<IActionResult> CreateOtaRollbackCommand(
            string updateGroupId,
            [FromBody] MobileAppOtaRollbackCommandDto? dto
        )
        {
            var result = await _service.CreateOtaRollbackCommandAsync(
                updateGroupId,
                dto ?? new MobileAppOtaRollbackCommandDto()
            );
            return ToLegacyOtaMutationResult(result);
        }

        private IActionResult ToLegacyOtaMutationResult<T>(ApiResponse<T> result)
        {
            if (
                !result.Success
                && result.ErrorCode
                    is AppOtaReleaseErrorCodes.FactConflict
                        or AppOtaReleaseErrorCodes.LegacyEndpointMigrated
            )
            {
                return Conflict(result);
            }

            // 保留旧端点其他校验错误的历史 200 + ApiResponse 合同。
            return Ok(result);
        }

        private bool IsValidSignature(byte[] bodyBytes, string? signatureHeader)
        {
            if (string.IsNullOrWhiteSpace(_options.Secret) || string.IsNullOrWhiteSpace(signatureHeader))
            {
                return false;
            }

            using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(_options.Secret));
            var computed = Convert.ToHexString(hmac.ComputeHash(bodyBytes)).ToLowerInvariant();
            var provided = signatureHeader.StartsWith("sha1=", StringComparison.OrdinalIgnoreCase)
                ? signatureHeader["sha1=".Length..]
                : signatureHeader;

            var computedBytes = Encoding.UTF8.GetBytes(computed);
            var providedBytes = Encoding.UTF8.GetBytes(provided.Trim().ToLowerInvariant());

            // 固定时间比较，避免签名逐字符比较带来的时间侧信道。
            return computedBytes.Length == providedBytes.Length
                && CryptographicOperations.FixedTimeEquals(computedBytes, providedBytes);
        }

        private static string? NormalizePublicProfile(string? profile)
        {
            var normalized = string.IsNullOrWhiteSpace(profile)
                ? "production"
                : profile.Trim().ToLowerInvariant();

            return normalized is "production" or "preview" ? normalized : null;
        }

        private static string? NormalizePosHandheldPublicProfile(string? profile)
        {
            var normalized = string.IsNullOrWhiteSpace(profile)
                ? "production"
                : profile.Trim().ToLowerInvariant();

            // pos-handheld 独立开放内部安装轨道，避免扩大旧 mobile 匿名路由的公开范围。
            return normalized is "production" or "preview" or "android-internal"
                ? normalized
                : null;
        }
    }
}
