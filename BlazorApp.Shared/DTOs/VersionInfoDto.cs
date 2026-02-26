using System.ComponentModel.DataAnnotations;
using BlazorApp.Service.Models.HBPOSM_POSM;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 版本信息数据传输对象
    /// </summary>
    public class VersionInfoDto
    {
        /// <summary>
        /// 版本号
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// 软件平台信息 WIN_POS/PDA_WH/PDA_Store/ios
        /// </summary>
        public string SoftName { get; set; } = string.Empty;

        /// <summary>
        /// 发布日期
        /// </summary>
        public DateTime? ReleaseDate { get; set; }

        /// <summary>
        /// 版本描述
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 文件名
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// 文件大小，单位为字节
        /// </summary>
        public long? FileSize { get; set; }

        /// <summary>
        /// 下载来源相对路径
        /// </summary>
        public string DownloadFromPath { get; set; } = string.Empty;

        /// <summary>
        /// 下载目标相对路径
        /// </summary>
        public string? DownloadToPath { get; set; }

        /// <summary>
        /// 解压相对路径
        /// </summary>
        public string? UnzipPath { get; set; }

        /// <summary>
        /// 文件的MD5值，用于完整性校验
        /// </summary>
        public string FileMD5 { get; set; } = string.Empty;

        /// <summary>
        /// 创建者信息
        /// </summary>
        public string CreatedBy { get; set; } = string.Empty;

        /// <summary>
        /// 创建日期
        /// </summary>
        public DateTime? CreatedDate { get; set; }

        /// <summary>
        /// 修改者信息
        /// </summary>
        public string ModifiedBy { get; set; } = string.Empty;

        /// <summary>
        /// 修改日期
        /// </summary>
        public DateTime? ModifiedDate { get; set; }
    }

    /// <summary>
    /// 创建版本信息DTO
    /// </summary>
    public class CreateVersionInfoDto
    {
        /// <summary>
        /// 版本号（必填）
        /// </summary>
        [Required(ErrorMessage = "版本号不能为空")]
        [StringLength(50, ErrorMessage = "版本号长度不能超过50个字符")]
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// 软件平台信息（必填）WIN_POS/PDA_WH/PDA_Store/ios
        /// </summary>
        [Required(ErrorMessage = "软件平台不能为空")]
        [RegularExpression("WIN_POS|PDA_WH|PDA_Store|ios", ErrorMessage = "软件平台必须是WIN_POS、PDA_WH、PDA_Store或ios")]
        public string SoftName { get; set; } = string.Empty;

        /// <summary>
        /// 发布日期
        /// </summary>
        public DateTime? ReleaseDate { get; set; }

        /// <summary>
        /// 版本描述
        /// </summary>
        [StringLength(500, ErrorMessage = "版本描述长度不能超过500个字符")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 文件名
        /// </summary>
        [StringLength(255, ErrorMessage = "文件名长度不能超过255个字符")]
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// 文件大小，单位为字节
        /// </summary>
        [Range(0, long.MaxValue, ErrorMessage = "文件大小必须为正数")]
        public long? FileSize { get; set; }

        /// <summary>
        /// 下载来源相对路径
        /// </summary>
        [StringLength(500, ErrorMessage = "下载路径长度不能超过500个字符")]
        public string DownloadFromPath { get; set; } = string.Empty;

        /// <summary>
        /// 下载目标相对路径
        /// </summary>
        [StringLength(500, ErrorMessage = "下载目标路径长度不能超过500个字符")]
        public string? DownloadToPath { get; set; }

        /// <summary>
        /// 解压相对路径
        /// </summary>
        [StringLength(500, ErrorMessage = "解压路径长度不能超过500个字符")]
        public string? UnzipPath { get; set; }

        /// <summary>
        /// 文件的MD5值，用于完整性校验
        /// </summary>
        [StringLength(32, ErrorMessage = "MD5值必须为32位字符")]
        [RegularExpression("^[a-fA-F0-9]{32}$", ErrorMessage = "MD5值格式不正确")]
        public string FileMD5 { get; set; } = string.Empty;
    }

    /// <summary>
    /// 更新版本信息DTO
    /// </summary>
    public class UpdateVersionInfoDto
    {
        /// <summary>
        /// 软件平台信息 WIN_POS/PDA_WH/PDA_Store/ios
        /// </summary>
        [RegularExpression("WIN_POS|PDA_WH|PDA_Store|ios", ErrorMessage = "软件平台必须是WIN_POS、PDA_WH、PDA_Store或ios")]
        public string? SoftName { get; set; }

        /// <summary>
        /// 发布日期
        /// </summary>
        public DateTime? ReleaseDate { get; set; }

        /// <summary>
        /// 版本描述
        /// </summary>
        [StringLength(500, ErrorMessage = "版本描述长度不能超过500个字符")]
        public string? Description { get; set; }

        /// <summary>
        /// 文件名
        /// </summary>
        [StringLength(255, ErrorMessage = "文件名长度不能超过255个字符")]
        public string? FileName { get; set; }

        /// <summary>
        /// 文件大小，单位为字节
        /// </summary>
        [Range(0, long.MaxValue, ErrorMessage = "文件大小必须为正数")]
        public long? FileSize { get; set; }

        /// <summary>
        /// 下载来源相对路径
        /// </summary>
        [StringLength(500, ErrorMessage = "下载路径长度不能超过500个字符")]
        public string? DownloadFromPath { get; set; }

        /// <summary>
        /// 下载目标相对路径
        /// </summary>
        [StringLength(500, ErrorMessage = "下载目标路径长度不能超过500个字符")]
        public string? DownloadToPath { get; set; }

        /// <summary>
        /// 解压相对路径
        /// </summary>
        [StringLength(500, ErrorMessage = "解压路径长度不能超过500个字符")]
        public string? UnzipPath { get; set; }

        /// <summary>
        /// 文件的MD5值，用于完整性校验
        /// </summary>
        [StringLength(32, ErrorMessage = "MD5值必须为32位字符")]
        [RegularExpression("^[a-fA-F0-9]{32}$", ErrorMessage = "MD5值格式不正确")]
        public string? FileMD5 { get; set; }
    }

    /// <summary>
    /// 版本信息查询DTO
    /// </summary>
    public class VersionInfoQueryDto
    {
        /// <summary>
        /// 页码
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// 每页数量
        /// </summary>
        public int PageSize { get; set; } = 20;

        /// <summary>
        /// 关键字搜索（版本号、描述、文件名）
        /// </summary>
        public string? Keyword { get; set; }

        /// <summary>
        /// 软件平台筛选 WIN_POS/PDA_WH/PDA_Store/ios
        /// </summary>
        public string? SoftName { get; set; }

        /// <summary>
        /// 排序字段
        /// </summary>
        public string SortBy { get; set; } = "ReleaseDate";

        /// <summary>
        /// 是否降序排序
        /// </summary>
        public bool SortDescending { get; set; } = true;
    }
}
