using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class DataSyncLegacyControllerProductSyncContractTests
{
    [Fact]
    public void 商品同步BUSY响应_零成功返回409_部分成功保留错误码并返回200()
    {
        var source = ReadControllerSource();

        Assert.Equal(
            2,
            CountOccurrences(
                source,
                "return result.TotalCount == 0 ? Conflict(response) : Ok(response);"
            )
        );
        Assert.Equal(
            2,
            CountOccurrences(
                source,
                "result.ErrorCode == SetChildPurchasePriceMutationLock.BusyErrorCode"
            )
        );
        Assert.Equal(
            2,
            CountOccurrences(
                source,
                "ApiResponse<SyncResult>.Error(\n                            result.Message,\n                            result.ErrorCode,\n                            result"
            )
        );
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string ReadControllerSource()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "services/backend/BlazorApp.Api/Controllers/DataSyncController.cs"
            );
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            current = current.Parent;
        }

        throw new FileNotFoundException("未找到 DataSyncController.cs");
    }
}
