using System.Runtime.ExceptionServices;
using BlazorApp.Shared.Constants;

namespace BlazorApp.Api.Services;

/// <summary>销售统计统一按悉尼业务日解析日期，避免 UTC 容器在澳洲上午仍刷新前一天。</summary>
internal static class SalesStatisticsBusinessDate
{
    private static readonly TimeZoneInfo BusinessTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById(StoreTimeZonePolicy.Sydney);

    internal static DateTime Resolve(DateTimeOffset utcNow) =>
        TimeZoneInfo.ConvertTime(utcNow, BusinessTimeZone).Date;
}

/// <summary>跨切片共享的事务执行器；始终保留最初的业务或提交异常。</summary>
internal static class SalesStatisticsTransactionExecutor
{
    internal static async Task ExecuteAsync(
        Func<Task> beginAsync,
        Func<Task> workAsync,
        Func<Task> commitAsync,
        Func<Task> rollbackAsync,
        ILogger logger,
        string operationName)
    {
        await beginAsync();

        Exception? originalException = null;
        try
        {
            await workAsync();
            try
            {
                await commitAsync();
            }
            catch (Exception commitException)
            {
                // 提交异常是主异常；后续回滚异常只能记录，不能覆盖它。
                originalException = commitException;
                logger.LogError(commitException, "{OperationName} 提交事务失败，准备尝试回滚", operationName);
                throw;
            }
        }
        catch (Exception ex)
        {
            originalException ??= ex;
            try
            {
                await rollbackAsync();
            }
            catch (Exception rollbackException)
            {
                logger.LogError(
                    rollbackException,
                    "{OperationName} 回滚事务失败，将保留原始异常继续抛出",
                    operationName
                );
            }

            ExceptionDispatchInfo.Capture(originalException).Throw();
            throw;
        }
    }
}

/// <summary>销售统计共享的代码规范化与分店解析规则。</summary>
internal static class SalesStatisticsCodeRules
{
    internal const string UnknownSupplierCode = "UNKNOWN";

    internal static string Normalize(string? code) => code?.Trim() ?? string.Empty;

    internal static string ResolveBranchCode(
        string? branchCode,
        string? deviceCode,
        IReadOnlyDictionary<string, string> deviceBranchMap)
    {
        if (!string.IsNullOrWhiteSpace(branchCode))
            return branchCode.Trim();

        var normalizedDeviceCode = Normalize(deviceCode);
        if (!string.IsNullOrWhiteSpace(normalizedDeviceCode)
            && deviceBranchMap.TryGetValue(normalizedDeviceCode, out var mappedBranch))
        {
            return mappedBranch?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    internal static List<string> NormalizeBranchCodes(List<string>? branchCodes) =>
        NormalizeCodes(branchCodes);

    internal static List<string> NormalizeSupplierCodes(List<string>? supplierCodes) =>
        NormalizeCodes(supplierCodes);

    private static List<string> NormalizeCodes(List<string>? codes) =>
        codes?
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];
}
