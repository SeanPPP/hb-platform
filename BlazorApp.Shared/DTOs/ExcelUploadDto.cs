using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// Excel上传请求DTO
    /// </summary>
    public class ExcelUploadRequestDto
    {
        /// <summary>
        /// 用户GUID
        /// </summary>
        [Required(ErrorMessage = "用户GUID不能为空")]
        public string UserGuid { get; set; } = string.Empty;

        /// <summary>
        /// 购物车GUID（可选，如果不提供则使用用户默认购物车）
        /// </summary>
        public string? CartGuid { get; set; }

        /// <summary>
        /// Excel文件数据（Base64编码）
        /// </summary>
        [Required(ErrorMessage = "Excel文件数据不能为空")]
        public string ExcelFileData { get; set; } = string.Empty;

        /// <summary>
        /// 文件名
        /// </summary>
        [Required(ErrorMessage = "文件名不能为空")]
        public string FileName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Excel解析结果项
    /// </summary>
    public class ExcelParseItemDto
    {
        /// <summary>
        /// 行号
        /// </summary>
        public int RowNumber { get; set; }

        /// <summary>
        /// 货号
        /// </summary>
        public string ItemNumber { get; set; } = string.Empty;

        /// <summary>
        /// 数量
        /// </summary>
        public int Quantity { get; set; }

        /// <summary>
        /// 价格（可选）
        /// </summary>
        public decimal? Price { get; set; }

        /// <summary>
        /// 是否解析成功
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Excel上传响应DTO
    /// </summary>
    public class ExcelUploadResponseDto
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 总行数
        /// </summary>
        public int TotalRows { get; set; }

        /// <summary>
        /// 成功解析的行数
        /// </summary>
        public int SuccessRows { get; set; }

        /// <summary>
        /// 失败的行数
        /// </summary>
        public int FailedRows { get; set; }

        /// <summary>
        /// 成功添加到购物车的商品数量
        /// </summary>
        public int AddedToCartCount { get; set; }

        /// <summary>
        /// 解析结果详情
        /// </summary>
        public List<ExcelParseItemDto> ParseResults { get; set; } = new();

        /// <summary>
        /// 未找到的货号列表
        /// </summary>
        public List<string> NotFoundItemNumbers { get; set; } = new();

        /// <summary>
        /// 购物车GUID
        /// </summary>
        public string? CartGuid { get; set; }
    }

    /// <summary>
    /// 批量查询商品请求DTO
    /// </summary>
    public class BatchQueryProductsRequestDto
    {
        /// <summary>
        /// 货号列表
        /// </summary>
        [Required(ErrorMessage = "货号列表不能为空")]
        public List<string> ItemNumbers { get; set; } = new();

        /// <summary>
        /// 用户GUID
        /// </summary>
        [Required(ErrorMessage = "用户GUID不能为空")]
        public string UserGuid { get; set; } = string.Empty;
    }

    /// <summary>
    /// 批量查询商品响应DTO
    /// </summary>
    public class BatchQueryProductsResponseDto
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 找到的商品列表
        /// </summary>
        public List<WarehouseProductListDto> Products { get; set; } = new();

        /// <summary>
        /// 未找到的货号列表
        /// </summary>
        public List<string> NotFoundItemNumbers { get; set; } = new();
    }
}