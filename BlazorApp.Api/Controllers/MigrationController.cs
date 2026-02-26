using BlazorApp.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize(Roles = "Admin")]
    public class MigrationController : ControllerBase
    {
        private readonly SqlSugarContext _context;
        private readonly ILogger<MigrationController> _logger;
        private readonly ILoggerFactory _loggerFactory;

        public MigrationController(
            SqlSugarContext context,
            ILogger<MigrationController> logger,
            ILoggerFactory loggerFactory
        )
        {
            _context = context;
            _logger = logger;
            _loggerFactory = loggerFactory;
        }

        /// <summary>
        /// 检查是否需要执行GUID主键迁移
        /// </summary>
        [HttpGet("check-migration")]
        public IActionResult CheckMigration()
        {
            try
            {
                var migration = new MigrationScripts(
                    _context.Db,
                    _logger as ILogger<MigrationScripts>,
                    _loggerFactory
                );
                var needsMigration = migration.NeedsMigration();

                return Ok(
                    new
                    {
                        success = true,
                        needsMigration = needsMigration,
                        message = needsMigration ? "需要执行GUID主键迁移" : "数据库已是最新结构",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查迁移状态失败");
                return StatusCode(
                    500,
                    new
                    {
                        success = false,
                        message = "检查迁移状态失败",
                        error = ex.Message,
                    }
                );
            }
        }

        /// <summary>
        /// 执行GUID主键迁移
        /// </summary>
        [HttpPost("execute-migration")]
        public async Task<IActionResult> ExecuteMigration()
        {
            try
            {
                var migration = new MigrationScripts(
                    _context.Db,
                    _logger as ILogger<MigrationScripts>,
                    _loggerFactory
                );

                if (!migration.NeedsMigration())
                {
                    return Ok(new { success = true, message = "数据库已是最新结构，无需迁移" });
                }

                await migration.MigrateToGuidPrimaryKeys();

                return Ok(new { success = true, message = "GUID主键迁移执行成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行迁移失败");
                return StatusCode(
                    500,
                    new
                    {
                        success = false,
                        message = "迁移执行失败",
                        error = ex.Message,
                    }
                );
            }
        }

        /// <summary>
        /// 强制重新创建所有表（清空数据）
        /// </summary>
        [HttpPost("force-recreate")]
        public async Task<IActionResult> ForceRecreate()
        {
            try
            {
                var migration = new MigrationScripts(
                    _context.Db,
                    _logger as ILogger<MigrationScripts>,
                    _loggerFactory
                );
                await migration.ForceRecreateAllTablesWithGuidKeys();

                // 重新创建索引
                _context.CreateIndexes();

                return Ok(new { success = true, message = "所有表强制重新创建完成（GUID主键）" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "强制重新创建表失败");
                return StatusCode(
                    500,
                    new
                    {
                        success = false,
                        message = "强制重新创建失败",
                        error = ex.Message,
                    }
                );
            }
        }

        /// <summary>
        /// 检查表结构状态
        /// </summary>
        [HttpGet("table-status")]
        public IActionResult GetTableStatus()
        {
            try
            {
                var sql =
                    @"
                    SELECT 
                        t.TABLE_NAME,
                        c.COLUMN_NAME,
                        c.DATA_TYPE,
                        c.IS_NULLABLE,
                        CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 'YES' ELSE 'NO' END as IS_PRIMARY_KEY
                    FROM INFORMATION_SCHEMA.TABLES t
                    LEFT JOIN INFORMATION_SCHEMA.COLUMNS c ON t.TABLE_NAME = c.TABLE_NAME
                    LEFT JOIN (
                        SELECT ku.TABLE_NAME, ku.COLUMN_NAME
                        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS AS tc
                        INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE AS ku
                            ON tc.CONSTRAINT_TYPE = 'PRIMARY KEY' 
                            AND tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                    ) pk ON c.TABLE_NAME = pk.TABLE_NAME AND c.COLUMN_NAME = pk.COLUMN_NAME
                    WHERE t.TABLE_TYPE = 'BASE TABLE'
                    AND t.TABLE_NAME IN ('User', 'Role', 'Store', 'UserRole', 'UserStore', 'RefreshToken', 'Product', 'Order', 'OrderItem', 'HqBranch', 'WarehouseCategory', 'WarehouseProduct')
                    ORDER BY t.TABLE_NAME, c.ORDINAL_POSITION";

                var result = _context.Db.Ado.SqlQuery<dynamic>(sql);

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取表结构状态失败");
                return StatusCode(
                    500,
                    new
                    {
                        success = false,
                        message = "获取表结构状态失败",
                        error = ex.Message,
                    }
                );
            }
        }
    }
}
