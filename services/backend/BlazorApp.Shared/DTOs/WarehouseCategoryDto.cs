using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 仓库类别数据传输对象，用于在应用各层之间传输仓库类别数据
    /// </summary>
    public class WarehouseCategoryDto
    {
        /// <summary>
        /// 类别唯一标识符
        /// </summary>
        public string CategoryGUID { get; set; } = string.Empty;
        
        /// <summary>
        /// 父级类别唯一标识符
        /// </summary>
        public string? ParentGUID { get; set; }
        
        /// <summary>
        /// 类别名称
        /// </summary>
        [Required(ErrorMessage = "类别名称不能为空")]
        [StringLength(100, ErrorMessage = "类别名称不能超过100个字符")]
        public string CategoryName { get; set; } = string.Empty;

        /// <summary>
        /// 中文名称
        /// </summary>
        [StringLength(100, ErrorMessage = "中文名称不能超过100个字符")]
        public string? ChineseName { get; set; }

        /// <summary>
        /// 是否启用状态
        /// </summary>
        public bool IsActive { get; set; } = true;
        
        /// <summary>
        /// 排序顺序
        /// </summary>
        public int? SortOrder { get; set; }

        /// <summary>
        /// 备注信息
        /// </summary>
        [StringLength(500, ErrorMessage = "备注不能超过500个字符")]
        public string? Remarks { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// 子类别列表
        /// </summary>
        public List<WarehouseCategoryDto> Children { get; set; } = new List<WarehouseCategoryDto>();
        
        /// <summary>
        /// 父级类别信息
        /// </summary>
        [JsonIgnore]
        public WarehouseCategoryDto? Parent { get; set; }
    }

    /// <summary>
    /// 创建仓库类别数据传输对象，用于创建新的仓库类别
    /// </summary>
    public class CreateWarehouseCategoryDto
    {
        /// <summary>
        /// 父级类别唯一标识符
        /// </summary>
        public string? ParentGUID { get; set; }

        /// <summary>
        /// 类别名称
        /// </summary>
        [Required(ErrorMessage = "类别名称不能为空")]
        [StringLength(100, ErrorMessage = "类别名称不能超过100个字符")]
        public string CategoryName { get; set; } = string.Empty;

        /// <summary>
        /// 中文名称
        /// </summary>
        [StringLength(100, ErrorMessage = "中文名称不能超过100个字符")]
        public string? ChineseName { get; set; }

        /// <summary>
        /// 是否启用状态
        /// </summary>
        public bool IsActive { get; set; } = true;
        
        /// <summary>
        /// 排序顺序
        /// </summary>
        public int? SortOrder { get; set; }

        /// <summary>
        /// 备注信息
        /// </summary>
        [StringLength(500, ErrorMessage = "备注不能超过500个字符")]
        public string? Remarks { get; set; }
    }

    /// <summary>
    /// 更新仓库类别数据传输对象，用于更新现有的仓库类别
    /// </summary>
    public class UpdateWarehouseCategoryDto
    {
        /// <summary>
        /// 类别唯一标识符
        /// </summary>
        [Required(ErrorMessage = "CategoryGUID不能为空")]
        public string CategoryGUID { get; set; } = string.Empty;

        /// <summary>
        /// 父级类别唯一标识符
        /// </summary>
        public string? ParentGUID { get; set; }

        /// <summary>
        /// 类别名称
        /// </summary>
        [Required(ErrorMessage = "类别名称不能为空")]
        [StringLength(100, ErrorMessage = "类别名称不能超过100个字符")]
        public string CategoryName { get; set; } = string.Empty;

        /// <summary>
        /// 中文名称
        /// </summary>
        [StringLength(100, ErrorMessage = "中文名称不能超过100个字符")]
        public string? ChineseName { get; set; }

        /// <summary>
        /// 是否启用状态
        /// </summary>
        public bool IsActive { get; set; } = true;
        
        /// <summary>
        /// 排序顺序
        /// </summary>
        public int? SortOrder { get; set; }

        /// <summary>
        /// 备注信息
        /// </summary>
        [StringLength(500, ErrorMessage = "备注不能超过500个字符")]
        public string? Remarks { get; set; }
    }

    /// <summary>
    /// 仓库类别过滤数据传输对象，用于查询时的过滤条件
    /// </summary>
    public class WarehouseCategoryFilterDto
    {
        /// <summary>
        /// 类别名称过滤条件
        /// </summary>
        public string? CategoryName { get; set; }
        
        /// <summary>
        /// 中文名称过滤条件
        /// </summary>
        public string? ChineseName { get; set; }
        
        /// <summary>
        /// 是否启用状态过滤条件
        /// </summary>
        public bool? IsActive { get; set; }
        
        /// <summary>
        /// 父级类别GUID过滤条件
        /// </summary>
        public string? ParentGUID { get; set; }
        
        /// <summary>
        /// 页码（从1开始）
        /// </summary>
        public int PageNumber { get; set; } = 1;
        
        /// <summary>
        /// 每页大小
        /// </summary>
        public int PageSize { get; set; } = 10;
        
        /// <summary>
        /// 排序字段
        /// </summary>
        public string? SortBy { get; set; } = "CategoryName";
        
        /// <summary>
        /// 是否降序排列
        /// </summary>
        public bool SortDescending { get; set; } = false;
    }
}