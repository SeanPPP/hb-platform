using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 增量保存请求DTO
    /// 用于保存单条或部分修改的数据
    /// </summary>
    public class IncrementalSaveRequest
    {
        /// <summary>
        /// 要保存的商品（可以是1条或多条）
        /// </summary>
        [Required(ErrorMessage = "商品数据不能为空")]
        [MinLength(1, ErrorMessage = "至少需要一条商品数据")]
        public List<WarehouseProductBatchDto> Products { get; set; } = new();

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
    /// 增量保存结果DTO
    /// </summary>
    public class IncrementalSaveResult
    {
        /// <summary>
        /// 是否全部成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 成功保存的数量
        /// </summary>
        public int SavedCount { get; set; }

        /// <summary>
        /// 失败的数量
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 更新后的数据（包含新的RowVersion）
        /// </summary>
        public List<WarehouseProductBatchDto>? UpdatedProducts { get; set; }

        /// <summary>
        /// 失败的商品详情
        /// </summary>
        public Dictionary<string, string>? FailedDetails { get; set; }
    }
}

