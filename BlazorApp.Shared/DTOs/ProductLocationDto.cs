using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 产品位置数据传输对象，用于在应用各层之间传输产品位置关联数据
    /// </summary>
    public class ProductLocationDto
    {
        /// <summary>
        /// 产品位置关联GUID
        /// </summary>
        public string Guid { get; set; } = string.Empty;

        /// <summary>
        /// 产品代码
        /// </summary>
        [Required(ErrorMessage = "商品代码不能为空")]
        [StringLength(50, ErrorMessage = "商品代码不能超过50个字符")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 位置GUID
        /// </summary>
        [Required(ErrorMessage = "位置GUID不能为空")]
        [StringLength(36, ErrorMessage = "位置GUID不能超过36个字符")]
        public string LocationGuid { get; set; } = string.Empty;

        /// <summary>
        /// 仓库产品信息
        /// </summary>
        public WarehouseProductDto? WarehouseProduct { get; set; }
        
        /// <summary>
        /// 位置信息
        /// </summary>
        public LocationDto? Location { get; set; }
    }

    /// <summary>
    /// 创建产品位置数据传输对象，用于创建新的产品位置关联
    /// </summary>
    public class CreateProductLocationDto
    {
        /// <summary>
        /// 产品代码
        /// </summary>
        [Required(ErrorMessage = "商品代码不能为空")]
        [StringLength(50, ErrorMessage = "商品代码不能超过50个字符")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 位置GUID
        /// </summary>
        [Required(ErrorMessage = "位置GUID不能为空")]
        [StringLength(36, ErrorMessage = "位置GUID不能超过36个字符")]
        public string LocationGuid { get; set; } = string.Empty;
    }

    /// <summary>
    /// 更新产品位置数据传输对象，用于更新现有产品位置关联
    /// </summary>
    public class UpdateProductLocationDto
    {
        /// <summary>
        /// 产品位置关联GUID
        /// </summary>
        [Required(ErrorMessage = "GUID不能为空")]
        public string Guid { get; set; } = string.Empty;

        /// <summary>
        /// 产品代码
        /// </summary>
        [Required(ErrorMessage = "商品代码不能为空")]
        [StringLength(50, ErrorMessage = "商品代码不能超过50个字符")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 位置GUID
        /// </summary>
        [Required(ErrorMessage = "位置GUID不能为空")]
        [StringLength(36, ErrorMessage = "位置GUID不能超过36个字符")]
        public string LocationGuid { get; set; } = string.Empty;
    }

    /// <summary>
    /// 产品位置过滤数据传输对象，用于查询时的过滤条件
    /// </summary>
    public class ProductLocationFilterDto
    {
        /// <summary>
        /// 产品代码过滤条件
        /// </summary>
        public string? ProductCode { get; set; }
        
        /// <summary>
        /// 位置GUID过滤条件
        /// </summary>
        public string? LocationGuid { get; set; }
        
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
        public string? SortBy { get; set; } = "ProductCode";
        
        /// <summary>
        /// 是否降序排列
        /// </summary>
        public bool SortDescending { get; set; } = false;
    }

    /// <summary>
    /// 批量产品位置数据传输对象，用于批量关联产品和位置
    /// </summary>
    public class BatchProductLocationDto
    {
        /// <summary>
        /// 产品代码
        /// </summary>
        public string ProductCode { get; set; } = string.Empty;
        
        /// <summary>
        /// 位置GUID列表
        /// </summary>
        public List<string> LocationGuids { get; set; } = new List<string>();
    }

    /// <summary>
    /// 带位置信息的产品数据传输对象
    /// </summary>
    public class ProductWithLocationsDto
    {
        /// <summary>
        /// 产品代码
        /// </summary>
        public string ProductCode { get; set; } = string.Empty;
        
        /// <summary>
        /// 产品名称
        /// </summary>
        public string? ProductName { get; set; }
        
        /// <summary>
        /// 位置列表
        /// </summary>
        public List<LocationDto> Locations { get; set; } = new List<LocationDto>();
    }

    /// <summary>
    /// 带产品信息的位置数据传输对象
    /// </summary>
    public class LocationWithProductsDto
    {
        /// <summary>
        /// 位置GUID
        /// </summary>
        public string LocationGuid { get; set; } = string.Empty;
        
        /// <summary>
        /// 位置代码
        /// </summary>
        public string LocationCode { get; set; } =string.Empty;
        
        /// <summary>
        /// 产品列表
        /// </summary>
        public List<WarehouseProductDto> Products { get; set; } = new List<WarehouseProductDto>();
    }
}