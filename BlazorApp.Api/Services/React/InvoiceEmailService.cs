using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 发票邮件发送服务。
    /// </summary>
    public class InvoiceEmailService : IInvoiceEmailService
    {
        private readonly ILogger<InvoiceEmailService> _logger;
        private readonly InvoiceEmailOptions _options;

        public InvoiceEmailService(
            ILogger<InvoiceEmailService> logger,
            IOptions<InvoiceEmailOptions> options
        )
        {
            _logger = logger;
            _options = options.Value ?? new InvoiceEmailOptions();
        }

        public async Task<ApiResponse<bool>> SendInvoiceAsync(StoreOrderInvoiceEmailMessage message)
        {
            var configError = GetConfigurationError();
            if (configError != null)
            {
                return ApiResponse<bool>.Error(configError, "INVOICE_EMAIL_NOT_CONFIGURED");
            }

            if (message.PdfBytes.Length == 0)
            {
                return ApiResponse<bool>.Error("PDF 附件不能为空", "INVOICE_EMAIL_EMPTY_PDF");
            }

            if (message.PdfBytes.LongLength > _options.MaxAttachmentBytes)
            {
                return ApiResponse<bool>.Error(
                    $"PDF 附件不能超过 {_options.MaxAttachmentBytes} 字节",
                    "INVOICE_EMAIL_ATTACHMENT_TOO_LARGE"
                );
            }

            try
            {
                var email = new MimeMessage();
                email.From.Add(
                    new MailboxAddress(
                        _options.FromName ?? _options.FromEmail!,
                        _options.FromEmail!
                    )
                );
                email.To.Add(MailboxAddress.Parse(message.ToEmail));
                email.Subject = message.Subject;

                var builder = new BodyBuilder
                {
                    TextBody = message.Body,
                };
                builder.Attachments.Add(message.PdfFileName, message.PdfBytes, ContentType.Parse("application/pdf"));
                email.Body = builder.ToMessageBody();

                using var smtpClient = new SmtpClient();
                var secureSocketOptions = _options.UseSsl
                    ? SecureSocketOptions.SslOnConnect
                    : SecureSocketOptions.StartTls;

                await smtpClient.ConnectAsync(_options.Host, _options.Port, secureSocketOptions);

                if (!string.IsNullOrWhiteSpace(_options.Username))
                {
                    await smtpClient.AuthenticateAsync(
                        _options.Username,
                        _options.Password ?? string.Empty
                    );
                }

                await smtpClient.SendAsync(email);
                await smtpClient.DisconnectAsync(true);

                return ApiResponse<bool>.OK(true, "发票邮件发送成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送发票邮件失败，收件人：{ToEmail}", message.ToEmail);
                return ApiResponse<bool>.Error("发票邮件发送失败，请检查 SMTP 配置或稍后重试", "INVOICE_EMAIL_SEND_FAILED");
            }
        }

        private string? GetConfigurationError()
        {
            if (string.IsNullOrWhiteSpace(_options.Host) || _options.Port <= 0)
            {
                return "未配置发票邮件 SMTP，请先完成 InvoiceEmail 配置";
            }

            if (string.IsNullOrWhiteSpace(_options.FromEmail))
            {
                return "未配置发件邮箱，请先完成 InvoiceEmail 配置";
            }

            if (_options.MaxAttachmentBytes <= 0)
            {
                return "InvoiceEmail.MaxAttachmentBytes 配置无效";
            }

            return null;
        }
    }
}
