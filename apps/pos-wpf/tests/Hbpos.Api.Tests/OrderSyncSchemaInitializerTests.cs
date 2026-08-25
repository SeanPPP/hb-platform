using System.Reflection;
using BlazorApp.Shared.Models.POSM;
using Hbpos.Api;
using Hbpos.Api.Services;
using Hbpos.Contracts.Orders;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace Hbpos.Api.Tests;

public sealed class OrderSyncSchemaInitializerTests
{
    [Fact]
    public async Task InitializeAsync_widens_payment_reference_for_structured_card_refunds()
    {
        var executor = new CapturingOrderSyncSchemaSqlExecutor();
        var initializer = new SqlSugarOrderSyncSchemaInitializer(executor);

        await initializer.InitializeAsync();

        var sql = Assert.Single(executor.SqlStatements);
        Assert.Contains("IF OBJECT_ID(N'[dbo].[payment_detail]', N'U') IS NOT NULL", sql);
        Assert.Contains("COL_LENGTH(N'dbo.payment_detail', N'Reference') < 1000", sql);
        Assert.Contains("ALTER TABLE [dbo].[payment_detail]", sql);
        Assert.Contains("ALTER COLUMN [Reference] VARCHAR(1000) NULL", sql);
    }

    [Fact]
    public async Task InitializeAsync_adds_indexes_for_return_order_lookup_hot_path()
    {
        var executor = new CapturingOrderSyncSchemaSqlExecutor();
        var initializer = new SqlSugarOrderSyncSchemaInitializer(executor);

        await initializer.InitializeAsync();

        var sql = Assert.Single(executor.SqlStatements);
        Assert.Contains("IX_payment_detail_OrderGuid", sql);
        Assert.Contains("ON [dbo].[payment_detail] ([OrderGuid])", sql);
        Assert.Contains("IX_BankTransaction_OrderGuid", sql);
        Assert.Contains("ON [dbo].[BankTransaction] ([OrderGuid])", sql);
        Assert.Contains("IX_sales_return_record_OriginalOrderGuid", sql);
        Assert.Contains("ON [dbo].[sales_return_record] ([OriginalOrderGuid])", sql);
        Assert.Contains("IX_sales_return_record_ReturnOrderGuid", sql);
        Assert.Contains("ON [dbo].[sales_return_record] ([ReturnOrderGuid])", sql);
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(sql, "ERROR_NUMBER\\(\\) <> 1913").Count);
    }

    [Fact]
    public void Payment_reference_model_can_store_structured_square_refund_reference()
    {
        var property = typeof(PaymentDetail).GetProperty(nameof(PaymentDetail.Reference));
        var column = property?.GetCustomAttribute<SugarColumn>();
        var reference = CardRefundReference.Format(
            $"SQRF:{new string('R', 68)}",
            $"SQ:{new string('O', 29)}");

        Assert.True(reference.Length > 100);
        Assert.NotNull(column);
        Assert.Equal(1000, column!.Length);
        Assert.True(reference.Length <= column.Length);
    }

    [Fact]
    public void AddHbposApiServices_registers_order_sync_schema_initializer()
    {
        var services = new ServiceCollection();

        services.AddHbposApiServices();

        var initializer = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IOrderSyncSchemaInitializer));
        Assert.Equal(typeof(SqlSugarOrderSyncSchemaInitializer), initializer.ImplementationType);

        var executor = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IOrderSyncSchemaSqlExecutor));
        Assert.Equal(typeof(SqlSugarOrderSyncSchemaSqlExecutor), executor.ImplementationType);
    }

    private sealed class CapturingOrderSyncSchemaSqlExecutor : IOrderSyncSchemaSqlExecutor
    {
        public List<string> SqlStatements { get; } = [];

        public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
        {
            SqlStatements.Add(sql);
            return Task.CompletedTask;
        }
    }
}

internal sealed class TestNoOpOrderSyncSchemaInitializer : IOrderSyncSchemaInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
