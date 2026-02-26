using System;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 简化的分店信息 DTO，用于前端订货页面显示已使用过的分店列表
    /// </summary>
    public class BranchDto
    {
        /// <summary>
        /// 分店 GUID（字符串形式）
        /// </summary>
        public string Guid { get; set; } = string.Empty;

        /// <summary>
        /// 分店代码
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// 分店名称
        /// </summary>
        public string Name { get; set; } = string.Empty;
    }
}

