using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    #region 检测相关DTO

    /// <summary>
    /// 商品检测结果DTO
    /// 用于返回商品是否存在以及仓库商品信息
    /// </summary>
    public class ProductDetectionResultDto
    {
        /// <summary>
        /// 商品编码
        /// </summary>
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 货号
        /// </summary>
        public string ItemNumber { get; set; } = string.Empty;

        /// <summary>
        /// 条码
        /// </summary>
        public string? Barcode { get; set; }

        /// <summary>
        /// 是否已存在（true=已存在，false=新商品）
        /// </summary>
        public bool Exists { get; set; }

        /// <summary>
        /// 检测结果描述（"新商品" / "已存在"）
        /// </summary>
        public string DetectionResult { get; set; } = string.Empty;

        /// <summary>
        /// 仓库商品的贴牌价格（如果存在）
        /// </summary>
        public decimal? WarehouseOEMPrice { get; set; }

        /// <summary>
        /// 仓库商品的进口价格（如果存在）
        /// </summary>
        public decimal? WarehouseImportPrice { get; set; }

        /// <summary>
        /// 仓库商品的国内价格（如果存在）
        /// </summary>
        public decimal? WarehouseDomesticPrice { get; set; }

        /// <summary>
        /// 仓库商品的单件体积（如果存在）
        /// </summary>
        public decimal? WarehouseVolume { get; set; }

        /// <summary>
        /// 仓库商品的上架状态（如果存在）
        /// </summary>
        public bool? WarehouseIsActive { get; set; }

        /// <summary>
        /// 仓库商品的英文名称（如果存在）
        /// 用于检测时自动填充英文名称到货柜明细
        /// </summary>
        public string? WarehouseEnglishName { get; set; }
    }

    /// <summary>
    /// 批量商品检测请求DTO
    /// </summary>
    public class BatchProductDetectionRequest
    {
        /// <summary>
        /// 要检测的商品列表
        /// </summary>
        [Required(ErrorMessage = "商品列表不能为空")]
        public List<ProductDetectionItem> Items { get; set; } = new();
    }

    /// <summary>
    /// 商品检测项
    /// </summary>
    public class ProductDetectionItem
    {
        /// <summary>
        /// 商品编码
        /// </summary>
        [Required(ErrorMessage = "商品编码不能为空")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 货号
        /// </summary>
        [Required(ErrorMessage = "货号不能为空")]
        public string ItemNumber { get; set; } = string.Empty;

        /// <summary>
        /// 条码（可选）
        /// </summary>
        public string? Barcode { get; set; }
    }

    #endregion

    #region 创建相关DTO

    /// <summary>
    /// 批量创建商品请求DTO
    /// </summary>
    public class BatchProductCreateRequest
    {
        /// <summary>
        /// 要创建的商品列表
        /// </summary>
        [Required(ErrorMessage = "商品列表不能为空")]
        public List<ProductCreateItem> Items { get; set; } = new();
    }

    /// <summary>
    /// 商品创建项
    /// </summary>
    public class ProductCreateItem
    {
        /// <summary>
        /// 商品编码
        /// </summary>
        [Required(ErrorMessage = "商品编码不能为空")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 货号
        /// </summary>
        [Required(ErrorMessage = "货号不能为空")]
        public string ItemNumber { get; set; } = string.Empty;

        /// <summary>
        /// 条码
        /// </summary>
        public string? Barcode { get; set; }

        /// <summary>
        /// 中文名称
        /// </summary>
        public string? ChineseName { get; set; }

        /// <summary>
        /// 英文名称
        /// </summary>
        public string? EnglishName { get; set; }

        /// <summary>
        /// 国内价格
        /// </summary>
        public decimal? DomesticPrice { get; set; }

        /// <summary>
        /// 进口价格
        /// </summary>
        public decimal? ImportPrice { get; set; }

        /// <summary>
        /// 贴牌价格（必填）
        /// </summary>
        [Required(ErrorMessage = "贴牌价格不能为空")]
        [Range(0.01, double.MaxValue, ErrorMessage = "贴牌价格必须大于0")]
        public decimal OEMPrice { get; set; }

        /// <summary>
        /// 单件体积
        /// </summary>
        public decimal? Volume { get; set; }

        /// <summary>
        /// 单件装箱数
        /// </summary>
        public int? PackingQuantity { get; set; }

        /// <summary>
        /// 商品图片URL
        /// </summary>
        public string? ImageUrl { get; set; }
    }

    #endregion

    #region 更新相关DTO

    /// <summary>
    /// 批量更新商品请求DTO
    /// </summary>
    public class BatchProductUpdateRequest
    {
        /// <summary>
        /// 要更新的商品列表
        /// </summary>
        [Required(ErrorMessage = "商品列表不能为空")]
        public List<ProductUpdateItem> Items { get; set; } = new();
    }

    /// <summary>
    /// 商品更新项
    /// </summary>
    public class ProductUpdateItem
    {
        /// <summary>
        /// 商品编码（必填）
        /// </summary>
        [Required(ErrorMessage = "商品编码不能为空")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 货号（可选，用于商品编码匹配不到时的备选匹配）
        /// </summary>
        public string? ItemNumber { get; set; }

        /// <summary>
        /// 国内价格
        /// </summary>
        public decimal? DomesticPrice { get; set; }

        /// <summary>
        /// 进口价格
        /// </summary>
        public decimal? ImportPrice { get; set; }

        /// <summary>
        /// 贴牌价格
        /// </summary>
        public decimal? OEMPrice { get; set; }

        /// <summary>
        /// 单件体积
        /// </summary>
        public decimal? Volume { get; set; }

        /// <summary>
        /// 单件装箱数
        /// </summary>
        public int? PackingQuantity { get; set; }

        /// <summary>
        /// 上架状态
        /// </summary>
        public bool IsActive { get; set; } = true;
    }

    #endregion

    #region 响应DTO

    /// <summary>
    /// 批量商品操作响应DTO
    /// </summary>
    public class BatchProductOperationResponse
    {
        /// <summary>
        /// 操作是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 响应消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 成功处理的数量
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// 失败的数量
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// 跳过的数量（用于二次检查时跳过已存在的商品）
        /// </summary>
        public int SkippedCount { get; set; }

        /// <summary>
        /// 错误信息列表
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// 跳过的商品列表
        /// </summary>
        public List<string> SkippedItems { get; set; } = new();

        /// <summary>
        /// 返回数据（用于检测结果）
        /// </summary>
        public object? Data { get; set; }
    }

    #endregion
}

