using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// Grid数据请求DTO（用于react-data-grid服务端分页）
    /// </summary>
    public class GridRequestDto
    {
        /// <summary>
        /// 起始行索引
        /// </summary>
        [Required]
        public int StartRow { get; set; }

        /// <summary>
        /// 结束行索引
        /// </summary>
        [Required]
        public int EndRow { get; set; }

        /// <summary>
        /// 每页大小
        /// </summary>
        [Range(1, 5000)]
        public int PageSize { get; set; } = 50;

        /// <summary>
        /// 全局搜索关键词（OR逻辑，搜索多个字段）
        /// </summary>
        public string? GlobalSearch { get; set; }

        /// <summary>
        /// 列筛选模型
        /// </summary>
        public Dictionary<string, FilterModelDto>? FilterModel { get; set; }

        /// <summary>
        /// 排序模型
        /// </summary>
        public List<SortModelDto>? SortModel { get; set; }
    }

    /// <summary>
    /// 筛选模型DTO
    /// </summary>
    public class FilterModelDto
    {
        /// <summary>
        /// 筛选类型 (text/number/date/set)
        /// </summary>
        public string? FilterType { get; set; }

        /// <summary>
        /// 筛选操作类型 (contains/equals/startswith/endswith等)
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// 筛选值
        /// </summary>
        public string? Filter { get; set; }

        /// <summary>
        /// 范围筛选的结束值（用于inRange类型）
        /// </summary>
        public string? FilterTo { get; set; }

        /// <summary>
        /// Set筛选的值列表
        /// </summary>
        public List<string>? Values { get; set; }
    }

    /// <summary>
    /// 排序模型DTO
    /// </summary>
    public class SortModelDto
    {
        /// <summary>
        /// 排序字段名
        /// </summary>
        [Required]
        public string ColId { get; set; } = string.Empty;

        /// <summary>
        /// 排序方向 (asc/desc)
        /// </summary>
        [Required]
        public string Sort { get; set; } = "asc";
    }

    /// <summary>
    /// Grid数据响应DTO
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    public class GridResponseDto<T>
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 数据列表
        /// </summary>
        public List<T>? Items { get; set; }

        /// <summary>
        /// 总记录数
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// 创建成功响应
        /// </summary>
        public static GridResponseDto<T> OK(List<T> items, int total, string? message = null)
        {
            return new GridResponseDto<T>
            {
                Success = true,
                Items = items,
                Total = total,
                Message = message ?? "获取数据成功"
            };
        }

        /// <summary>
        /// 创建错误响应
        /// </summary>
        public static GridResponseDto<T> Error(string message)
        {
            return new GridResponseDto<T>
            {
                Success = false,
                Items = new List<T>(),
                Total = 0,
                Message = message
            };
        }
    }
}
