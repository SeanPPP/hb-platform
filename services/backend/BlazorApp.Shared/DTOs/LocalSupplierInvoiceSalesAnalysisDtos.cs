namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 指定分店进货单的商品销量分析响应。
    /// </summary>
    public class LocalSupplierInvoiceSalesAnalysisResponseDto
    {
        public string InvoiceGUID { get; set; } = string.Empty;
        public string? InvoiceNo { get; set; }
        public string? StoreCode { get; set; }
        public string? StoreName { get; set; }
        public string? SupplierCode { get; set; }
        public string? SupplierName { get; set; }
        public DateTime? OrderDate { get; set; }
        public DateTime? InboundDate { get; set; }
        public DateTime? AnalysisDate { get; set; }
        public DateTime? SalesStatisticLastUpdate { get; set; }
        public List<LocalSupplierInvoiceSalesAnalysisItemDto> Items { get; set; } = new();
        public string CalculationNote { get; set; } =
            "进货后30/60/90天销量从本次进货日期次日开始统计；上次到本次区间销量仍按历史区间显示。";
    }

    /// <summary>
    /// 指定进货单商品行的销量分析。
    /// </summary>
    public class LocalSupplierInvoiceSalesAnalysisItemDto
    {
        public string DetailGUID { get; set; } = string.Empty;
        public string? ProductCode { get; set; }
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
        public string? ProductName { get; set; }
        public string? ProductImage { get; set; }
        public string? Specification { get; set; }
        public string? Unit { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? RetailPrice { get; set; }
        public decimal? Amount { get; set; }
        public int SalesQty30 { get; set; }
        public int SalesQty60 { get; set; }
        public int SalesQty90 { get; set; }
        public DateTime? PreviousPurchaseDate { get; set; }
        public int? PreviousToCurrentDays { get; set; }
        public int? SalesSincePreviousPurchase { get; set; }
        public int? SalesSincePreviousPurchase30 { get; set; }
        public int? SalesSincePreviousPurchase60 { get; set; }
        public int? SalesSincePreviousPurchase90 { get; set; }
        public DateTime? SalesStatisticLastUpdate { get; set; }
    }
}
