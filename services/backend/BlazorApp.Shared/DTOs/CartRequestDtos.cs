using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 创建购物车请求
    /// </summary>
    public class CreateCartRequest
    {
        /// <summary>
        /// 购物车名称
        /// </summary>
        [Required(ErrorMessage = "购物车名称不能为空")]
        [StringLength(100, ErrorMessage = "购物车名称长度不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 购物车描述
        /// </summary>
        [StringLength(500, ErrorMessage = "购物车描述长度不能超过500个字符")]
        public string? Description { get; set; }

        /// <summary>
        /// 分店GUID
        /// </summary>
        public string? StoreGuid { get; set; }

        /// <summary>
        /// 购物车商品列表
        /// </summary>
        public List<CartItemDto> Items { get; set; } = new List<CartItemDto>();
    }

    /// <summary>
    /// 更新购物车请求
    /// </summary>
    public class UpdateCartRequest
    {
        /// <summary>
        /// 购物车ID
        /// </summary>
        [Required(ErrorMessage = "购物车ID不能为空")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 购物车名称
        /// </summary>
        [Required(ErrorMessage = "购物车名称不能为空")]
        [StringLength(100, ErrorMessage = "购物车名称长度不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 购物车描述
        /// </summary>
        [StringLength(500, ErrorMessage = "购物车描述长度不能超过500个字符")]
        public string? Description { get; set; }

        /// <summary>
        /// 购物车商品列表
        /// </summary>
        public List<CartItemDto> Items { get; set; } = new List<CartItemDto>();
    }

    /// <summary>
    /// 批量添加商品到购物车请求
    /// </summary>
    public class BatchAddToCartRequest
    {
        /// <summary>
        /// 购物车GUID
        /// </summary>
        [Required(ErrorMessage = "购物车GUID不能为空")]
        public string CartGuid { get; set; } = string.Empty;

        /// <summary>
        /// 商品列表
        /// </summary>
        public List<BatchAddCartItem> Items { get; set; } = new List<BatchAddCartItem>();
    }

    /// <summary>
    /// 批量添加购物车商品项
    /// </summary>
    public class BatchAddCartItem
    {
        /// <summary>
        /// 商品GUID
        /// </summary>
        [Required(ErrorMessage = "商品GUID不能为空")]
        public string ProductGuid { get; set; } = string.Empty;

        /// <summary>
        /// 数量
        /// </summary>
        [Range(1, int.MaxValue, ErrorMessage = "数量必须大于0")]
        public int Quantity { get; set; } = 1;

        /// <summary>
        /// 备注
        /// </summary>
        public string? Remarks { get; set; }
    }

    #region PDA设备专用购物车请求DTO

    /// <summary>
    /// PDA设备添加商品到购物车请求
    /// </summary>
    public class AddProductToCartRequest
    {
        /// <summary>
        /// 商品代码
        /// </summary>
        [Required(ErrorMessage = "商品代码不能为空")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 数量
        /// </summary>
        [Range(1, int.MaxValue, ErrorMessage = "数量必须大于0")]
        public int Quantity { get; set; }

        /// <summary>
        /// 单价（可选，如果不提供则使用商品默认价格）
        /// </summary>
        public decimal? UnitPrice { get; set; }
    }

    /// <summary>
    /// PDA设备批量添加商品到购物车请求
    /// </summary>
    public class BatchAddProductsRequest
    {
        /// <summary>
        /// 商品列表
        /// </summary>
        [Required(ErrorMessage = "商品列表不能为空")]
        public List<AddProductToCartRequest> Items { get; set; } = new List<AddProductToCartRequest>();
    }


    #endregion

}
