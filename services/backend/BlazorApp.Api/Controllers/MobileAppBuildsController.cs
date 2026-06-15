using System.Security.Cryptography;
using System.Text;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/mobile-app-builds")]
    [Authorize]
    public class MobileAppBuildsController : ControllerBase
    {
        private readonly IMobileAppBuildService _service;
        private readonly EasWebhookOptions _options;
        private readonly ILogger<MobileAppBuildsController> _logger;

        public MobileAppBuildsController(
            IMobileAppBuildService service,
            IOptions<EasWebhookOptions> options,
            ILogger<MobileAppBuildsController> logger
        )
        {
            _service = service;
            _options = options.Value;
            _logger = logger;
        }

        [HttpPost("eas-webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> EasWebhook()
        {
            using var memory = new MemoryStream();
            await Request.Body.CopyToAsync(memory);
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
        public async Task<IActionResult> Latest([FromQuery] string profile = "production")
        {
            var result = await _service.GetLatestAsync(profile);
            return Ok(result);
        }

        [HttpGet]
        [Authorize(Policy = Permissions.System.ViewAppDownloads)]
        public async Task<IActionResult> History([FromQuery] MobileAppBuildQueryDto query)
        {
            var result = await _service.GetHistoryAsync(query);
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
    }
}
