namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 发票邮件 SMTP 配置。
    /// </summary>
    public class InvoiceEmailOptions
    {
        /// <summary>
        /// SMTP 主机
        /// </summary>
        public string? Host { get; set; }

        /// <summary>
        /// SMTP 端口
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// 是否启用 SSL
        /// </summary>
        public bool UseSsl { get; set; } = true;

        /// <summary>
        /// 是否检查 SMTP 证书吊销状态
        /// </summary>
        public bool CheckCertificateRevocation { get; set; } = true;

        /// <summary>
        /// SMTP 用户名
        /// </summary>
        public string? Username { get; set; }

        /// <summary>
        /// SMTP 密码
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// 发件邮箱
        /// </summary>
        public string? FromEmail { get; set; }

        /// <summary>
        /// 发件人名称
        /// </summary>
        public string? FromName { get; set; }

        /// <summary>
        /// 附件大小上限（字节）
        /// </summary>
        public long MaxAttachmentBytes { get; set; } = 5 * 1024 * 1024;
    }
}
