using System.Collections.Generic;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 分页结果DTO（泛型）
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    public class PagedResultDto<T>
    {
        /// <summary>
        /// 数据列表
        /// </summary>
        public List<T> Data { get; set; } = new();

        /// <summary>
        /// 总记录数
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// 当前页码
        /// </summary>
        public int PageIndex { get; set; }

        /// <summary>
        /// 每页数量
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// 总页数
        /// </summary>
        public int TotalPages => PageSize > 0 ? (TotalCount + PageSize - 1) / PageSize : 0;

        /// <summary>
        /// 是否有上一页
        /// </summary>
        public bool HasPreviousPage => PageIndex > 1;

        /// <summary>
        /// 是否有下一页
        /// </summary>
        public bool HasNextPage => PageIndex < TotalPages;
    }
}

