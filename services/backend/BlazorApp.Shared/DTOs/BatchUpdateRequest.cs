using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 批量更新请求DTO
    /// </summary>
    public class BatchUpdateRequest
    {
        /// <summary>
        /// 要更新的商品列表
        /// </summary>
        [Required(ErrorMessage = "商品列表不能为空")]
        [MinLength(1, ErrorMessage = "至少需要一条商品数据")]
        public List<WarehouseProductBatchDto> Products { get; set; } = new();

        /// <summary>
        /// 操作类型（update-更新，delete-删除）
        /// </summary>
        public string OperationType { get; set; } = "update";

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
    /// 批量更新结果DTO
    /// </summary>
    public class BatchUpdateResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 成功更新的数量
        /// </summary>
        public int UpdatedCount { get; set; }

        /// <summary>
        /// 失败的数量
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 失败的商品编码列表
        /// </summary>
        public List<string>? FailedProductCodes { get; set; }

        /// <summary>
        /// 详细错误信息（商品编码 -> 错误原因）
        /// </summary>
        public Dictionary<string, string>? DetailErrors { get; set; }
    }
}

