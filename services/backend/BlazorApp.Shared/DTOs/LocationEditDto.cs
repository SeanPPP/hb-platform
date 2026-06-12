using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 仓位编辑DTO
    /// 用于编辑商品的仓位信息
    /// </summary>
    public class LocationEditDto
    {
        /// <summary>
        /// 商品编码
        /// </summary>
        [Required(ErrorMessage = "商品编码不能为空")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 仓位GUID（UI限制只能选1个）
        /// </summary>
        public string? LocationGuid { get; set; }

        /// <summary>
        /// 操作人ID
        /// </summary>
        public string? OperatorId { get; set; }
    }

    /// <summary>
    /// 仓位信息DTO（用于下拉选择）
    /// </summary>
    public class LocationOptionDto
    {
        /// <summary>
        /// 仓位GUID
        /// </summary>
        public string LocationGuid { get; set; } = string.Empty;

        /// <summary>
        /// 仓位代码
        /// </summary>
        public string? LocationCode { get; set; }

        /// <summary>
        /// 仓位条码
        /// </summary>
        public string? LocationBarcode { get; set; }

        /// <summary>
        /// 仓位类型
        /// </summary>
        public int? LocationType { get; set; }

        /// <summary>
        /// 状态（0-禁用，1-启用）
        /// </summary>
        public int? Status { get; set; }

        /// <summary>
        /// 显示文本（仓位代码 + 条码）
        /// </summary>
        public string DisplayText => !string.IsNullOrEmpty(LocationCode) 
            ? $"{LocationCode} {(string.IsNullOrEmpty(LocationBarcode) ? "" : $"({LocationBarcode})")}"
            : LocationBarcode ?? "未命名仓位";
    }
}

