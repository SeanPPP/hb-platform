using BlazorApp.Api.Services.React;
using Microsoft.Data.Sqlite;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class CashierBarcodeMutationLockTests
{
    [Fact]
    public async Task AcquireAsync_SQLite无需应用锁即可继续()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });

        await CashierBarcodeMutationLock.AcquireAsync(db);
    }

    [Fact]
    public async Task 所有条码写入口共用同一事务级全局锁()
    {
        var root = FindRepoRoot();
        var lockSource = await ReadAsync(root,
            "services/backend/BlazorApp.Api/Services/React/CashierBarcodeMutationLock.cs");
        var employee = await ReadAsync(root,
            "services/backend/BlazorApp.Api/Services/EmployeeCashierBarcodeService.cs");
        var legacy = await ReadAsync(root,
            "services/backend/BlazorApp.Api/Services/React/CashRegisterUserReactService.cs");
        var full = await ReadAsync(root,
            "services/backend/BlazorApp.Api/Services/React/DataSyncFullService.cs");
        var incremental = await ReadAsync(root,
            "services/backend/BlazorApp.Api/Services/React/DataSyncIncrementalService.cs");

        Assert.Equal("CashierBarcodeMutation", CashierBarcodeMutationLock.ResourceName);
        Assert.Contains("@LockOwner = 'Transaction'", lockSource);
        Assert.Contains("@Resource = @resource", lockSource);

        AssertTransactionThenLock(employee, "RefreshOnceAsync", "Insertable(new CashierBarcodeReservation");
        AssertTransactionThenLock(legacy, "CreateAsync", "Insertable(new CashierBarcodeReservation");
        AssertTransactionThenLock(legacy, "UpdateAsync", "Insertable(new CashierBarcodeReservation");
        AssertTransactionThenLock(full, "SyncCashRegisterUsersFromHqAsync", "ValidateAndReserveHqBatchAsync");
        AssertTransactionThenLock(incremental, "SyncCashRegisterUsersFromHqIncrementalAsync", "ValidateAndReserveHqBatchAsync");
    }

    private static void AssertTransactionThenLock(string source, string method, string guardedOperation)
    {
        var methodIndex = source.IndexOf(method, StringComparison.Ordinal);
        var transactionIndex = source.IndexOf("BeginTranAsync", methodIndex, StringComparison.Ordinal);
        var lockIndex = source.IndexOf(
            "CashierBarcodeMutationLock.AcquireAsync",
            transactionIndex,
            StringComparison.Ordinal);
        var operationIndex = source.IndexOf(guardedOperation, lockIndex, StringComparison.Ordinal);

        Assert.True(methodIndex >= 0, $"未找到入口 {method}");
        Assert.True(transactionIndex > methodIndex, $"{method} 必须先开启事务");
        Assert.True(lockIndex > transactionIndex, $"{method} 必须在事务开始后获取全局锁");
        Assert.True(operationIndex > lockIndex, $"{method} 必须先获取全局锁再执行条码跨表操作");
    }

    private static Task<string> ReadAsync(string root, string relativePath) =>
        File.ReadAllTextAsync(Path.Combine(root, relativePath));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName,
                    "services/backend/BlazorApp.Api/Program.cs")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("未找到仓库根目录");
    }
}
