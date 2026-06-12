using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 分页查询基类
    /// </summary>
    public class PagedQuery
    {
        /// <summary>
        /// 页码，从1开始
        /// </summary>
        [Range(1, int.MaxValue, ErrorMessage = "页码必须大于0")]
        public int Page { get; set; } = 1;

        /// <summary>
        /// 每页大小
        /// </summary>
        [Range(1, 1000, ErrorMessage = "每页大小必须在1-1000之间")]
        public int PageSize { get; set; } = 20;

        /// <summary>
        /// 跳过的记录数
        /// </summary>
        public int Skip => (Page - 1) * PageSize;

        /// <summary>
        /// 获取记录数
        /// </summary>
        public int Take => PageSize;
    }
}
