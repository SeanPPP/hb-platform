using System.Data.Common;
using BlazorApp.Api.Services.React;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ContainerMutationLockTests
{
    [Fact]
    public async Task AcquireContainersAsync_事务外调用应拒绝()
    {
        using var db = CreateSqliteClient();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ContainerMutationLock.AcquireContainersAsync(db, new[] { "C-1" })
        );

        Assert.Equal("货柜明细业务锁必须在数据库事务内获取", error.Message);
    }

    [Fact]
    public async Task AcquireContainersAsync_SQLite按规范化编码返回覆盖范围()
    {
        using var db = CreateSqliteClient();
        await db.Ado.BeginTranAsync();
        try
        {
            var scope = await ContainerMutationLock.AcquireContainersAsync(
                db,
                new[] { " c-2 ", "C-1", "c-1", null }
            );

            Assert.Equal(new[] { "C-1", "C-2" }, scope.ContainerCodes);
            scope.EnsureCovers(db, new[] { "c-1", " C-2 " });
            Assert.Throws<ContainerMutationScopeChangedException>(() =>
                scope.EnsureCovers(db, new[] { "C-3" })
            );
        }
        finally
        {
            await db.Ado.RollbackTranAsync();
        }
    }

    [Fact]
    public async Task AcquireAllAsync_SQLite返回全量覆盖范围()
    {
        using var db = CreateSqliteClient();
        await db.Ado.BeginTranAsync();
        try
        {
            var scope = await ContainerMutationLock.AcquireAllAsync(db);

            Assert.True(scope.LocksAllContainers);
            scope.EnsureCovers(db, new[] { "ANY-CONTAINER" });
        }
        finally
        {
            await db.Ado.RollbackTranAsync();
        }
    }

    [Fact]
    public void TryResolveConflict_识别包装后的货柜锁异常()
    {
        var original = new ContainerMutationLockException("resource", -1);

        var matched = ContainerMutationLock.TryResolveConflict(
            new InvalidOperationException("outer", original),
            out var conflict
        );

        Assert.True(matched);
        Assert.Same(original, conflict);
        Assert.Equal("CONTAINER_DETAIL_BUSY", ContainerMutationLock.BusyErrorCode);
    }

    [Fact]
    public void TryResolveConflict_锁范围变化时返回可重试的货柜冲突()
    {
        var scopeChanged = new ContainerMutationScopeChangedException(new[] { "C-2" });

        var matched = ContainerMutationLock.TryResolveConflict(
            new InvalidOperationException("outer", scopeChanged),
            out var conflict
        );

        Assert.True(matched);
        Assert.NotNull(conflict);
        Assert.Equal("scope-changed", conflict.Resource);
        Assert.Equal(-1, conflict.ResultCode);
        Assert.Same(scopeChanged, conflict.InnerException);
    }

    [Theory]
    [InlineData(1205, false, true, -3)]
    [InlineData(1222, false, true, -1)]
    [InlineData(-2, false, false, 0)]
    [InlineData(-2, true, true, -1)]
    [InlineData(50000, true, false, 0)]
    public void TryResolveSqlConflictResultCode_只在取锁阶段识别命令超时(
        int sqlErrorNumber,
        bool includeCommandTimeout,
        bool expectedMatched,
        int expectedResultCode
    )
    {
        var matched = ContainerMutationLock.TryResolveSqlConflictResultCode(
            sqlErrorNumber,
            includeCommandTimeout,
            out var resultCode
        );

        Assert.Equal(expectedMatched, matched);
        Assert.Equal(expectedResultCode, resultCode);
    }

    [Theory]
    [InlineData(1205, 0, true)]
    [InlineData(1205, 1, false)]
    [InlineData(1222, 0, false)]
    [InlineData(-2, 0, false)]
    public void ShouldRetryDeadlock_只允许1205完整重试一次(
        int sqlErrorNumber,
        int completedRetryCount,
        bool expected
    )
    {
        Assert.Equal(
            expected,
            ContainerMutationLock.ShouldRetryDeadlock(sqlErrorNumber, completedRetryCount)
        );
    }

    [Fact]
    public void ResetFailedTransaction_应清除僵尸事务并关闭旧连接()
    {
        using var db = CreateSqliteClient();
        db.Ado.Open();
        db.Ado.Transaction = new Mock<DbTransaction>().Object;

        ContainerMutationLock.ResetFailedTransaction(db);

        Assert.Null(db.Ado.Transaction);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Ado.Connection.State);
    }

    [Fact]
    public void AlignDomesticProductCodeAsync_全局改码必须先获取独占总闸()
    {
        var source = ReadApiSource("Services/React/ContainerReactService.cs");
        var methodStart = source.IndexOf(
            "public async Task<AlignDomesticProductCodeResultDto> AlignDomesticProductCodeAsync(",
            StringComparison.Ordinal
        );
        var nextMethod = source.IndexOf(
            "public async Task<int> BatchUpdateDetailsAsync(",
            methodStart,
            StringComparison.Ordinal
        );
        Assert.True(methodStart >= 0 && nextMethod > methodStart);

        var method = source[methodStart..nextMethod];
        var transaction = method.IndexOf("BeginTranAsync", StringComparison.Ordinal);
        var globalLock = method.IndexOf(
            "ContainerMutationLock.AcquireAllAsync",
            StringComparison.Ordinal
        );
        var updateStart = method.IndexOf(
            ".SetColumns(d => d.ProductCode == targetProductCode)",
            globalLock,
            StringComparison.Ordinal
        );
        var globalUpdate = method.IndexOf(
            ".Where(d => d.ProductCode == oldProductCode && !d.IsDeleted)",
            updateStart,
            StringComparison.Ordinal
        );

        Assert.True(transaction >= 0 && globalLock > transaction);
        Assert.True(updateStart > globalLock && globalUpdate > updateStart);
        Assert.DoesNotContain("AcquireContainersAsync", method);
    }

    [Fact]
    public void ScopedBatchMutations_必须在同一货柜事务锁内重读并直接执行单次更新()
    {
        var source = ReadApiSource("Services/React/ContainerReactService.cs");
        var helper = ReadMethod(
            source,
            "private async Task<int> ExecuteScopedBatchUpdateUnderContainerLockAsync(",
            "public async Task<int> ApplyFloatRateByScopeAsync("
        );

        var transaction = helper.IndexOf("BeginTranAsync", StringComparison.Ordinal);
        var containerLock = helper.IndexOf(
            "ContainerMutationLock.AcquireContainersAsync",
            StringComparison.Ordinal
        );
        var scopeResolution = helper.IndexOf(
            "ResolveContainerDetailBatchScopeHguidsAsync",
            StringComparison.Ordinal
        );
        var routedReload = helper.IndexOf(
            "detail.ContainerCode == containerGuid",
            StringComparison.Ordinal
        );
        var updateAttempt = helper.IndexOf(
            "BatchUpdateDetailsAttemptAsync",
            StringComparison.Ordinal
        );
        var commit = helper.IndexOf("CommitTranAsync", StringComparison.Ordinal);

        Assert.True(transaction >= 0 && containerLock > transaction);
        Assert.True(scopeResolution > containerLock && routedReload > scopeResolution);
        Assert.True(updateAttempt > routedReload && commit > updateAttempt);

        foreach (
            var methodName in new[]
            {
                "ApplyFloatRateByScopeAsync",
                "ApplyPricesByScopeAsync",
                "RecalculateCostsByScopeAsync",
            }
        )
        {
            var methodStart = source.IndexOf(
                $"public async Task<int> {methodName}(",
                StringComparison.Ordinal
            );
            Assert.True(methodStart >= 0);
            var methodEnd = source.IndexOf(
                "public async Task<int>",
                methodStart + 1,
                StringComparison.Ordinal
            );
            Assert.True(methodEnd > methodStart);
            var method = source[methodStart..methodEnd];
            Assert.Contains("ExecuteScopedBatchUpdateUnderContainerLockAsync", method);
            Assert.DoesNotContain("BatchUpdateDetailsAsync(updates)", method);
        }
    }

    [Fact]
    public void UpdateContainerAsync_必须在独占总闸内重读并限定列更新()
    {
        var source = ReadApiSource("Services/React/ContainerReactService.cs");
        var method = ReadMethod(
            source,
            "public async Task<bool> UpdateContainerAsync(",
            "public async Task<List<ContainerDetailDto>> GetContainerProductsAsync("
        );

        var transaction = method.IndexOf("BeginTranAsync", StringComparison.Ordinal);
        var containerLock = method.IndexOf(
            "ContainerMutationLock.AcquireAllAsync",
            StringComparison.Ordinal
        );
        var reload = method.IndexOf("Queryable<Container>()", containerLock, StringComparison.Ordinal);
        var narrowUpdate = method.IndexOf("UpdateColumns", reload, StringComparison.Ordinal);
        var commit = method.IndexOf("CommitTranAsync", narrowUpdate, StringComparison.Ordinal);

        Assert.True(transaction >= 0 && containerLock > transaction);
        Assert.True(reload > containerLock && narrowUpdate > reload);
        Assert.True(commit > narrowUpdate);
    }

    [Fact]
    public void SubmitContainer_必须先取货柜锁再重读明细和获取商品锁()
    {
        var source = ReadApiSource(
            "Services/React/ContainerProductCreationExecutorService.cs"
        );
        var method = ReadMethod(
            source,
            "public async Task<ContainerProductCreationResultDto> ExecuteAsync(\n            ContainerProductCreationJobRequestDto request,\n            string? actorUserGuid,",
            "private async Task<List<ContainerProductCreationSourceRow>> LoadRowsAsync("
        );

        var transaction = method.IndexOf("BeginTran", StringComparison.Ordinal);
        var containerLock = method.IndexOf(
            "ContainerMutationLock.AcquireContainersAsync",
            transaction,
            StringComparison.Ordinal
        );
        var lockedReload = method.IndexOf("LoadRowsAsync", containerLock, StringComparison.Ordinal);
        var productLock = method.IndexOf(
            "SetChildPurchasePriceMutationLock.Acquire",
            lockedReload,
            StringComparison.Ordinal
        );

        Assert.True(transaction >= 0 && containerLock > transaction);
        Assert.True(lockedReload > containerLock && productLock > lockedReload);
        Assert.Contains("ContainerMutationLock.BusyErrorCode", method);
        Assert.DoesNotContain("Updateable<ContainerDetail>", method);
        Assert.DoesNotContain("Insertable<ContainerDetail>", method);
    }

    private static SqlSugarClient CreateSqliteClient() =>
        new(
            new ConnectionConfig
            {
                ConnectionString = "DataSource=:memory:",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            }
        );

    private static string ReadApiSource(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "services",
                "backend",
                "BlazorApp.Api",
                relativePath
            );
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"无法定位后端源码: {relativePath}");
    }

    private static string ReadMethod(string source, string startMarker, string endMarker)
    {
        var methodStart = source.IndexOf(startMarker, StringComparison.Ordinal);
        var methodEnd = source.IndexOf(endMarker, methodStart + 1, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        return source[methodStart..methodEnd];
    }
}
