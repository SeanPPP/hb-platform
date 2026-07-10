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
    /// React 货柜明细导出请求
    /// </summary>
    public class ReactContainerDetailsExportRequest
    {
        /// <summary>
        /// 导出格式：excel 或 pdf
        /// </summary>
        public string Format { get; set; } = "excel";

        /// <summary>
        /// 当前明细筛选条件
        /// </summary>
        public ContainerDetailQueryDto? Query { get; set; }

        /// <summary>
        /// 已勾选的明细 HGUID；为空时按 Query 导出当前筛选结果
        /// </summary>
        public List<string>? SelectedHguids { get; set; } = new();

        /// <summary>
        /// 导出列；为空时使用默认固定列
        /// </summary>
        public List<string>? Columns { get; set; } = new();

        /// <summary>
        /// 兼容 web 端旧字段名
        /// </summary>
        public List<string>? ExportColumns { get; set; }
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
