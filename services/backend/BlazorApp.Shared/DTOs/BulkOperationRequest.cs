using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 批量操作请求基类
    /// </summary>
    public class BulkOperationRequestBase
    {
        /// <summary>
        /// 要操作的商品编码列表
        /// </summary>
        [Required(ErrorMessage = "商品编码列表不能为空")]
        [MinLength(1, ErrorMessage = "至少需要一个商品编码")]
        public List<string> ProductCodes { get; set; } = new();

        /// <summary>
        /// 操作人ID
        /// </summary>
        public string? OperatorId { get; set; }

        /// <summary>
        /// 操作备注
        /// </summary>
        public string? Remark { get; set; }
    }

    /// <summary>
    /// 批量设置价格请求DTO
    /// </summary>
    public class BulkSetPriceRequest : BulkOperationRequestBase
    {
        /// <summary>
        /// 价格类型（Domestic-国内价，OEM-零售价，Import-进口价）
        /// </summary>
        [Required(ErrorMessage = "价格类型不能为空")]
        public string PriceType { get; set; } = string.Empty;

        /// <summary>
        /// 新价格值
        /// </summary>
        [Required(ErrorMessage = "价格值不能为空")]
        [Range(0, double.MaxValue, ErrorMessage = "价格不能为负数")]
        public decimal Price { get; set; }
    }

    /// <summary>
    /// 批量调整库存请求DTO
    /// </summary>
    public class BulkAdjustStockRequest : BulkOperationRequestBase
    {
        /// <summary>
        /// 调整类型（Set-设置为，Add-增加，Subtract-减少）
        /// </summary>
        [Required(ErrorMessage = "调整类型不能为空")]
        public string AdjustType { get; set; } = string.Empty;

        /// <summary>
        /// 调整数量
        /// </summary>
        [Required(ErrorMessage = "调整数量不能为空")]
        [Range(0, int.MaxValue, ErrorMessage = "调整数量不能为负数")]
        public int Quantity { get; set; }
    }

    /// <summary>
    /// 批量设置状态请求DTO
    /// </summary>
    public class BulkSetStatusRequest : BulkOperationRequestBase
    {
        /// <summary>
        /// 使用状态（true-启用，false-禁用）
        /// </summary>
        [Required(ErrorMessage = "状态值不能为空")]
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// 批量设置仓位请求DTO
    /// </summary>
    public class BulkSetLocationRequest : BulkOperationRequestBase
    {
        /// <summary>
        /// 仓位GUID
        /// </summary>
        [Required(ErrorMessage = "仓位不能为空")]
        public string LocationGuid { get; set; } = string.Empty;
    }

    /// <summary>
    /// 批量操作结果DTO
    /// </summary>
    public class BulkOperationResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 影响的行数
        /// </summary>
        public int AffectedCount { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 失败的商品编码列表
        /// </summary>
        public List<string>? FailedProductCodes { get; set; }
    }
}
