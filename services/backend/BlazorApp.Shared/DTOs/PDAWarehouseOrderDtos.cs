using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    #region 订单DTOs

    public class PDAWarehouseOrderDto
    {
        public string OrderGUID { get; set; } = string.Empty;

        public string? StoreCode { get; set; }

        public string? OrderNo { get; set; }

        public DateTime? OrderDate { get; set; }

        public DateTime? OutboundDate { get; set; }

        public decimal? ShippingFee { get; set; }

        public decimal? ImportTotalAmount { get; set; }

        public decimal? OEMTotalAmount { get; set; }

        public string? Remarks { get; set; }

        public int? FlowStatus { get; set; }

        public string? FlowStatusText { get; set; }

        public int? InboundStatus { get; set; }

        public string? InboundStatusText { get; set; }

        public DateTime? CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public List<PDAWarehouseOrderDetailDto> OrderDetails { get; set; } = new();

        public int DetailCount => OrderDetails.Count;

        public decimal? TotalQuantity => OrderDetails.Sum(d => d.Quantity);
    }

    public class PDAWarehouseOrderDetailDto
    {
        public string DetailGUID { get; set; } = string.Empty;

        public string? OrderGUID { get; set; }

        public string? StoreCode { get; set; }

        public string? StoreProductCode { get; set; }

        public string? ProductCode { get; set; }

        public string? ItemNumber { get; set; }

        public string? ProductName { get; set; }

        public string? ProductImage { get; set; }

        public string? Barcode { get; set; }

        public decimal? Quantity { get; set; }

        public decimal? AllocQuantity { get; set; }

        public decimal? LastCost { get; set; }

        public decimal? ImportPrice { get; set; }

        public decimal? ImportAmount { get; set; }

        public decimal? OEMPrice { get; set; }

        public decimal? OEMAmount { get; set; }

        public decimal? StockQuantity { get; set; }

        public int? MinOrderQuantity { get; set; }
    }

    #endregion

    #region 请求DTOs

    public class CreatePDAWarehouseOrderRequestDto
    {
        // PDA 创建订单的分店由设备绑定关系决定，客户端 StoreCode 仅保留兼容旧请求。
        public string StoreCode { get; set; } = string.Empty;

        public DateTime? OrderDate { get; set; }

        public string? Remarks { get; set; }
    }

    public class UpdatePDAWarehouseOrderRequestDto
    {
        [Required(ErrorMessage = "订单GUID不能为空")]
        public string OrderGUID { get; set; } = string.Empty;

        public DateTime? OrderDate { get; set; }

        public string? Remarks { get; set; }

        public decimal? ShippingFee { get; set; }
    }

    public class AddPDAWarehouseOrderLineRequestDto
    {
        [Required(ErrorMessage = "订单GUID不能为空")]
        public string OrderGUID { get; set; } = string.Empty;

        [Required(ErrorMessage = "商品代码不能为空")]
        public string ProductCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "数量不能为空")]
        [Range(0.01, double.MaxValue, ErrorMessage = "数量必须大于0")]
        public decimal Quantity { get; set; }

        public decimal? AllocQuantity { get; set; }
    }

    public class UpdatePDAWarehouseOrderLineRequestDto
    {
        [Required(ErrorMessage = "订单明细GUID不能为空")]
        public string DetailGUID { get; set; } = string.Empty;

        [Required(ErrorMessage = "数量不能为空")]
        [Range(0, double.MaxValue, ErrorMessage = "数量不能为负数")]
        public decimal Quantity { get; set; }

        public decimal? AllocQuantity { get; set; }

        public decimal? ImportPrice { get; set; }

        public decimal? OEMPrice { get; set; }
    }

    public class BatchAddPDAWarehouseOrderLinesRequestDto
    {
        [Required(ErrorMessage = "订单GUID不能为空")]
        public string OrderGUID { get; set; } = string.Empty;

        [Required(ErrorMessage = "订单明细列表不能为空")]
        [MinLength(1, ErrorMessage = "至少需要一个订单明细")]
        public List<AddPDAWarehouseOrderLineRequestDto> Lines { get; set; } = new();
    }

    public class SubmitPDAWarehouseOrderRequestDto
    {
        [Required(ErrorMessage = "订单GUID不能为空")]
        public string OrderGUID { get; set; } = string.Empty;

        public string? Remarks { get; set; }
    }

    public class PDAWarehouseOrderFilterDto
    {
        public string? StoreCode { get; set; }

        public int? FlowStatus { get; set; }

        public int? InboundStatus { get; set; }

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public string? Keyword { get; set; }

        public int PageNumber { get; set; } = 1;

        public int PageSize { get; set; } = 20;

        public string? SortBy { get; set; } = "OrderDate";

        public bool SortDescending { get; set; } = true;
    }

    #endregion

    #region 商品DTOs

    public class PDAWarehouseProductDto
    {
        public string ProductCode { get; set; } = string.Empty;

        public string? ItemNumber { get; set; }

        public string? ProductName { get; set; }

        public string? ProductImage { get; set; }

        public string? Barcode { get; set; }

        public string? CategoryName { get; set; }

        public decimal? DomesticPrice { get; set; }

        public decimal? OEMPrice { get; set; }

        public decimal? ImportPrice { get; set; }

        public decimal? StockQuantity { get; set; }

        public int? MinOrderQuantity { get; set; }

        public int? StockAlertQuantity { get; set; }

        public decimal? Volume { get; set; }

        public bool IsActive { get; set; } = true;

        public string? LocationCode { get; set; }

        public DateTime? CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }

    public class PDAWarehouseProductFilterDto
    {
        public string? Keyword { get; set; }

        public string? CategoryGUID { get; set; }

        public bool? IsActive { get; set; }

        public bool? OnlyInStock { get; set; }

        public int? MinStockQuantity { get; set; }

        public int? MaxStockQuantity { get; set; }

        public decimal? MinPrice { get; set; }

        public decimal? MaxPrice { get; set; }

        public string? PriceType { get; set; }

        public int PageNumber { get; set; } = 1;

        public int PageSize { get; set; } = 20;

        public string? SortBy { get; set; } = "ItemNumber";

        public bool SortDescending { get; set; } = false;
    }

    #endregion

    #region 响应DTOs

    public class PDAWarehouseOrderResponseDto
    {
        public bool Success { get; set; }

        public string? OrderGUID { get; set; }

        public string? OrderNo { get; set; }

        public string? Message { get; set; }
    }

    public class PDAWarehouseOrderDetailResponseDto
    {
        public bool Success { get; set; }

        public string? DetailGUID { get; set; }

        public string? Message { get; set; }
    }

    public class PDAWarehouseOrderListResponseDto
    {
        public List<PDAWarehouseOrderDto> Orders { get; set; } = new();

        public int TotalCount { get; set; }

        public int PageNumber { get; set; }

        public int PageSize { get; set; }

        public int TotalPages { get; set; }
    }

    public class PDAWarehouseProductListResponseDto
    {
        public List<PDAWarehouseProductDto> Products { get; set; } = new();

        public int TotalCount { get; set; }

        public int PageNumber { get; set; }

        public int PageSize { get; set; }

        public int TotalPages { get; set; }
    }

    #endregion

    #region 扫码查询DTOs

    public class PDAScanProductRequestDto
    {
        public string? Barcode { get; set; }

        public string? ItemNumber { get; set; }

        public string? ProductCode { get; set; }
    }

    public class PDABatchScanProductsRequestDto
    {
        [Required(ErrorMessage = "货号列表不能为空")]
        public List<string> ItemNumbers { get; set; } = new();
    }

    #endregion
}
