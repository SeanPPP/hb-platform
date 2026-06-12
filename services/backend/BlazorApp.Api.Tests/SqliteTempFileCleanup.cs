namespace BlazorApp.Api.Tests;

internal static class SqliteTempFileCleanup
{
    public static void DeleteIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // SQLite 在 Windows 上可能短暂持有临时库文件句柄，清理失败不应覆盖业务断言结果。
        }
        catch (UnauthorizedAccessException)
        {
            // 防止文件句柄释放延迟导致测试清理阶段误报失败。
        }
    }
}
