using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 产品套装多码数据传输对象
    /// </summary>
    public class ProductSetCodeDto
    {
        /// <summary>
        /// 套装编码主键
        /// </summary>
        public string SetCodeId { get; set; } = string.Empty;

        /// <summary>
        /// 产品编码
        /// </summary>
        [Required(ErrorMessage = "产品编码不能为空")]
        [Display(Name = "产品编码")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 套装货号
        /// </summary>
        [Required(ErrorMessage = "套装货号不能为空")]
        [Display(Name = "套装货号")]
        public string SetItemNumber { get; set; } = string.Empty;

        /// <summary>
        /// 套装条码
        /// </summary>
        [Display(Name = "套装条码")]
        public string? SetBarcode { get; set; }

        /// <summary>
        /// 套装名称
        /// </summary>
        [Display(Name = "套装名称")]
        public string? SetName { get; set; }


        /// <summary>
        /// 套装数量
        /// </summary>
        [Required(ErrorMessage = "套装数量不能为空")]
        [Range(1, int.MaxValue, ErrorMessage = "套装数量必须大于0")]
        [Display(Name = "套装数量")]
        public int SetQuantity { get; set; } = 1;

        /// <summary>
        /// 套装采购价格
        /// </summary>
        [Display(Name = "套装采购价格")]
        public decimal? SetPurchasePrice { get; set; }

        /// <summary>
        /// 套装零售价格
        /// </summary>
        [Display(Name = "套装零售价格")]
        public decimal? SetRetailPrice { get; set; }


        /// <summary>
        /// 套装类型（1:组合套装, 2:固定套装, 3:变量套装）
        /// </summary>
        [Required(ErrorMessage = "套装类型不能为空")]
        [Range(1, 3, ErrorMessage = "套装类型必须在1-3之间")]
        [Display(Name = "套装类型")]
        public int SetType { get; set; } = 1;

        /// <summary>
        /// 是否启用套装
        /// </summary>
        [Display(Name = "是否启用")]
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
        /// 创建者
        /// </summary>
        public string? CreatedBy { get; set; }

        /// <summary>
        /// 更新者
        /// </summary>
        public string? UpdatedBy { get; set; }

        /// <summary>
        /// 是否已删除
        /// </summary>
        public bool IsDeleted { get; set; } = false;

        // 关联数据
        /// <summary>
        /// 产品名称（来自关联Product）
        /// </summary>
        [Display(Name = "产品名称")]
        public string? ProductName { get; set; }


        // 计算属性
        /// <summary>
        /// 套装类型描述
        /// </summary>
        public string SetTypeDescription
        {
            get
            {
                return SetType switch
                {
                    1 => "组合套装",
                    2 => "固定套装",
                    3 => "变量套装",
                    _ => "未知类型"
                };
            }
        }

        /// <summary>
        /// 状态描述
        /// </summary>
        public string StatusDescription
        {
            get
            {
                if (IsDeleted)
                    return "已删除";
                return IsActive ? "启用" : "禁用";
            }
        }

        /// <summary>
        /// 单品平均价格
        /// </summary>
        public decimal? AverageUnitPrice
        {
            get
            {
                if (SetRetailPrice.HasValue && SetQuantity > 0)
                    return SetRetailPrice.Value / SetQuantity;
                return null;
            }
        }

        /// <summary>
        /// 格式化的套装价格显示
        /// </summary>
        public string FormattedSetPrice
        {
            get
            {
                if (SetRetailPrice.HasValue)
                    return $"¥{SetRetailPrice.Value:F2}";
                return "未设置";
            }
        }

        /// <summary>
        /// 格式化的单品平均价格显示
        /// </summary>
        public string FormattedAverageUnitPrice
        {
            get
            {
                if (AverageUnitPrice.HasValue)
                    return $"¥{AverageUnitPrice.Value:F2}";
                return "未计算";
            }
        }
    }

    /// <summary>
    /// 产品套装多码创建请求DTO
    /// </summary>
    public class CreateProductSetCodeDto
    {
        /// <summary>
        /// 产品编码
        /// </summary>
        [Required(ErrorMessage = "产品编码不能为空")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 套装货号
        /// </summary>
        [Required(ErrorMessage = "套装货号不能为空")]
        public string SetItemNumber { get; set; } = string.Empty;

        /// <summary>
        /// 套装条码
        /// </summary>
        public string? SetBarcode { get; set; }

        /// <summary>
        /// 套装名称
        /// </summary>
        public string? SetName { get; set; }


        /// <summary>
        /// 套装数量
        /// </summary>
        [Required(ErrorMessage = "套装数量不能为空")]
        [Range(1, int.MaxValue, ErrorMessage = "套装数量必须大于0")]
        public int SetQuantity { get; set; } = 1;

        /// <summary>
        /// 套装采购价格
        /// </summary>
        public decimal? SetPurchasePrice { get; set; }

        /// <summary>
        /// 套装零售价格
        /// </summary>
        public decimal? SetRetailPrice { get; set; }


        /// <summary>
        /// 套装类型（1:组合套装, 2:固定套装, 3:变量套装）
        /// </summary>
        [Required(ErrorMessage = "套装类型不能为空")]
        [Range(1, 3, ErrorMessage = "套装类型必须在1-3之间")]
        public int SetType { get; set; } = 1;

        /// <summary>
        /// 是否启用套装
        /// </summary>
        public bool IsActive { get; set; } = true;


    }

    /// <summary>
    /// 产品套装多码更新请求DTO
    /// </summary>
    public class UpdateProductSetCodeDto : CreateProductSetCodeDto
    {
        /// <summary>
        /// 套装编码主键
        /// </summary>
        [Required(ErrorMessage = "套装编码不能为空")]
        public string SetCodeId { get; set; } = string.Empty;
    }

    /// <summary>
    /// 产品套装多码查询请求DTO
    /// </summary>
    public class ProductSetCodeQueryDto
    {
        /// <summary>
        /// 产品编码
        /// </summary>
        public string? ProductCode { get; set; }

        /// <summary>
        /// 套装货号
        /// </summary>
        public string? SetItemNumber { get; set; }

        /// <summary>
        /// 套装条码
        /// </summary>
        public string? SetBarcode { get; set; }

        /// <summary>
        /// 套装名称
        /// </summary>
        public string? SetName { get; set; }

        /// <summary>
        /// 套装类型
        /// </summary>
        public int? SetType { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool? IsActive { get; set; }


        /// <summary>
        /// 价格范围 - 最小值
        /// </summary>
        public decimal? MinPrice { get; set; }

        /// <summary>
        /// 价格范围 - 最大值
        /// </summary>
        public decimal? MaxPrice { get; set; }

        /// <summary>
        /// 数量范围 - 最小值
        /// </summary>
        public int? MinQuantity { get; set; }

        /// <summary>
        /// 数量范围 - 最大值
        /// </summary>
        public int? MaxQuantity { get; set; }

        /// <summary>
        /// 创建时间范围 - 开始时间
        /// </summary>
        public DateTime? CreatedStartDate { get; set; }

        /// <summary>
        /// 创建时间范围 - 结束时间
        /// </summary>
        public DateTime? CreatedEndDate { get; set; }

        /// <summary>
        /// 页码
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// 每页记录数
        /// </summary>
        public int PageSize { get; set; } = 20;

        /// <summary>
        /// 排序字段
        /// </summary>
        public string? SortField { get; set; } = "CreatedAt";

        /// <summary>
        /// 排序方向（asc/desc）
        /// </summary>
        public string? SortDirection { get; set; } = "desc";

        /// <summary>
        /// 是否包含已删除记录
        /// </summary>
        public bool IncludeDeleted { get; set; } = false;
    }
}
