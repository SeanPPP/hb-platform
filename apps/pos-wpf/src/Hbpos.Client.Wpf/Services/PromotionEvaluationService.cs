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
        var discountsByLine = new Dictionary<CartLine, decimal>();

        foreach (var rule in rulesToEvaluate)
        {
            var ruleProductWeights = BuildRuleProductWeights(rule);
            if (ruleProductWeights.Count == 0)
            {
                continue;
            }

            var ruleUnits = ExpandRuleUnits(eligibleLines, ruleProductWeights);
            var applicationCount = ruleUnits.Count / rule.ApplyQuantity;
            if (rule.MaxApplicationsPerOrder is int maxApplications)
            {
                applicationCount = Math.Min(applicationCount, maxApplications);
            }

            for (var applicationIndex = 0; applicationIndex < applicationCount; applicationIndex++)
            {
                // 与 Web 端评估保持一致：每条非排他规则独立按购物车顺序分组，不跨规则消费同一份展开单位。
                var selectedUnits = ruleUnits
                    .Skip(applicationIndex * rule.ApplyQuantity)
                    .Take(rule.ApplyQuantity)
                    .ToArray();

                AddGroupDiscount(discountsByLine, selectedUnits, rule.FixedPrice);
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

    private static List<RuleUnit> ExpandRuleUnits(
        IReadOnlyList<CartLine> lines,
        IReadOnlyDictionary<string, int> ruleProductWeights)
    {
        var expandedUnits = new List<RuleUnit>();
        var nextExpandedId = 0;

        foreach (var line in lines)
        {
            var productCode = NormalizeProductCode(line.ProductCode);
            if (!ruleProductWeights.TryGetValue(productCode, out var unitWeight))
            {
                continue;
            }

            var quantity = decimal.ToInt32(line.Quantity);
            for (var quantityIndex = 0; quantityIndex < quantity; quantityIndex++)
            {
                // Web 端的权重语义是按 qty * UnitWeight 展开同价单位，每个展开单位自身权重都视为 1。
                for (var weightIndex = 0; weightIndex < unitWeight; weightIndex++)
                {
                    expandedUnits.Add(new RuleUnit(
                        nextExpandedId++,
                        line,
                        productCode,
                        line.UnitPrice,
                        quantityIndex,
                        weightIndex));
                }
            }
        }

        return expandedUnits;
    }

    private static void AddGroupDiscount(
        Dictionary<CartLine, decimal> discountsByLine,
        IReadOnlyList<RuleUnit> selectedUnits,
        decimal fixedPrice)
    {
        var groupTotal = decimal.Round(selectedUnits.Sum(unit => unit.UnitPrice), 2, MidpointRounding.AwayFromZero);
        var groupDiscount = decimal.Round(groupTotal - fixedPrice, 2, MidpointRounding.AwayFromZero);
        if (groupDiscount <= 0m)
        {
            return;
        }

        var groupedLines = selectedUnits
            .GroupBy(unit => unit.Line)
            .Select(group => new
            {
                Line = group.Key,
                Amount = decimal.Round(group.Sum(unit => unit.UnitPrice), 2, MidpointRounding.AwayFromZero),
                SortOrder = group.Min(unit => unit.SortOrder)
            })
            .OrderBy(group => group.SortOrder)
            .ToArray();

        var remainingDiscount = groupDiscount;
        for (var index = 0; index < groupedLines.Length; index++)
        {
            var group = groupedLines[index];
            var lineDiscount = index == groupedLines.Length - 1
                ? remainingDiscount
                : decimal.Round(groupDiscount * group.Amount / groupTotal, 2, MidpointRounding.AwayFromZero);
            lineDiscount = Math.Clamp(lineDiscount, 0m, remainingDiscount);

            discountsByLine[group.Line] = discountsByLine.TryGetValue(group.Line, out var currentDiscount)
                ? decimal.Round(currentDiscount + lineDiscount, 2, MidpointRounding.AwayFromZero)
                : lineDiscount;
            remainingDiscount -= lineDiscount;
        }
    }

    private static string NormalizeProductCode(string? productCode)
    {
        return (productCode ?? string.Empty).Trim();
    }

    private sealed record RuleUnit(
        int Id,
        CartLine Line,
        string ProductCode,
        decimal UnitPrice,
        int SortOrder,
        int ExpandedIndex);
}
