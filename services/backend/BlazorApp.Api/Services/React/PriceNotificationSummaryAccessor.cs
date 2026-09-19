using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services.React;

/// <summary>
/// 请求级通知汇总。同一请求内同一「分店 + 商品」可能被评估多次
/// （先被自动下发登记、再被审计挂钩复核），因此按键保留最后一次结果，读取时再计数，避免重复统计。
/// </summary>
public sealed class PriceNotificationSummaryAccessor : IPriceNotificationSummaryAccessor
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Store, string Product), PriceNotificationOutcome> _outcomes = new();
    private bool _evaluated;

    public void Record(string storeCode, string productCode, PriceNotificationOutcome outcome)
    {
        lock (_gate)
        {
            _evaluated = true;
            var key = (storeCode.Trim().ToUpperInvariant(), productCode.Trim().ToUpperInvariant());
            // 已经得到"有通知"的结论后，不让随后的"无变化"复核把它覆盖成 None。
            if (
                outcome == PriceNotificationOutcome.None
                && _outcomes.TryGetValue(key, out var existing)
                && existing != PriceNotificationOutcome.None
            )
            {
                return;
            }
            _outcomes[key] = outcome;
        }
    }

    public PriceNotificationSummaryDto? GetSummary()
    {
        lock (_gate)
        {
            if (!_evaluated)
            {
                return null;
            }

            return new PriceNotificationSummaryDto
            {
                ProductCount = _outcomes.Keys.Select(key => key.Product).Distinct().Count(),
                NeedsPriceUpdateStores = _outcomes.Values.Count(v => v == PriceNotificationOutcome.NeedsPriceUpdate),
                LabelOnlyStores = _outcomes.Values.Count(v => v == PriceNotificationOutcome.LabelOnly),
                CancelledStores = _outcomes.Values.Count(v => v == PriceNotificationOutcome.Cancelled),
                SkippedSpecialStores = _outcomes.Values.Count(v => v == PriceNotificationOutcome.SkippedSpecial),
            };
        }
    }
}
