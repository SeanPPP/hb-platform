using BlazorApp.Api.Services.React;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SetChildPurchasePriceSqlServerFactAttribute : FactAttribute
{
    private const string ConnectionEnvironmentVariable =
        "SET_CHILD_PURCHASE_PRICE_SQLSERVER_TEST_CONNECTION";

    public SetChildPurchasePriceSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)))
        {
            Skip = $"未配置 {ConnectionEnvironmentVariable}，跳过真实 SQL Server 套装成本锁验证。";
        }
    }
}

public sealed class SetChildPurchasePriceSqlServerIntegrationTests
{
    private const string ConnectionEnvironmentVariable =
        "SET_CHILD_PURCHASE_PRICE_SQLSERVER_TEST_CONNECTION";

    [Fact]
    public void NormalizeProductCodes_忽略大小写空白重复并稳定排序()
    {
        var result = SetChildPurchasePriceMutationLock.NormalizeProductCodes(
            new string?[] { " b ", "A", null, "a", "" }
        );

        Assert.Equal(new[] { "A", "B" }, result);
    }

    [Fact]
    public void TryResolveConflictResultCode_普通取消不映射为业务锁冲突()
    {
        var exception = new InvalidOperationException(
            "outer",
            new OperationCanceledException("cancelled")
        );

        var matched = SetChildPurchasePriceMutationLock.TryResolveConflictResultCode(
            exception,
            out var resultCode
        );

        Assert.False(matched);
        Assert.Equal(0, resultCode);
        Assert.False(
            SetChildPurchasePriceMutationLock.TryResolveConflictResultCode(
                new InvalidOperationException("ordinary"),
                out _
            )
        );
    }

    [SetChildPurchasePriceSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task AcquireProductsAsync_SqlServer同商品锁跨连接互斥()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var productCode = $"LOCK-{Guid.NewGuid():N}".ToUpperInvariant();
        using var firstDb = CreateClient(connectionString);
        using var secondDb = CreateClient(connectionString);
        await firstDb.Ado.BeginTranAsync();
        await secondDb.Ado.BeginTranAsync();
        try
        {
            await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                firstDb,
                new[] { productCode }
            );

            var gateResult = await TryAcquireAsync(
                secondDb,
                "HB:SetChildPurchasePrice:Gate",
                "Shared"
            );
            var productResult = await TryAcquireAsync(
                secondDb,
                "HB:SetChildPurchasePrice:Product:" + productCode,
                "Exclusive"
            );

            Assert.True(gateResult >= 0);
            Assert.True(productResult < 0);
        }
        finally
        {
            await secondDb.Ado.RollbackTranAsync();
            await firstDb.Ado.RollbackTranAsync();
        }
    }

    [SetChildPurchasePriceSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task AcquireProductsPartiallyAsync_SqlServer共享总预算且保留已成功商品锁()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var suffix = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var availableCode = $"LOCK-A-{suffix}";
        var busyCode = $"LOCK-B-{suffix}";
        using var firstDb = CreateClient(connectionString);
        using var secondDb = CreateClient(connectionString);
        await firstDb.Ado.BeginTranAsync();
        await secondDb.Ado.BeginTranAsync();
        try
        {
            await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                firstDb,
                new[] { busyCode }
            );
            var startedAt = DateTime.UtcNow;

            var result = await SetChildPurchasePriceMutationLock.AcquireProductsPartiallyAsync(
                secondDb,
                new[] { busyCode, availableCode },
                totalWaitMilliseconds: 250
            );

            Assert.True(DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(3));
            Assert.Equal(new[] { busyCode }, result.BusyProductCodes);
            result.LockScope.EnsureCovers(secondDb, new[] { availableCode });
            Assert.Throws<InvalidOperationException>(() =>
            {
                result.LockScope.EnsureCovers(secondDb, new[] { busyCode });
            });
        }
        finally
        {
            await secondDb.Ado.RollbackTranAsync();
            await firstDb.Ado.RollbackTranAsync();
        }
    }

    [SetChildPurchasePriceSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task AcquireProductsPartiallyAsync_SqlServer总闸等待也计入共享预算()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var productCode = $"LOCK-GATE-{Guid.NewGuid():N}".ToUpperInvariant();
        using var firstDb = CreateClient(connectionString);
        using var secondDb = CreateClient(connectionString);
        await firstDb.Ado.BeginTranAsync();
        await secondDb.Ado.BeginTranAsync();
        try
        {
            await SetChildPurchasePriceMutationLock.AcquireAllAsync(firstDb);
            var startedAt = DateTime.UtcNow;

            var result = await SetChildPurchasePriceMutationLock.AcquireProductsPartiallyAsync(
                secondDb,
                new[] { productCode },
                totalWaitMilliseconds: 250
            );

            Assert.True(DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(3));
            Assert.Equal(new[] { productCode }, result.BusyProductCodes);
            Assert.Throws<InvalidOperationException>(() =>
            {
                result.LockScope.EnsureCovers(secondDb, new[] { productCode });
            });
        }
        finally
        {
            await secondDb.Ado.RollbackTranAsync();
            await firstDb.Ado.RollbackTranAsync();
        }
    }

    [SetChildPurchasePriceSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task AcquireAllAsync_SqlServer全量锁阻止普通共享总闸()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        using var firstDb = CreateClient(connectionString);
        using var secondDb = CreateClient(connectionString);
        await firstDb.Ado.BeginTranAsync();
        await secondDb.Ado.BeginTranAsync();
        try
        {
            await SetChildPurchasePriceMutationLock.AcquireAllAsync(firstDb);

            var result = await TryAcquireAsync(
                secondDb,
                "HB:SetChildPurchasePrice:Gate",
                "Shared"
            );

            Assert.True(result < 0);
        }
        finally
        {
            await secondDb.Ado.RollbackTranAsync();
            await firstDb.Ado.RollbackTranAsync();
        }
    }

    [SetChildPurchasePriceSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task AcquireProductsAsync_SqlServer反向输入按稳定顺序获取且不死锁()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var suffix = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var firstCode = $"LOCK-A-{suffix}";
        var secondCode = $"LOCK-B-{suffix}";
        using var firstDb = CreateClient(connectionString);
        using var secondDb = CreateClient(connectionString);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task AcquireAndCommitAsync(ISqlSugarClient db, string[] productCodes)
        {
            await start.Task;
            await db.Ado.BeginTranAsync();
            try
            {
                await SetChildPurchasePriceMutationLock.AcquireProductsAsync(db, productCodes);
                await Task.Delay(100);
                await db.Ado.CommitTranAsync();
            }
            catch
            {
                await db.Ado.RollbackTranAsync();
                throw;
            }
        }

        var firstTask = AcquireAndCommitAsync(firstDb, new[] { firstCode, secondCode });
        var secondTask = AcquireAndCommitAsync(secondDb, new[] { secondCode, firstCode });
        start.SetResult();

        await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(15));
    }

    [SetChildPurchasePriceSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task AcquireProductsAsync_SqlServer锁内重读保证最终成本来自最后提交的源价格()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var suffix = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var productCode = $"RACE-{suffix}";
        var tableName = $"SetChildCostRace_{suffix}";
        using var setupDb = CreateClient(connectionString);
        using var firstDb = CreateClient(connectionString);
        using var secondDb = CreateClient(connectionString);

        await setupDb.Ado.ExecuteCommandAsync(
            $"CREATE TABLE [{tableName}] (ProductCode nvarchar(100) NOT NULL PRIMARY KEY, SourcePrice decimal(18,2) NOT NULL, DerivedCost decimal(18,2) NOT NULL);"
        );
        try
        {
            await setupDb.Ado.ExecuteCommandAsync(
                $"INSERT INTO [{tableName}] (ProductCode, SourcePrice, DerivedCost) VALUES (@ProductCode, 10, 10);",
                new SugarParameter("@ProductCode", productCode)
            );

            var firstHasLock = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var allowFirstCommit = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            async Task UpdateSourceAndDerivedAsync(
                ISqlSugarClient db,
                decimal sourcePrice,
                TaskCompletionSource? lockedSignal = null,
                Task? commitGate = null
            )
            {
                await db.Ado.BeginTranAsync();
                try
                {
                    await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                        db,
                        new[] { productCode }
                    );
                    lockedSignal?.SetResult();
                    await db.Ado.ExecuteCommandAsync(
                        $"UPDATE [{tableName}] SET SourcePrice = @SourcePrice WHERE ProductCode = @ProductCode;",
                        new SugarParameter("@SourcePrice", sourcePrice),
                        new SugarParameter("@ProductCode", productCode)
                    );
                    if (commitGate != null)
                    {
                        await commitGate;
                    }

                    // 关键断言场景：必须取得业务锁后再读取源值并生成派生成本。
                    var latestSource = await db.Ado.SqlQuerySingleAsync<decimal>(
                        $"SELECT SourcePrice FROM [{tableName}] WHERE ProductCode = @ProductCode;",
                        new SugarParameter("@ProductCode", productCode)
                    );
                    await db.Ado.ExecuteCommandAsync(
                        $"UPDATE [{tableName}] SET DerivedCost = @DerivedCost WHERE ProductCode = @ProductCode;",
                        new SugarParameter("@DerivedCost", latestSource),
                        new SugarParameter("@ProductCode", productCode)
                    );
                    await db.Ado.CommitTranAsync();
                }
                catch
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }
            }

            var firstTask = UpdateSourceAndDerivedAsync(
                firstDb,
                20m,
                firstHasLock,
                allowFirstCommit.Task
            );
            await firstHasLock.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var secondTask = UpdateSourceAndDerivedAsync(secondDb, 30m);
            await Task.Delay(200);
            allowFirstCommit.SetResult();
            await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(15));

            var finalValues = await setupDb.Ado.SqlQuerySingleAsync<RaceResult>(
                $"SELECT SourcePrice, DerivedCost FROM [{tableName}] WHERE ProductCode = @ProductCode;",
                new SugarParameter("@ProductCode", productCode)
            );
            Assert.Equal(30m, finalValues.SourcePrice);
            Assert.Equal(30m, finalValues.DerivedCost);
        }
        finally
        {
            await setupDb.Ado.ExecuteCommandAsync($"DROP TABLE IF EXISTS [{tableName}];");
        }
    }

    [SetChildPurchasePriceSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task ContainerDetailCaseUpdate_SqlServer同一明细不同字段并发保存互不覆盖()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var suffix = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var tableName = $"ContainerDetailCaseRace_{suffix}";
        const string detailCode = "D-CONCURRENT";
        using var setupDb = CreateClient(connectionString);
        using var firstDb = CreateClient(connectionString);
        using var secondDb = CreateClient(connectionString);
        await setupDb.Ado.ExecuteCommandAsync(
            $"CREATE TABLE [{tableName}] (DetailCode nvarchar(100) NOT NULL PRIMARY KEY, DomesticPrice decimal(18,2) NULL, OEMPrice decimal(18,2) NULL);"
        );
        try
        {
            await setupDb.Ado.ExecuteCommandAsync(
                $"INSERT INTO [{tableName}] (DetailCode, DomesticPrice, OEMPrice) VALUES (@DetailCode, 1, 2);",
                new SugarParameter("@DetailCode", detailCode)
            );

            await firstDb.Ado.BeginTranAsync();
            try
            {
                // 与货柜保存一致：每次请求只为被接受字段生成 CASE，绝不能带回另一字段的旧值。
                await firstDb.Ado.ExecuteCommandAsync(
                    $"UPDATE [{tableName}] SET DomesticPrice = CASE WHEN DetailCode = @DetailCode THEN @DomesticPrice ELSE DomesticPrice END WHERE DetailCode IN (@DetailCode);",
                    new SugarParameter("@DetailCode", detailCode),
                    new SugarParameter("@DomesticPrice", 10m)
                );

                var secondTask = Task.Run(async () =>
                {
                    await secondDb.Ado.BeginTranAsync();
                    try
                    {
                        await secondDb.Ado.ExecuteCommandAsync(
                            $"UPDATE [{tableName}] SET OEMPrice = CASE WHEN DetailCode = @DetailCode THEN @OEMPrice ELSE OEMPrice END WHERE DetailCode IN (@DetailCode);",
                            new SugarParameter("@DetailCode", detailCode),
                            new SugarParameter("@OEMPrice", 20m)
                        );
                        await secondDb.Ado.CommitTranAsync();
                    }
                    catch
                    {
                        await secondDb.Ado.RollbackTranAsync();
                        throw;
                    }
                });

                await Task.Delay(200);
                await firstDb.Ado.CommitTranAsync();
                await secondTask.WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch
            {
                await firstDb.Ado.RollbackTranAsync();
                throw;
            }

            var finalValues = await setupDb.Ado.SqlQuerySingleAsync<ContainerDetailCaseRaceResult>(
                $"SELECT DomesticPrice, OEMPrice FROM [{tableName}] WHERE DetailCode = @DetailCode;",
                new SugarParameter("@DetailCode", detailCode)
            );
            Assert.Equal(10m, finalValues.DomesticPrice);
            Assert.Equal(20m, finalValues.OEMPrice);
        }
        finally
        {
            await setupDb.Ado.ExecuteCommandAsync($"DROP TABLE IF EXISTS [{tableName}];");
        }
    }

    private sealed class RaceResult
    {
        public decimal SourcePrice { get; set; }
        public decimal DerivedCost { get; set; }
    }

    private sealed class ContainerDetailCaseRaceResult
    {
        public decimal DomesticPrice { get; set; }
        public decimal OEMPrice { get; set; }
    }

    private static SqlSugarClient CreateClient(string connectionString) => new(new ConnectionConfig
    {
        ConnectionString = connectionString,
        DbType = DbType.SqlServer,
        IsAutoCloseConnection = true,
        InitKeyType = InitKeyType.Attribute,
    });

    private static Task<int> TryAcquireAsync(
        ISqlSugarClient db,
        string resource,
        string lockMode
    ) => db.Ado.SqlQuerySingleAsync<int>(
        """
        DECLARE @Result int;
        EXEC @Result = sys.sp_getapplock
            @Resource = @Resource,
            @LockMode = @LockMode,
            @LockOwner = N'Transaction',
            @LockTimeout = 0;
        SELECT @Result;
        """,
        new SugarParameter("@Resource", resource),
        new SugarParameter("@LockMode", lockMode)
    );
}
