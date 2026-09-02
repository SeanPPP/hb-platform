using BlazorApp.Api.Services.React;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SetChildPurchasePriceMutationLockTests
{
    [Fact]
    public void CanContinuePartialLockFailure_仅安全的锁请求失败可在原事务继续()
    {
        Assert.True(
            SetChildPurchasePriceMutationLock.CanContinuePartialLockFailure(
                new SetChildPurchasePriceLockException(
                    "timeout",
                    -1,
                    new TimeoutException()
                )
            )
        );
        Assert.True(
            SetChildPurchasePriceMutationLock.CanContinuePartialLockFailure(
                new SetChildPurchasePriceLockException("cancelled", -2)
            )
        );
        Assert.True(
            SetChildPurchasePriceMutationLock.CanContinuePartialLockFailure(
                new SetChildPurchasePriceLockException("direct-applock-deadlock", -3)
            )
        );
        Assert.False(
            SetChildPurchasePriceMutationLock.CanContinuePartialLockFailure(
                new SetChildPurchasePriceLockException(
                    "sql-deadlock",
                    -3,
                    new InvalidOperationException("模拟 SqlException 1205 包装")
                )
            )
        );
        Assert.False(
            SetChildPurchasePriceMutationLock.CanContinuePartialLockFailure(
                new SetChildPurchasePriceLockException("invalid", -999)
            )
        );
    }

    [Fact]
    public async Task AcquireProductsPartiallyAsync_SQLite返回覆盖全部成功商品的单一锁范围()
    {
        using var db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = "DataSource=:memory:",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        await db.Ado.BeginTranAsync();
        try
        {
            var result = await SetChildPurchasePriceMutationLock.AcquireProductsPartiallyAsync(
                db,
                new[] { " b ", "A", "a" }
            );

            Assert.Empty(result.BusyProductCodes);
            result.LockScope.EnsureCovers(db, new[] { "A", "B" });
            Assert.Throws<InvalidOperationException>(() =>
            {
                result.LockScope.EnsureCovers(db, new[] { "C" });
            });
        }
        finally
        {
            await db.Ado.RollbackTranAsync();
        }
    }

    [Fact]
    public void TryResolveConflict_识别事务包装后的业务锁异常()
    {
        var original = new SetChildPurchasePriceLockException("resource", -1);
        var wrapped = new InvalidOperationException("transaction failed", original);

        var matched = SetChildPurchasePriceMutationLock.TryResolveConflict(
            wrapped,
            out var conflict
        );

        Assert.True(matched);
        Assert.Same(original, conflict);
        Assert.Equal(
            "SET_CHILD_PURCHASE_PRICE_BUSY",
            SetChildPurchasePriceMutationLock.BusyErrorCode
        );
    }

    [Fact]
    public void TryResolveConflict_普通异常不识别为锁冲突()
    {
        var matched = SetChildPurchasePriceMutationLock.TryResolveConflict(
            new InvalidOperationException("ordinary"),
            out var conflict
        );

        Assert.False(matched);
        Assert.Null(conflict);
    }

    [Fact]
    public void TryResolveConflict_普通取消不识别为业务锁冲突()
    {
        var matched = SetChildPurchasePriceMutationLock.TryResolveConflict(
            new InvalidOperationException(
                "outer",
                new OperationCanceledException("request cancelled")
            ),
            out var conflict
        );

        Assert.False(matched);
        Assert.Null(conflict);
    }

    [Fact]
    public void TryResolveConflictResultCode_专用锁异常保留结果码()
    {
        var matched = SetChildPurchasePriceMutationLock.TryResolveConflictResultCode(
            new InvalidOperationException(
                "outer",
                new SetChildPurchasePriceLockException("resource", -2)
            ),
            out var resultCode
        );

        Assert.True(matched);
        Assert.Equal(-2, resultCode);
    }

    [Theory]
    [InlineData(1205, false, true, -3)]
    [InlineData(1222, false, true, -1)]
    [InlineData(-2, false, false, 0)]
    [InlineData(-2, true, true, -1)]
    [InlineData(50000, true, false, 0)]
    public void TryResolveSqlConflictResultCode_只在取业务锁阶段识别命令超时(
        int sqlErrorNumber,
        bool includeCommandTimeout,
        bool expectedMatched,
        int expectedResultCode
    )
    {
        var matched = SetChildPurchasePriceMutationLock.TryResolveSqlConflictResultCode(
            sqlErrorNumber,
            includeCommandTimeout,
            out var resultCode
        );

        Assert.Equal(expectedMatched, matched);
        Assert.Equal(expectedResultCode, resultCode);
    }
}
