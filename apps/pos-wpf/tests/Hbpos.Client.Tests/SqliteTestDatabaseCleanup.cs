using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Tests;

/// <summary>
/// 测试临时 SQLite 库的共享清理逻辑。Windows 上 Microsoft.Data.Sqlite 在连接 Dispose 后
/// 可能极短时间仍持有文件句柄，直接 <see cref="File.Delete(string)"/> 会随机抛出
/// "being used by another process"，把已经通过的行为断言变成清理阶段的失败。
/// </summary>
internal static class SqliteTestDatabaseCleanup
{
    private const int MaxAttempts = 20;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// 先清理连接池，再按文件状态重试删除主库与 WAL/SHM 附属文件；
    /// 最终仍被系统持有时采用 best-effort，不让测试清理覆盖已通过的断言。
    /// </summary>
    public static async Task DeleteDatabaseFilesAsync(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = databasePath + suffix;
            for (var attempt = 0; attempt < MaxAttempts && File.Exists(path); attempt++)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException) when (attempt < MaxAttempts - 1)
                {
                    await Task.Delay(RetryDelay);
                }
                catch (IOException)
                {
                    // 临时库最终仍被系统持有时采用 best-effort，不能覆盖已经通过的行为断言。
                    break;
                }
                catch (UnauthorizedAccessException)
                {
                    // 测试清理权限竞态不作为业务失败。
                    break;
                }
            }
        }
    }
}
