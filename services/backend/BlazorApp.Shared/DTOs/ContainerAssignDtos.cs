using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 检查冲突请求 DTO
    /// </summary>
    public class CheckConflictsRequestDto
    {
        /// <summary>
        /// 货柜编号或货柜编码（兼容）
        /// </summary>
        [Required]
        public string ContainerId { get; set; } = string.Empty;

        /// <summary>
        /// 待检查的商品项（按 ProductCode）
        /// </summary>
        public List<CheckConflictItemDto> Items { get; set; } = new();
    }

    /// <summary>
    /// 待检查的商品项（仅包含 ProductCode）
    /// </summary>
    public class CheckConflictItemDto
    {
        public string ProductCode { get; set; } = string.Empty;
    }

    /// <summary>
    /// 冲突项返回 DTO
    /// </summary>
    public class ContainerConflictItemDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public decimal? ExistingPieces { get; set; }
        public decimal? ExistingPackingQuantity { get; set; }
        public decimal? ExistingUnitVolume { get; set; }
    }

    /// <summary>
    /// 分配商品请求 DTO
    /// </summary>
    public class AssignProductsRequestDto
    {
        [Required]
        public string ContainerId { get; set; } = string.Empty;

        [Required]
        public string Resolution { get; set; } = "increase"; // override/increase

        public string? Notes { get; set; }

        public List<AssignProductItemDto> Items { get; set; } = new();
    }

    /// <summary>
    /// 分配的商品项
    /// </summary>
    public class AssignProductItemDto
    {
        [Required]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 件数 → 装柜件数（LoadingPieces）
        /// </summary>
        [Range(0.0001, double.MaxValue)]
        public decimal Quantity { get; set; }

        /// <summary>
        /// 单件装箱数（PackingQuantity）
        /// </summary>
        public decimal? PackingQuantity { get; set; }

        /// <summary>
        /// 单件体积（UnitVolume）
        /// </summary>
        public decimal? UnitVolume { get; set; }

        /// <summary>
        /// 国内价格（DomesticPrice）
        /// 说明：用于计算实际单价（考虑调整浮率）与合计装柜金额
        /// </summary>
        public decimal? DomesticPrice { get; set; }

        /// <summary>
        /// 贴牌价格（OEMPrice）
        /// 说明：可选，保持与后续批量更新接口一致
        /// </summary>
        public decimal? OEMPrice { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string? Notes { get; set; }
    }

    /// <summary>
    /// 分配结果 DTO
    /// </summary>
    public class AssignProductsResultDto
    {
        public int Created { get; set; }
        public int Updated { get; set; }
        public List<AssignProductFailedItemDto> Failed { get; set; } = new();
    }

    public class AssignProductFailedItemDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }
}