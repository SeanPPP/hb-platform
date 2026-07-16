using System.Data;
using System.Data.Common;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SqlSugarContextNoLockTransactionTests
{
    [Fact]
    public void ProductionSqlServerConfig_授权查询仅在事务外使用NoLock()
    {
        var context = CreateProductionSqlServerContext();

        var outsideTransactionSql = BuildAuthorizationSql(context);
        Assert.All(outsideTransactionSql, sql => Assert.True(ContainsNoLock(sql), sql));

        // 关键逻辑：不连接真实 SQL Server，仅注入 Serializable 事务来验证生产方言的 SQL 生成行为。
        var transaction = new Mock<DbTransaction>();
        transaction.SetupGet(item => item.IsolationLevel).Returns(IsolationLevel.Serializable);
        context.Db.Ado.Transaction = transaction.Object;
        var insideTransactionSql = BuildAuthorizationSql(context);

        Assert.All(insideTransactionSql, sql => Assert.False(ContainsNoLock(sql), sql));
    }

    private static SqlSugarContext CreateProductionSqlServerContext()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=127.0.0.1;Database=hb_platform_sql_generation;"
                    + "User Id=sa;Password=SqlOnly_123;TrustServerCertificate=True",
                ["Database:InitializeOnStartup"] = "false",
            })
            .Build();
        return new SqlSugarContext(
            configuration,
            NullLogger<SqlSugarContext>.Instance,
            Mock.Of<ICurrentUserService>()
        );
    }

    private static IReadOnlyList<string> BuildAuthorizationSql(SqlSugarContext context)
    {
        const string reviewerGuid = "reviewer-guid";
        const string targetGuid = "target-guid";
        var storeGuids = new[] { "store-guid" };
        var userGuids = new[] { targetGuid };

        return
        [
            context.Db.Queryable<UserStore, Store>((userStore, store) =>
                    userStore.StoreGUID == store.StoreGUID)
                .Where((userStore, store) =>
                    userStore.UserGUID == reviewerGuid
                    && !userStore.IsDeleted
                    && userStore.IsPrimary
                    && !store.IsDeleted)
                .ToSql().Key,
            context.Db.Queryable<UserStore>()
                .Where(userStore =>
                    userStore.UserGUID == targetGuid
                    && !userStore.IsDeleted
                    && storeGuids.Contains(userStore.StoreGUID))
                .ToSql().Key,
            context.Db.Queryable<UserRole, Role>((userRole, role) =>
                    userRole.RoleGUID == role.RoleGUID)
                .Where((userRole, role) =>
                    userGuids.Contains(userRole.UserGUID)
                    && !userRole.IsDeleted
                    && !role.IsDeleted)
                .ToSql().Key,
        ];
    }

    private static bool ContainsNoLock(string sql) =>
        sql.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Contains("WITH(NOLOCK)", StringComparison.OrdinalIgnoreCase);
}
