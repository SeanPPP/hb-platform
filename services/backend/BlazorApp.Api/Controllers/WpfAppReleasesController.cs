using System.Security.Cryptography;
using System.Text;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/wpf-app-releases")]
    [Authorize]
    public class WpfAppReleasesController : ControllerBase
    {
        private const string CheckApiKeyHeaderName = "X-HBPOS-App-Update-Key";
        private const string DeviceIdHeaderName = "X-Device-Id";
        private const string AuthCodeHeaderName = "X-Auth-Code";

        private readonly IWpfAppReleaseService _service;
        private readonly IWpfAppReleaseTargetingService _targetingService;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;

        public WpfAppReleasesController(
            IWpfAppReleaseService service,
            IConfiguration configuration,
            IHostEnvironment environment,
            IWpfAppReleaseTargetingService targetingService
        )
        {
            _service = service;
            _configuration = configuration;
            _environment = environment;
            _targetingService = targetingService;
        }

        [HttpGet]
        [Authorize(Policy = Permissions.System.ViewAppDownloads)]
        public async Task<IActionResult> GetReleases([FromQuery] WpfAppReleaseQuery query)
        {
            var result = await _service.GetReleasesAsync(query);
            return Ok(result);
        }

        [HttpPost("upload/init")]
        [Authorize(Policy = Permissions.System.ManageAppDownloads)]
        public async Task<IActionResult> InitUpload(
            [FromBody] WpfAppReleaseUploadInitRequest request,
            CancellationToken cancellationToken
        )
        {
            var result = await _service.CreateUploadInitAsync(request, cancellationToken);
            return Ok(result);
        }

        [HttpPost]
        [Authorize(Policy = Permissions.System.ManageAppDownloads)]
        public async Task<IActionResult> CreateRelease([FromBody] WpfAppReleaseCreateRequest request)
        {
            var currentUser = User.Identity?.Name ?? "System";
            var result = await _service.CreateReleaseAsync(request, currentUser);
            return Ok(result);
        }

        [HttpPut("{id:guid}")]
        [Authorize(Policy = Permissions.System.ManageAppDownloads)]
        public async Task<IActionResult> UpdateRelease(
            Guid id,
            [FromBody] WpfAppReleaseUpdateRequest request
        )
        {
            var currentUser = User.Identity?.Name ?? "System";
            var result = await _service.UpdateReleaseAsync(id, request, currentUser);
            return Ok(result);
        }

        [HttpPost("policy/{channel}")]
        [Authorize(Policy = Permissions.System.ManageAppDownloads)]
        public async Task<IActionResult> SetPolicy(
            [FromRoute] string channel,
            [FromBody] WpfUpdatePolicyRequest request
        )
        {
            if (!TryNormalizeManagedChannel(channel, out var managedChannel))
            {
                return BadRequest(
                    ApiResponse<WpfUpdatePolicyDto>.Error(
                        "Only production and preview policy channels are supported.",
                        "WPF_POLICY_CHANNEL_INVALID"
                    )
                );
            }

            // 策略渠道只由路由决定，拒绝请求体伪造其他渠道后绕过后台当前页面的策略范围。
            request.Channel = managedChannel;
            var currentUser = User.Identity?.Name ?? "System";
            var result = await _targetingService.SetPolicyAsync(request, currentUser);
            return Ok(result);
        }

        [HttpGet("target-options/stores")]
        [Authorize(Policy = Permissions.System.ManageAppDownloads)]
        public async Task<IActionResult> GetTargetStoreOptions()
        {
            return Ok(await _targetingService.GetStoreOptionsAsync());
        }

        [HttpGet("target-options/devices")]
        [Authorize(Policy = Permissions.System.ManageAppDownloads)]
        public async Task<IActionResult> GetTargetDeviceOptions(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? keyword = null
        )
        {
            return Ok(await _targetingService.GetDeviceOptionsAsync(page, pageSize, keyword));
        }

        [HttpGet("check")]
        [AllowAnonymous]
        public async Task<IActionResult> Check(
            [FromQuery] string channel = "production",
            [FromQuery] string? currentVersion = null
        )
        {
            if (!IsCheckAuthorized())
            {
                return Unauthorized(
                    ApiResponse<WpfUpdateCheckResponse>.Error(
                        "WPF update check is not authorized.",
                        "APP_UPDATE_CHECK_UNAUTHORIZED"
                    )
                );
            }

            var result = await _targetingService.CheckUpdateAsync(
                channel,
                currentVersion,
                Request.Headers[DeviceIdHeaderName].FirstOrDefault(),
                Request.Headers[AuthCodeHeaderName].FirstOrDefault()
            );
            return Ok(result);
        }

        private bool IsCheckAuthorized()
        {
            var expectedApiKey = GetFirstNonBlankValue(
                _configuration["WpfAppUpdate:CheckApiKey"],
                _configuration["AppUpdate:CheckApiKey"],
                Environment.GetEnvironmentVariable("HBPOS_APP_UPDATE_CHECK_KEY")
            );
            if (expectedApiKey is null)
            {
                // 只有显式 Development 环境才允许无 key 放行，其他环境默认拒绝。
                return _environment.IsDevelopment();
            }

            if (!Request.Headers.TryGetValue(CheckApiKeyHeaderName, out var providedValues))
            {
                return false;
            }

            var providedApiKey = NormalizeOptional(providedValues.FirstOrDefault());
            return providedApiKey is not null && FixedTimeEquals(providedApiKey, expectedApiKey);
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static bool TryNormalizeManagedChannel(string? channel, out string normalizedChannel)
        {
            normalizedChannel = NormalizeOptional(channel)?.ToLowerInvariant() ?? string.Empty;
            return normalizedChannel is "production" or "preview";
        }

        private static string? GetFirstNonBlankValue(params string?[] values)
        {
            foreach (var value in values)
            {
                // 空白配置不能短路后备 key，必须继续向下取第一个有效值。
                var normalized = NormalizeOptional(value);
                if (normalized is not null)
                {
                    return normalized;
                }
            }

            return null;
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            var leftBytes = Encoding.UTF8.GetBytes(left);
            var rightBytes = Encoding.UTF8.GetBytes(right);
            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
    }
}
