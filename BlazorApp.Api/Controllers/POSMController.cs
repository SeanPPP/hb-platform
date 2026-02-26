using BlazorApp.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// POSM 数据库控制器
    /// 提供 POSM 数据库的基本操作和信息查询功能
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class POSMController : ControllerBase
    {
        private readonly POSMSqlSugarContext _posmContext;
        private readonly ILogger<POSMController> _logger;

        public POSMController(POSMSqlSugarContext posmContext, ILogger<POSMController> logger)
        {
            _posmContext = posmContext;
            _logger = logger;
        }

        /// <summary>
        /// 测试 POSM 数据库连接
        /// </summary>
        /// <returns></returns>
        [HttpGet("test-connection")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> TestConnection()
        {
            try
            {
                var isConnected = await _posmContext.TestConnectionAsync();

                if (isConnected)
                {
                    var version = await _posmContext.GetDatabaseVersionAsync();
                    var sizeInfo = await _posmContext.GetDatabaseSizeAsync();

                    return Ok(
                        new
                        {
                            success = true,
                            message = "POSM数据库连接成功",
                            data = new
                            {
                                connected = true,
                                version = version,
                                databaseName = sizeInfo.DatabaseName,
                                usedSpaceMB = sizeInfo.UsedSpaceMB,
                                allocatedSpaceMB = sizeInfo.AllocatedSpaceMB,
                            },
                        }
                    );
                }
                else
                {
                    return StatusCode(500, new { success = false, message = "POSM数据库连接失败" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "测试POSM数据库连接时发生错误");
                return StatusCode(
                    500,
                    new
                    {
                        success = false,
                        message = "测试POSM数据库连接时发生错误",
                        error = ex.Message,
                    }
                );
            }
        }

        /// <summary>
        /// 获取 POSM 数据库中所有表的信息
        /// </summary>
        /// <returns></returns>
        [HttpGet("tables")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> GetTables()
        {
            try
            {
                var tables = await _posmContext.GetTablesInfoAsync();

                return Ok(
                    new
                    {
                        success = true,
                        message = "获取POSM数据库表信息成功",
                        data = tables,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取POSM数据库表信息时发生错误");
                return StatusCode(
                    500,
                    new
                    {
                        success = false,
                        message = "获取POSM数据库表信息失败",
                        error = ex.Message,
                    }
                );
            }
        }

        /// <summary>
        /// 获取指定表的列信息
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <returns></returns>
        [HttpGet("tables/{tableName}/columns")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> GetTableColumns(string tableName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(tableName))
                {
                    return BadRequest(new { success = false, message = "表名不能为空" });
                }

                var columns = await _posmContext.GetTableColumnsAsync(tableName);

                return Ok(
                    new
                    {
                        success = true,
                        message = $"获取表 {tableName} 的列信息成功",
                        data = new { tableName = tableName, columns = columns },
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取表 {TableName} 列信息时发生错误", tableName);
                return StatusCode(
                    500,
                    new
                    {
                        success = false,
                        message = $"获取表 {tableName} 列信息失败",
                        error = ex.Message,
                    }
                );
            }
        }

        /// <summary>
        /// 执行自定义SQL查询
        /// </summary>
        /// <param name="request">查询请求</param>
        /// <returns></returns>
        [HttpPost("query")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ExecuteQuery([FromBody] QueryRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Sql))
                {
                    return BadRequest(new { success = false, message = "SQL语句不能为空" });
                }

                // 安全检查：只允许 SELECT 语句
                var sqlTrimmed = request.Sql.Trim().ToUpperInvariant();
                if (!sqlTrimmed.StartsWith("SELECT"))
                {
                    return BadRequest(
                        new { success = false, message = "出于安全考虑，只允许执行 SELECT 查询" }
                    );
                }

                var result = await _posmContext.QueryAsync<dynamic>(request.Sql);

                return Ok(
                    new
                    {
                        success = true,
                        message = "查询执行成功",
                        data = result,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行POSM数据库查询时发生错误: {Sql}", request.Sql);
                return StatusCode(
                    500,
                    new
                    {
                        success = false,
                        message = "查询执行失败",
                        error = ex.Message,
                    }
                );
            }
        }

        /// <summary>
        /// 初始化POSM数据库表
        /// </summary>
        /// <returns></returns>
        [HttpPost("initialize-tables")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> InitializeTables()
        {
            try
            {
                await _posmContext.InitializeTablesAsync();

                return Ok(new { success = true, message = "POSM数据库表初始化成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化POSM数据库表时发生错误");
                return StatusCode(
                    500,
                    new
                    {
                        success = false,
                        message = "初始化POSM数据库表失败",
                        error = ex.Message,
                    }
                );
            }
        }

        /// <summary>
        /// 强制重新创建所有表（危险操作）
        /// </summary>
        /// <returns></returns>
        [HttpPost("force-recreate-tables")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ForceRecreateTables()
        {
            try
            {
                await _posmContext.ForceRecreateTablesAsync();

                return Ok(
                    new
                    {
                        success = true,
                        message = "POSM数据库表强制重新创建成功",
                        warning = "此操作已删除所有现有数据",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "强制重新创建POSM数据库表时发生错误");
                return StatusCode(
                    500,
                    new
                    {
                        success = false,
                        message = "强制重新创建POSM数据库表失败",
                        error = ex.Message,
                    }
                );
            }
        }

        /// <summary>
        /// 获取数据库统计信息
        /// </summary>
        /// <returns></returns>
        [HttpGet("statistics")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> GetStatistics()
        {
            try
            {
                var tables = await _posmContext.GetTablesInfoAsync();
                var sizeInfo = await _posmContext.GetDatabaseSizeAsync();
                var version = await _posmContext.GetDatabaseVersionAsync();

                return Ok(
                    new
                    {
                        success = true,
                        message = "获取POSM数据库统计信息成功",
                        data = new
                        {
                            databaseInfo = new
                            {
                                name = sizeInfo.DatabaseName,
                                version = version,
                                usedSpaceMB = sizeInfo.UsedSpaceMB,
                                allocatedSpaceMB = sizeInfo.AllocatedSpaceMB,
                            },
                            tableCount = tables.Count,
                            tables = tables
                                .Select(t => new
                                {
                                    name = t.TableName,
                                    type = t.TableType,
                                    comment = t.TableComment,
                                })
                                .ToList(),
                        },
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取POSM数据库统计信息时发生错误");
                return StatusCode(
                    500,
                    new
                    {
                        success = false,
                        message = "获取数据库统计信息失败",
                        error = ex.Message,
                    }
                );
            }
        }
    }

    /// <summary>
    /// 查询请求模型
    /// </summary>
    public class QueryRequest
    {
        /// <summary>
        /// SQL查询语句
        /// </summary>
        public string Sql { get; set; } = string.Empty;
    }
}
