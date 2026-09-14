using System.Security.Cryptography;
using System.Text;
using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services;

internal sealed class HourlySalesPublicationUnavailableException(string message)
    : InvalidOperationException(message);

/// <summary>统一分时读取来源：认证日期整日使用发布版本，其余日期使用原统计。</summary>
internal static class HourlySalesStatisticsReadSource
{
    internal static string GetTableName(SqlSugarContext context)
    {
        return HourlySalesBackfillProtection.GetSchemaState(context) switch
        {
            HourlySalesBackfillSchemaState.Absent => "HourlySalesStatistic",
            HourlySalesBackfillSchemaState.Ready => "HourlySalesReadStatistic",
            _ => throw new HourlySalesPublicationUnavailableException("分时发布结构不完整，暂时无法读取认证统计"),
        };
    }

    internal static async Task<List<HourlySalesBackfillDay>> ReadAppliedAsync(
        SqlSugarContext context, DateTime start, DateTime end,
        DateTime? compareStart = null, DateTime? compareEnd = null,
        CancellationToken token = default)
    {
        if (GetTableName(context) == "HourlySalesStatistic") return [];
        var hasCompare = compareStart.HasValue && compareEnd.HasValue;
        // 只读取 manifest；不把包含完整候选/前像的大 JSON 带进报表请求。
        var ruleVersion = HourlySalesBackfillRules.Version;
        return await context.Db.Queryable<HourlySalesBackfillDay>()
            .LeftJoin<HourlySalesBackfillBatch>((day, batch) => day.BatchId == batch.Id)
            .Where((day, batch) => day.Status == "Applied"
                && ((day.Date >= start && day.Date <= end)
                    || (hasCompare && day.Date >= compareStart!.Value && day.Date <= compareEnd!.Value)))
            .Select((day, batch) => new HourlySalesBackfillDay
            {
                BatchId = day.BatchId, Date = day.Date, Status = day.Status,
                AfterHash = day.AfterHash,
                Error = batch.RuleVersion == ruleVersion ? day.Error : "unsupported-rule-version",
                UpdatedAtUtc = day.UpdatedAtUtc,
            }).ToListAsync(token);
    }

    internal static async Task<string> GetVersionAsync(SqlSugarContext context, DateRangeDto range)
    {
        var rows = await ReadAppliedAsync(context, range.StartDate.Date, range.EndDate.Date,
            range.CompareStartDate?.Date, range.CompareEndDate?.Date);
        var value = string.Join("|", rows.OrderBy(day => day.Date).Select(day =>
            $"{day.Date:yyyyMMdd}:{day.BatchId:N}:{day.AfterHash}:{day.Error}:{day.UpdatedAtUtc.Ticks}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
