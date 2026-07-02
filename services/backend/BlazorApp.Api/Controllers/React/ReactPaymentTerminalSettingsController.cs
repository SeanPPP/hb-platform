using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/payment-terminal-settings")]
    [Authorize]
    public class ReactPaymentTerminalSettingsController : ControllerBase
    {
        private readonly PaymentTerminalSettingsService _settingsService;
        private readonly ICurrentUserService _currentUserService;

        public ReactPaymentTerminalSettingsController(
            PaymentTerminalSettingsService settingsService,
            ICurrentUserService currentUserService
        )
        {
            _settingsService = settingsService;
            _currentUserService = currentUserService;
        }

        [HttpGet]
        [Authorize(Policy = Permissions.System.ManageSettings)]
        public async Task<IActionResult> Get(
            [FromQuery] string? storeCode,
            CancellationToken cancellationToken
        )
        {
            var result = await _settingsService.GetSettingsAsync(storeCode, cancellationToken);
            return Ok(result);
        }

        [HttpPut("square")]
        [Authorize(Policy = Permissions.System.ManageSettings)]
        public async Task<IActionResult> UpdateSquare(
            [FromBody] UpdateSquareTokenDto request,
            [FromQuery] string? storeCode,
            CancellationToken cancellationToken
        )
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse<object>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState));
            }

            var result = await _settingsService.UpdateSquareTokenAsync(
                request,
                _currentUserService.GetCurrentUsername(),
                storeCode,
                cancellationToken
            );
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpPut("linkly")]
        [Authorize(Policy = Permissions.System.ManageSettings)]
        public async Task<IActionResult> UpdateLinkly(
            [FromBody] UpdateLinklyCredentialDto request,
            CancellationToken cancellationToken
        )
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse<object>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState));
            }

            var result = await _settingsService.UpdateLinklyCredentialAsync(
                request,
                _currentUserService.GetCurrentUsername(),
                cancellationToken
            );
            return result.Success ? Ok(result) : BadRequest(result);
        }
    }
}
