using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using Microsoft.Data.Sqlite;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public class DomesticSetProductSchemaTests
{
    [Fact]
    public void EnsureDomesticSetProductNameColumn_旧表无损补充子项名称列()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = connection.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        db.Ado.ExecuteCommand(
            """
            CREATE TABLE [DomesticSetProduct] (
                [SetProductCode] varchar(50) NOT NULL PRIMARY KEY,
                [ProductCode] varchar(50) NOT NULL,
                [SetProductNo] varchar(50) NOT NULL
            )
            """
        );
        var context = CreateSqlSugarContext(db);
        var ensureMethod = typeof(SqlSugarContext).GetMethod(
            "EnsureDomesticSetProductNameColumn",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        Assert.NotNull(ensureMethod);
        ensureMethod!.Invoke(context, null);
        ensureMethod.Invoke(context, null);

        Assert.Contains(
            db.DbMaintenance.GetColumnInfosByTableName("DomesticSetProduct", false),
            column =>
                string.Equals(
                    column.DbColumnName,
                    "SetProductName",
                    StringComparison.OrdinalIgnoreCase
                )
        );
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(SqlSugarContext)
        );
        var dbField = typeof(SqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, db);
        return context;
    }
}
