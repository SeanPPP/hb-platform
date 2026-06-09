using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 发票邮件 SMTP 配置展示 DTO。
    /// </summary>
    public class InvoiceEmailSettingsDto
    {
        public string? Host { get; set; }
        public int Port { get; set; }
        public bool UseSsl { get; set; } = true;
        public bool CheckCertificateRevocation { get; set; } = true;
        public string? Username { get; set; }
        public bool HasPassword { get; set; }
        public string? FromEmail { get; set; }
        public string? FromName { get; set; }
        public long MaxAttachmentBytes { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public string? UpdatedBy { get; set; }
    }

    /// <summary>
    /// 发票邮件 SMTP 配置保存请求。
    /// </summary>
    public class UpdateInvoiceEmailSettingsDto
    {
        [Required(ErrorMessage = "SMTP 主机不能为空")]
        [StringLength(200, ErrorMessage = "SMTP 主机长度不能超过200个字符")]
        public string Host { get; set; } = string.Empty;

        [Range(1, 65535, ErrorMessage = "SMTP 端口必须在1到65535之间")]
        public int Port { get; set; }

        public bool UseSsl { get; set; } = true;

        public bool CheckCertificateRevocation { get; set; } = true;

        [StringLength(200, ErrorMessage = "SMTP 用户名长度不能超过200个字符")]
        public string? Username { get; set; }

        [StringLength(500, ErrorMessage = "SMTP 密码长度不能超过500个字符")]
        public string? Password { get; set; }

        public bool ClearPassword { get; set; }

        [Required(ErrorMessage = "发件邮箱不能为空")]
        [EmailAddress(ErrorMessage = "发件邮箱格式不正确")]
        [StringLength(200, ErrorMessage = "发件邮箱长度不能超过200个字符")]
        public string FromEmail { get; set; } = string.Empty;

        [StringLength(200, ErrorMessage = "发件人名称长度不能超过200个字符")]
        public string? FromName { get; set; }

        [Range(1, long.MaxValue, ErrorMessage = "附件大小上限必须大于0")]
        public long MaxAttachmentBytes { get; set; } = 5 * 1024 * 1024;
    }

    /// <summary>
    /// 发票邮件 SMTP 测试请求。
    /// </summary>
    public class TestInvoiceEmailSettingsDto : UpdateInvoiceEmailSettingsDto
    {
        [Required(ErrorMessage = "测试收件邮箱不能为空")]
        [EmailAddress(ErrorMessage = "测试收件邮箱格式不正确")]
        [StringLength(100, ErrorMessage = "测试收件邮箱长度不能超过100个字符")]
        public string TestToEmail { get; set; } = string.Empty;
    }
}
