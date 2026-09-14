using BlazorApp.Api.Services.Pricing;
using BlazorApp.Shared.Models.HBweb;
using Xunit;

namespace BlazorApp.Api.Tests;

public class PricingCurveTests
{
    private readonly AutoPricingService _service = new(null!);
    private static PricingStrategyDetail Rule(decimal a, decimal b, decimal ra, decimal rb,
        string algorithm = "Linear", decimal bend = 0) => new()
        { MinPrice = a, MaxPrice = b, StartRate = ra, EndRate = rb, Algorithm = algorithm, CurveBend = bend };
    private static PricingStrategy Strategy(params PricingStrategyDetail[] rules) => new() { Details = rules.ToList() };

    [Fact]
    public void 示例全区间99801点_尾数合法成率有界售价不降()
    {
        var strategy = Strategy(Rule(1, 50, 4, 3.5m), Rule(50, 200, 3.5m, 3), Rule(200, 999, 3, 2));
        var previous = 0m; var previousRate = 5m;
        for (var cents = 100; cents <= 99900; cents++)
        {
            var cost = cents / 100m;
            var retail = _service.CalculateRetailPrice(cost, strategy);
            var rate = _service.CalculateRate(cost, strategy);
            Assert.True(retail >= previous, $"成本{cost}零售价倒挂");
            Assert.InRange(retail / cost, 1.5m, 5m);
            Assert.True(PricingCurveMath.IsLegalTail(retail));
            Assert.True(rate <= previousRate);
            previous = retail; previousRate = rate;
        }
        Assert.Equal(316.67m, Math.Round(PricingCurveMath.TheoreticalRetail(100, strategy.Details[1]), 2));
    }

    [Fact]
    public void 原反例十元四倍二十元二点一倍_中段不再先涨后降()
    {
        var strategy = Strategy(Rule(10, 20, 4, 2.1m));
        Assert.Equal(39.99m, _service.CalculateRetailPrice(10, strategy));
        Assert.Equal(40.99m, _service.CalculateRetailPrice(15, strategy));
        Assert.Equal(41.99m, _service.CalculateRetailPrice(20, strategy));
    }

    [Theory]
    [InlineData("ArcUp", 0.1)]
    [InlineData("ArcDown", -0.1)]
    public void 合法弧线全段满足约束(string algorithm, double bend)
    {
        var rule = Rule(10, 20, 4, 2.5m, algorithm, (decimal)bend);
        PricingCurveMath.Validate(new[] { rule });
        var previous = 0m; var previousRate = 5m;
        for (var cents = 1000; cents <= 2000; cents++)
        {
            var x = cents / 100m; var p = _service.CalculateRetailPrice(x, Strategy(rule));
            var rate = _service.CalculateRate(x, Strategy(rule));
            Assert.True(p >= previous); Assert.True(rate <= previousRate);
            previous = p; previousRate = rate;
        }
    }

    [Fact]
    public void 旧零成本起点可在有效成本计算但新保存拒绝零起点()
    {
        var rule = Rule(0, 10, 4, 3);
        Assert.Equal(3m, _service.CalculateRate(1, Strategy(rule)));
        Assert.Equal(2.99m, _service.CalculateRetailPrice(1, Strategy(rule)));
        Assert.Throws<ArgumentException>(() => PricingCurveMath.Validate(new[] { rule }));
    }

    [Fact]
    public void 过大弧度和不连续节点拒绝()
    {
        Assert.Throws<ArgumentException>(() => PricingCurveMath.Validate(new[] { Rule(10, 20, 3, 3, "ArcUp", .1m) }));
        Assert.Throws<ArgumentException>(() => PricingCurveMath.Validate(new[] { Rule(10, 20, 4, 3), Rule(21, 30, 3, 2) }));
        Assert.Throws<ArgumentException>(() => _service.CalculateRetailPrice(15, Strategy(Rule(10, 20, 2, 3))));
    }

    [Fact]
    public void 重复小数不把精确尾数推入下一档且节点端点直返()
    {
        var rule = Rule(.5m, 2m, 3m, 2.25m);
        rule.StartRetailPrice = 1.5m; rule.EndRetailPrice = 4.5m;
        Assert.Equal(3.5m, PricingCurveMath.TheoreticalRetail(1.5m, rule));
        Assert.Equal(3.5m, _service.CalculateRetailPrice(1.5m, Strategy(rule)));
        Assert.Equal(3.5m, PricingCurveMath.AdjustTail(1.5m, 3.5000000000000000000000000001m));
        Assert.Equal(3.99m, PricingCurveMath.AdjustTail(1.5m, 3.50000001m));
        Assert.Equal(1.5m, PricingCurveMath.TheoreticalRetail(.5m, rule));
        Assert.Equal(4.5m, PricingCurveMath.TheoreticalRetail(2m, rule));
    }

    [Fact]
    public void 显式节点和所有合法尾数保持原值()
    {
        foreach (var price in new[] { .5m, .99m, 1m, 1.5m, 1.99m, 2m, 2.5m, 2.99m, 49.99m })
            Assert.Equal(price, PricingCurveMath.AdjustTail(price / 2m, price));
        var rule = Rule(10, 20, 4, 2.5m); rule.StartRetailPrice = 39.99m; rule.EndRetailPrice = 49.99m;
        Assert.Equal(39.99m, _service.CalculateRetailPrice(10, Strategy(rule)));
        Assert.Equal(49.99m, _service.CalculateRetailPrice(20, Strategy(rule)));
    }

    [Fact]
    public void 成率上下限只使用合法尾数且极低成本明确失败()
    {
        Assert.Throws<ArgumentException>(() => _service.CalculateRetailPrice(20.01m, Strategy(Rule(10, 20, 4, 3))));
        Assert.Throws<ArgumentException>(() => _service.CalculateRetailPrice(.09m, null));
        Assert.Equal(.5m, _service.CalculateRetailPrice(.1m, null));
        for (var cents = 10; cents <= 10000; cents++)
        {
            var cost = cents / 100m;
            foreach (var rate in new[] { 1.5m, 5m })
            {
                var p = PricingCurveMath.AdjustTail(cost, cost * rate);
                Assert.InRange(p / cost, 1.5m, 5m);
                Assert.True(PricingCurveMath.IsLegalTail(p));
            }
        }
    }
}
