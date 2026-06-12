using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 货柜明细导出请求
    /// </summary>
    public class ContainerDetailsExportRequest
    {
        /// <summary>
        /// 货柜代码
        /// </summary>
        [Required]
        public string ContainerCode { get; set; } = string.Empty;

        /// <summary>
        /// 货柜编号
        /// </summary>
        public string ContainerNumber { get; set; } = string.Empty;

        /// <summary>
        /// 导出格式 (excel/pdf)
        /// </summary>
        [Required]
        public string ExportFormat { get; set; } = "excel";

        /// <summary>
        /// 导出列列表
        /// </summary>
        public List<string> ExportColumns { get; set; } = new();

        /// <summary>
        /// 要导出的明细代码列表
        /// </summary>
        public List<string> Details { get; set; } = new();
    }

    /// <summary>
    /// 文件导出响应
    /// </summary>
    public class FileExportResponse
    {
        /// <summary>
        /// 文件内容 (Base64编码)
        /// </summary>
        public string FileContent { get; set; } = string.Empty;

        /// <summary>
        /// 内容类型
        /// </summary>
        public string ContentType { get; set; } = string.Empty;

        /// <summary>
        /// 文件名
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// 文件大小
        /// </summary>
        public long FileSize { get; set; }
    }
}
