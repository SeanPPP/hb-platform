using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BlazorApp.Api.Services;
using Microsoft.Extensions.Logging;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// PostgreSQL数据库测试控制器
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize]
    public class PostgreSqlTestController : ControllerBase
    {
        private readonly ILogger<PostgreSqlTestController> _logger;
        private readonly IPostgreSqlService _postgreSqlService;
        
        public PostgreSqlTestController(
            ILogger<PostgreSqlTestController> logger,
            IPostgreSqlService postgreSqlService)
        {
            _logger = logger;
            _postgreSqlService = postgreSqlService;
        }
        
        /// <summary>
        /// 测试PostgreSQL数据库连接
        /// </summary>
        /// <returns>连接测试结果</returns>
        [HttpGet("connection")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> TestConnection()
        {
            try
            {
                _logger.LogInformation("开始测试PostgreSQL数据库连接");
                
                var isConnected = await _postgreSqlService.TestConnectionAsync();
                
                if (isConnected)
                {
                    _logger.LogInformation("PostgreSQL数据库连接测试成功");
                    return Ok(new 
                    { 
                        success = true, 
                        message = "PostgreSQL数据库连接成功",
                        timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    _logger.LogWarning("PostgreSQL数据库连接测试失败");
                    return StatusCode(500, new 
                    { 
                        success = false, 
                        message = "PostgreSQL数据库连接失败",
                        timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "测试PostgreSQL连接时发生异常");
                return StatusCode(500, new 
                { 
                    success = false, 
                    message = "数据库连接测试异常：" + ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }
        
        /// <summary>
        /// 获取PostgreSQL版本信息
        /// </summary>
        /// <returns>数据库版本信息</returns>
        [HttpGet("version")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> GetVersion()
        {
            try
            {
                _logger.LogInformation("获取PostgreSQL版本信息");
                
                var versionInfo = await _postgreSqlService.QueryFirstOrDefaultAsync<string>("SELECT version()");
                
                return Ok(new 
                { 
                    success = true, 
                    data = new 
                    {
                        version = versionInfo,
                        database_type = "PostgreSQL"
                    },
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PostgreSQL版本信息时发生异常");
                return StatusCode(500, new 
                { 
                    success = false, 
                    message = "获取数据库版本失败：" + ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }
        
        /// <summary>
        /// 执行简单的PostgreSQL查询测试
        /// </summary>
        /// <returns>查询测试结果</returns>
        [HttpGet("query-test")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> QueryTest()
        {
            try
            {
                _logger.LogInformation("执行PostgreSQL查询测试");
                
                // 执行一个简单的查询测试
                var result = await _postgreSqlService.QueryAsync<dynamic>(@"
                    SELECT 
                        'Hello from PostgreSQL' as message,
                        NOW() as current_time,
                        CURRENT_DATABASE() as database_name,
                        CURRENT_USER as current_user
                ");
                
                return Ok(new 
                { 
                    success = true, 
                    data = result.FirstOrDefault(),
                    message = "PostgreSQL查询测试成功",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PostgreSQL查询测试时发生异常");
                return StatusCode(500, new 
                { 
                    success = false, 
                    message = "查询测试失败：" + ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }
        
        /// <summary>
        /// 获取数据库基本信息
        /// </summary>
        /// <returns>数据库基本信息</returns>
        [HttpGet("info")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> GetDatabaseInfo()
        {
            try
            {
                _logger.LogInformation("获取PostgreSQL数据库基本信息");
                
                // 获取数据库基本信息
                var dbInfo = await _postgreSqlService.QueryAsync<dynamic>(@"
                    SELECT 
                        CURRENT_DATABASE() as database_name,
                        CURRENT_USER as current_user,
                        CURRENT_SCHEMA() as current_schema,
                        INET_SERVER_ADDR() as server_address,
                        INET_SERVER_PORT() as server_port,
                        PG_POSTMASTER_START_TIME() as server_start_time
                ");
                
                // 获取数据库大小
                var dbSize = await _postgreSqlService.QueryFirstOrDefaultAsync<string>(@"
                    SELECT pg_size_pretty(pg_database_size(current_database()))
                ");
                
                var info = dbInfo.FirstOrDefault();
                
                return Ok(new 
                { 
                    success = true, 
                    data = new 
                    {
                        database_info = info,
                        database_size = dbSize,
                        connection_status = "Connected"
                    },
                    message = "获取数据库信息成功",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PostgreSQL数据库信息时发生异常");
                return StatusCode(500, new 
                { 
                    success = false, 
                    message = "获取数据库信息失败：" + ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }
    }
}