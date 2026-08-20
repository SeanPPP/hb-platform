using Hbpos.Client.Wpf.Models;
using Hbpos.Contracts.Promotions;

namespace Hbpos.Client.Wpf.Services;

public interface IPromotionEvaluationService
{
    Task<IReadOnlyList<PromotionLineDiscount>> EvaluateAsync(
        IReadOnlyList<CartLine> lines,
        string storeCode,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);
}

public sealed record PromotionLineDiscount(
    CartLine Line,
    decimal DiscountAmount);

public sealed class PromotionEvaluationService(ILocalPromotionRepository localPromotionRepository) : IPromotionEvaluationService
{
    public async Task<IReadOnlyList<PromotionLineDiscount>> EvaluateAsync(
        IReadOnlyList<CartLine> lines,
        string storeCode,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        if (lines.Count == 0 || string.IsNullOrWhiteSpace(storeCode))
        {
            return [];
        }

        var activeRules = await localPromotionRepository.GetActiveRulesAsync(storeCode, asOf, cancellationToken);
        if (activeRules.Count == 0)
        {
            return [];
        }

        var eligibleLines = lines
            .Where(IsEligibleSaleLine)
            .ToArray();
        if (eligibleLines.Length == 0)
        {
            return [];
        }

        var applicableRules = activeRules
            .Where(rule => rule.ApplyQuantity > 0 && rule.Products.Count > 0 && RuleHasEligibleProduct(rule, eligibleLines))
            .ToArray();
        if (applicableRules.Length == 0)
        {
            return [];
        }

        var rulesToEvaluate = SelectRulesToEvaluate(applicableRules);
        var plans = new List<PromotionEvaluationPlan>();
        foreach (var rule in rulesToEvaluate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ruleProductWeights = BuildRuleProductWeights(rule);
            if (ruleProductWeights.Count == 0)
            {
                continue;
            }

            var budget = PromotionComputationBudget.CalculateRule(
                rule.Id,
                eligibleLines.Select(line => new PromotionBudgetLine(
                    NormalizeProductCode(line.ProductCode),
                    line.Quantity)),
                ruleProductWeights,
                rule.ApplyQuantity,
                rule.MaxApplicationsPerOrder);
            plans.Add(new PromotionEvaluationPlan(rule, ruleProductWeights, budget));
        }

        // 所有选中规则先统一预检；任一超限都不允许留下部分自动促销结果。
        PromotionComputationBudget.EnsureOrderLimit(plans.Select(plan => plan.Budget));
        var discountsByLine = new Dictionary<CartLine, decimal>();

        foreach (var plan in plans)
        {
            if (plan.Budget.WorkUnits <= 0)
            {
                continue;
            }

            // 与 Web 端评估保持一致：每条非排他规则独立按购物车顺序分组，不跨规则消费同一份展开单位。
            var selectedUnits = new List<RuleUnit>(
                Math.Min(plan.Rule.ApplyQuantity, checked((int)plan.Budget.WorkUnits)));
            foreach (var unit in EnumerateRuleUnits(
                         eligibleLines,
                         plan.ProductWeights,
                         plan.Budget.WorkUnits))
            {
                selectedUnits.Add(unit);
                if (selectedUnits.Count != plan.Rule.ApplyQuantity)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                AddGroupDiscount(discountsByLine, selectedUnits, plan.Rule.FixedPrice);
                selectedUnits.Clear();
            }
        }

        return discountsByLine
            .Where(entry => entry.Value > 0m)
            .Select(entry => new PromotionLineDiscount(
                entry.Key,
                decimal.Round(entry.Value, 2, MidpointRounding.AwayFromZero)))
            .ToArray();
    }

    private static bool IsEligibleSaleLine(CartLine line)
    {
        return !line.IsReturnLine &&
            !line.IsOpenItem &&
            line.DiscountSource != CartLineDiscountSource.Manual &&
            // 不依赖金额舍入后的来源：只要有 catalog baseline 就不能参与固定总价分组。
            line.CatalogDiscountBasisPoints <= 0 &&
            line.UnitPrice > 0m &&
            line.GrossAmount > 0m &&
            PosCartService.IsPositiveIntegerQuantity(line.Quantity);
    }

    private static bool RuleHasEligibleProduct(PromotionRuleDto rule, IReadOnlyList<CartLine> lines)
    {
        var productCodes = rule.Products
            .Select(product => NormalizeProductCode(product.ProductCode))
            .Where(productCode => !string.IsNullOrEmpty(productCode))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return lines.Any(line => productCodes.Contains(NormalizeProductCode(line.ProductCode)));
    }

    private static IReadOnlyList<PromotionRuleDto> SelectRulesToEvaluate(IReadOnlyList<PromotionRuleDto> rules)
    {
        var exclusiveRule = rules
            .Where(rule => rule.IsExclusive)
            .OrderByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (exclusiveRule is not null)
        {
            return [exclusiveRule];
        }

        return rules
            .OrderByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Dictionary<string, int> BuildRuleProductWeights(PromotionRuleDto rule)
    {
        var weights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var product in rule.Products)
        {
            var productCode = NormalizeProductCode(product.ProductCode);
            if (string.IsNullOrEmpty(productCode))
            {
                continue;
            }

            weights[productCode] = product.UnitWeight > 0 ? product.UnitWeight : 1;
        }

        return weights;
    }

    private static IEnumerable<RuleUnit> EnumerateRuleUnits(
        IReadOnlyList<CartLine> lines,
        IReadOnlyDictionary<string, int> ruleProductWeights,
        long workUnits)
    {
        long emittedUnits = 0;
        foreach (var line in lines)
        {
            var productCode = NormalizeProductCode(line.ProductCode);
            if (!ruleProductWeights.TryGetValue(productCode, out var unitWeight))
            {
                continue;
            }

            var quantity = decimal.ToInt64(line.Quantity);
            for (long quantityIndex = 0; quantityIndex < quantity; quantityIndex++)
            {
                // Web 端的权重语义是按 qty * UnitWeight 展开同价单位，每个展开单位自身权重都视为 1。
                for (var weightIndex = 0; weightIndex < unitWeight; weightIndex++)
                {
                    yield return new RuleUnit(
                        line,
                        line.UnitPrice,
                        quantityIndex);
                    emittedUnits++;
                    if (emittedUnits >= workUnits)
                    {
                        yield break;
                    }
                }
            }
        }
    }

    private static void AddGroupDiscount(
        Dictionary<CartLine, decimal> discountsByLine,
        IReadOnlyList<RuleUnit> selectedUnits,
        decimal fixedPrice)
    {
        var groupedLineUnits = selectedUnits
            .GroupBy(unit => new RuleUnitGroupKey(unit.Line, unit.SortOrder))
            .Select(group => new
            {
                group.Key.Line,
                group.Key.SortOrder,
                Amount = decimal.Round(group.First().UnitPrice, 2, MidpointRounding.AwayFromZero)
            })
            .OrderBy(group => group.SortOrder)
            .ToArray();

        var groupTotal = decimal.Round(groupedLineUnits.Sum(unit => unit.Amount), 2, MidpointRounding.AwayFromZero);
        var groupDiscount = decimal.Round(groupTotal - fixedPrice, 2, MidpointRounding.AwayFromZero);
        if (groupDiscount <= 0m)
        {
            return;
        }

        var groupedLines = groupedLineUnits
            .GroupBy(unit => unit.Line)
            .Select(group => new
            {
                Line = group.Key,
                Amount = decimal.Round(group.Sum(unit => unit.Amount), 2, MidpointRounding.AwayFromZero),
                SortOrder = group.Min(unit => unit.SortOrder),
                MaxAdditionalDiscount = GetRemainingLineDiscountCapacity(discountsByLine, group.Key)
            })
            .Where(group => group.MaxAdditionalDiscount > 0m)
            .OrderBy(group => group.SortOrder)
            .ToArray();
        if (groupedLines.Length == 0)
        {
            return;
        }

        var remainingDiscount = groupDiscount;
        var remainingAmount = decimal.Round(
            groupedLines.Sum(item => item.Amount),
            2,
            MidpointRounding.AwayFromZero);
        for (var index = 0; index < groupedLines.Length && remainingDiscount > 0m; index++)
        {
            var group = groupedLines[index];
            if (remainingAmount <= 0m)
            {
                break;
            }

            var lineDiscount = index == groupedLines.Length - 1
                ? remainingDiscount
                : decimal.Round(remainingDiscount * group.Amount / remainingAmount, 2, MidpointRounding.AwayFromZero);
            lineDiscount = Math.Clamp(lineDiscount, 0m, Math.Min(remainingDiscount, group.MaxAdditionalDiscount));
            if (lineDiscount > 0m)
            {
                discountsByLine[group.Line] = discountsByLine.TryGetValue(group.Line, out var currentDiscount)
                    ? decimal.Round(currentDiscount + lineDiscount, 2, MidpointRounding.AwayFromZero)
                    : lineDiscount;
                remainingDiscount -= lineDiscount;
            }

            // Amount 均已按分币舍入；递减与旧实现每轮 Skip(index).Sum 的结果一致，且避免 O(n²)。
            remainingAmount = decimal.Round(
                remainingAmount - group.Amount,
                2,
                MidpointRounding.AwayFromZero);
        }
    }

    private static decimal GetRemainingLineDiscountCapacity(
        IReadOnlyDictionary<CartLine, decimal> discountsByLine,
        CartLine line)
    {
        var currentDiscount = discountsByLine.TryGetValue(line, out var value)
            ? value
            : 0m;
        // 中文注释：自动促销对同一行的累计折扣不能超过该行真实金额，避免 UnitWeight 把折扣上限放大。
        return Math.Max(0m, decimal.Round(line.GrossAmount - currentDiscount, 2, MidpointRounding.AwayFromZero));
    }

    private static string NormalizeProductCode(string? productCode)
    {
        return (productCode ?? string.Empty).Trim();
    }

    private sealed record PromotionEvaluationPlan(
        PromotionRuleDto Rule,
        IReadOnlyDictionary<string, int> ProductWeights,
        PromotionRuleBudget Budget);

    private sealed record RuleUnit(
        CartLine Line,
        decimal UnitPrice,
        long SortOrder);

    private sealed record RuleUnitGroupKey(
        CartLine Line,
        long SortOrder);
}
