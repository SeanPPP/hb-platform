using BlazorApp.Api.Services;
using BlazorApp.Api.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class NavigationController : ControllerBase
    {
        private readonly INavigationService _navigationService;
        private readonly IDeviceRegistrationService _deviceRegistrationService;

        public NavigationController(
            INavigationService navigationService,
            IDeviceRegistrationService deviceRegistrationService
        )
        {
            _navigationService = navigationService;
            _deviceRegistrationService = deviceRegistrationService;
        }

        /// <summary>
        /// 获取当前用户可见的导航菜单树
        /// </summary>
        [HttpGet("menu")]
        public IActionResult GetMenu()
        {
            var menu = _navigationService.BuildMenu(User);
            return Ok(menu);
        }

        /// <summary>
        /// 获取 Expo app 当前账号或设备可见的底部导航
        /// </summary>
        [HttpGet("app-menu")]
        [AllowAnonymous]
        public async Task<IActionResult> GetAppMenu()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return Ok(_navigationService.BuildAppMenu(User));
            }

            var hardwareId = Request.Headers["X-Device-Id"].FirstOrDefault();
            var authCode = Request.Headers["X-Auth-Code"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(hardwareId) || string.IsNullOrWhiteSpace(authCode))
            {
                return Unauthorized(new { success = false, message = "缺少设备授权信息" });
            }

            var isValid = await _deviceRegistrationService.ValidateDeviceAuthCodeAsync(
                hardwareId,
                authCode
            );

            if (!isValid)
            {
                return Unauthorized(new { success = false, message = "设备未授权" });
            }

            var device = await _deviceRegistrationService.GetDeviceByHardwareIdAsync(hardwareId);
            return Ok(_navigationService.BuildDeviceAppMenu(device?.设备类型));
        }
    }
}
