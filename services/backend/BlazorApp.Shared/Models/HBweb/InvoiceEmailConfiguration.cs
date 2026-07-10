using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb
{
    /// <summary>
    /// 发票邮件 SMTP 配置。
    /// </summary>
    [SugarTable("InvoiceEmailConfiguration")]
    public class InvoiceEmailConfiguration : BaseEntity
    {
        public const string DefaultId = "default";

        [SugarColumn(IsPrimaryKey = true, Length = 50)]
        public string Id { get; set; } = DefaultId;

        [SugarColumn(Length = 100, IsNullable = false)]
        public string Name { get; set; } = "默认发件账号";

        [SugarColumn(IsNullable = false)]
        public bool IsDefault { get; set; } = true;

        [SugarColumn(Length = 200, IsNullable = false)]
        public string Host { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false)]
        public int Port { get; set; }

        [SugarColumn(IsNullable = false)]
        public bool UseSsl { get; set; } = true;

        [SugarColumn(IsNullable = false)]
        public bool CheckCertificateRevocation { get; set; } = true;

        [SugarColumn(Length = 200, IsNullable = true)]
        public string? Username { get; set; }

        /// <summary>
        /// SMTP 密码存储字段。历史字段名保留，当前按明文保存。
        /// </summary>
        [SugarColumn(Length = 2000, IsNullable = true)]
        public string? EncryptedPassword { get; set; }

        [SugarColumn(Length = 200, IsNullable = false)]
        public string FromEmail { get; set; } = string.Empty;

        [SugarColumn(Length = 200, IsNullable = true)]
        public string? FromName { get; set; }

        [SugarColumn(IsNullable = false)]
        public long MaxAttachmentBytes { get; set; } = 5 * 1024 * 1024;

        [SugarColumn(IsNullable = false)]
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
