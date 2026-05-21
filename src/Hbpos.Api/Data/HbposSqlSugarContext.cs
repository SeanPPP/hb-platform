using SqlSugar;

namespace Hbpos.Api.Data;

public sealed class HbposSqlSugarContext
{
    public HbposSqlSugarContext(IConfiguration configuration, ILogger<HbposSqlSugarContext> logger)
    {
        MainDb = CreateClient(configuration, logger, "MainConnection", "DefaultConnection");
        PosmDb = CreateClient(configuration, logger, "PosmConnection", "HBPOSMConnection");
    }

    public ISqlSugarClient MainDb { get; }

    public ISqlSugarClient PosmDb { get; }

    private static ISqlSugarClient CreateClient(
        IConfiguration configuration,
        ILogger logger,
        string primaryName,
        string fallbackName)
    {
        var connectionString = configuration.GetConnectionString(primaryName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = configuration.GetConnectionString(fallbackName);
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"缺少数据库连接字符串：ConnectionStrings:{primaryName}");
        }

        if (!connectionString.Contains("MultipleActiveResultSets", StringComparison.OrdinalIgnoreCase))
        {
            connectionString += ";MultipleActiveResultSets=True";
        }

        var client = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.SqlServer,
            InitKeyType = InitKeyType.Attribute,
            IsAutoCloseConnection = true,
            MoreSettings = new ConnMoreSettings
            {
                IsWithNoLockQuery = true
            }
        });

        client.Ado.CommandTimeOut = configuration.GetValue("Database:CommandTimeoutSeconds", 60);
        client.Aop.OnLogExecuting = (sql, _) =>
        {
            if (configuration.GetValue("Database:EnableSqlLogging", false))
            {
                logger.LogDebug("SQL: {Sql}", sql);
            }
        };

        return client;
    }
}
