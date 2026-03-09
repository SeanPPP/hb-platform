using SqlSugar;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace BlazorApp.Api.Data
{
    public class MigrationScripts
    {
        private readonly ISqlSugarClient _db;
        private readonly ILogger<MigrationScripts> _logger;
        private readonly ILoggerFactory _loggerFactory;

        public MigrationScripts(ISqlSugarClient db, ILogger<MigrationScripts> logger, ILoggerFactory loggerFactory)
        {
            _db = db;
            _logger = logger;
            _loggerFactory = loggerFactory;
        }

        public bool NeedsMigration()
        {
            // 简化版本：检查新表是否存在
            var tablesToCheck = new[] { "Product", "Order", "OrderItem", "HqBranch", "WarehouseCategory", "DIC_仓库商品信息" };

            foreach (var tableName in tablesToCheck)
            {
                if (!_db.DbMaintenance.IsAnyTable(tableName))
                {
                    return true;
                }
            }

            return false;
        }

        public async Task MigrateToGuidPrimaryKeys()
        {
            // 创建新表
            _db.CodeFirst.InitTables(typeof(Product));

            _db.CodeFirst.InitTables(typeof(HqBranch));
            _db.CodeFirst.InitTables(typeof(WarehouseCategory));
            _db.CodeFirst.InitTables(typeof(WarehouseProduct));

            await Task.CompletedTask;
        }

        public async Task ForceRecreateAllTablesWithGuidKeys()
        {
            // 强制重新创建所有表
            var configuration = new ConfigurationBuilder().Build();
            var sqlSugarLogger = _loggerFactory.CreateLogger<SqlSugarContext>() ?? throw new InvalidOperationException("Failed to create logger for SqlSugarContext");
            var context = new SqlSugarContext(configuration, sqlSugarLogger);
            context.ForceRecreateAllTables();

            await Task.CompletedTask;
        }
    }
}