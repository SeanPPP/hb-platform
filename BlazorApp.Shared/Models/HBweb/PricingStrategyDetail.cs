using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb
{
    [SugarTable("PricingStrategyDetail")]
    public class PricingStrategyDetail
    {
        [SugarColumn(IsPrimaryKey = true)]
        public string Id { get; set; } = string.Empty;

        public string StrategyId { get; set; } = string.Empty;

        [SugarColumn(DecimalDigits = 4)]
        public decimal MinPrice { get; set; }

        [SugarColumn(DecimalDigits = 4)]
        public decimal MaxPrice { get; set; }

        [SugarColumn(DecimalDigits = 4)]
        public decimal StartRate { get; set; }

        [SugarColumn(DecimalDigits = 4)]
        public decimal EndRate { get; set; }

        /// <summary>
        /// Linear, Exponential, Step
        /// </summary>
        [SugarColumn(Length = 20)]
        public string Algorithm { get; set; } = "Linear";
    }
}
