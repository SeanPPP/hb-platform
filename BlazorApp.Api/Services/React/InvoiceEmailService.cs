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

            if (message.Attachments.Count == 0)
            {
                return ApiResponse<bool>.Error("发票附件不能为空", "INVOICE_EMAIL_EMPTY_ATTACHMENT");
            }

            if (message.Attachments.Any(attachment => attachment.Bytes.Length == 0))
            {
                return ApiResponse<bool>.Error("发票附件不能为空", "INVOICE_EMAIL_EMPTY_ATTACHMENT");
            }

            var totalAttachmentBytes = message.Attachments.Sum(attachment => attachment.Bytes.LongLength);
            if (totalAttachmentBytes > _options.MaxAttachmentBytes)
            {
                return ApiResponse<bool>.Error("发票附件不能超过配置限制", "INVOICE_EMAIL_ATTACHMENT_TOO_LARGE");
            }

            try
            {
                var email = BuildMimeMessage(message);

                using var smtpClient = CreateSmtpClient();
                var secureSocketOptions = _options.UseSsl
                    ? SecureSocketOptions.SslOnConnect
                    : SecureSocketOptions.StartTls;

                await ConnectSmtpClientAsync(smtpClient, secureSocketOptions);

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
            catch (SslHandshakeException ex)
            {
                _logger.LogError(ex, "发票邮件 TLS 握手失败，收件人：{ToEmail}", message.ToEmail);
                return ApiResponse<bool>.Error(
                    "发票邮件 TLS 握手失败，请检查 SMTP 证书或 InvoiceEmail.CheckCertificateRevocation 配置",
                    "INVOICE_EMAIL_TLS_HANDSHAKE_FAILED"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送发票邮件失败，收件人：{ToEmail}", message.ToEmail);
                return ApiResponse<bool>.Error("发票邮件发送失败，请检查 SMTP 配置或稍后重试", "INVOICE_EMAIL_SEND_FAILED");
            }
        }

        protected virtual SmtpClient CreateSmtpClient()
        {
            // 部分服务器无法完成 CRL/OCSP 查询时，只允许通过配置关闭吊销检查，仍保留证书主体校验。
            return new SmtpClient
            {
                CheckCertificateRevocation = _options.CheckCertificateRevocation,
            };
        }

        protected virtual MimeMessage BuildMimeMessage(StoreOrderInvoiceEmailMessage message)
        {
            var email = new MimeMessage();
            email.From.Add(
                new MailboxAddress(_options.FromName ?? _options.FromEmail!, _options.FromEmail!)
            );
            email.To.Add(MailboxAddress.Parse(message.ToEmail));
            email.Subject = message.Subject;

            var builder = new BodyBuilder
            {
                TextBody = message.Body,
            };
            foreach (var attachment in message.Attachments)
            {
                var contentType = string.IsNullOrWhiteSpace(attachment.ContentType)
                    ? ContentType.Parse("application/octet-stream")
                    : ContentType.Parse(attachment.ContentType);
                builder.Attachments.Add(attachment.FileName, attachment.Bytes, contentType);
            }

            email.Body = builder.ToMessageBody();
            return email;
        }

        protected virtual Task ConnectSmtpClientAsync(
            SmtpClient smtpClient,
            SecureSocketOptions secureSocketOptions
        )
        {
            return smtpClient.ConnectAsync(_options.Host, _options.Port, secureSocketOptions);
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
