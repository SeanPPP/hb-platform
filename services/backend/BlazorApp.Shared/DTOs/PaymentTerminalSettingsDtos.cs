using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    public class PaymentTerminalSettingsDto
    {
        public List<PaymentTerminalEnvironmentStatusDto> Square { get; set; } = new();
        public List<PaymentTerminalStoreOptionDto> Stores { get; set; } = new();
        public string? SelectedStoreCode { get; set; }
        public List<LinklyCloudCredentialAdminDto> Linkly { get; set; } = new();
    }

    public class PaymentTerminalEnvironmentStatusDto
    {
        public string Environment { get; set; } = string.Empty;
        public bool Configured { get; set; }
        public bool Enabled { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public class PaymentTerminalStoreOptionDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public string StoreName { get; set; } = string.Empty;
    }

    public class LinklyCloudCredentialAdminDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public string? Username { get; set; }
        public bool HasPassword { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public class UpdateSquareTokenDto
    {
        [Required(ErrorMessage = "支付环境不能为空")]
        [StringLength(32, ErrorMessage = "支付环境长度不能超过32个字符")]
        public string Environment { get; set; } = string.Empty;

        [StringLength(2048, ErrorMessage = "Square Token 长度不能超过2048个字符")]
        public string? AccessToken { get; set; }

        public bool ClearToken { get; set; }
    }

    public class UpdateLinklyCredentialDto
    {
        [Required(ErrorMessage = "门店编码不能为空")]
        [StringLength(32, ErrorMessage = "门店编码长度不能超过32个字符")]
        public string StoreCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "支付环境不能为空")]
        [StringLength(32, ErrorMessage = "支付环境长度不能超过32个字符")]
        public string Environment { get; set; } = string.Empty;

        [StringLength(256, ErrorMessage = "Linkly 用户名长度不能超过256个字符")]
        public string? Username { get; set; }

        [StringLength(256, ErrorMessage = "Linkly 密码长度不能超过256个字符")]
        public string? Password { get; set; }

        public bool ClearCredential { get; set; }
    }
}
