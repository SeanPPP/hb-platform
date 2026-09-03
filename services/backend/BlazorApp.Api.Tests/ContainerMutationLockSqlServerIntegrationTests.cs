using BlazorApp.Api.Services.React;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ContainerMutationSqlServerFactAttribute : FactAttribute
{
    private const string ConnectionEnvironmentVariable =
        "CONTAINER_MUTATION_SQLSERVER_TEST_CONNECTION";

    public ContainerMutationSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)))
        {
            Skip = $"未配置 {ConnectionEnvironmentVariable}，跳过真实 SQL Server 货柜并发锁验证。";
        }
    }
}

public sealed class ContainerMutationLockSqlServerIntegrationTests
{
    private const string ConnectionEnvironmentVariable =
        "CONTAINER_MUTATION_SQLSERVER_TEST_CONNECTION";

    [ContainerMutationSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task AcquireContainersAsync_SqlServer同柜跨连接互斥并返回Busy()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var containerCode = $"LOCK-{Guid.NewGuid():N}";
        using var firstDb = CreateClient(connectionString);
        using var secondDb = CreateClient(connectionString);
        await firstDb.Ado.BeginTranAsync();
        await secondDb.Ado.BeginTranAsync();
        try
        {
            await ContainerMutationLock.AcquireContainersAsync(
                firstDb,
                new[] { containerCode }
            );

            var startedAt = DateTime.UtcNow;
            var error = await Assert.ThrowsAsync<ContainerMutationLockException>(() =>
                ContainerMutationLock.AcquireContainersAsync(
                    secondDb,
                    new[] { containerCode.ToLowerInvariant() }
                )
            );

            Assert.Equal(-1, error.ResultCode);
            Assert.True(DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(8));
            Assert.True(ContainerMutationLock.TryResolveConflict(error, out var conflict));
            Assert.Same(error, conflict);
        }
        finally
        {
            if (secondDb.Ado.Transaction != null)
            {
                await secondDb.Ado.RollbackTranAsync();
            }
            if (firstDb.Ado.Transaction != null)
            {
                await firstDb.Ado.RollbackTranAsync();
            }
        }
    }

    [ContainerMutationSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task AcquireContainersAsync_SqlServer反向多柜输入按稳定顺序且不死锁()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var suffix = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var firstCode = $"LOCK-A-{suffix}";
        var secondCode = $"LOCK-B-{suffix}";
        using var firstDb = CreateClient(connectionString);
        using var secondDb = CreateClient(connectionString);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task AcquireAndCommitAsync(ISqlSugarClient db, string[] containerCodes)
        {
            await start.Task;
            await db.Ado.BeginTranAsync();
            try
            {
                var scope = await ContainerMutationLock.AcquireContainersAsync(
                    db,
                    containerCodes
                );
                Assert.Equal(new[] { firstCode, secondCode }, scope.ContainerCodes);
                await Task.Delay(100);
                await db.Ado.CommitTranAsync();
            }
            catch
            {
                if (db.Ado.Transaction != null)
                {
                    await db.Ado.RollbackTranAsync();
                }
                throw;
            }
        }

        var firstTask = AcquireAndCommitAsync(firstDb, new[] { firstCode, secondCode });
        var secondTask = AcquireAndCommitAsync(secondDb, new[] { secondCode, firstCode });
        start.SetResult();

        await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static SqlSugarClient CreateClient(string connectionString) =>
        new(
            new ConnectionConfig
            {
                ConnectionString = connectionString,
                DbType = DbType.SqlServer,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            }
        );
}
