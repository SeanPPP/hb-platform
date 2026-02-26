using System.ComponentModel.DataAnnotations;
using BlazorApp.Shared.Constants;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 购物车数据传输对象
    /// </summary>
    public class CartDto
    {
        /// <summary>
        /// 购物车GUID
        /// </summary>
        public string? CartGUID { get; set; }

        /// <summary>
        /// 用户GUID
        /// </summary>
        public string UserGUID { get; set; } = string.Empty;

        /// <summary>
        /// 门店GUID（可空 - 购物车创建时不绑定门店，订单确认时选择）
        /// </summary>
        public string? StoreGUID { get; set; }

        /// <summary>
        /// 门店名称（用于显示）
        /// </summary>
        public string? StoreName { get; set; }

        /// <summary>
        /// 门店地址（用于显示）
        /// </summary>
        public string? StoreAddress { get; set; }

        /// <summary>
        /// 用户名（用于仓库管理员显示）
        /// </summary>
        public string? UserName { get; set; }

        /// <summary>
        /// 用户邮箱（用于仓库管理员显示）
        /// </summary>
        public string? UserEmail { get; set; }

        /// <summary>
        /// 购物车名称
        /// </summary>
        public string? CartName { get; set; }
        
        /// <summary>
        /// 购物车订单号（格式：ORD-YYYY-0001）
        /// </summary>
        public string? OrderNumber { get; set; }

        /// <summary>
        /// 购物车状态（Active/Save/Submitted）
        /// </summary>
        public string CartStatus { get; set; } = CartStatusConstants.Active;

        /// <summary>
        /// 总金额
        /// </summary>
        public decimal? TotalAmount { get; set; }

        /// <summary>
        /// 总商品数量
        /// </summary>
        public int? TotalQuantity { get; set; }

        /// <summary>
        /// 总商品体积
        /// </summary>
        public decimal? TotalVolume { get; set; }

        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime LastModified { get; set; }

        /// <summary>
        /// 购物车备注信息
        /// </summary>
        public string? Remarks { get; set; }

        /// <summary>
        /// 折扣金额
        /// </summary>
        public decimal? Discount { get; set; }

        /// <summary>
        /// 运费
        /// </summary>
        public decimal? FreightFee { get; set; }

        /// <summary>
        /// GST税费 (10%)
        /// </summary>
        public decimal? GST { get; set; }

        /// <summary>
        /// 购物车项列表
        /// </summary>
        public List<CartItemDto> CartItems { get; set; } = new List<CartItemDto>();
    }

    /// <summary>
    /// 购物车项目数据传输对象
    /// </summary>
    public class CartItemDto
    {
        /// <summary>
        /// 购物车项GUID
        /// </summary>
        public string? CartItemGUID { get; set; }

        /// <summary>
        /// 购物车GUID
        /// </summary>
        public string? CartGUID { get; set; }

        /// <summary>
        /// 商品GUID
        /// </summary>
        [Required(ErrorMessage = "商品GUID不能为空")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 商品代码
        /// </summary>
        public string? ItemNumber { get; set; }

        /// <summary>
        /// 商品名称
        /// </summary>
        public string? ProductName { get; set; }

        /// <summary>
        /// 商品图片
        /// </summary>
        public string? ProductImage { get; set; }

        /// <summary>
        /// 单价
        /// </summary>
        public decimal UnitPrice { get; set; }

        /// <summary>
        /// 商品数量（正数表示购买，负数表示退货，0表示移除）
        /// </summary>
        [Required(ErrorMessage = "商品数量不能为空")]
        public int Quantity { get; set; } = 1;

        /// <summary>
        /// 总价
        /// </summary>
        public decimal? TotalPrice { get; set; }

        /// <summary>
        /// 体积
        /// </summary>
        public decimal? Volume { get; set; }

        /// <summary>
        /// 重量
        /// </summary>
        public decimal? Weight { get; set; }

        /// <summary>
        /// 最小订货量
        /// </summary>
        public int MinOrderQuantity { get; set; }

        /// <summary>
        /// 添加到购物车的时间
        /// </summary>
        public DateTime AddedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.Now;

        /// <summary>
        /// 备注
        /// </summary>
        public string? Remarks { get; set; }

        /// <summary>
        /// 实际配货价格（仓库管理员设置，也用于发票打印）
        /// </summary>
        public decimal? ActualPrice { get; set; }

        /// <summary>
        /// 实际配货数量（仓库管理员设置，也是已分配数量）
        /// </summary>
        public int? ActualQuantity { get; set; }

        /// <summary>
        /// 商品货位编码
        /// </summary>
        public string? LocationCode { get; set; }

        /// <summary>
        /// RRP零售价格（来自WarehouseProduct.OEMPrice）
        /// </summary>
        public decimal? RRPPrice { get; set; }

        /// <summary>
        /// 捡包数量（配货数量/最小订货数）- 计算字段
        /// </summary>
        public int PickingPackageCount =>
            MinOrderQuantity > 0 ? 
                ( Quantity) / MinOrderQuantity : 
                ( Quantity);
    }

    /// <summary>
    /// 添加到购物车请求DTO（购物车不再绑定门店）
    /// </summary>
    public class AddToCartRequest
    {
        /// <summary>
        /// 购物车项数据传输对象
        /// </summary>
        [Required(ErrorMessage = "CartItem is required")]
        public CartItemDto CartItem { get; set; } = new CartItemDto();

        /// <summary>
        /// 是否替换现有商品（如果购物车中已有相同商品）
        /// </summary>
        public bool ReplaceIfExists { get; set; } = false;
    }

    /// <summary>
    /// 批量添加到购物车请求DTO
    /// </summary>
    public class AddMultipleToCartRequest
    {
        /// <summary>
        /// 购物车项列表
        /// </summary>
        [Required(ErrorMessage = "CartItems are required")]
        [MinLength(1, ErrorMessage = "At least one cart item is required")]
        public List<CartItemDto> CartItems { get; set; } = new List<CartItemDto>();

        /// <summary>
        /// 是否替换现有商品（如果购物车中已有相同商品）
        /// </summary>
        public bool ReplaceIfExists { get; set; } = false;
    }

    /// <summary>
    /// 更新购物车项数量请求DTO
    /// </summary>
    public class UpdateCartItemQuantityRequest
    {
        /// <summary>
        /// 购物车项GUID
        /// </summary>
        [Required(ErrorMessage = "购物车项GUID不能为空")]
        public string CartItemGUID { get; set; } = string.Empty;

        /// <summary>
        /// 新数量（正数表示购买，负数表示退货，0表示移除）
        /// </summary>
        [Required(ErrorMessage = "商品数量不能为空")]
        public int Quantity { get; set; } = 1;
    }

    /// <summary>
    /// 购物车同步请求DTO
    /// </summary>
    public class CartSyncRequest
    {
        /// <summary>
        /// 本地购物车项列表
        /// </summary>
        public List<LocalCartItem> LocalCartItems { get; set; } = new List<LocalCartItem>();

        /// <summary>
        /// 门店GUID
        /// </summary>
        public string? StoreGUID { get; set; }
    }

    /// <summary>
    /// 本地购物车项（用于同步）
    /// </summary>
    public class LocalCartItem
    {
        /// <summary>
        /// 商品代码
        /// </summary>
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 商品名称
        /// </summary>
        public string ProductName { get; set; } = string.Empty;

        /// <summary>
        /// 商品图片
        /// </summary>
        public string? ProductImage { get; set; }

        /// <summary>
        /// 单价
        /// </summary>
        public decimal UnitPrice { get; set; }

        /// <summary>
        /// 数量
        /// </summary>
        public int Quantity { get; set; }

        /// <summary>
        /// 体积
        /// </summary>
        public decimal Volume { get; set; }

        /// <summary>
        /// 重量
        /// </summary>
        public decimal Weight { get; set; }

        /// <summary>
        /// 最小订货量
        /// </summary>
        public int MinOrderQuantity { get; set; }

        /// <summary>
        /// 添加时间
        /// </summary>
        public DateTime AddedAt { get; set; }
    }

    /// <summary>
    /// 购物车摘要DTO
    /// </summary>
    public class CartSummaryDto
    {
        /// <summary>
        /// 总商品数量
        /// </summary>
        public int TotalItems { get; set; }

        /// <summary>
        /// 唯一商品种数
        /// </summary>
        public int UniqueItems { get; set; }

        /// <summary>
        /// 总金额
        /// </summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// 总体积
        /// </summary>
        public decimal TotalVolume { get; set; }

        /// <summary>
        /// 总重量
        /// </summary>
        public decimal TotalWeight { get; set; }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastModified { get; set; }
    }

    /// <summary>
    /// 订单确认请求DTO（选择门店并创建订单）
    /// </summary>
    public class CreateOrderFromCartRequest
    {
        /// <summary>
        /// 选择的门店GUID
        /// </summary>
        [Required(ErrorMessage = "Store selection is required")]
        public string StoreGUID { get; set; } = string.Empty;

        /// <summary>
        /// 配送地址
        /// </summary>
        public string? DeliveryAddress { get; set; }

        /// <summary>
        /// 订单备注
        /// </summary>
        public string? OrderRemarks { get; set; }

        /// <summary>
        /// 期望配送日期
        /// </summary>
        public DateTime? ExpectedDeliveryDate { get; set; }
    }
    
    /// <summary>
    /// 批量检查商品请求
    /// </summary>
    public class BatchCheckProductsRequest
    {
        /// <summary>
        /// 商品代码列表
        /// </summary>
        public List<string> ProductCodes { get; set; } = new List<string>();
    }
    
    /// <summary>
    /// 保存购物车状态请求DTO（更新状态为Save）
    /// </summary>
    public class SaveCartStatusRequest
    {
        /// <summary>
        /// 购物车名称
        /// </summary>
        public string? CartName { get; set; }
        
        /// <summary>
        /// 选择的门店GUID
        /// </summary>
        public string? StoreGUID { get; set; }
    }
    
    /// <summary>
    /// 提交购物车请求DTO（Checkout）
    /// </summary>
    public class SubmitCartRequest
    {
        /// <summary>
        /// 选择的门店GUID
        /// </summary>
        [Required(ErrorMessage = "Store selection is required")]
        public string StoreGUID { get; set; } = string.Empty;

        /// <summary>
        /// 配送地址
        /// </summary>
        public string? DeliveryAddress { get; set; }

        /// <summary>
        /// 订单备注
        /// </summary>
        public string? OrderRemarks { get; set; }

        /// <summary>
        /// 期望配送日期
        /// </summary>
        public DateTime? ExpectedDeliveryDate { get; set; }
    }
    
    /// <summary>
    /// 购物车列表查询请求DTO
    /// </summary>
    public class CartListRequest
    {
        /// <summary>
        /// 状态过滤（可选）
        /// </summary>
        public string? Status { get; set; }
        
        /// <summary>
        /// 页码（从1开始）
        /// </summary>
        public int Page { get; set; } = 1;
        
        /// <summary>
        /// 每页大小
        /// </summary>
        public int PageSize { get; set; } = 20;
        
        /// <summary>
        /// 搜索关键词（搜索购物车名称或订单号）
        /// </summary>
        public string? SearchKeyword { get; set; }
        
        /// <summary>
        /// 分店ID过滤（可选）
        /// </summary>
        public string? StoreId { get; set; }
        
        /// <summary>
        /// 要排除的状态列表（可选）
        /// </summary>
        public List<string>? ExcludeStatuses { get; set; }
    }
    
    /// <summary>
    /// 购物车列表响应DTO
    /// </summary>
    public class CartListResponse
    {
        /// <summary>
        /// 购物车列表
        /// </summary>
        public List<CartDto> Carts { get; set; } = new List<CartDto>();
        
        /// <summary>
        /// 总数量
        /// </summary>
        public int TotalCount { get; set; }
        
        /// <summary>
        /// 当前页码
        /// </summary>
        public int CurrentPage { get; set; }
        
        /// <summary>
        /// 每页大小
        /// </summary>
        public int PageSize { get; set; }
        
        /// <summary>
        /// 总页数
        /// </summary>
        public int TotalPages { get; set; }
    }
    
    /// <summary>
    /// 检查活跃购物车响应DTO
    /// </summary>
    public class ActiveCartCheckResponse
    {
        /// <summary>
        /// 是否有活跃购物车
        /// </summary>
        public bool HasActiveCart { get; set; }
        
        /// <summary>
        /// 活跃购物车信息
        /// </summary>
        public CartDto? ActiveCart { get; set; }
    }
    
    /// <summary>
    /// 购物车状态切换请求DTO
    /// </summary>
    public class CartStatusSwitchRequest
    {
        /// <summary>
        /// 要切换状态的购物车GUID
        /// </summary>
        [Required(ErrorMessage = "购物车GUID不能为空")]
        public string FromCartGuid { get; set; } = string.Empty;
        
        /// <summary>
        /// 目标状态
        /// </summary>
        [Required(ErrorMessage = "目标状态不能为空")]
        public string ToStatus { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// 购物车合并请求DTO
    /// </summary>
    public class CartMergeRequest
    {
        /// <summary>
        /// 源购物车GUID（Save状态）
        /// </summary>
        [Required(ErrorMessage = "源购物车GUID不能为空")]
        public string SourceCartGuid { get; set; } = string.Empty;
        
        /// <summary>
        /// 目标购物车GUID（Active状态）
        /// </summary>
        [Required(ErrorMessage = "目标购物车GUID不能为空")]
        public string TargetCartGuid { get; set; } = string.Empty;
        
        /// <summary>
        /// 是否删除源购物车（默认改为已使用状态）
        /// </summary>
        public bool DeleteSourceCart { get; set; } = false;
        
        /// <summary>
        /// 重复商品处理策略（add: 累加数量, replace: 替换数量, skip: 跳过）
        /// </summary>
        public string DuplicateStrategy { get; set; } = "add";
    }

    /// <summary>
    /// 更新购物车备注请求
    /// </summary>
    public class UpdateCartRemarksRequest
    {
        /// <summary>
        /// 备注内容
        /// </summary>
        public string? Remarks { get; set; }
    }

    /// <summary>
    /// 更改分店请求
    /// </summary>
    public class ChangeStoreRequest
    {
        /// <summary>
        /// 购物车GUID
        /// </summary>
        public string CartGuid { get; set; } = string.Empty;
        
        /// <summary>
        /// 新分店GUID
        /// </summary>
        [Required(ErrorMessage = "新分店GUID不能为空")]
        public string NewStoreGuid { get; set; } = string.Empty;
        
        /// <summary>
        /// 更改原因
        /// </summary>
        [Required(ErrorMessage = "更改原因不能为空")]
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// 更新购物车项价格请求DTO
    /// </summary>
    public class UpdateCartItemPriceRequest
    {
        /// <summary>
        /// 购物车项GUID
        /// </summary>
        [Required(ErrorMessage = "购物车项GUID不能为空")]
        public string CartItemGUID { get; set; } = string.Empty;

        /// <summary>
        /// 实际价格
        /// </summary>
        [Range(0, double.MaxValue, ErrorMessage = "价格不能为负数")]
        public decimal? ActualPrice { get; set; }
    }

    /// <summary>
    /// 批量更新价格请求DTO
    /// </summary>
    public class BatchUpdatePricesRequest
    {
        /// <summary>
        /// 价格更新字典：CartItemGUID -> ActualPrice
        /// </summary>
        [Required(ErrorMessage = "更新数据不能为空")]
        public Dictionary<string, decimal?> Updates { get; set; } = new();
    }

    /// <summary>
    /// 更新购物车折扣和运费请求
    /// </summary>
    public class UpdateCartDiscountFreightRequest
    {
        /// <summary>
        /// 折扣金额
        /// </summary>
        public decimal? Discount { get; set; }

        /// <summary>
        /// 运费
        /// </summary>
        public decimal? FreightFee { get; set; }
    }

    /// <summary>
    /// 批量删除购物车项请求
    /// </summary>
    public class BatchRemoveCartItemsRequest
    {
        /// <summary>
        /// 要删除的购物车项GUID列表
        /// </summary>
        [Required(ErrorMessage = "购物车项GUID列表不能为空")]
        public List<string> CartItemGuids { get; set; } = new();
    }
}