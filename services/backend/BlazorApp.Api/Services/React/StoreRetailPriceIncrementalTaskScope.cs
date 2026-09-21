using System.Text.Json;
using BlazorApp.Shared.Models.HBweb;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 分店零售价增量任务日志的范围标记。
    /// 页面同步、旧接口的局部同步和旧接口的全分店水位续跑共用同一个任务类型，
    /// 只有“全分店、未指定起止日期”的成功运行才能作为下一次增量的水位，
    /// 因此写日志时必须把范围记下来，读水位时据此筛选。
    /// </summary>
    public static class StoreRetailPriceIncrementalTaskScope
    {
        public const string ScopeParameterKey = "StoreRetailPriceSyncScope";
        public const string EffectiveStartParameterKey = "EffectiveStart";

        /// <summary>全分店、未指定起止日期、从水位（或默认窗口）续跑，可作为下一次的水位。</summary>
        public const string AllStoresFromWatermark = "AllStoresFromWatermark";

        /// <summary>指定了分店或起止日期的局部同步，不能作为水位。</summary>
        public const string Scoped = "Scoped";

        /// <summary>
        /// 用于 SQL 预筛选的 JSON 片段。System.Text.Json 默认输出不含空白，
        /// 片段里也没有 LIKE 通配符（% _ [），可以直接做包含匹配。
        /// </summary>
        public static readonly string WatermarkEligibleJsonFragment =
            $"\"{ScopeParameterKey}\":\"{AllStoresFromWatermark}\"";

        public static TaskParameters BuildWatermarkParameters(DateTime? effectiveStart)
        {
            var custom = new Dictionary<string, object>
            {
                [ScopeParameterKey] = AllStoresFromWatermark,
            };
            if (effectiveStart.HasValue)
            {
                // 仅供排查使用；StartDate 保持为空，表示请求方没有指定起始日期。
                custom[EffectiveStartParameterKey] = effectiveStart.Value.ToString("o");
            }

            return new TaskParameters { CustomParameters = custom };
        }

        public static TaskParameters BuildScopedParameters(
            List<string>? selectedStoreCodes,
            DateTime? startDate,
            DateTime? endDate
        )
        {
            var branchCodes = NormalizeStoreCodes(selectedStoreCodes);
            return new TaskParameters
            {
                BranchCodes = branchCodes.Count > 0 ? branchCodes : null,
                StartDate = startDate?.ToString("o"),
                EndDate = endDate?.ToString("o"),
                CustomParameters = new Dictionary<string, object>
                {
                    [ScopeParameterKey] = Scoped,
                },
            };
        }

        public static List<string> NormalizeStoreCodes(List<string>? selectedStoreCodes)
        {
            return selectedStoreCodes?
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                ?? new List<string>();
        }

        /// <summary>
        /// 判断一条任务日志能否作为全分店水位。
        /// 除了范围标记，还要求分店与起止日期都为空，标记与参数矛盾时按不可用处理。
        /// </summary>
        public static bool IsWatermarkEligible(ScheduledTaskLog taskLog)
        {
            var parameters = taskLog.GetParameters();
            if (parameters.BranchCodes?.Count > 0
                || !string.IsNullOrWhiteSpace(parameters.StartDate)
                || !string.IsNullOrWhiteSpace(parameters.EndDate))
            {
                return false;
            }

            return string.Equals(
                ReadScope(parameters),
                AllStoresFromWatermark,
                StringComparison.Ordinal
            );
        }

        /// <summary>
        /// 旧格式日志：没有范围标记，无法判断当时是全分店还是局部同步。
        /// </summary>
        public static bool IsLegacyUnscoped(ScheduledTaskLog taskLog)
        {
            return ReadScope(taskLog.GetParameters()) == null;
        }

        private static string? ReadScope(TaskParameters parameters)
        {
            if (parameters.CustomParameters == null
                || !parameters.CustomParameters.TryGetValue(ScopeParameterKey, out var value))
            {
                return null;
            }

            // 反序列化后 Dictionary<string, object> 的值是 JsonElement，刚构造的则是 string。
            return value switch
            {
                string text => text,
                JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
                _ => null,
            };
        }
    }
}
