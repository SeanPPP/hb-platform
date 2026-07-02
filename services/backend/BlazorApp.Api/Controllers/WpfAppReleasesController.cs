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

        private readonly IWpfAppReleaseService _service;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;

        public WpfAppReleasesController(
            IWpfAppReleaseService service,
            IConfiguration configuration,
            IHostEnvironment environment
        )
        {
            _service = service;
            _configuration = configuration;
            _environment = environment;
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

        [HttpPost("policy")]
        [Authorize(Policy = Permissions.System.ManageAppDownloads)]
        public async Task<IActionResult> SetPolicy([FromBody] WpfUpdatePolicyRequest request)
        {
            var currentUser = User.Identity?.Name ?? "System";
            var result = await _service.SetPolicyAsync(request, currentUser);
            return Ok(result);
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

            var result = await _service.CheckUpdateAsync(channel, currentVersion);
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
