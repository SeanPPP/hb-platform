using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 位置数据传输对象，用于在应用各层之间传输位置数据
    /// </summary>
    public class LocationDto
    {

        /// <summary>
        /// 位置GUID
        /// </summary>
        [StringLength(36, ErrorMessage = "Location GUID不能超过36个字符")]
        public string? LocationGuid { get; set; }

        /// <summary>
        /// 位置类型
        /// </summary>
        [StringLength(50, ErrorMessage = "位置类型不能超过50个字符")]
        public string? LocationType { get; set; }

        /// <summary>
        /// 位置代码
        /// </summary>
        [StringLength(50, ErrorMessage = "位置代码不能超过50个字符")]
        public string? LocationCode { get; set; }

        /// <summary>
        /// 位置条码
        /// </summary>
        [StringLength(50, ErrorMessage = "位置条码不能超过50个字符")]
        public string? LocationBarcode { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        public int? Status { get; set; }

        /// <summary>
        /// 创建者
        /// </summary>
        [StringLength(50, ErrorMessage = "创建者不能超过50个字符")]
        public string? FGC_Creator { get; set; }

        /// <summary>
        /// 创建日期
        /// </summary>
        [StringLength(50, ErrorMessage = "创建日期不能超过50个字符")]
        public string? FGC_CreateDate { get; set; }

        /// <summary>
        /// 最后修改者
        /// </summary>
        [StringLength(50, ErrorMessage = "最后修改者不能超过50个字符")]
        public string? FGC_LastModifier { get; set; }

        /// <summary>
        /// 最后修改日期
        /// </summary>
        [StringLength(50, ErrorMessage = "最后修改日期不能超过50个字符")]
        public string? FGC_LastModifyDate { get; set; }

        /// <summary>
        /// 更新帮助
        /// </summary>
        [StringLength(50, ErrorMessage = "更新帮助不能超过50个字符")]
        public string? FGC_UpdateHelp { get; set; }

        /// <summary>
        /// 行版本
        /// </summary>
        [StringLength(50, ErrorMessage = "行版本不能超过50个字符")]
        public string? FGC_Rowversion { get; set; }

        /// <summary>
        /// 产品列表
        /// </summary>
        public List<ProductDto> Products { get; set; } = new List<ProductDto>();
    }

    /// <summary>
    /// 创建位置数据传输对象，用于创建新的位置
    /// </summary>
    public class CreateLocationDto
    {
        /// <summary>
        /// 位置GUID
        /// </summary>
        [StringLength(36, ErrorMessage = "Location GUID不能超过36个字符")]
        public string? LocationGuid { get; set; }

        /// <summary>
        /// 位置类型
        /// </summary>
        [StringLength(50, ErrorMessage = "位置类型不能超过50个字符")]
        public string? LocationType { get; set; }

        /// <summary>
        /// 位置代码
        /// </summary>
        [Required(ErrorMessage = "位置代码不能为空")]
        [StringLength(50, ErrorMessage = "位置代码不能超过50个字符")]
        public string LocationCode { get; set; } = string.Empty;

        /// <summary>
        /// 位置条码
        /// </summary>
        [StringLength(50, ErrorMessage = "位置条码不能超过50个字符")]
        public string? LocationBarcode { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        public int? Status { get; set; } = 1;

        /// <summary>
        /// 创建者
        /// </summary>
        [StringLength(50, ErrorMessage = "创建者不能超过50个字符")]
        public string? FGC_Creator { get; set; }

        /// <summary>
        /// 创建日期
        /// </summary>
        [StringLength(50, ErrorMessage = "创建日期不能超过50个字符")]
        public string? FGC_CreateDate { get; set; }
    }

    /// <summary>
    /// 更新位置数据传输对象，用于更新现有位置
    /// </summary>
    public class UpdateLocationDto
    {
        

        /// <summary>
        /// 位置GUID
        /// </summary>
        [StringLength(36, ErrorMessage = "Location GUID不能超过36个字符")]
        public string? LocationGuid { get; set; }

        /// <summary>
        /// 位置类型
        /// </summary>
        [StringLength(50, ErrorMessage = "位置类型不能超过50个字符")]
        public string? LocationType { get; set; }

        /// <summary>
        /// 位置代码
        /// </summary>
        [Required(ErrorMessage = "位置代码不能为空")]
        [StringLength(50, ErrorMessage = "位置代码不能超过50个字符")]
        public string LocationCode { get; set; } = string.Empty;

        /// <summary>
        /// 位置条码
        /// </summary>
        [StringLength(50, ErrorMessage = "位置条码不能超过50个字符")]
        public string? LocationBarcode { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        public int? Status { get; set; }

        /// <summary>
        /// 最后修改者
        /// </summary>
        [StringLength(50, ErrorMessage = "最后修改者不能超过50个字符")]
        public string? FGC_LastModifier { get; set; }

        /// <summary>
        /// 最后修改日期
        /// </summary>
        [StringLength(50, ErrorMessage = "最后修改日期不能超过50个字符")]
        public string? FGC_LastModifyDate { get; set; }

        /// <summary>
        /// 更新帮助
        /// </summary>
        [StringLength(50, ErrorMessage = "更新帮助不能超过50个字符")]
        public string? FGC_UpdateHelp { get; set; }

        /// <summary>
        /// 行版本
        /// </summary>
        [StringLength(50, ErrorMessage = "行版本不能超过50个字符")]
        public string? FGC_Rowversion { get; set; }
    }

    /// <summary>
    /// 位置过滤数据传输对象，用于查询时的过滤条件
    /// </summary>
    public class LocationFilterDto
    {
        /// <summary>
        /// 位置代码过滤条件
        /// </summary>
        public string? LocationCode { get; set; }
        
        /// <summary>
        /// 位置类型过滤条件
        /// </summary>
        public int? LocationType { get; set; }
        
        /// <summary>
        /// 位置条码过滤条件
        /// </summary>
        public string? LocationBarcode { get; set; }
        
        /// <summary>
        /// 状态过滤条件
        /// </summary>
        public int? Status { get; set; }
        
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
        public string? SortBy { get; set; } = "LocationCode";
        
        /// <summary>
        /// 是否降序排列
        /// </summary>
        public bool SortDescending { get; set; } = false;
    }

    /// <summary>
    /// 带产品数量的位置数据传输对象
    /// </summary>
    public class LocationWithProductCountDto
    {
        /// <summary>
        /// 位置全局唯一标识符
        /// </summary>
        public string LocationGUID { get; set; } = string.Empty;

        /// <summary>
        /// 位置代码
        /// </summary>
        [Required(ErrorMessage = "位置代码不能为空")]
        [StringLength(50, ErrorMessage = "位置代码不能超过50个字符")]
        public string LocationCode { get; set; } = string.Empty;

        /// <summary>
        /// 位置名称
        /// </summary>
        [Required(ErrorMessage = "位置名称不能为空")]
        [StringLength(100, ErrorMessage = "位置名称不能超过100个字符")]
        public string LocationName { get; set; } = string.Empty;

        /// <summary>
        /// 描述
        /// </summary>
        [StringLength(500, ErrorMessage = "描述不能超过500个字符")]
        public string? Description { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// 产品数量
        /// </summary>
        public int ProductCount { get; set; }
    }
}