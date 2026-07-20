namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 进货金额看板响应。
    /// </summary>
    public class LocalPurchaseDashboardResponseDto
    {
        public List<string> Months { get; set; } = new();
        public decimal WarehouseTotal { get; set; }
        public decimal LocalSupplierTotal { get; set; }
        public decimal TotalAmount { get; set; }
        public List<LocalPurchaseDashboardStoreDto> Stores { get; set; } = new();
    }

    /// <summary>
    /// 分店在滚动十二个月内的进货金额。
    /// </summary>
    public class LocalPurchaseDashboardStoreDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public string StoreName { get; set; } = string.Empty;
        public decimal WarehouseTotal { get; set; }
        public decimal LocalSupplierTotal { get; set; }
        public decimal TotalAmount { get; set; }
        public List<LocalPurchaseDashboardStoreMonthDto> Months { get; set; } = new();
    }

    /// <summary>
    /// 分店单月的仓库、本地供应商、采购合计与营业额。
    /// </summary>
    public class LocalPurchaseDashboardStoreMonthDto
    {
        public string Month { get; set; } = string.Empty;
        public decimal WarehouseAmount { get; set; }
        public decimal LocalSupplierAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal SalesAmount { get; set; }
    }

    /// <summary>
    /// 指定分店的供应商进货金额明细。
    /// </summary>
    public class LocalPurchaseDashboardStoreSuppliersDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public string StoreName { get; set; } = string.Empty;
        public List<string> Months { get; set; } = new();
        public decimal WarehouseTotal { get; set; }
        public decimal LocalSupplierTotal { get; set; }
        public decimal TotalAmount { get; set; }
        public List<LocalPurchaseDashboardSupplierDto> Suppliers { get; set; } = new();
    }

    /// <summary>
    /// 仓库订单虚拟来源或本地供应商的月度金额。
    /// </summary>
    public class LocalPurchaseDashboardSupplierDto
    {
        public string SourceCode { get; set; } = string.Empty;
        public string? SupplierCode { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public bool IsUnassigned { get; set; }
        public decimal TotalAmount { get; set; }
        public List<LocalPurchaseDashboardSupplierMonthDto> Months { get; set; } = new();
    }

    /// <summary>
    /// 单一来源或供应商的单月金额。
    /// </summary>
    public class LocalPurchaseDashboardSupplierMonthDto
    {
        public string Month { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }
}
