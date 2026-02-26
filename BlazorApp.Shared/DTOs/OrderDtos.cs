using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 订单查询DTO
    /// </summary>
    public class OrderQueryDto
    {
        /// <summary>
        /// 页码，从1开始
        /// </summary>
        public int Page { get; set; } = 1;
        
        /// <summary>
        /// 每页大小
        /// </summary>
        public int PageSize { get; set; } = 20;
        
        /// <summary>
        /// 搜索关键字
        /// </summary>
        public string? Search { get; set; }
        
        /// <summary>
        /// 门店GUID过滤条件
        /// </summary>
        public string? StoreGUID { get; set; }
        
        /// <summary>
        /// 订单状态过滤条件
        /// </summary>
        public string? OrderStatus { get; set; }
        
        /// <summary>
        /// 订单类型过滤条件
        /// </summary>
        public string? OrderType { get; set; }
        
        /// <summary>
        /// 开始日期过滤条件
        /// </summary>
        public DateTime? StartDate { get; set; }
        
        /// <summary>
        /// 结束日期过滤条件
        /// </summary>
        public DateTime? EndDate { get; set; }
    }

    /// <summary>
    /// 创建订单DTO
    /// </summary>
    public class CreateOrderDto
    {
        /// <summary>
        /// 门店GUID
        /// </summary>
        [Required(ErrorMessage = "门店GUID不能为空")]
        public string StoreGUID { get; set; } = string.Empty;

        /// <summary>
        /// 订单类型
        /// </summary>
        [Required(ErrorMessage = "订单类型不能为空")]
        [StringLength(20, ErrorMessage = "订单类型长度不能超过20个字符")]
        public string OrderType { get; set; } = string.Empty;

        /// <summary>
        /// 本地供应商GUID
        /// </summary>
        public string? LocalSupplierGUID { get; set; }
        
        /// <summary>
        /// 中国供应商GUID
        /// </summary>
        public string? ChineseSupplierGUID { get; set; }
        
        /// <summary>
        /// 发货日期
        /// </summary>
        public DateTime? ShipDate { get; set; }
        
        /// <summary>
        /// 收货日期
        /// </summary>
        public DateTime? ReceiveDate { get; set; }
        
        /// <summary>
        /// 订单总金额
        /// </summary>
        public decimal? TotalAmount { get; set; }

        /// <summary>
        /// 订单状态
        /// </summary>
        [StringLength(20, ErrorMessage = "订单状态长度不能超过20个字符")]
        public string OrderStatus { get; set; } = "购物车";

        /// <summary>
        /// 备注信息
        /// </summary>
        [StringLength(500, ErrorMessage = "备注长度不能超过500个字符")]
        public string? Remarks { get; set; }

        /// <summary>
        /// 订单项列表
        /// </summary>
        public List<CreateOrderItemDto> OrderItems { get; set; } = new List<CreateOrderItemDto>();
    }

    /// <summary>
    /// 更新订单DTO
    /// </summary>
    public class UpdateOrderDto
    {
        /// <summary>
        /// 门店GUID
        /// </summary>
        [Required(ErrorMessage = "门店GUID不能为空")]
        public string StoreGUID { get; set; } = string.Empty;

        /// <summary>
        /// 订单类型
        /// </summary>
        [Required(ErrorMessage = "订单类型不能为空")]
        [StringLength(20, ErrorMessage = "订单类型长度不能超过20个字符")]
        public string OrderType { get; set; } = string.Empty;

        /// <summary>
        /// 本地供应商GUID
        /// </summary>
        public string? LocalSupplierGUID { get; set; }
        
        /// <summary>
        /// 中国供应商GUID
        /// </summary>
        public string? ChineseSupplierGUID { get; set; }
        
        /// <summary>
        /// 发货日期
        /// </summary>
        public DateTime? ShipDate { get; set; }
        
        /// <summary>
        /// 收货日期
        /// </summary>
        public DateTime? ReceiveDate { get; set; }
        
        /// <summary>
        /// 订单总金额
        /// </summary>
        public decimal? TotalAmount { get; set; }

        /// <summary>
        /// 订单状态
        /// </summary>
        [Required(ErrorMessage = "订单状态不能为空")]
        [StringLength(20, ErrorMessage = "订单状态长度不能超过20个字符")]
        public string OrderStatus { get; set; } = string.Empty;

        /// <summary>
        /// 备注信息
        /// </summary>
        [StringLength(500, ErrorMessage = "备注长度不能超过500个字符")]
        public string? Remarks { get; set; }
    }

    /// <summary>
    /// 订单DTO
    /// </summary>
    public class OrderDto
    {
        /// <summary>
        /// 订单GUID
        /// </summary>
        public string OrderGUID { get; set; } = string.Empty;
        
        /// <summary>
        /// 订单编号
        /// </summary>
        public string OrderNumber { get; set; } = string.Empty;
        
        /// <summary>
        /// 门店GUID
        /// </summary>
        public string StoreGUID { get; set; } = string.Empty;
        
        /// <summary>
        /// 门店名称
        /// </summary>
        public string StoreName { get; set; } = string.Empty;
        
        /// <summary>
        /// 订单日期
        /// </summary>
        public DateTime OrderDate { get; set; }
        
        /// <summary>
        /// 订单类型
        /// </summary>
        public string OrderType { get; set; } = string.Empty;
        
        /// <summary>
        /// 本地供应商GUID
        /// </summary>
        public string? LocalSupplierGUID { get; set; }
        
        /// <summary>
        /// 中国供应商GUID
        /// </summary>
        public string? ChineseSupplierGUID { get; set; }
        
        /// <summary>
        /// 发货日期
        /// </summary>
        public DateTime? ShipDate { get; set; }
        
        /// <summary>
        /// 收货日期
        /// </summary>
        public DateTime? ReceiveDate { get; set; }
        
        /// <summary>
        /// 订单总金额
        /// </summary>
        public decimal? TotalAmount { get; set; }
        
        /// <summary>
        /// 订单状态
        /// </summary>
        public string OrderStatus { get; set; } = string.Empty;
        
        /// <summary>
        /// 备注信息
        /// </summary>
        public string? Remarks { get; set; }
        
        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }
        
        /// <summary>
        /// 订单项数量
        /// </summary>
        public int ItemCount { get; set; }
    }

    /// <summary>
    /// 订单详情DTO
    /// </summary>
    public class OrderDetailDto
    {
        /// <summary>
        /// 订单GUID
        /// </summary>
        public string OrderGUID { get; set; } = string.Empty;
        
        /// <summary>
        /// 订单编号
        /// </summary>
        public string OrderNumber { get; set; } = string.Empty;
        
        /// <summary>
        /// 门店GUID
        /// </summary>
        public string StoreGUID { get; set; } = string.Empty;
        
        /// <summary>
        /// 门店名称
        /// </summary>
        public string StoreName { get; set; } = string.Empty;
        
        /// <summary>
        /// 订单日期
        /// </summary>
        public DateTime OrderDate { get; set; }
        
        /// <summary>
        /// 订单类型
        /// </summary>
        public string OrderType { get; set; } = string.Empty;
        
        /// <summary>
        /// 本地供应商GUID
        /// </summary>
        public string? LocalSupplierGUID { get; set; }
        
        /// <summary>
        /// 中国供应商GUID
        /// </summary>
        public string? ChineseSupplierGUID { get; set; }
        
        /// <summary>
        /// 发货日期
        /// </summary>
        public DateTime? ShipDate { get; set; }
        
        /// <summary>
        /// 收货日期
        /// </summary>
        public DateTime? ReceiveDate { get; set; }
        
        /// <summary>
        /// 订单总金额
        /// </summary>
        public decimal? TotalAmount { get; set; }
        
        /// <summary>
        /// 订单状态
        /// </summary>
        public string OrderStatus { get; set; } = string.Empty;
        
        /// <summary>
        /// 备注信息
        /// </summary>
        public string? Remarks { get; set; }
        
        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }
        
        /// <summary>
        /// 订单项数量
        /// </summary>
        public int ItemCount { get; set; }

        /// <summary>
        /// 订单项列表
        /// </summary>
        public List<OrderItemDto> OrderItems { get; set; } = new List<OrderItemDto>();
    }

    /// <summary>
    /// 创建订单项DTO
    /// </summary>
    public class CreateOrderItemDto
    {
        /// <summary>
        /// 产品GUID
        /// </summary>
        [Required(ErrorMessage = "商品GUID不能为空")]
        public string ProductGUID { get; set; } = string.Empty;

        /// <summary>
        /// 单价
        /// </summary>
        [Range(0.01, double.MaxValue, ErrorMessage = "单价必须大于0")]
        public decimal UnitPrice { get; set; }

        /// <summary>
        /// 数量
        /// </summary>
        [Range(1, int.MaxValue, ErrorMessage = "数量必须大于0")]
        public int Quantity { get; set; }

        /// <summary>
        /// 已分配数量
        /// </summary>
        public int? AllocatedQuantity { get; set; }
        
        /// <summary>
        /// 总价
        /// </summary>
        public decimal? TotalPrice { get; set; }
    }

    /// <summary>
    /// 更新订单项DTO
    /// </summary>
    public class UpdateOrderItemDto : CreateOrderItemDto
    {
    }

    /// <summary>
    /// 订单项DTO
    /// </summary>
    public class OrderItemDto
    {
        /// <summary>
        /// 订单项GUID
        /// </summary>
        public string OrderItemGUID { get; set; } = string.Empty;
        
        /// <summary>
        /// 订单GUID
        /// </summary>
        public string OrderGUID { get; set; } = string.Empty;
        
        /// <summary>
        /// 产品GUID
        /// </summary>
        public string ProductGUID { get; set; } = string.Empty;
        
        /// <summary>
        /// 产品名称
        /// </summary>
        public string ProductName { get; set; } = string.Empty;
        
        /// <summary>
        /// 产品代码
        /// </summary>
        public string ProductCode { get; set; } = string.Empty;
        
        /// <summary>
        /// 单价
        /// </summary>
        public decimal UnitPrice { get; set; }
        
        /// <summary>
        /// 数量
        /// </summary>
        public int Quantity { get; set; }
        
        /// <summary>
        /// 已分配数量
        /// </summary>
        public int? AllocatedQuantity { get; set; }
        
        /// <summary>
        /// 总价
        /// </summary>
        public decimal? TotalPrice { get; set; }
        
        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// 订单状态更新DTO
    /// </summary>
    public class UpdateOrderStatusDto
    {
        /// <summary>
        /// 订单状态
        /// </summary>
        [Required(ErrorMessage = "订单状态不能为空")]
        [StringLength(20, ErrorMessage = "订单状态长度不能超过20个字符")]
        public string OrderStatus { get; set; } = string.Empty;

        /// <summary>
        /// 备注信息
        /// </summary>
        [StringLength(500, ErrorMessage = "备注长度不能超过500个字符")]
        public string? Remarks { get; set; }
    }
}