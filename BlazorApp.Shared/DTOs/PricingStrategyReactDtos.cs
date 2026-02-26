namespace BlazorApp.Shared.DTOs
{
    public class PricingStrategyListDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Level { get; set; } = "Global";
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }
        public List<string> StoreCodes { get; set; } = new();
        public List<string> SupplierCodes { get; set; } = new();
        public int DetailsCount { get; set; }
        public int TargetsCount { get; set; }
    }

    public class PricingStrategyRuleDto
    {
        public string? Id { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public decimal StartRate { get; set; }
        public decimal EndRate { get; set; }
        public string? Algorithm { get; set; }
    }

    public class PricingStrategyTargetDto
    {
        public string? Id { get; set; }
        public string TargetType { get; set; } = "Global";
        public string? TargetCode { get; set; }
    }

    public class PricingStrategyDetailDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Level { get; set; } = "Global";
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }
        public List<PricingStrategyRuleDto> Details { get; set; } = new();
        public List<PricingStrategyTargetDto> Targets { get; set; } = new();
    }

    public class CreatePricingStrategyDto
    {
        public string Name { get; set; } = string.Empty;
        public string Level { get; set; } = "Global";
        public int Priority { get; set; }
        public bool IsEnabled { get; set; } = true;
        public List<PricingStrategyRuleDto> Details { get; set; } = new();
        public List<PricingStrategyTargetDto> Targets { get; set; } = new();
    }

    public class UpdatePricingStrategyDto
    {
        public string Name { get; set; } = string.Empty;
        public string Level { get; set; } = "Global";
        public int Priority { get; set; }
        public bool IsEnabled { get; set; } = true;
        public List<PricingStrategyRuleDto> Details { get; set; } = new();
        public List<PricingStrategyTargetDto> Targets { get; set; } = new();
    }

    public class PricingEvaluateRequest
    {
        public decimal PurchasePrice { get; set; }
        public string? StoreCode { get; set; }
        public string? SupplierCode { get; set; }
    }

    public class PricingEvaluateResponse
    {
        public decimal RetailPrice { get; set; }
        public decimal Rate { get; set; }
        public string? StrategyId { get; set; }
        public PricingEvaluateRuleInfo? Rule { get; set; }
    }

    public class PricingEvaluateRuleInfo
    {
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public string? Algorithm { get; set; }
        public decimal StartRate { get; set; }
        public decimal EndRate { get; set; }
    }
}
