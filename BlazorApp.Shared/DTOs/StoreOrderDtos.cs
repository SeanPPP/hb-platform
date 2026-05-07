using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
  /// <summary>
  /// 订货页面商品DTO
  /// </summary>
  public class StoreOrderProductDto
  {
    public string ProductCode { get; set; } = string.Empty;
    public string? ItemNumber { get; set; }
    public string? Barcode { get; set; }
    public string? ProductName { get; set; }
    public string? ProductImage { get; set; }
    public string? CategoryName { get; set; }
    public string? WarehouseCategoryGUID { get; set; }

    /// <summary>
    /// 贴牌价格 (订货价格)
    /// </summary>
    public decimal? OEMPrice { get; set; }

    /// <summary>
    /// 最小订货量
    /// </summary>
    public int MinOrderQuantity { get; set; } = 1;

    /// <summary>
    /// 库存数量
    /// </summary>
    public int StockQuantity { get; set; }

    /// <summary>
    /// 是否有库存
    /// </summary>
    public bool IsInStock => StockQuantity > 0;

    /// <summary>
    /// 包装数量
    /// </summary>
    public int? PackQty { get; set; }

    /// <summary>
    /// 进口价格 (参考)
    /// </summary>
    public decimal? ImportPrice { get; set; }
  }

  /// <summary>
  /// 订货页面查询过滤DTO
  /// </summary>
  public class StoreOrderFilterDto
  {
    /// <summary>
    /// 货号 (精确或模糊匹配)
    /// </summary>
    public string? ItemNumber { get; set; }

    /// <summary>
    /// 商品名称 (模糊匹配)
    /// </summary>
    public string? ProductName { get; set; }

    /// <summary>
    /// 分类GUID
    /// </summary>
    public string? CategoryGUID { get; set; }

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 24;

    /// <summary>
    /// 排序字段: Default, PriceAsc, PriceDesc, Name
    /// </summary>
    public string SortBy { get; set; } = "Default";
  }

  /// <summary>
  /// 购物车DTO (FlowStatus=0 的 WareHouseOrder)
  /// </summary>
  public class StoreOrderCartDto
  {
    public string OrderGUID { get; set; } = string.Empty;
    public string? OrderNo { get; set; }
    public string? StoreCode { get; set; }
    public decimal TotalAmount { get; set; }
    public int TotalQuantity { get; set; }

    /// <summary>
    /// 总进口金额
    /// </summary>
    public decimal TotalImportAmount { get; set; }

    /// <summary>
    /// 总体积 (m³)
    /// </summary>
    public decimal TotalVolume { get; set; }

    /// <summary>
    /// 运费
    /// </summary>
    public decimal? ShippingFee { get; set; }

    public string? Remarks { get; set; }

    /// <summary>
    /// 门店地址
    /// </summary>
    public string? StoreAddress { get; set; }

    public DateTime? OrderDate { get; set; }

    public int TotalAllocQuantity { get; set; }

    public int TotalSKU { get; set; }

    /// <summary>
    /// 流程状态（0=草稿, 1=已提交, 2=已完成, 3=配货中）
    /// </summary>
    public int? FlowStatus { get; set; }

    public List<StoreOrderCartItemDto> Items { get; set; } = new();
  }

  /// <summary>
  /// 购物车明细DTO
  /// </summary>
  public class StoreOrderCartItemDto
  {
    public string DetailGUID { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string? ItemNumber { get; set; }
    public string? Barcode { get; set; }
    public string? ProductName { get; set; }
    public string? ProductImage { get; set; }
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal? AllocQuantity { get; set; }
    public decimal Amount { get; set; }

    /// <summary>
    /// 进口价格
    /// </summary>
    public decimal ImportPrice { get; set; }

    /// <summary>
    /// 进口金额
    /// </summary>
    public decimal ImportAmount { get; set; }

    /// <summary>
    /// 单件体积 (m³)
    /// </summary>
    public decimal? Volume { get; set; }

    /// <summary>
    /// 小计体积 (m³)
    /// </summary>
    public decimal? TotalVolume { get; set; }

    /// <summary>
    /// 最小订货量
    /// </summary>
    public int MinOrderQuantity { get; set; } = 1;

    /// <summary>
    /// 是否上架
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// 货位代码 (配货位)
    /// </summary>
    public string? LocationCode { get; set; }

    /// <summary>
    /// 零售价 (RRP)
    /// </summary>
    public decimal? RRP { get; set; }
  }

  /// <summary>
  /// 添加到购物车请求
  /// </summary>
  public class AddToCartRequestDto
  {
    [Required]
    public string StoreCode { get; set; } = string.Empty;

    [Required]
    public string ProductCode { get; set; } = string.Empty;

    [Required]
    public decimal Quantity { get; set; }

    public decimal? ImportPrice { get; set; }
  }

  /// <summary>
  /// 移除购物车项请求
  /// </summary>
  public class RemoveFromCartRequestDto
  {
    [Required]
    public string StoreCode { get; set; } = string.Empty;

    [Required]
    public string DetailGUID { get; set; } = string.Empty;
  }

  /// <summary>
  /// 清空购物车请求
  /// </summary>
  public class ClearCartRequestDto
  {
    [Required]
    public string StoreCode { get; set; } = string.Empty;
  }

  /// <summary>
  /// 提交订单请求
  /// </summary>
  public class SubmitStoreOrderRequestDto
  {
    [Required]
    public string StoreCode { get; set; } = string.Empty;

    public string? Remarks { get; set; }
  }

  /// <summary>
  /// 动态数据请求 (历史订单 + 购物车数量)
  /// </summary>
  public class StoreOrderDynamicDataRequestDto
  {
    [Required]
    public string StoreCode { get; set; } = string.Empty;
    public List<string> ProductCodes { get; set; } = new();
  }

  /// <summary>
  /// 商品动态数据 (历史订单 + 购物车数量)
  /// </summary>
  public class StoreOrderDynamicDataDto
  {
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>
    /// 最近订货日期
    /// </summary>
    public DateTime? LastOrderDate { get; set; }

    /// <summary>
    /// 最近订货数量
    /// </summary>
    public decimal? LastQuantity { get; set; }

    /// <summary>
    /// 最近配货数量
    /// </summary>
    public decimal? LastAllocQuantity { get; set; }

    /// <summary>
    /// 当前购物车数量
    /// </summary>
    public decimal CartQuantity { get; set; }
  }

  /// <summary>
  /// 订单列表过滤DTO
  /// </summary>
  public class StoreOrderListFilterDto
  {
    public string? Keyword { get; set; }
    public string? StoreCode { get; set; }
    public List<string>? StoreCodes { get; set; }
    public DateTime? StartDate { get; set;}
    public DateTime? EndDate { get; set; }
    public List<int>? StatusList { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; }
    public bool? SortDescending { get; set; }
  }

  /// <summary>
  /// 订单列表项DTO
  /// </summary>
  public class StoreOrderListItemDto
  {
    public string OrderGUID { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public string? StoreCode { get; set; }
    public string? StoreName { get; set; }
    public DateTime? OrderDate { get; set; }
    public int FlowStatus { get; set; }

    /// <summary>
    /// 预计销售金额 (Order Qty * OEMPrice)
    /// </summary>
    public decimal TotalAmount { get; set; }

    public decimal OEMTotalAmount { get; set; }

    /// <summary>
    /// 发货金额 (Alloc Qty * OEMPrice)
    /// </summary>
    public decimal ImportTotalAmount { get; set; }

    /// <summary>
    /// 订货金额 (Order Qty * OEMPrice)
    /// </summary>
    public decimal TotalOrderAmount { get; set; }

    public int TotalQuantity { get; set; }

    /// <summary>
    /// 发货数量 (Alloc Qty)
    /// </summary>
    public int TotalAllocQuantity { get; set; }

    public string? Remarks { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 创建者
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// 更新者
    /// </summary>
    public string? UpdatedBy { get; set; }
  }

  /// <summary>
  /// 创建订单请求
  /// </summary>
  public class CreateStoreOrderDto
  {
    [Required]
    public string StoreCode { get; set; } = string.Empty;

    public string? Remarks { get; set; }
  }

  /// <summary>
  /// 添加商品到指定订单请求
  /// </summary>
  public class AddOrderLineDto
  {
    [Required]
    public string OrderGUID { get; set; } = string.Empty;

    [Required]
    public string ProductCode { get; set; } = string.Empty;

    [Required]
    public decimal Quantity { get; set; }

    public decimal? ImportPrice { get; set; }
  }

  public class ProductQuantityDto
  {
    public string ProductCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal? ImportPrice { get; set; }
  }

  /// <summary>
  /// 批量更新订单行请求 (数量或价格)
  /// </summary>
  public class BatchUpdateOrderLineDto
  {
    [Required]
    public string OrderGUID { get; set; } = string.Empty;

    public List<BatchUpdateItemDto> Items { get; set; } = new();
  }

  public class BatchUpdateItemDto
  {
    public string ProductCode { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public decimal? ImportPrice { get; set; }
  }

  /// <summary>
  /// 批量添加商品请求
  /// </summary>
  public class BatchAddOrderLineDto
  {
    [Required]
    public string OrderGUID { get; set; } = string.Empty;

    public List<ProductQuantityDto> Items { get; set; } = new();
  }

  /// <summary>
  /// 更新订单行请求
  /// </summary>
  public class UpdateOrderLineDto
  {
    [Required]
    public string OrderGUID { get; set; } = string.Empty;

    [Required]
    public string ProductCode { get; set; } = string.Empty;

    [Required]
    public decimal Quantity { get; set; }

    public decimal? ImportPrice { get; set; }
  }

  /// <summary>
  /// 软删除订单行请求
  /// </summary>
  public class RemoveOrderLineDto
  {
    [Required]
    public string OrderGUID { get; set; } = string.Empty;

    [Required]
    public string DetailGUID { get; set; } = string.Empty;
  }

  /// <summary>
  /// 更新订单头请求
  /// </summary>
  public class UpdateOrderHeaderDto
  {
    public string OrderGuid { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public decimal? ShippingFee { get; set; }
    public DateTime? OrderDate { get; set; }
    public string? StoreCode { get; set; }
  }
}

/// <summary>
/// 更新商品状态请求
/// </summary>
public class UpdateProductStatusDto
{
  [Required]
  public string ProductCode { get; set; } = string.Empty;

  [Required]
  public bool IsActive { get; set; }
}

/// <summary>
/// 批量更新商品状态请求
/// </summary>
public class BatchUpdateProductStatusDto
{
  [Required]
  public List<string> ProductCodes { get; set; } = new();

  [Required]
  public bool IsActive { get; set; }
}

/// <summary>
/// 复制订单请求
/// </summary>
public class CopyOrderDto
{
  [Required]
  public string SourceOrderGUID { get; set; } = string.Empty;

  [Required]
  public string TargetStoreCode { get; set; } = string.Empty;

  public bool CopyOrderQuantity { get; set; } = false;

  public bool CopyAllocQuantity { get; set; } = false;
}

/// <summary>
/// 复制订单结果
/// </summary>
public class CopyOrderResultDto
{
    public string OrderGUID { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
}

/// <summary>
/// 同步缺失订单的返回结果
/// </summary>
public class SyncMissingOrdersResultDto
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public int OrdersSynced { get; set; }

    public int DetailsSynced { get; set; }

    public int OrdersUpdated { get; set; }

    public int DetailsUpdated { get; set; }
}

/// <summary>
/// 同步缺失订单的请求参数
/// </summary>
public class SyncMissingOrdersRequestDto
{
    /// <summary>
    /// 分店代码（可选，不传则同步所有分店）
    /// </summary>
    public string? StoreCode { get; set; }
}

/// <summary>
/// 更新订单状态请求
/// </summary>
public class UpdateOrderStatusDto
{
    /// <summary>
    /// 订单GUID
    /// </summary>
    [Required]
    public string OrderGUID { get; set; } = string.Empty;

    /// <summary>
    /// 新状态 (1=Submitted, 2=Completed)
    /// </summary>
    [Required]
    public int NewStatus { get; set; }
}

/// <summary>
/// 批量更新订单状态请求
/// </summary>
public class BatchUpdateOrderStatusDto
{
    /// <summary>
    /// 订单GUID列表
    /// </summary>
    [Required]
    public List<string> OrderGUIDs { get; set; } = new();

    /// <summary>
    /// 新状态 (1=Submitted, 2=Completed)
    /// </summary>
    [Required]
    public int NewStatus { get; set; }
}
