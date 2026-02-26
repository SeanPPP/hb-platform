using SqlSugar;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace BlazorApp.Api.Services
{
    public interface IPostgreSqlService
    {
        ISqlSugarClient GetClient();
        Task<bool> TestConnectionAsync();
        Task<List<T>> QueryAsync<T>(string sql, object? parameters = null);
        Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? parameters = null);
        Task<int> ExecuteAsync(string sql, object? parameters = null);
    }
    
    public class PostgreSqlService : IPostgreSqlService
    {
        private readonly ILogger<PostgreSqlService> _logger;
        private readonly ISqlSugarClient _sqlSugarClient;
        
        public PostgreSqlService(ILogger<PostgreSqlService> logger, IConfiguration configuration)
        {
            _logger = logger;
            
            var connectionString = configuration.GetConnectionString("PostgresConnection");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentException("PostgreSQL连接字符串未配置");
            }
            
            _sqlSugarClient = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = connectionString,
                DbType = DbType.PostgreSQL,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
                ConfigureExternalServices = new ConfigureExternalServices
                {
                    EntityNameService = (type, entity) =>
                    {
                        // 表名转换为小写
                        entity.DbTableName = entity.DbTableName.ToLower();
                    },
                    EntityService = (property, column) =>
                    {
                        // 列名转换为小写
                        column.DbColumnName = column.DbColumnName.ToLower();
                    }
                },
                MoreSettings = new ConnMoreSettings
                {
                    PgSqlIsAutoToLower = true, // PostgreSQL自动转小写
                    IsAutoRemoveDataCache = true
                }
            });
            
            // 配置日志
            _sqlSugarClient.Aop.OnLogExecuting = (sql, pars) =>
            {
                _logger.LogInformation("执行SQL: {Sql}", sql);
                if (pars != null && pars.Length > 0)
                {
                    _logger.LogInformation("参数: {Parameters}", string.Join(", ", pars.Select(p => $"{p.ParameterName}={p.Value}")));
                }
            };
            
            _sqlSugarClient.Aop.OnError = (exp) =>
            {
                _logger.LogError(exp, "SQL执行错误: {Message}", exp.Message);
            };
        }
        
        public ISqlSugarClient GetClient()
        {
            return _sqlSugarClient;
        }
        
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                await _sqlSugarClient.Ado.GetDataTableAsync("SELECT 1");
                _logger.LogInformation("PostgreSQL数据库连接测试成功");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PostgreSQL数据库连接测试失败");
                return false;
            }
        }
        
        public async Task<List<T>> QueryAsync<T>(string sql, object? parameters = null)
        {
            try
            {
                if (parameters != null)
                {
                    return await _sqlSugarClient.Ado.SqlQueryAsync<T>(sql, parameters);
                }
                return await _sqlSugarClient.Ado.SqlQueryAsync<T>(sql);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询数据失败: {Sql}", sql);
                throw;
            }
        }
        
        public async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? parameters = null)
        {
            try
            {
                var result = await QueryAsync<T>(sql, parameters);
                return result.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询单条数据失败: {Sql}", sql);
                throw;
            }
        }
        
        public async Task<int> ExecuteAsync(string sql, object? parameters = null)
        {
            try
            {
                if (parameters != null)
                {
                    return await _sqlSugarClient.Ado.ExecuteCommandAsync(sql, parameters);
                }
                return await _sqlSugarClient.Ado.ExecuteCommandAsync(sql);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行SQL失败: {Sql}", sql);
                throw;
            }
        }
        
        public void Dispose()
        {
            _sqlSugarClient?.Dispose();
        }
    }
}