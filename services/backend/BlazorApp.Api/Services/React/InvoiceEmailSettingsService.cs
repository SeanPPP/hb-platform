using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 发票邮件 SMTP 配置读写服务。
    /// </summary>
    public class InvoiceEmailSettingsService : IInvoiceEmailSettingsService
    {
        private const string DataProtectionPurpose = "Hbweb.InvoiceEmail.SmtpPassword.v1";

        private readonly SqlSugarContext _context;
        private readonly InvoiceEmailOptions _fallbackOptions;
        private readonly IDataProtector _protector;
        private readonly ILogger<InvoiceEmailSettingsService> _logger;

        public InvoiceEmailSettingsService(
            SqlSugarContext context,
            IOptions<InvoiceEmailOptions> fallbackOptions,
            IDataProtectionProvider dataProtectionProvider,
            ILogger<InvoiceEmailSettingsService> logger
        )
        {
            _context = context;
            _fallbackOptions = fallbackOptions.Value ?? new InvoiceEmailOptions();
            _protector = dataProtectionProvider.CreateProtector(DataProtectionPurpose);
            _logger = logger;
        }

        public async Task<ApiResponse<InvoiceEmailSettingsDto>> GetSettingsAsync(
            CancellationToken cancellationToken = default
        )
        {
            var model = await QueryModelAsync();
            var dto = model == null
                ? ToDto(_fallbackOptions)
                : ToDto(model);

            return ApiResponse<InvoiceEmailSettingsDto>.OK(dto, "查询成功");
        }

        public async Task<ApiResponse<InvoiceEmailSettingsDto>> UpdateSettingsAsync(
            UpdateInvoiceEmailSettingsDto request,
            string? updatedBy,
            CancellationToken cancellationToken = default
        )
        {
            var existing = await QueryModelAsync();
            var now = DateTime.UtcNow;
            string? encryptedPassword;
            try
            {
                encryptedPassword = ResolveEncryptedPassword(existing, request);
            }
            catch (InvoiceEmailPasswordDecryptException)
            {
                return ApiResponse<InvoiceEmailSettingsDto>.Error(
                    "发票邮件 SMTP 密码解密失败，请重新输入 SMTP 密码后保存发票邮箱配置",
                    "INVOICE_EMAIL_PASSWORD_DECRYPT_FAILED"
                );
            }

            var model = existing ?? new InvoiceEmailConfiguration
            {
                Id = InvoiceEmailConfiguration.DefaultId,
            };

            model.Host = request.Host.Trim();
            model.Port = request.Port;
            model.UseSsl = request.UseSsl;
            model.CheckCertificateRevocation = request.CheckCertificateRevocation;
            model.Username = NormalizeOptional(request.Username);
            model.EncryptedPassword = encryptedPassword;
            model.FromEmail = request.FromEmail.Trim();
            model.FromName = NormalizeOptional(request.FromName);
            model.MaxAttachmentBytes = request.MaxAttachmentBytes;
            model.UpdatedAtUtc = now;
            model.UpdatedBy = NormalizeOptional(updatedBy);

            if (existing == null)
            {
                await _context.Db.Insertable(model).ExecuteCommandAsync();
            }
            else
            {
                await _context.Db.Updateable(model).ExecuteCommandAsync();
            }

            return ApiResponse<InvoiceEmailSettingsDto>.OK(ToDto(model), "发票邮件配置已更新");
        }

        public async Task<InvoiceEmailOptions> GetEffectiveOptionsAsync(
            CancellationToken cancellationToken = default
        )
        {
            var model = await QueryModelAsync();
            if (model == null)
            {
                return CloneOptions(_fallbackOptions);
            }

            return new InvoiceEmailOptions
            {
                Host = model.Host,
                Port = model.Port,
                UseSsl = model.UseSsl,
                CheckCertificateRevocation = model.CheckCertificateRevocation,
                Username = model.Username,
                Password = UnprotectPassword(model.EncryptedPassword),
                FromEmail = model.FromEmail,
                FromName = model.FromName,
                MaxAttachmentBytes = model.MaxAttachmentBytes,
            };
        }

        public async Task<InvoiceEmailOptions> BuildTransientOptionsAsync(
            UpdateInvoiceEmailSettingsDto request,
            CancellationToken cancellationToken = default
        )
        {
            // 测试邮件不保存表单，但密码栏留空时沿用当前已保存密码，符合保存接口的保留密码约定。
            var requestPassword = NormalizeOptional(request.Password);
            var password = request.ClearPassword
                ? null
                : requestPassword;
            if (!request.ClearPassword && password == null)
            {
                var existingOptions = await GetEffectiveOptionsAsync(cancellationToken);
                password = existingOptions.Password;
            }

            return new InvoiceEmailOptions
            {
                Host = request.Host.Trim(),
                Port = request.Port,
                UseSsl = request.UseSsl,
                CheckCertificateRevocation = request.CheckCertificateRevocation,
                Username = NormalizeOptional(request.Username),
                Password = password,
                FromEmail = request.FromEmail.Trim(),
                FromName = NormalizeOptional(request.FromName),
                MaxAttachmentBytes = request.MaxAttachmentBytes,
            };
        }

        private async Task<InvoiceEmailConfiguration?> QueryModelAsync()
        {
            return await _context.Db.Queryable<InvoiceEmailConfiguration>()
                .Where(item => item.Id == InvoiceEmailConfiguration.DefaultId)
                .FirstAsync();
        }

        private string? ResolveEncryptedPassword(
            InvoiceEmailConfiguration? existing,
            UpdateInvoiceEmailSettingsDto request
        )
        {
            if (request.ClearPassword)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(request.Password))
            {
                if (!string.IsNullOrWhiteSpace(existing?.EncryptedPassword))
                {
                    // 密码留空表示沿用旧密码；先校验旧密文可解，避免继续保存已经失效的 key ring 密文。
                    _ = UnprotectPassword(existing.EncryptedPassword);
                }

                return existing?.EncryptedPassword;
            }

            return _protector.Protect(request.Password);
        }

        private string? UnprotectPassword(string? encryptedPassword)
        {
            if (string.IsNullOrWhiteSpace(encryptedPassword))
            {
                return null;
            }

            try
            {
                return _protector.Unprotect(encryptedPassword);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解密发票邮件 SMTP 密码失败");
                throw new InvoiceEmailPasswordDecryptException("发票邮件 SMTP 密码解密失败", ex);
            }
        }

        private static InvoiceEmailSettingsDto ToDto(InvoiceEmailOptions options) => new()
        {
            Host = options.Host,
            Port = options.Port,
            UseSsl = options.UseSsl,
            CheckCertificateRevocation = options.CheckCertificateRevocation,
            Username = options.Username,
            HasPassword = !string.IsNullOrWhiteSpace(options.Password),
            FromEmail = options.FromEmail,
            FromName = options.FromName,
            MaxAttachmentBytes = options.MaxAttachmentBytes,
        };

        private static InvoiceEmailSettingsDto ToDto(InvoiceEmailConfiguration model) => new()
        {
            Host = model.Host,
            Port = model.Port,
            UseSsl = model.UseSsl,
            CheckCertificateRevocation = model.CheckCertificateRevocation,
            Username = model.Username,
            HasPassword = !string.IsNullOrWhiteSpace(model.EncryptedPassword),
            FromEmail = model.FromEmail,
            FromName = model.FromName,
            MaxAttachmentBytes = model.MaxAttachmentBytes,
            UpdatedAtUtc = model.UpdatedAtUtc,
            UpdatedBy = model.UpdatedBy,
        };

        private static InvoiceEmailOptions CloneOptions(InvoiceEmailOptions options) => new()
        {
            Host = options.Host,
            Port = options.Port,
            UseSsl = options.UseSsl,
            CheckCertificateRevocation = options.CheckCertificateRevocation,
            Username = options.Username,
            Password = options.Password,
            FromEmail = options.FromEmail,
            FromName = options.FromName,
            MaxAttachmentBytes = options.MaxAttachmentBytes,
        };

        private static string? NormalizeOptional(string? value)
        {
            var trimmed = value?.Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }
    }

    public sealed class InvoiceEmailPasswordDecryptException : Exception
    {
        public InvoiceEmailPasswordDecryptException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
