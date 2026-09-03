using BlazorApp.Api.Data.SchemaMigrations;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ContainerDetailQueryIndexMigrationTests
{
    [Fact]
    public void Migration_应注册为独立主库步骤并保持ArithabortOff可用()
    {
        Assert.Contains(
            SchemaMigrationCoordinator.MainMigrationSteps,
            step =>
                step.MigrationId
                == SchemaMigrationCoordinator.ContainerDetailQueryIndexesMigrationId
        );

        var sql = ContainerDetailQueryIndexSchema.ApplySql;
        Assert.Contains("SET ARITHABORT ON", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET NUMERIC_ROUNDABORT OFF", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "IX_ContainerDetail_ContainerCode_IsDeleted_ProductCode_All",
            sql,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "UX_Product_ProductCode_ContainerDetailLookup_All",
            sql,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "IX_Product_LocalSupplierCode_ItemNumber_ProductCode_All",
            sql,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "IX_DomesticProduct_SupplierCode_HBProductNo_IsDeleted_ProductCode_All",
            sql,
            StringComparison.Ordinal
        );
        Assert.Contains("CREATE UNIQUE NONCLUSTERED INDEX", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WHERE [IsDeleted] = 0", sql, StringComparison.OrdinalIgnoreCase);

        var verifySql = ContainerDetailQueryIndexSchema.VerifySql;
        Assert.Contains("i.has_filter = 0", verifySql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("i.is_disabled = 0", verifySql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("i.is_hypothetical = 0", verifySql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("i.is_unique = 1", verifySql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ic.key_ordinal = 4", verifySql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ic.is_included_column = 1", verifySql, StringComparison.OrdinalIgnoreCase);
    }
}
