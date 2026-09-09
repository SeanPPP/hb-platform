using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Models.HBweb;
using SqlSugar;

namespace BlazorApp.Api.Services.Pricing
{
    public class AutoPricingService : IAutoPricingService
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
            var strategies = await GetAllActiveStrategiesAsync();
            return FindScopedStrategy(strategies, supplierCode, storeCode);
        }

        /// <summary>
        /// 先按进货价过滤，再按供应商和分店的实际目标范围选择策略。
        /// </summary>
        public async Task<PricingStrategy?> FindStrategyForPriceAsync(
            decimal purchasePrice,
            string? supplierCode,
            string? storeCode
        )
        {
            var strategies = await GetAllActiveStrategiesAsync();
            var matched = strategies
                .Where(s => s.Details?.Any(d =>
                    purchasePrice >= d.MinPrice && purchasePrice <= d.MaxPrice
                ) ?? false)
                .ToList();

            return FindScopedStrategy(matched, supplierCode, storeCode);
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

        /// <summary>
        /// 获取所有启用的定价策略（包含明细和目标），用于批量计算时预加载避免N+1查询
        /// </summary>
        public async Task<List<PricingStrategy>> GetAllActiveStrategiesAsync()
        {
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

            // 旧单目标数据仅在内存中补齐 Targets，供手机和进货单按同一范围分组，不写回数据库。
            // 已有新 Targets 时以新配置为准，避免陈旧的 TargetCode 扩大适用范围。
            foreach (var strategy in strategies)
            {
                if (strategy.Targets.Count == 0
                    && strategy.Level is "Supplier" or "Store"
                    && !string.IsNullOrWhiteSpace(strategy.TargetCode))
                {
                    strategy.Targets.Add(new PricingStrategyTarget
                    {
                        Id = $"legacy:{strategy.Id}",
                        StrategyId = strategy.Id,
                        TargetType = strategy.Level,
                        TargetCode = strategy.TargetCode,
                    });
                }
            }

            return strategies;
        }

        /// <summary>
        /// 在内存中查找最佳定价策略（用于批量计算时避免N+1查询）
        /// 优先级：供应商+分店 > 供应商 > 分店 > 全局
        /// </summary>
        public PricingStrategy? FindBestStrategyForPrice(
            decimal purchasePrice,
            List<PricingStrategy> supplierStrategies,
            List<PricingStrategy> storeStrategies,
            List<PricingStrategy> globalStrategies
        )
        {
            List<PricingStrategy> MatchByPrice(List<PricingStrategy> list)
            {
                return list
                    .Where(s =>
                        s.Details?.Any(d =>
                            purchasePrice >= d.MinPrice && purchasePrice <= d.MaxPrice
                        ) ?? false
                    )
                    .ToList();
            }

            return SelectScopedStrategy(
                MatchByPrice(supplierStrategies),
                MatchByPrice(storeStrategies),
                MatchByPrice(globalStrategies)
            );
        }

        private static PricingStrategy? FindScopedStrategy(
            List<PricingStrategy> strategies,
            string? supplierCode,
            string? storeCode
        )
        {
            return SelectScopedStrategy(
                strategies.Where(s => MatchesTarget(s, "Supplier", supplierCode)).ToList(),
                strategies.Where(s => MatchesTarget(s, "Store", storeCode)).ToList(),
                strategies.Where(s => s.Level == "Global" || s.Targets == null || s.Targets.Count == 0).ToList()
            );
        }

        private static PricingStrategy? SelectScopedStrategy(
            List<PricingStrategy> supplierStrategies,
            List<PricingStrategy> storeStrategies,
            List<PricingStrategy> globalStrategies
        )
        {
            // 同类目标允许命中任意一个；同时配置供应商和分店时，两类目标必须都命中同一策略。
            var matchedStoreIds = storeStrategies.Select(s => s.Id).ToHashSet();
            var bothStrategy = supplierStrategies
                .Where(s => HasTarget(s, "Store") && matchedStoreIds.Contains(s.Id))
                .OrderByDescending(s => s.Priority)
                .FirstOrDefault();
            if (bothStrategy != null)
                return bothStrategy;

            // 单类回退不能重新捞回另一类目标不匹配的混合策略。
            var supplierStrategy = supplierStrategies
                .Where(s => !HasTarget(s, "Store"))
                .OrderByDescending(s => s.Priority)
                .FirstOrDefault();
            if (supplierStrategy != null)
                return supplierStrategy;

            var storeStrategy = storeStrategies
                .Where(s => !HasTarget(s, "Supplier"))
                .OrderByDescending(s => s.Priority)
                .FirstOrDefault();
            if (storeStrategy != null)
                return storeStrategy;

            // 级别为全局或旧数据没有目标行，也不能绕过其已经配置的范围限制。
            return globalStrategies
                .Where(s => !HasTarget(s, "Supplier") && !HasTarget(s, "Store"))
                .OrderByDescending(s => s.Priority)
                .FirstOrDefault();
        }

        private static bool HasTarget(PricingStrategy strategy, string targetType)
        {
            if (strategy.Targets?.Count > 0)
                return strategy.Targets.Any(t => t.TargetType == targetType);

            // 兼容尚未迁移到多目标表的旧策略，避免把它们误当作全局策略。
            return strategy.Level == targetType && !string.IsNullOrWhiteSpace(strategy.TargetCode);
        }

        private static bool MatchesTarget(PricingStrategy strategy, string targetType, string? targetCode)
        {
            if (string.IsNullOrWhiteSpace(targetCode))
                return false;

            if (strategy.Targets?.Count > 0)
                return strategy.Targets.Any(t => t.TargetType == targetType && t.TargetCode == targetCode);

            return strategy.Level == targetType && strategy.TargetCode == targetCode;
        }
    }
}
