using BlazorApp.Shared.DTOs;
using System.Text.RegularExpressions;

namespace BlazorApp.Api.Services.Performance;

public static class PerformanceMetricBatchValidator
{
    public const int MaxEvents = 200;
    public const int MaxDimensions = 10;

    private static readonly IReadOnlySet<string> AllowedDimensions = new HashSet<string>(StringComparer.Ordinal)
    {
        "metricId",
        "route",
        "method",
        "statusClass",
        "environment",
        "instance",
        "app",
        "version",
        "channel",
        "store",
        "paymentType",
        "outcome",
        "databaseContext",
        "sqlFingerprint",
        "sqlTemplate",
        "taskType",
        "operation",
        "lane",
        "component",
        "source",
        "release",
        "dist",
        "project",
        "action",
    };

    private static readonly IReadOnlyDictionary<string, string> MetricUnits =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PerformanceMetricNames.ApiRequestDuration] = PerformanceMetricUnits.Milliseconds,
            [PerformanceMetricNames.SqlCommandDuration] = PerformanceMetricUnits.Milliseconds,
            [PerformanceMetricNames.HqSyncDuration] = PerformanceMetricUnits.Milliseconds,
            [PerformanceMetricNames.HqSyncSuccessRate] = PerformanceMetricUnits.Ratio,
            [PerformanceMetricNames.HqSyncFailureRate] = PerformanceMetricUnits.Ratio,
            [PerformanceMetricNames.HqSyncBacklog] = PerformanceMetricUnits.Count,
            [PerformanceMetricNames.BackgroundJobDuration] = PerformanceMetricUnits.Milliseconds,
            [PerformanceMetricNames.BackgroundJobSuccessRate] = PerformanceMetricUnits.Ratio,
            [PerformanceMetricNames.BackgroundJobFailureRate] = PerformanceMetricUnits.Ratio,
            [PerformanceMetricNames.WebFirstScreenBytes] = PerformanceMetricUnits.Bytes,
            [PerformanceMetricNames.WebLargestInitialChunkBytes] = PerformanceMetricUnits.Bytes,
            [PerformanceMetricNames.WebTableReactCommit] = PerformanceMetricUnits.Milliseconds,
            [PerformanceMetricNames.WebTableRenderToPaint] = PerformanceMetricUnits.Milliseconds,
            [PerformanceMetricNames.PosColdStart] = PerformanceMetricUnits.Milliseconds,
            [PerformanceMetricNames.PosScanToCart] = PerformanceMetricUnits.Milliseconds,
            [PerformanceMetricNames.PosPaymentResponse] = PerformanceMetricUnits.Milliseconds,
            [PerformanceMetricNames.SentryCrashFreeSession] = PerformanceMetricUnits.Ratio,
            [PerformanceMetricNames.CiRunDuration] = PerformanceMetricUnits.Milliseconds,
        };

    private static readonly IReadOnlySet<string> ClientMetrics = new HashSet<string>(StringComparer.Ordinal)
    {
        PerformanceMetricNames.WebTableReactCommit,
        PerformanceMetricNames.WebTableRenderToPaint,
        PerformanceMetricNames.PosColdStart,
        PerformanceMetricNames.PosScanToCart,
        PerformanceMetricNames.PosPaymentResponse,
    };

    private static readonly IReadOnlySet<string> AutomationMetrics = new HashSet<string>(StringComparer.Ordinal)
    {
        PerformanceMetricNames.CiRunDuration,
        PerformanceMetricNames.WebFirstScreenBytes,
        PerformanceMetricNames.WebLargestInitialChunkBytes,
    };

    private static readonly IReadOnlySet<string> WebClientDimensions =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "environment",
            "metricId",
            "outcome",
        };

    private static readonly IReadOnlySet<string> PosClientDimensions =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "environment",
            "app",
            "version",
            "channel",
            "store",
            "outcome",
        };

    private static readonly IReadOnlySet<string> ClientEnvironments =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Production",
            "Staging",
            "Preview",
            "Development",
            "Test",
            "UAT",
        };

    private static readonly IReadOnlySet<string> ClientOutcomes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "success",
            "failure",
            "failed",
            "error",
            "rejected",
            "timeout",
        };

    private static readonly Regex SafeDimensionValue = new(
        "^[A-Za-z0-9][A-Za-z0-9._:@/-]*$",
        RegexOptions.CultureInvariant
    );
    private static readonly Regex SensitiveDimensionValue = new(
        "(?:bearer|password|passwd|secret|token|api[-_]?key|authorization)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );
    private static readonly Regex PaymentCardLikeValue = new(
        "(?<![0-9])[0-9]{13,19}(?![0-9])",
        RegexOptions.CultureInvariant
    );

    public static List<string> Validate(
        PerformanceMetricBatchV1Dto? request,
        DateTime utcNow,
        string? sourceType = null,
        string? projectCode = null
    )
    {
        var errors = new List<string>();
        if (request == null)
        {
            errors.Add("请求体不能为空");
            return errors;
        }

        if (request.SchemaVersion != 1)
        {
            errors.Add("schemaVersion 仅支持 1");
        }

        if (request.Events == null)
        {
            errors.Add("events 不能为空");
            return errors;
        }

        if (request.Events.Count is < 1 or > MaxEvents)
        {
            errors.Add($"events 数量必须在 1 到 {MaxEvents} 之间");
        }

        foreach (var item in request.Events.Take(MaxEvents + 1))
        {
            if (item == null)
            {
                errors.Add("events 不能包含空事件");
                continue;
            }

            if (item.EventId == Guid.Empty)
            {
                errors.Add("eventId 不能为空");
            }

            if (!PerformanceMetricNames.All.Contains(item.Metric))
            {
                errors.Add($"指标 {SafeIdentifier(item.Metric)} 不在白名单");
            }

            if (!PerformanceMetricUnits.All.Contains(item.Unit))
            {
                errors.Add($"单位 {SafeIdentifier(item.Unit)} 不受支持");
            }
            else if (
                !string.IsNullOrEmpty(item.Metric)
                && MetricUnits.TryGetValue(item.Metric, out var expectedUnit)
                && !string.Equals(item.Unit, expectedUnit, StringComparison.Ordinal)
            )
            {
                errors.Add(
                    $"指标 {SafeIdentifier(item.Metric)} 的单位必须是 {expectedUnit}"
                );
            }

            var normalizedSource = sourceType?.Trim().ToLowerInvariant();
            var sourceMetrics = normalizedSource switch
            {
                "client" => ClientMetrics,
                "ci" => AutomationMetrics,
                _ => null,
            };
            if (sourceMetrics != null && !sourceMetrics.Contains(item.Metric))
            {
                errors.Add(
                    $"指标 {SafeIdentifier(item.Metric)} 不允许从 {normalizedSource} 入口上报"
                );
            }

            if (!double.IsFinite(item.Value) || item.Value < 0)
            {
                errors.Add("value 必须是非负有限数值");
            }
            else if (item.Unit == PerformanceMetricUnits.Ratio && item.Value > 1)
            {
                errors.Add("ratio 指标 value 必须在 0 到 1 之间");
            }
            else if (
                item.Unit == PerformanceMetricUnits.Count
                && item.Value != Math.Truncate(item.Value)
            )
            {
                errors.Add("count 指标 value 必须是整数");
            }

            var observedAt = PerformanceUtc.Normalize(item.ObservedAt);
            if (observedAt > utcNow.AddMinutes(5))
            {
                errors.Add("observedAt 不能超过服务器时间未来 5 分钟");
            }
            if (observedAt < utcNow.AddDays(-30))
            {
                errors.Add("observedAt 不能早于过去 30 天");
            }

            if (item.Dimensions == null)
            {
                errors.Add("dimensions 不能为空");
                continue;
            }

            if (item.Dimensions.Count > MaxDimensions)
            {
                errors.Add($"dimensions 不能超过 {MaxDimensions} 个");
            }

            foreach (var dimension in item.Dimensions)
            {
                if (!AllowedDimensions.Contains(dimension.Key))
                {
                    errors.Add($"维度 {SafeIdentifier(dimension.Key)} 不在白名单");
                }
                else if (dimension.Value == null)
                {
                    errors.Add($"维度 {SafeIdentifier(dimension.Key)} 的值不能为空");
                }
                else if (
                    dimension.Key.Length > 64
                    || dimension.Value.Length > (dimension.Key == "sqlTemplate" ? 500 : 120)
                )
                {
                    errors.Add($"维度 {SafeIdentifier(dimension.Key)} 长度超限");
                }
                else if (
                    string.Equals(sourceType, "client", StringComparison.OrdinalIgnoreCase)
                    && dimension.Key
                        is "databaseContext" or "sqlFingerprint" or "sqlTemplate" or "instance"
                )
                {
                    errors.Add(
                        $"维度 {SafeIdentifier(dimension.Key)} 不允许从 client 入口上报"
                    );
                }
            }

            if (string.Equals(sourceType, "client", StringComparison.OrdinalIgnoreCase))
            {
                ValidateClientMetric(item, projectCode, errors);
            }
        }

        if (string.Equals(sourceType, "client", StringComparison.OrdinalIgnoreCase))
        {
            var environments = request.Events
                .Where(item => item?.Dimensions != null)
                .Select(item => item.Dimensions.GetValueOrDefault("environment"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .Count();
            if (environments > 1)
            {
                errors.Add("同一 client 批次只能包含一个 environment");
            }
        }

        return errors;
    }

    private static void ValidateClientMetric(
        PerformanceMetricEventV1Dto item,
        string? projectCode,
        ICollection<string> errors
    )
    {
        var project = projectCode?.Trim();
        var isWebMetric = item.Metric is PerformanceMetricNames.WebTableReactCommit
            or PerformanceMetricNames.WebTableRenderToPaint;
        var isPosMetric = item.Metric is PerformanceMetricNames.PosColdStart
            or PerformanceMetricNames.PosScanToCart
            or PerformanceMetricNames.PosPaymentResponse;

        var expectedApp = project switch
        {
            "hbpos_ipad" => "pos-ipad",
            "hbpos_handheld" => "pos-handheld",
            _ => null,
        };
        var projectMatchesMetric =
            (string.Equals(project, "hbweb_rv", StringComparison.Ordinal) && isWebMetric)
            || (expectedApp != null && isPosMetric);
        if (!projectMatchesMetric)
        {
            errors.Add("client 项目与指标组合不受支持");
        }

        var allowed = isWebMetric
            ? WebClientDimensions
            : PosClientDimensions;
        var required = isWebMetric
            ? new[] { "environment", "metricId", "outcome" }
            : item.Metric == PerformanceMetricNames.PosPaymentResponse
                ? new[] { "environment", "app", "version", "channel", "outcome", "paymentType" }
                : new[] { "environment", "app", "version", "channel", "outcome" };

        foreach (var key in item.Dimensions.Keys)
        {
            if (!allowed.Contains(key) && !(isPosMetric && key == "paymentType"))
            {
                errors.Add($"指标 {SafeIdentifier(item.Metric)} 不允许 client 维度 {SafeIdentifier(key)}");
            }
        }
        foreach (var key in required)
        {
            if (
                !item.Dimensions.TryGetValue(key, out var value)
                || string.IsNullOrWhiteSpace(value)
            )
            {
                errors.Add($"指标 {SafeIdentifier(item.Metric)} 缺少 client 维度 {key}");
            }
        }

        foreach (var dimension in item.Dimensions)
        {
            var value = dimension.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
            if (
                !SafeDimensionValue.IsMatch(value)
                || SensitiveDimensionValue.IsMatch(value)
                || PaymentCardLikeValue.IsMatch(value)
            )
            {
                errors.Add($"client 维度 {SafeIdentifier(dimension.Key)} 的值格式不受支持");
            }
        }

        if (
            item.Dimensions.TryGetValue("environment", out var environment)
            && !ClientEnvironments.Contains(environment)
        )
        {
            errors.Add("client 维度 environment 不在允许环境列表");
        }
        if (
            item.Dimensions.TryGetValue("outcome", out var outcome)
            && !ClientOutcomes.Contains(outcome)
        )
        {
            errors.Add("client 维度 outcome 不在允许结果列表");
        }
        if (
            expectedApp != null
            && item.Dimensions.TryGetValue("app", out var app)
            && !string.Equals(app, expectedApp, StringComparison.Ordinal)
        )
        {
            errors.Add("client 维度 app 与项目不匹配");
        }
    }

    private static string SafeIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        return value.Length <= 80 ? value : value[..80];
    }
}
