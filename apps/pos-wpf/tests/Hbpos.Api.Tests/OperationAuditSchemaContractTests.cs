using System.Reflection;
using BlazorApp.Shared.Models.POSM;
using SqlSugar;

namespace Hbpos.Api.Tests;

public sealed class OperationAuditSchemaContractTests
{
    [Fact]
    public void Operation_audit_preserves_database_owned_trust_level_column()
    {
        var property = typeof(PosOperationAudit).GetProperty("TrustLevel");

        Assert.NotNull(property);
        Assert.Equal(typeof(string), property!.PropertyType);

        var column = property.GetCustomAttribute<SugarColumn>();
        Assert.NotNull(column);
        Assert.Equal("trust_level", column!.ColumnName);
        Assert.Equal(40, column.Length);
        Assert.False(column.IsNullable);

        var audit = new PosOperationAudit();
        Assert.Equal("DeviceReportedUnverified", property.GetValue(audit));
    }
}
