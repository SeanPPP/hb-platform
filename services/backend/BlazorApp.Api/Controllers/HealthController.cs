using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// 健康检查控制器
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        private readonly ILogger<HealthController> _logger;

        public HealthController(ILogger<HealthController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 健康检查端点（无需认证）
        /// </summary>
        [HttpGet]
        public IActionResult Get()
        {
            _logger.LogInformation("健康检查被调用");
            return Ok(
                new
                {
                    status = "healthy",
                    timestamp = DateTime.UtcNow,
                    message = "API服务器正在正常运行",
                }
            );
        }

        /// <summary>
        /// 认证测试端点（需要认证）
        /// </summary>
        [HttpGet("auth")]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public IActionResult AuthTest()
        {
            _logger.LogInformation("认证测试被调用");
            var user = HttpContext.User;
            var claims = user.Claims.Select(c => new { c.Type, c.Value }).ToList();

            return Ok(
                new
                {
                    status = "authenticated",
                    timestamp = DateTime.UtcNow,
                    user = user.Identity?.Name,
                    claims = claims,
                }
            );
        }
    }
}
