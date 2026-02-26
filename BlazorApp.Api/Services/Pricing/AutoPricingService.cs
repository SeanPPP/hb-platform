using BlazorApp.Api.Data;
using BlazorApp.Shared.Models.HBweb;
using SqlSugar;

namespace BlazorApp.Api.Services.Pricing
{
    public class AutoPricingService
    {
        private readonly SqlSugarContext _context;

        public AutoPricingService(SqlSugarContext context)
        {
            _context = context;
        }

        /// <summary>
        /// 查找适用的定价策略
        /// 优先级：供应商 > 分店 > 全局
        /// </summary>
        public async Task<PricingStrategy?> FindStrategyAsync(
            string? supplierCode,
            string? storeCode
        )
        {
            var strategies = await _context
                .PricingStrategyDb.AsQueryable()
                .Includes(s => s.Details)
                .Includes(s => s.Targets)
                .Where(s => s.IsEnabled)
                .ToListAsync();

            if (!string.IsNullOrEmpty(supplierCode))
            {
                var supplierStrategy = strategies
                    .Where(s =>
                        (
                            s.Targets?.Any(t =>
                                t.TargetType == "Supplier" && t.TargetCode == supplierCode
                            ) ?? false
                        ) || (s.Level == "Supplier" && s.TargetCode == supplierCode)
                    )
                    .OrderByDescending(s => s.Priority)
                    .FirstOrDefault();
                if (supplierStrategy != null)
                    return supplierStrategy;
            }

            if (!string.IsNullOrEmpty(storeCode))
            {
                var storeStrategy = strategies
                    .Where(s =>
                        (
                            s.Targets?.Any(t =>
                                t.TargetType == "Store" && t.TargetCode == storeCode
                            ) ?? false
                        ) || (s.Level == "Store" && s.TargetCode == storeCode)
                    )
                    .OrderByDescending(s => s.Priority)
                    .FirstOrDefault();
                if (storeStrategy != null)
                    return storeStrategy;
            }

            return strategies
                .Where(s => s.Level == "Global" || (s.Targets == null || s.Targets.Count == 0))
                .OrderByDescending(s => s.Priority)
                .FirstOrDefault();
        }

        /// <summary>
        /// 先按进货价命中的区间过滤策略，再按等级（Supplier > Store > Global）与Priority选择
        /// </summary>
        public async Task<PricingStrategy?> FindStrategyForPriceAsync(
            decimal purchasePrice,
            string? supplierCode,
            string? storeCode
        )
        {
            // 使用 LeftJoin 扁平化查询并在内存组装 Details/Targets，避免导航加载问题
            var flatRows = await _context
                .PricingStrategyDb.AsQueryable()
                .LeftJoin<PricingStrategyDetail>((s, d) => s.Id == d.StrategyId)
                .LeftJoin<PricingStrategyTarget>((s, d, t) => s.Id == t.StrategyId)
                .Where((s, d, t) => s.IsEnabled)
                .Select(
                    (s, d, t) =>
                        new
                        {
                            StrategyId = s.Id,
                            StrategyName = s.Name,
                            StrategyLevel = s.Level,
                            StrategyTargetCode = s.TargetCode,
                            StrategyPriority = s.Priority,
                            DetailId = d.Id,
                            DetailMinPrice = d.MinPrice,
                            DetailMaxPrice = d.MaxPrice,
                            DetailStartRate = d.StartRate,
                            DetailEndRate = d.EndRate,
                            DetailAlgorithm = d.Algorithm,
                            TargetId = t.Id,
                            TargetType = t.TargetType,
                            TargetCode = t.TargetCode,
                        }
                )
                .ToListAsync();

            var strategies = flatRows
                .GroupBy(r => new
                {
                    r.StrategyId,
                    r.StrategyName,
                    r.StrategyLevel,
                    r.StrategyTargetCode,
                    r.StrategyPriority,
                })
                .Select(g => new PricingStrategy
                {
                    Id = g.Key.StrategyId,
                    Name = g.Key.StrategyName,
                    Level = g.Key.StrategyLevel,
                    TargetCode = g.Key.StrategyTargetCode,
                    Priority = g.Key.StrategyPriority,
                    Details = g.Where(x => x.DetailId != null)
                        .GroupBy(x => x.DetailId)
                        .Select(gg => new PricingStrategyDetail
                        {
                            Id = gg.Key!,
                            StrategyId = g.Key.StrategyId,
                            MinPrice = gg.First().DetailMinPrice,
                            MaxPrice = gg.First().DetailMaxPrice,
                            StartRate = gg.First().DetailStartRate,
                            EndRate = gg.First().DetailEndRate,
                            Algorithm = gg.First().DetailAlgorithm,
                        })
                        .ToList(),
                    Targets = g.Where(x => x.TargetId != null)
                        .GroupBy(x => x.TargetId)
                        .Select(gg => new PricingStrategyTarget
                        {
                            Id = gg.Key!,
                            StrategyId = g.Key.StrategyId,
                            TargetType = gg.First().TargetType,
                            TargetCode = gg.First().TargetCode,
                        })
                        .ToList(),
                })
                .ToList();

            // 若未提供分店参数，仅保留“无 Store 目标”的策略（全局或仅供应商）
            if (string.IsNullOrEmpty(storeCode))
            {
                strategies = strategies
                    .Where(s => !(s.Targets?.Any(t => t.TargetType == "Store") ?? false))
                    .ToList();
            }

            // 仅保留命中区间的策略
            var matched = strategies
                .Where(s =>
                    (
                        s.Details?.Any(d =>
                            purchasePrice >= d.MinPrice && purchasePrice <= d.MaxPrice
                        ) ?? false
                    )
                )
                .ToList();

            // 1) 同时命中：供应商 + 分店
            if (!string.IsNullOrEmpty(supplierCode) && !string.IsNullOrEmpty(storeCode))
            {
                var bothStrategy = matched
                    .Where(s =>
                        (
                            s.Targets?.Any(t =>
                                t.TargetType == "Supplier" && t.TargetCode == supplierCode
                            ) ?? false
                        )
                        && (
                            s.Targets?.Any(t =>
                                t.TargetType == "Store" && t.TargetCode == storeCode
                            ) ?? false
                        )
                    )
                    .OrderByDescending(s => s.Priority)
                    .FirstOrDefault();
                if (bothStrategy != null)
                    return bothStrategy;
            }

            // 2) 供应商单独命中
            if (!string.IsNullOrEmpty(supplierCode))
            {
                var supplierStrategy = matched
                    .Where(s =>
                        s.Targets?.Any(t =>
                            t.TargetType == "Supplier" && t.TargetCode == supplierCode
                        ) ?? false
                    )
                    .OrderByDescending(s => s.Priority)
                    .FirstOrDefault();
                if (supplierStrategy != null)
                    return supplierStrategy;
            }

            // 3) 分店单独命中
            if (!string.IsNullOrEmpty(storeCode))
            {
                var storeStrategy = matched
                    .Where(s =>
                        s.Targets?.Any(t => t.TargetType == "Store" && t.TargetCode == storeCode)
                        ?? false
                    )
                    .OrderByDescending(s => s.Priority)
                    .FirstOrDefault();
                if (storeStrategy != null)
                    return storeStrategy;
            }

            // 全局最后
            return matched
                .Where(s => s.Level == "Global" || (s.Targets == null || s.Targets.Count == 0))
                .OrderByDescending(s => s.Priority)
                .FirstOrDefault();
        }

        /// <summary>
        /// 计算倍率（rate）
        /// </summary>
        public decimal CalculateRate(decimal purchasePrice, PricingStrategy? strategy)
        {
            if (strategy == null || strategy.Details == null || !strategy.Details.Any())
                return 2.5m;

            // 找到匹配的区间
            // 假设区间是 [Min, Max) 或者闭区间，这里使用 Min <= p <= Max
            // 如果有重叠，取第一个匹配的
            var rule = strategy.Details.FirstOrDefault(d =>
                purchasePrice >= d.MinPrice && purchasePrice <= d.MaxPrice
            );

            if (rule == null)
                return 2.5m;

            decimal rate = 1.0m;

            switch (rule.Algorithm?.ToLower())
            {
                case "linear": // 线性插值
                    if (rule.MaxPrice == rule.MinPrice)
                    {
                        rate = rule.StartRate;
                    }
                    else
                    {
                        // rate = start + (end - start) * (p - min) / (max - min)
                        decimal ratio =
                            (purchasePrice - rule.MinPrice) / (rule.MaxPrice - rule.MinPrice);
                        rate = rule.StartRate + (rule.EndRate - rule.StartRate) * ratio;
                    }
                    break;

                case "exponential": // 指数插值
                    if (rule.MaxPrice == rule.MinPrice)
                    {
                        rate = rule.StartRate;
                    }
                    else
                    {
                        // rate = start * (end/start) ^ ratio
                        // 注意：如果 StartRate 或 EndRate <= 0 会有问题
                        if (rule.StartRate <= 0 || rule.EndRate <= 0)
                        {
                            rate = rule.StartRate; // 降级处理
                        }
                        else
                        {
                            double ratio =
                                (double)(purchasePrice - rule.MinPrice)
                                / (double)(rule.MaxPrice - rule.MinPrice);
                            double r =
                                (double)rule.StartRate
                                * Math.Pow((double)rule.EndRate / (double)rule.StartRate, ratio);
                            rate = (decimal)r;
                        }
                    }
                    break;

                case "step": // 阶梯/固定
                default:
                    rate = rule.StartRate; // 直接使用起始浮率
                    break;
            }

            return rate;
        }

        /// <summary>
        /// 计算建议零售价
        /// </summary>
        public decimal CalculateRetailPrice(decimal purchasePrice, PricingStrategy? strategy)
        {
            var rate = CalculateRate(purchasePrice, strategy);
            decimal retailPrice = purchasePrice * rate;

            // 兜底保护：零售价不能低于进货价
            if (retailPrice < purchasePrice)
            {
                retailPrice = purchasePrice;
            }

            // 心理价规则：
            // - 0.5 倍数出现：小数部分 <= 0.5 调整到 .50；大于 0.5 进位到 .99
            // - 1 和 2 两个整数保留不变
            // - 其它整数（3、4、...）若恰好为整数则减少 0.01 到 .99
            decimal adjusted = retailPrice;
            if (adjusted <= 0.5m)
            {
                adjusted = 0.5m;
            }
            else
            {
                int integer = (int)Math.Floor(adjusted);
                decimal frac = adjusted - integer;

                if (frac == 0m)
                {
                    if (integer == 1 || integer == 2)
                    {
                        adjusted = integer;
                    }
                    else
                    {
                        adjusted = Math.Round(integer - 0.01m, 2);
                    }
                }
                else if (frac <= 0.5m)
                {
                    adjusted = integer + 0.5m;
                }
                else
                {
                    if ((integer + 1) == 1 || (integer + 1) == 2)
                    {
                        adjusted = integer + 1m;
                    }
                    else
                    {
                        adjusted = integer + 0.99m;
                    }
                }
            }

            // 再次兜底：不低于进货价
            if (adjusted < purchasePrice)
            {
                adjusted = purchasePrice;
            }

            return Math.Round(adjusted, 2);
        }

        /// <summary>
        /// 便捷方法：直接计算
        /// </summary>
        public async Task<decimal> GetAutoRetailPriceAsync(
            decimal purchasePrice,
            string? supplierCode,
            string? storeCode
        )
        {
            var strategy = await FindStrategyForPriceAsync(purchasePrice, supplierCode, storeCode);
            if (strategy == null)
                return purchasePrice;
            return CalculateRetailPrice(purchasePrice, strategy);
        }
    }
}
