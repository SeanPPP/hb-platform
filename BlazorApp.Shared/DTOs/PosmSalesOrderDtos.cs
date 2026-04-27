using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    public enum OrderType
    {
        All = -1,
        Pending = 0,
        Paid = 1,
        Cancelled = 2,
        Refunded = 3,
        Installment = 4
    }

    public enum OrderStatus
    {
        Pending = 0,
        Paid = 1,
        Cancelled = 2,
        Refunded = 3,
        Installment = 4
    }

    /// <summary>
    /// POSM 销售订单查询参数
    /// </summary>
    public class PosmSalesOrderQueryParams
    {
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? BranchCode { get; set; }
        public string? DeviceCode { get; set; }
        public OrderType? OrderType { get; set; }
        public string? Keyword { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    /// <summary>
    /// POSM 销售订单 DTO
    /// </summary>
    public class PosmSalesOrderDto
    {
        public string? OrderGuid { get; set; }
        public string? BranchCode { get; set; }
        public string? BranchName { get; set; }
        public string? ABN { get; set; }
        public string? BrandName { get; set; }
        public string? DeviceCode { get; set; }
        public DateTime? OrderTime { get; set; }
        public int? SkuCount { get; set; }
        public int? ItemCount { get; set; }
        public decimal? TotalAmount { get; set; }
        public decimal? DiscountAmount { get; set; }
        public decimal? ActualAmount { get; set; }
        public int? Status { get; set; }
    }

    /// <summary>
    /// POSM 销售订单明细 DTO
    /// </summary>
    public class PosmSalesOrderDetailDto
    {
        public string? ProductImage { get; set; }
        public string? ProductCode { get; set; }
        public string? ProductName { get; set; }
        public int? Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? DiscountAmount { get; set; }
        public decimal? ActualAmount { get; set; }
    }

    /// <summary>
    /// POSM 支付明细 DTO
    /// </summary>
    public class PosmPaymentDetailDto
    {
        public DateTime? PaymentTime { get; set; }
        public int? PaymentMethod { get; set; }
        public string? PaymentMethodName { get; set; }
        public decimal? Amount { get; set; }
    }

    /// <summary>
    /// POSM 销售订单详情（含明细和支付）
    /// </summary>
    public class PosmSalesOrderDetailResponse
    {
        public PosmSalesOrderDto? Order { get; set; }
        public List<PosmSalesOrderDetailDto>? OrderDetails { get; set; }
        public List<PosmPaymentDetailDto>? PaymentDetails { get; set; }
    }
}
