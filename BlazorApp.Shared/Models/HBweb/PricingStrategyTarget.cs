using System.Collections.Generic;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb
{
    [SugarTable("PricingStrategyTarget")]
    public class PricingStrategyTarget
    {
        [SugarColumn(IsPrimaryKey = true)]
        public string Id { get; set; } = string.Empty;

        public string StrategyId { get; set; } = string.Empty;

        [SugarColumn(Length = 20)]
        public string TargetType { get; set; } = "Global";

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? TargetCode { get; set; }
    }
}
