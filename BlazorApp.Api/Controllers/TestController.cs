using BlazorApp.Api.Services;
using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// 测试控制器 - 演示不同角色的授权功能
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class TestController : ControllerBase
    {
        private readonly SqlSugarContext _dbContext;

        public TestController(SqlSugarContext dbContext)
        {
            _dbContext = dbContext;
        }
        /// <summary>
        /// 公开接口 - 任何人都可以访问
        /// </summary>
        /// <returns>公开信息</returns>
        [HttpGet("public")]
        [AllowAnonymous]
        public IActionResult PublicEndpoint()
        {
            return Ok(new { message = "这是公开接口，无需认证", timestamp = DateTime.UtcNow });
        }

        /// <summary>
        /// 需要认证的接口 - 任何已登录用户都可以访问
        /// </summary>
        /// <returns>用户信息</returns>
        [HttpGet("authenticated")]
        [Authorize]
        public IActionResult AuthenticatedEndpoint()
        {
            var user = HttpContext.User;
            return Ok(
                new
                {
                    message = "这是需要认证的接口",
                    username = user.Identity?.Name,
                    claims = user.Claims.Select(c => new { c.Type, c.Value }),
                    timestamp = DateTime.UtcNow,
                }
            );
        }

        /// <summary>
        /// 管理员专用接口 - 只有Admin角色可以访问
        /// </summary>
        /// <returns>管理员信息</returns>
        [HttpGet("admin")]
        [Authorize(Roles = "Admin")]
        public IActionResult AdminEndpoint()
        {
            var user = HttpContext.User;
            return Ok(
                new
                {
                    message = "这是管理员专用接口",
                    username = user.Identity?.Name,
                    roles = user.Claims.Where(c => c.Type == "role").Select(c => c.Value),
                    timestamp = DateTime.UtcNow,
                }
            );
        }

        /// <summary>
        /// 管理员或经理接口 - Admin或Manager角色都可以访问
        /// </summary>
        /// <returns>管理信息</returns>
        [HttpGet("management")]
        [Authorize(Roles = "Admin,Manager")]
        public IActionResult ManagementEndpoint()
        {
            var user = HttpContext.User;
            return Ok(
                new
                {
                    message = "这是管理级接口",
                    username = user.Identity?.Name,
                    roles = user.Claims.Where(c => c.Type == "role").Select(c => c.Value),
                    timestamp = DateTime.UtcNow,
                }
            );
        }

        /// <summary>
        /// 获取当前用户信息
        /// </summary>
        /// <returns>当前用户详细信息</returns>
        [HttpGet("profile")]
        [Authorize]
        public IActionResult GetProfile()
        {
            var user = HttpContext.User;
            return Ok(
                new
                {
                    message = "当前用户信息",
                    username = user.Identity?.Name,
                    isAuthenticated = user.Identity?.IsAuthenticated,
                    authenticationType = user.Identity?.AuthenticationType,
                    claims = user.Claims.Select(c => new { c.Type, c.Value }),
                    timestamp = DateTime.UtcNow,
                }
            );
        }

        /// <summary>
        /// 测试JWT令牌生成（仅用于调试）
        /// </summary>
        /// <returns>测试令牌信息</returns>
        /// <remarks>
        /// 生成的令牌可以在Swagger中使用：
        /// 1. 点击"Authorize"按钮
        /// 2. 输入格式：Bearer + 空格 + 令牌值
        /// 3. 示例：Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
        /// 4. 注意：必须包含"Bearer"前缀和空格，不能使用大括号{}
        /// </remarks>
        [HttpGet("test-jwt")]
        [AllowAnonymous]
        public IActionResult TestJwtToken()
        {
            try
            {
                // 创建一个测试用户
                var testUser = new BlazorApp.Shared.Models.User
                {
                    UserGUID = Guid.NewGuid().ToString(),
                    Username = "testuser",
                    Email = "test@example.com",
                    Roles = new List<BlazorApp.Shared.Models.Role>
                    {
                        new BlazorApp.Shared.Models.Role
                        {
                            RoleGUID = Guid.NewGuid().ToString(),
                            RoleName = "Admin",
                        },
                    },
                };

                // 获取AuthService
                var authService = HttpContext.RequestServices.GetRequiredService<IAuthService>();

                // 生成JWT令牌
                var token = authService.GenerateJwtToken(testUser);

                return Ok(
                    new
                    {
                        message = "JWT令牌生成测试",
                        token = token,
                        user = new
                        {
                            testUser.UserGUID,
                            testUser.Username,
                            testUser.Email,
                            roles = testUser.Roles?.Select(r => r.RoleName),
                        },
                        timestamp = DateTime.UtcNow,
                    }
                );
            }
            catch (Exception ex)
            {
                return BadRequest(
                    new
                    {
                        message = "JWT令牌生成失败",
                        error = ex.Message,
                        stackTrace = ex.StackTrace,
                    }
                );
            }
        }

        [HttpGet("create-holiday-product-table")]
        [AllowAnonymous]
        public IActionResult CreateHolidayProductTable()
        {
            try
            {
                _dbContext.Db.CodeFirst.InitTables<HolidayProduct>();
                return Ok(new { message = "HolidayProduct 表创建成功" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "创建失败", error = ex.Message });
            }
        }
    }
}
