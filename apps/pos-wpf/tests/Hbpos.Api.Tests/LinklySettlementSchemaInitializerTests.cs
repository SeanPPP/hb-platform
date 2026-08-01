using Hbpos.Api.Services;

namespace Hbpos.Api.Tests;

public sealed class LinklySettlementSchemaInitializerTests
{
    [Fact]
    public async Task InitializeAsync_creates_independent_idempotent_POSM_settlement_schema()
    {
        var executor = new CapturingExecutor();
        var initializer = new SqlSugarLinklySettlementSchemaInitializer(executor);

        await initializer.InitializeAsync();

        var sql = Assert.Single(executor.Commands);
        Assert.Contains("POSM_LinklySettlement", sql, StringComparison.Ordinal);
        Assert.Contains("UX_POSM_LinklySettlement_ScopeGuid", sql, StringComparison.Ordinal);
        Assert.Contains("UX_POSM_LinklySettlement_ProviderSession", sql, StringComparison.Ordinal);
        Assert.Contains("UX_POSM_LinklySettlement_CloudBackendSession", sql, StringComparison.Ordinal);
        Assert.Contains("ClientRevision", sql, StringComparison.Ordinal);
        Assert.Contains("[ProviderSubmissionState] NVARCHAR(32) NULL", sql, StringComparison.Ordinal);
        Assert.Contains("COL_LENGTH(N'dbo.POSM_LinklySettlement', N'ProviderSubmissionState') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("CK_POSM_LinklySettlement_ProviderSubmissionState", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE [ProviderSubmissionState] IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("AND [ConnectionMode] = N'CloudBackendAsync'", sql, StringComparison.Ordinal);
        Assert.Contains("N'NotSubmitted'", sql, StringComparison.Ordinal);
        Assert.Contains("N'Submitted'", sql, StringComparison.Ordinal);
        Assert.Contains("N'Unknown'", sql, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT ON", sql, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRANSACTION", sql, StringComparison.Ordinal);
        Assert.Contains("sys.sp_getapplock", sql, StringComparison.Ordinal);
        Assert.Contains("@LockOwner = N'Transaction'", sql, StringComparison.Ordinal);
        Assert.Contains("COMMIT TRANSACTION", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE TABLE [dbo].[POSM_LinklyCloudBackendSession]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Cloud_backend_link_lookup_does_not_duplicate_mutable_notification_facts()
    {
        var sql = SqlSugarLinklySettlementRepository.SelectCloudBackendSettlementSql;

        Assert.Contains("session.[OperationType] = N'Settlement'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("POSM_LinklyCloudBackendNotification", sql, StringComparison.Ordinal);
    }

    private sealed class CapturingExecutor : ILinklySettlementSchemaSqlExecutor
    {
        public List<string> Commands { get; } = [];

        public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
        {
            Commands.Add(sql);
            return Task.CompletedTask;
        }
    }
}
