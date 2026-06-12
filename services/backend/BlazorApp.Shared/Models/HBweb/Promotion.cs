using System;
using System.Collections.Generic;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb
{
    [SugarTable("Promotion")]
    public class Promotion : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true)]
        public string Id { get; set; } = string.Empty;

        [SugarColumn(Length = 100)]
        public string Name { get; set; } = string.Empty;

        [SugarColumn(Length = 500, IsNullable = true)]
        public string? Description { get; set; }

        public DateTime EffectiveStart { get; set; }

        public DateTime EffectiveEnd { get; set; }

        public bool IsEnabled { get; set; } = true;

        public bool IsExclusive { get; set; } = true;

        public int Priority { get; set; } = 0;

        public int ApplyQuantity { get; set; }

        [SugarColumn(DecimalDigits = 4)]
        public decimal FixedPrice { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? MaxApplicationsPerOrder { get; set; }

        [SugarColumn(IsIgnore = true)]
        [Navigate(NavigateType.OneToMany, nameof(PromotionProduct.PromotionId))]
        public List<PromotionProduct> Products { get; set; } = new();

        [SugarColumn(IsIgnore = true)]
        [Navigate(NavigateType.OneToMany, nameof(PromotionStore.PromotionId))]
        public List<PromotionStore> Stores { get; set; } = new();
    }
}
