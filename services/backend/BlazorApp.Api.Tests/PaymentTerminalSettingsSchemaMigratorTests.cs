using BlazorApp.Api.Services.React;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class PaymentTerminalSettingsSchemaMigratorTests
{
    [Fact]
    public void Scripts_ContainPaymentTerminalTablesAndEnvironmentConstraints()
    {
        var sql = string.Join("\n", PaymentTerminalSettingsSchemaMigrator.SqlScriptsForTests);

        Assert.Contains("POSM_SquareToken", sql);
        Assert.Contains("POSM_LinklyCloudCredential", sql);
        Assert.Contains("CK_POSM_SquareToken_Environment", sql);
        Assert.Contains("CK_POSM_LinklyCloudCredential_Environment", sql);
        Assert.Contains("N'Production'", sql);
        Assert.Contains("N'Sandbox'", sql);
    }

    [Fact]
    public void Scripts_KeepOneEnabledSquareTokenAndOneLinklyCredentialPerStoreEnvironment()
    {
        var sql = string.Join("\n", PaymentTerminalSettingsSchemaMigrator.SqlScriptsForTests);

        Assert.Contains("UX_POSM_SquareToken_Environment_Enabled", sql);
        Assert.Contains("WHERE [IsEnabled] = 1", sql);
        Assert.Contains("DF_POSM_LinklyCloudCredential_Environment", sql);
        Assert.Contains("DEFAULT (N'Production') FOR [Environment]", sql);
        Assert.Contains("UX_POSM_LinklyCloudCredential_StoreCode_Environment", sql);
        Assert.Contains("UNIQUE ([StoreCode], [Environment])", sql);
    }

    [Fact]
    public void Scripts_BackfillExistingLinklyEnvironmentBeforeAddingConstraints()
    {
        var sql = string.Join("\n", PaymentTerminalSettingsSchemaMigrator.SqlScriptsForTests);

        Assert.Contains("SET [Environment] = N'Production'", sql);
        Assert.Contains("ALTER COLUMN [Environment] NVARCHAR(32) NOT NULL", sql);
    }
}
