using Hbpos.Api.Services;

namespace Hbpos.Api.Tests;

public sealed class InstallmentRepaymentClaimSchemaInitializerTests
{
    [Fact]
    public async Task Initializer_creates_transactional_claim_schema_and_payment_audit_column()
    {
        var executor = new CapturingExecutor();
        var initializer = new SqlSugarInstallmentRepaymentClaimSchemaInitializer(executor);

        await initializer.InitializeAsync(CancellationToken.None);

        var sql = Assert.Single(executor.Commands);
        Assert.Contains("SET XACT_ABORT ON", sql);
        Assert.Contains("sys.sp_getapplock", sql);
        Assert.Contains("[dbo].[POSM_InstallmentRepaymentClaim]", sql);
        Assert.Contains("N'Prepared', N'ProviderPending', N'Committed', N'Released', N'Declined', N'Unknown'", sql);
        Assert.Contains("WHERE [IsBlocking] = 1", sql);
        Assert.Contains("UX_POSM_InstallmentRepaymentClaim_Idempotency", sql);
        Assert.Contains("UX_POSM_InstallmentRepaymentClaim_ProviderAttempt", sql);
        Assert.Contains("ADD [CashierName] NVARCHAR(100) NULL", sql);
        Assert.Contains("[CommitResponseJson] NVARCHAR(MAX) NULL", sql);
        Assert.Contains("COL_LENGTH(N'dbo.POSM_InstallmentRepaymentClaim', N'CommitResponseJson') IS NULL", sql);
        Assert.Contains("ADD [CommitResponseJson] NVARCHAR(MAX) NULL", sql);
        Assert.Contains("ADD [LastRecoveryCashierId] NVARCHAR(50) NULL", sql);
        Assert.Contains("ADD [LastRecoveryCashierName] NVARCHAR(100) NULL", sql);
        Assert.Contains("ADD [LastRecoveryCashierUserGuid] NVARCHAR(50) NULL", sql);
        Assert.Contains("ADD [RecoveredAtUtc] DATETIME2(7) NULL", sql);
        Assert.Contains("COMMIT TRANSACTION", sql);
    }

    private sealed class CapturingExecutor : IInstallmentRepaymentClaimSchemaSqlExecutor
    {
        public List<string> Commands { get; } = [];

        public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
        {
            Commands.Add(sql);
            return Task.CompletedTask;
        }
    }
}

internal sealed class TestNoOpInstallmentRepaymentClaimSchemaInitializer
    : IInstallmentRepaymentClaimSchemaInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class TestNoOpLinklySettlementSchemaInitializer
    : ILinklySettlementSchemaInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
