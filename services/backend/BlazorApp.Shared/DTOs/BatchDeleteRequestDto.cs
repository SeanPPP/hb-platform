using System.Collections.Generic;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 批量删除请求 DTO
    /// </summary>
    public class BatchDeleteRequestDto
    {
        /// <summary>
        /// 要删除的商品编码列表
        /// </summary>
        public List<string> ProductCodes { get; set; } = new List<string>();
    }
}

