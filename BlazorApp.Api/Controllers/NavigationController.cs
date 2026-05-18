using BlazorApp.Api.Services;
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

        public NavigationController(INavigationService navigationService)
        {
            _navigationService = navigationService;
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
    }
}
