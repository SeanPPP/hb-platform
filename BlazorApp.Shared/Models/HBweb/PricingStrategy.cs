using System.Collections.Generic;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb
{
    [SugarTable("PricingStrategy")]
    public class PricingStrategy
    {
        [SugarColumn(IsPrimaryKey = true)]
        public string Id { get; set; } = string.Empty;

        [SugarColumn(Length = 100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Level: Supplier, Store, Global
        /// </summary>
        [SugarColumn(Length = 20)]
        public string Level { get; set; } = "Global";

        /// <summary>
        /// SupplierCode or StoreCode, null for Global
        /// </summary>
        [SugarColumn(Length = 50, IsNullable = true)]
        public string? TargetCode { get; set; }

        public int Priority { get; set; } = 0;

        public bool IsEnabled { get; set; } = true;

        [SugarColumn(IsIgnore = true)]
        [Navigate(NavigateType.OneToMany, nameof(PricingStrategyDetail.StrategyId))]
        public List<PricingStrategyDetail> Details { get; set; } = new();

        [SugarColumn(IsIgnore = true)]
        [Navigate(NavigateType.OneToMany, nameof(PricingStrategyTarget.StrategyId))]
        public List<PricingStrategyTarget> Targets { get; set; } = new();
    }
}
