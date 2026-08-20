using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class PromotionComputationBudgetTests
{
    private static readonly IReadOnlyDictionary<string, int> UnitWeights =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["SKU-1"] = 1
        };

    [Fact]
    public void Exact_rule_and_order_limits_are_allowed()
    {
        var budget = PromotionComputationBudget.CalculateRule(
            "PROMO-EXACT",
            [new PromotionBudgetLine("SKU-1", PromotionComputationBudget.MaxWorkUnitsPerRule)],
            UnitWeights,
            applyQuantity: 1,
            maxApplicationsPerOrder: null);

        Assert.Equal(PromotionComputationBudget.MaxWorkUnitsPerRule, budget.ExpandedUnits);
        Assert.Equal(PromotionComputationBudget.MaxWorkUnitsPerRule, budget.WorkUnits);
        Assert.Equal(
            PromotionComputationBudget.MaxWorkUnitsPerOrder,
            PromotionComputationBudget.EnsureOrderLimit(Enumerable.Repeat(budget, 5)));
    }

    [Fact]
    public void Rule_above_limit_is_rejected()
    {
        var exception = Assert.Throws<PromotionComputationBudgetExceededException>(() =>
            PromotionComputationBudget.CalculateRule(
                "PROMO-OVER",
                [new PromotionBudgetLine("SKU-1", PromotionComputationBudget.MaxWorkUnitsPerRule + 1)],
                UnitWeights,
                applyQuantity: 1,
                maxApplicationsPerOrder: null));

        Assert.Equal("rule", exception.Scope);
        Assert.Equal(PromotionComputationBudget.MaxWorkUnitsPerRule + 1, exception.CalculatedWorkUnits);
    }

    [Fact]
    public void Max_applications_is_applied_before_work_limit()
    {
        var budget = PromotionComputationBudget.CalculateRule(
            "PROMO-CAPPED",
            [new PromotionBudgetLine("SKU-1", 1_000_000m)],
            UnitWeights,
            applyQuantity: 2,
            maxApplicationsPerOrder: 1);

        Assert.Equal(1_000_000, budget.ExpandedUnits);
        Assert.Equal(1, budget.ApplicationCount);
        Assert.Equal(2, budget.WorkUnits);
    }

    [Fact]
    public void Quantity_conversion_overflow_is_rejected()
    {
        var exception = Assert.Throws<PromotionComputationBudgetExceededException>(() =>
            PromotionComputationBudget.CalculateRule(
                "PROMO-OVERFLOW",
                [new PromotionBudgetLine("SKU-1", decimal.MaxValue)],
                UnitWeights,
                applyQuantity: 1,
                maxApplicationsPerOrder: 1));

        Assert.Equal("arithmetic", exception.Scope);
        Assert.Null(exception.CalculatedWorkUnits);
    }

    [Fact]
    public void Order_above_limit_is_rejected_before_any_rule_is_applied()
    {
        var budget = new PromotionRuleBudget(
            "PROMO-ORDER",
            PromotionComputationBudget.MaxWorkUnitsPerRule,
            PromotionComputationBudget.MaxWorkUnitsPerRule,
            PromotionComputationBudget.MaxWorkUnitsPerRule);

        var exception = Assert.Throws<PromotionComputationBudgetExceededException>(() =>
            PromotionComputationBudget.EnsureOrderLimit(Enumerable.Repeat(budget, 6)));

        Assert.Equal("order", exception.Scope);
        Assert.Equal(600_000, exception.CalculatedWorkUnits);
    }
}
