using System;
using System.Collections.Generic;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 保存的购物车DTO
    /// </summary>
    public class SavedCartDto
    {
        /// <summary>
        /// 保存的购物车ID
        /// </summary>
        public string? SavedCartId { get; set; }
        
        /// <summary>
        /// 购物车名称
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// 保存时间
        /// </summary>
        public DateTime SavedAt { get; set; }
        
        /// <summary>
        /// 分店GUID
        /// </summary>
        public string? StoreGuid { get; set; }
        
        /// <summary>
        /// 分店名称
        /// </summary>
        public string? StoreName { get; set; }
        
        /// <summary>
        /// 用户GUID
        /// </summary>
        public string? UserGuid { get; set; }
        
        /// <summary>
        /// 购物车项目列表
        /// </summary>
        public List<SavedCartItemDto> Items { get; set; } = new List<SavedCartItemDto>();
        
        /// <summary>
        /// 总金额
        /// </summary>
        public decimal TotalAmount { get; set; }
        
        /// <summary>
        /// 总数量
        /// </summary>
        public int TotalItems { get; set; }
        
        /// <summary>
        /// 商品种类数量
        /// </summary>
        public int UniqueItems { get; set; }
        
        /// <summary>
        /// 描述/备注
        /// </summary>
        public string? Description { get; set; }
        
        /// <summary>
        /// 是否为模板（可重复使用）
        /// </summary>
        public bool IsTemplate { get; set; } = false;
        
        /// <summary>
        /// 最后使用时间
        /// </summary>
        public DateTime? LastUsedAt { get; set; }
        
        /// <summary>
        /// 使用次数
        /// </summary>
        public int UseCount { get; set; } = 0;
    }
    
    /// <summary>
    /// 保存的购物车项目DTO
    /// </summary>
    public class SavedCartItemDto
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
        /// 商品编号
        /// </summary>
        public string ItemNumber { get; set; } = string.Empty;
        
        /// <summary>
        /// 数量
        /// </summary>
        public int Quantity { get; set; }
        
        /// <summary>
        /// 单价
        /// </summary>
        public decimal UnitPrice { get; set; }
        
        /// <summary>
        /// 总价
        /// </summary>
        public decimal TotalPrice => UnitPrice * Quantity;
        
        /// <summary>
        /// 商品图片
        /// </summary>
        public string? ProductImage { get; set; }
        
        /// <summary>
        /// 最小订货量
        /// </summary>
        public int? MinOrderQuantity { get; set; }
        
        /// <summary>
        /// 体积
        /// </summary>
        public decimal? Volume { get; set; }
        
        /// <summary>
        /// 保存时的价格（用于比较价格变动）
        /// </summary>
        public decimal SavedPrice { get; set; }
        
        /// <summary>
        /// 保存时间
        /// </summary>
        public DateTime SavedAt { get; set; }
    }
    
    /// <summary>
    /// 保存购物车请求DTO
    /// </summary>
    public class SaveCartRequest
    {
        /// <summary>
        /// 购物车名称
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// 描述
        /// </summary>
        public string? Description { get; set; }
        
        /// <summary>
        /// 是否为模板
        /// </summary>
        public bool IsTemplate { get; set; } = false;
        
        /// <summary>
        /// 保存后是否清空当前购物车
        /// </summary>
        public bool ClearCurrentCart { get; set; } = false;
    }
    
    /// <summary>
    /// 恢复保存的购物车请求DTO
    /// </summary>
    public class RestoreSavedCartRequest
    {
        /// <summary>
        /// 保存的购物车ID
        /// </summary>
        public string SavedCartId { get; set; } = string.Empty;
        
        /// <summary>
        /// 是否清空当前购物车后再恢复
        /// </summary>
        public bool ClearCurrentCart { get; set; } = true;
        
        /// <summary>
        /// 是否保留原有数量（false则重置为保存时的数量）
        /// </summary>
        public bool KeepExistingQuantities { get; set; } = false;
    }
    
    /// <summary>
    /// 保存的购物车列表响应DTO
    /// </summary>
    public class SavedCartListResponse
    {
        /// <summary>
        /// 保存的购物车列表
        /// </summary>
        public List<SavedCartDto> SavedCarts { get; set; } = new List<SavedCartDto>();
        
        /// <summary>
        /// 总数量
        /// </summary>
        public int TotalCount { get; set; }
        
        /// <summary>
        /// 成功标志
        /// </summary>
        public bool Success { get; set; }
        
        /// <summary>
        /// 错误消息
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}