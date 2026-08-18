namespace Hbpos.Client.Wpf.Services;

internal readonly record struct PromotionBudgetLine(string ProductCode, decimal Quantity);

internal readonly record struct PromotionRuleBudget(
    string RuleId,
    long ExpandedUnits,
    long ApplicationCount,
    long WorkUnits);

internal sealed class PromotionComputationBudgetExceededException : Exception
{
    internal PromotionComputationBudgetExceededException(
        string ruleId,
        string scope,
        long? calculatedWorkUnits,
        long limit,
        Exception? innerException = null)
        : base("Automatic promotion computation budget exceeded.", innerException)
    {
        RuleId = ruleId;
        Scope = scope;
        CalculatedWorkUnits = calculatedWorkUnits;
        Limit = limit;
    }

    public string RuleId { get; }

    public string Scope { get; }

    public long? CalculatedWorkUnits { get; }

    public long Limit { get; }

    public string ToDiagnosticText()
    {
        var calculated = CalculatedWorkUnits?.ToString() ?? "overflow";
        return $"scope={Scope} ruleId={RuleId} workUnits={calculated} limit={Limit}";
    }
}

/// <summary>
/// 自动促销只按实际会消费的展开单位计费；先应用每单最大次数，再校验单规则和整单预算。
/// </summary>
internal static class PromotionComputationBudget
{
    internal const long MaxWorkUnitsPerRule = 100_000;
    internal const long MaxWorkUnitsPerOrder = 500_000;

    internal static PromotionRuleBudget CalculateRule(
        string ruleId,
        IEnumerable<PromotionBudgetLine> lines,
        IReadOnlyDictionary<string, int> productWeights,
        int applyQuantity,
        int? maxApplicationsPerOrder)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(productWeights);

        if (applyQuantity <= 0 || productWeights.Count == 0)
        {
            return new PromotionRuleBudget(ruleId, 0, 0, 0);
        }

        try
        {
            long expandedUnits = 0;
            foreach (var line in lines)
            {
                if (!productWeights.TryGetValue(line.ProductCode, out var configuredWeight))
                {
                    continue;
                }

                // 与旧促销展开保持一致：decimal 数量转换为整数单位；任何转换或乘加溢出都 fail-safe。
                var quantity = decimal.ToInt64(line.Quantity);
                if (quantity <= 0)
                {
                    continue;
                }

                var unitWeight = Math.Max(1, configuredWeight);
                expandedUnits = checked(expandedUnits + checked(quantity * unitWeight));
            }

            var applicationCount = expandedUnits / applyQuantity;
            if (maxApplicationsPerOrder is int configuredMaximum)
            {
                applicationCount = Math.Min(applicationCount, Math.Max(0, configuredMaximum));
            }

            var workUnits = checked(applicationCount * applyQuantity);
            if (workUnits > MaxWorkUnitsPerRule)
            {
                throw new PromotionComputationBudgetExceededException(
                    ruleId,
                    "rule",
                    workUnits,
                    MaxWorkUnitsPerRule);
            }

            return new PromotionRuleBudget(ruleId, expandedUnits, applicationCount, workUnits);
        }
        catch (PromotionComputationBudgetExceededException)
        {
            throw;
        }
        catch (OverflowException ex)
        {
            throw new PromotionComputationBudgetExceededException(
                ruleId,
                "arithmetic",
                null,
                MaxWorkUnitsPerRule,
                ex);
        }
    }

    internal static long EnsureOrderLimit(IEnumerable<PromotionRuleBudget> budgets)
    {
        ArgumentNullException.ThrowIfNull(budgets);

        long totalWorkUnits = 0;
        foreach (var budget in budgets)
        {
            try
            {
                totalWorkUnits = checked(totalWorkUnits + budget.WorkUnits);
            }
            catch (OverflowException ex)
            {
                throw new PromotionComputationBudgetExceededException(
                    budget.RuleId,
                    "order-arithmetic",
                    null,
                    MaxWorkUnitsPerOrder,
                    ex);
            }

            if (totalWorkUnits > MaxWorkUnitsPerOrder)
            {
                throw new PromotionComputationBudgetExceededException(
                    budget.RuleId,
                    "order",
                    totalWorkUnits,
                    MaxWorkUnitsPerOrder);
            }
        }

        return totalWorkUnits;
    }
}
