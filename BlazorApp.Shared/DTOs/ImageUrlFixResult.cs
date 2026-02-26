namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 图片URL修复结果
    /// </summary>
    public class ImageUrlFixResult
    {
        /// <summary>
        /// 总共扫描的商品数量
        /// </summary>
        public int TotalScanned { get; set; }

        /// <summary>
        /// 发现问题的商品数量
        /// </summary>
        public int ProblemsFound { get; set; }

        /// <summary>
        /// 成功修复的商品数量
        /// </summary>
        public int SuccessfullyFixed { get; set; }

        /// <summary>
        /// 修复失败的商品数量
        /// </summary>
        public int FailedToFix { get; set; }

        /// <summary>
        /// 是否为模拟运行
        /// </summary>
        public bool IsDryRun { get; set; }

        /// <summary>
        /// 详细信息列表
        /// </summary>
        public List<ImageUrlFixDetail> Details { get; set; } = new();
    }

    /// <summary>
    /// 图片URL修复详情
    /// </summary>
    public class ImageUrlFixDetail
    {
        /// <summary>
        /// 商品编码
        /// </summary>
        public string? ProductCode { get; set; }

        /// <summary>
        /// HB货号
        /// </summary>
        public string? HBProductNo { get; set; }

        /// <summary>
        /// 商品名称
        /// </summary>
        public string? ProductName { get; set; }

        /// <summary>
        /// 原始图片URL
        /// </summary>
        public string? OriginalImageUrl { get; set; }

        /// <summary>
        /// 修复后的图片URL
        /// </summary>
        public string? FixedImageUrl { get; set; }

        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}

