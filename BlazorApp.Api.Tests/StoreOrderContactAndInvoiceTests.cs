using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using ClosedXML.Excel;
using iTextSharp.text.pdf;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using Xunit;
using BlazorApp.Shared.Models.HBweb;

namespace BlazorApp.Api.Tests;

public sealed class StoreOrderContactAndInvoiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;

    public StoreOrderContactAndInvoiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _sqliteConnection = new SqliteConnection($"Data Source={_dbPath}");
        _sqliteConnection.Open();

        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _sqliteConnection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });

        _db.CodeFirst.InitTables(
            typeof(Store),
            typeof(WareHouseOrder),
            typeof(WareHouseOrderDetails),
            typeof(Product),
            typeof(WarehouseProduct),
            typeof(DomesticProduct),
            typeof(ProductLocation),
            typeof(Location),
            typeof(ProductGrade),
            typeof(User),
            typeof(UserStore),
            typeof(InvoiceEmailConfiguration)
        );
    }

    [Fact]
    public async Task GetStoreByCodeAsync_ReturnsContactEmailFromStore()
    {
        await _db.Insertable(
            new Store
            {
                StoreGUID = "store-1",
                StoreCode = "S001",
                StoreName = "Test Store",
                Address = "1 Test Street",
                Phone = "123456",
                ContactEmail = "store@example.com",
                IsActive = true,
            }
        ).ExecuteCommandAsync();

        var service = CreateStoreService();

        var result = await service.GetStoreByCodeAsync("S001");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("store@example.com", result.Data!.ContactEmail);
    }

    [Fact]
    public async Task GetOrderDetailAsync_AndFull_ReturnStoreContactEmail()
    {
        await SeedStoreOrderGraphAsync();
        var service = CreateStoreOrderService();

        var detailResult = await service.GetOrderDetailAsync("order-1");
        var fullResult = await service.GetOrderDetailFullAsync("order-1");

        Assert.True(detailResult.Success);
        Assert.Equal("1 Test Street", detailResult.Data!.StoreAddress);
        Assert.Equal("store@example.com", detailResult.Data.StoreContactEmail);
        Assert.True(fullResult.Success);
        Assert.Equal("store@example.com", fullResult.Data!.StoreContactEmail);
    }

    [Fact]
    public async Task UpdateStoreContactAsync_UpdatesStoreAddressAndEmail()
    {
        await SeedStoreOrderGraphAsync();
        var service = CreateStoreOrderService("tester");

        var result = await service.UpdateStoreContactAsync(
            new UpdateStoreOrderStoreContactDto
            {
                OrderGUID = "order-1",
                StoreCode = "S001",
                Address = "99 Updated Road",
                ContactEmail = "updated@example.com",
            }
        );

        var store = await _db.Queryable<Store>().Where(x => x.StoreGUID == "store-1").FirstAsync();

        Assert.True(result.Success);
        Assert.Equal("99 Updated Road", store!.Address);
        Assert.Equal("updated@example.com", store.ContactEmail);
        Assert.Equal("tester", store.UpdatedBy);
        Assert.NotNull(store.UpdatedAt);
    }

    [Fact]
    public async Task UpdateStoreContactAsync_WhenAddressOmitted_PreservesExistingAddress()
    {
        await SeedStoreOrderGraphAsync();
        var service = CreateStoreOrderService("tester");

        var result = await service.UpdateStoreContactAsync(
            new UpdateStoreOrderStoreContactDto
            {
                OrderGUID = "order-1",
                StoreCode = "S001",
                ContactEmail = "email-only@example.com",
            }
        );

        var store = await _db.Queryable<Store>().Where(x => x.StoreGUID == "store-1").FirstAsync();

        Assert.True(result.Success);
        Assert.Equal("1 Test Street", store!.Address);
        Assert.Equal("email-only@example.com", store.ContactEmail);
    }

    [Fact]
    public async Task InvoiceEmailService_WhenSmtpNotConfigured_ReturnsClearFailure()
    {
        var service = new InvoiceEmailService(
            NullLogger<InvoiceEmailService>.Instance,
            CreateSettingsService(new InvoiceEmailOptions())
        );

        var result = await service.SendInvoiceAsync(
            new StoreOrderInvoiceEmailMessage
            {
                ToEmail = "customer@example.com",
                Subject = "Invoice SO001",
                Body = "Hello",
                Attachments =
                {
                    new StoreOrderInvoiceEmailAttachment
                    {
                        FileName = "Invoice_S001_SO001.pdf",
                        ContentType = "application/pdf",
                        Bytes = new byte[] { 1, 2, 3, 4 },
                    },
                    new StoreOrderInvoiceEmailAttachment
                    {
                        FileName = "Invoice_S001_SO001.xlsx",
                        ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        Bytes = new byte[] { 5, 6, 7, 8 },
                    },
                },
            }
        );

        Assert.False(result.Success);
        Assert.Equal("未配置发票邮件 SMTP，请先完成 InvoiceEmail 配置", result.Message);
    }

    [Fact]
    public async Task StoreOrderInvoiceEmailJobService_StartsJobAndMarksSucceeded()
    {
        var request = new SendStoreOrderInvoiceEmailDto
        {
            OrderGUID = "order-1",
            ToEmail = "customer@example.com",
        };
        var pdfAttachment = new StoreOrderInvoiceEmailAttachment
        {
            FileName = "Invoice_S001_SO001.pdf",
            ContentType = "application/pdf",
            Bytes = new byte[] { 1, 2, 3, 4 },
        };
        var excelAttachment = new StoreOrderInvoiceEmailAttachment
        {
            FileName = "Invoice_S001_SO001.xlsx",
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            Bytes = new byte[] { 5, 6, 7, 8 },
        };
        var attachmentService = new Mock<IStoreOrderInvoiceAttachmentService>(MockBehavior.Strict);
        attachmentService
            .Setup(item => item.GenerateAttachmentsAsync("order-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<StoreOrderInvoiceAttachmentBundle>.OK(
                new StoreOrderInvoiceAttachmentBundle
                {
                    OrderGUID = "order-1",
                    OrderNo = "SO001",
                    StoreCode = "S001",
                    Attachments = new List<StoreOrderInvoiceEmailAttachment>
                    {
                        pdfAttachment,
                        excelAttachment,
                    },
                }
            ));
        StoreOrderInvoiceEmailMessage? capturedMessage = null;
        var invoiceEmailService = new Mock<IInvoiceEmailService>(MockBehavior.Strict);
        invoiceEmailService
            .Setup(item => item.SendInvoiceAsync(It.IsAny<StoreOrderInvoiceEmailMessage>()))
            .Callback<StoreOrderInvoiceEmailMessage>(message => capturedMessage = message)
            .ReturnsAsync(ApiResponse<bool>.OK(true, "发票邮件发送成功"));
        var jobService = CreateInvoiceEmailJobService(attachmentService, invoiceEmailService);

        var started = await jobService.StartJobAsync(request);
        var completed = await WaitForInvoiceEmailJobAsync(jobService, started.JobId);

        Assert.False(string.IsNullOrWhiteSpace(started.JobId));
        Assert.Equal(StoreOrderInvoiceEmailJobStatusConstants.Succeeded, completed.Status);
        Assert.Equal("发票邮件发送成功", completed.Message);
        Assert.Equal("order-1", completed.OrderGUID);
        Assert.Equal("customer@example.com", completed.ToEmail);
        Assert.NotNull(completed.CompletedAt);
        attachmentService.Verify(
            item => item.GenerateAttachmentsAsync("order-1", It.IsAny<CancellationToken>()),
            Times.Once
        );
        invoiceEmailService.Verify(
            item => item.SendInvoiceAsync(It.IsAny<StoreOrderInvoiceEmailMessage>()),
            Times.Once
        );
        Assert.NotNull(capturedMessage);
        Assert.Equal("customer@example.com", capturedMessage!.ToEmail);
        Assert.Collection(
            capturedMessage.Attachments,
            attachment => Assert.Equal("application/pdf", attachment.ContentType),
            attachment => Assert.Equal(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                attachment.ContentType
            )
        );
    }

    [Fact]
    public async Task StoreOrderInvoiceEmailJobService_WhenAttachmentGenerationFails_MarksFailedWithMessage()
    {
        var request = new SendStoreOrderInvoiceEmailDto
        {
            OrderGUID = "order-1",
            ToEmail = "customer@example.com",
        };
        var attachmentService = new Mock<IStoreOrderInvoiceAttachmentService>(MockBehavior.Strict);
        attachmentService
            .Setup(item => item.GenerateAttachmentsAsync("order-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<StoreOrderInvoiceAttachmentBundle>.Error("生成发票附件失败"));
        var invoiceEmailService = new Mock<IInvoiceEmailService>(MockBehavior.Strict);
        var jobService = CreateInvoiceEmailJobService(attachmentService, invoiceEmailService);

        var started = await jobService.StartJobAsync(request);
        var completed = await WaitForInvoiceEmailJobAsync(jobService, started.JobId);

        Assert.Equal(StoreOrderInvoiceEmailJobStatusConstants.Failed, completed.Status);
        Assert.Equal("生成发票附件失败", completed.Message);
        Assert.NotNull(completed.CompletedAt);
        invoiceEmailService.Verify(
            item => item.SendInvoiceAsync(It.IsAny<StoreOrderInvoiceEmailMessage>()),
            Times.Never
        );
    }

    [Fact]
    public async Task StoreOrderInvoiceAttachmentService_GeneratesPdfAndExcel()
    {
        var orderService = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        orderService
            .Setup(item => item.GetOrderDetailFullAsync("order-1"))
            .ReturnsAsync(ApiResponse<StoreOrderCartDto?>.OK(CreateInvoiceOrder()));
        var service = new StoreOrderInvoiceAttachmentService(
            orderService.Object,
            NullLogger<StoreOrderInvoiceAttachmentService>.Instance
        );

        var result = await service.GenerateAttachmentsAsync("order-1");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("order-1", result.Data!.OrderGUID);
        Assert.Equal("SO001", result.Data.OrderNo);
        Assert.Equal("S001", result.Data.StoreCode);
        Assert.Collection(
            result.Data.Attachments,
            pdf =>
            {
                Assert.EndsWith(".pdf", pdf.FileName);
                Assert.Equal("application/pdf", pdf.ContentType);
                Assert.NotEmpty(pdf.Bytes);
                Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Bytes.Take(4).ToArray()));
                var pdfText = ExtractPdfText(pdf.Bytes);
                Assert.Contains("WAREHOUSE ADDRESS", pdfText);
                Assert.Contains("3 Rogilla close Maryland", pdfText);
                Assert.Contains("A.B.N. 35 160 589 793", pdfText);
                Assert.Contains("WAREHOUSE EMAIL", pdfText);
                Assert.Contains("INVOICE NO. SO001", pdfText);
                Assert.Contains("PAYMENT DETAIL: DIRECT DEBIT", pdfText);
                Assert.Contains("NAME:", pdfText);
                Assert.Contains("HOT BARGAIN INTERNATIONAL", pdfText);
                Assert.Contains("BSB:", pdfText);
                Assert.Contains("12532", pdfText);
                Assert.Contains("ACCOUNT:", pdfText);
                Assert.Contains("208034605", pdfText);
                Assert.Contains("All products remain the property of Hot Bargain International Pty Ltd", pdfText);
                Assert.True(HasPdfImageXObject(pdf.Bytes), "发票邮件 PDF 应嵌入 HOT BARGAIN logo 图片");
            },
            excel =>
            {
                Assert.EndsWith(".xlsx", excel.FileName);
                Assert.Equal(
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    excel.ContentType
                );
                using var workbook = new XLWorkbook(new MemoryStream(excel.Bytes));
                var sheet = workbook.Worksheet("Invoice");
                Assert.Equal("Item No", sheet.Cell(1, 2).GetString());
                Assert.Equal("HB001", sheet.Cell(2, 2).GetString());
                Assert.Equal(2m, sheet.Cell(2, 6).GetValue<decimal>());
                Assert.Equal(1m, sheet.Cell(2, 7).GetValue<decimal>());
                Assert.Equal(8.5m, sheet.Cell(2, 8).GetValue<decimal>());
            }
        );
    }

    [Fact]
    public void InvoiceEmailService_BuildsMessageWithPdfAndExcelAttachments()
    {
        var service = new TestableInvoiceEmailService(
            CreateSettingsService(new InvoiceEmailOptions
            {
                Host = "smtp.example.com",
                Port = 465,
                FromEmail = "warehouse@example.com",
            })
        );

        var email = service.BuildMimeMessageForTest(
            new StoreOrderInvoiceEmailMessage
            {
                ToEmail = "customer@example.com",
                Subject = "invoice",
                Body = "body",
                Attachments = new List<StoreOrderInvoiceEmailAttachment>
                {
                    new()
                    {
                        FileName = "invoice.pdf",
                        ContentType = "application/pdf",
                        Bytes = new byte[] { 1, 2, 3 },
                    },
                    new()
                    {
                        FileName = "invoice.xlsx",
                        ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        Bytes = new byte[] { 4, 5, 6 },
                    },
                },
            }
        );

        var multipart = Assert.IsType<MimeKit.Multipart>(email.Body);
        Assert.Equal(3, multipart.Count);
    }

    [Fact]
    public async Task StoreOrderInvoiceEmailTextTranslationService_TranslatesEditedEmailTextBothWays()
    {
        var translationService = new Mock<ITranslationService>(MockBehavior.Strict);
        translationService
            .Setup(item => item.TranslateAsync("您好，附件请查收。", "en"))
            .ReturnsAsync("Hello, please find the attachment.");
        translationService
            .Setup(item => item.TranslateAsync("Hello, please find the attachment.", "zh"))
            .ReturnsAsync("您好，请查收附件。");
        translationService
            .Setup(item => item.TranslateAsync("Hello, please find the attachment.", "en"))
            .ReturnsAsync("Hello, please find the attachment.");
        var service = new StoreOrderInvoiceEmailTextTranslationService(
            translationService.Object,
            NullLogger<StoreOrderInvoiceEmailTextTranslationService>.Instance
        );

        var toEnglish = await service.TranslateAsync(
            new StoreOrderInvoiceEmailTextTranslationRequestDto
            {
                TargetLanguage = "en",
                Subject = "您好，附件请查收。",
                Body = "Hello, please find the attachment.",
            }
        );
        var toChinese = await service.TranslateAsync(
            new StoreOrderInvoiceEmailTextTranslationRequestDto
            {
                TargetLanguage = "zh",
                Subject = "Hello, please find the attachment.",
            }
        );

        Assert.True(toEnglish.Success);
        Assert.Equal("Hello, please find the attachment.", toEnglish.Data!.Subject);
        Assert.Equal("Hello, please find the attachment.", toEnglish.Data.Body);
        Assert.True(toChinese.Success);
        Assert.Equal("您好，请查收附件。", toChinese.Data!.Subject);
    }

    [Fact]
    public async Task StoreOrderInvoiceEmailTextTranslationService_WhenProviderFails_ReturnsClearFailure()
    {
        var translationService = new Mock<ITranslationService>(MockBehavior.Strict);
        translationService
            .Setup(item => item.TranslateAsync("custom subject", "zh"))
            .ThrowsAsync(new InvalidOperationException("provider failed"));
        var service = new StoreOrderInvoiceEmailTextTranslationService(
            translationService.Object,
            NullLogger<StoreOrderInvoiceEmailTextTranslationService>.Instance
        );

        var result = await service.TranslateAsync(
            new StoreOrderInvoiceEmailTextTranslationRequestDto
            {
                TargetLanguage = "zh",
                Subject = "custom subject",
            }
        );

        Assert.False(result.Success);
        Assert.Equal("翻译邮件内容失败，请稍后重试", result.Message);
    }

    [Fact]
    public void InvoiceEmailOptions_DefaultsToCheckingCertificateRevocation()
    {
        var options = new InvoiceEmailOptions();

        Assert.True(options.CheckCertificateRevocation);
    }

    [Fact]
    public void InvoiceEmailService_CreateSmtpClient_AppliesRevocationOption()
    {
        var service = new TestableInvoiceEmailService(
            CreateSettingsService(new InvoiceEmailOptions
            {
                CheckCertificateRevocation = false,
            })
        );

        using var client = service.CreateConfiguredClientForTest();

        Assert.False(client.CheckCertificateRevocation);
    }

    [Fact]
    public async Task InvoiceEmailService_WhenTlsHandshakeFails_ReturnsClearFailure()
    {
        var service = new TlsFailingInvoiceEmailService(
            CreateSettingsService(new InvoiceEmailOptions
            {
                Host = "mail.hotbargain.com.au",
                Port = 465,
                UseSsl = true,
                FromEmail = "sean@hotbargain.com.au",
            })
        );

        var result = await service.SendInvoiceAsync(
            new StoreOrderInvoiceEmailMessage
            {
                ToEmail = "customer@example.com",
                Subject = "invoice",
                Body = "body",
                Attachments = new List<StoreOrderInvoiceEmailAttachment>
                {
                    new()
                    {
                        FileName = "invoice.pdf",
                        ContentType = "application/pdf",
                        Bytes = new byte[] { 1, 2, 3, 4 },
                    },
                },
            }
        );

        Assert.False(result.Success);
        Assert.Equal("INVOICE_EMAIL_TLS_HANDSHAKE_FAILED", result.ErrorCode);
        Assert.Equal(
            "发票邮件 TLS 握手失败，请检查 SMTP 证书或 InvoiceEmail.CheckCertificateRevocation 配置",
            result.Message
        );
    }

    [Fact]
    public async Task InvoiceEmailService_WhenPasswordDecryptFails_ReturnsClearFailure()
    {
        var settingsService = CreateSettingsService();
        await _db.Insertable(
            new InvoiceEmailConfiguration
            {
                Id = InvoiceEmailConfiguration.DefaultId,
                Host = "mail.hotbargain.com.au",
                Port = 465,
                UseSsl = true,
                Username = "sender@hotbargain.com.au",
                EncryptedPassword = "invalid-protected-payload",
                FromEmail = "sender@hotbargain.com.au",
                MaxAttachmentBytes = 5_242_880,
            }
        ).ExecuteCommandAsync();
        var service = new InvoiceEmailService(
            NullLogger<InvoiceEmailService>.Instance,
            settingsService
        );

        var result = await service.SendInvoiceAsync(
            new StoreOrderInvoiceEmailMessage
            {
                ToEmail = "customer@example.com",
                Subject = "invoice",
                Body = "body",
                Attachments = new List<StoreOrderInvoiceEmailAttachment>
                {
                    new()
                    {
                        FileName = "invoice.pdf",
                        ContentType = "application/pdf",
                        Bytes = new byte[] { 1, 2, 3, 4 },
                    },
                },
            }
        );

        Assert.False(result.Success);
        Assert.Equal("INVOICE_EMAIL_PASSWORD_DECRYPT_FAILED", result.ErrorCode);
        Assert.Equal("发票邮件 SMTP 密码解密失败，请重新保存发票邮箱配置", result.Message);
    }

    [Fact]
    public async Task InvoiceEmailService_UsesDatabaseSettingsForSmtpAndSender()
    {
        var settingsService = CreateSettingsService();
        await settingsService.UpdateSettingsAsync(
            new UpdateInvoiceEmailSettingsDto
            {
                Host = "db.smtp.example.com",
                Port = 587,
                UseSsl = false,
                CheckCertificateRevocation = false,
                Username = "db-user",
                Password = "db-secret",
                FromEmail = "configured@example.com",
                FromName = "Configured Sender",
                MaxAttachmentBytes = 5_242_880,
            },
            "admin"
        );
        var service = new SendingCaptureInvoiceEmailService(settingsService);

        var result = await service.SendInvoiceAsync(
            new StoreOrderInvoiceEmailMessage
            {
                ToEmail = "customer@example.com",
                Subject = "invoice",
                Body = "body",
                Attachments = new List<StoreOrderInvoiceEmailAttachment>
                {
                    new()
                    {
                        FileName = "invoice.pdf",
                        ContentType = "application/pdf",
                        Bytes = new byte[] { 1, 2, 3, 4 },
                    },
                },
            }
        );

        Assert.True(result.Success);
        Assert.Equal("db.smtp.example.com", service.ConnectedHost);
        Assert.Equal(587, service.ConnectedPort);
        Assert.Equal(SecureSocketOptions.StartTls, service.ConnectedSocketOptions);
        Assert.Equal("db-user", service.AuthenticatedUsername);
        Assert.Equal("db-secret", service.AuthenticatedPassword);
        Assert.False(service.CreatedClientRevocationCheck);
        Assert.Equal("Configured Sender", service.SentMessage!.From.Mailboxes.Single().Name);
        Assert.Equal("configured@example.com", service.SentMessage.From.Mailboxes.Single().Address);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
        if (File.Exists(_dbPath))
        {
            SqliteTempFileCleanup.DeleteIfExists(_dbPath);
        }
    }

    private StoreService CreateStoreService()
    {
        return new StoreService(CreateSqlSugarContext(_db), NullLogger<StoreService>.Instance);
    }

    private StoreOrderReactService CreateStoreOrderService(
        string? username = null,
        IInvoiceEmailService? invoiceEmailService = null
    )
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = CreateUser(username),
            },
        };

        return new StoreOrderReactService(
            CreateSqlSugarContext(_db),
            NullLogger<StoreOrderReactService>.Instance,
            httpContextAccessor,
            Mock.Of<IOrderNumberGenerator>(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IMapper>(),
            invoiceEmailService ?? Mock.Of<IInvoiceEmailService>()
        );
    }

    private static StoreOrderInvoiceEmailJobService CreateInvoiceEmailJobService(
        Mock<IStoreOrderInvoiceAttachmentService> attachmentService,
        Mock<IInvoiceEmailService> invoiceEmailService
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton(attachmentService.Object);
        services.AddSingleton(invoiceEmailService.Object);
        var provider = services.BuildServiceProvider();

        return new StoreOrderInvoiceEmailJobService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StoreOrderInvoiceEmailJobService>.Instance
        );
    }

    private static StoreOrderCartDto CreateInvoiceOrder()
    {
        return new StoreOrderCartDto
        {
            OrderGUID = "order-1",
            OrderNo = "SO001",
            StoreCode = "S001",
            StoreAddress = "1 Test Street",
            StoreContactEmail = "customer@example.com",
            TotalImportAmount = 8.5m,
            ShippingFee = 1.5m,
            Items = new List<StoreOrderCartItemDto>
            {
                new()
                {
                    DetailGUID = "detail-1",
                    ProductCode = "P001",
                    ItemNumber = "HB001",
                    Barcode = "930000000001",
                    ProductName = "Test Product",
                    Quantity = 2m,
                    AllocQuantity = 1m,
                    ImportPrice = 8.5m,
                },
            },
        };
    }

    private static string ExtractPdfText(byte[] pdfBytes)
    {
        using var reader = new PdfReader(pdfBytes);
        return string.Join(
            "\n",
            Enumerable
                .Range(1, reader.NumberOfPages)
                .Select(page => System.Text.Encoding.Latin1.GetString(reader.GetPageContent(page)))
        );
    }

    private static bool HasPdfImageXObject(byte[] pdfBytes)
    {
        using var reader = new PdfReader(pdfBytes);
        for (var page = 1; page <= reader.NumberOfPages; page++)
        {
            var resources = reader.GetPageN(page).GetAsDict(new PdfName("Resources"));
            var xObjects = resources?.GetAsDict(new PdfName("XObject"));
            if (xObjects == null)
            {
                continue;
            }

            foreach (var name in xObjects.Keys)
            {
                var xObject = PdfReader.GetPdfObject(xObjects.Get(name)) as PdfDictionary;
                if (new PdfName("Image").Equals(xObject?.GetAsName(new PdfName("Subtype"))))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static async Task<StoreOrderInvoiceEmailJobDto> WaitForInvoiceEmailJobAsync(
        StoreOrderInvoiceEmailJobService jobService,
        string jobId
    )
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var job = await jobService.GetJobAsync(jobId);
            if (
                job?.Status == StoreOrderInvoiceEmailJobStatusConstants.Succeeded
                || job?.Status == StoreOrderInvoiceEmailJobStatusConstants.Failed
            )
            {
                return job;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("发票邮件发送 job 未在测试时间内完成");
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        var dbField = typeof(SqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, db);
        return context;
    }

    private IInvoiceEmailSettingsService CreateSettingsService(InvoiceEmailOptions? fallback = null)
    {
        return new InvoiceEmailSettingsService(
            CreateSqlSugarContext(_db),
            Options.Create(fallback ?? new InvoiceEmailOptions()),
            DataProtectionProvider.Create("StoreOrderContactAndInvoiceTests"),
            NullLogger<InvoiceEmailSettingsService>.Instance
        );
    }

    private sealed class TestableInvoiceEmailService : InvoiceEmailService
    {
        public TestableInvoiceEmailService(IInvoiceEmailSettingsService settingsService)
            : base(NullLogger<InvoiceEmailService>.Instance, settingsService)
        {
            SettingsService = settingsService;
        }

        public SmtpClient CreateConfiguredClientForTest()
        {
            var options = SettingsService.GetEffectiveOptionsAsync().GetAwaiter().GetResult();
            return CreateSmtpClient(options);
        }

        public MimeKit.MimeMessage BuildMimeMessageForTest(StoreOrderInvoiceEmailMessage message)
        {
            return BuildMimeMessage(message);
        }

        private IInvoiceEmailSettingsService SettingsService { get; }
    }

    private sealed class TlsFailingInvoiceEmailService : InvoiceEmailService
    {
        public TlsFailingInvoiceEmailService(IInvoiceEmailSettingsService settingsService)
            : base(NullLogger<InvoiceEmailService>.Instance, settingsService)
        {
        }

        protected override Task ConnectSmtpClientAsync(
            SmtpClient smtpClient,
            InvoiceEmailOptions options,
            SecureSocketOptions secureSocketOptions
        )
        {
            throw new SslHandshakeException("handshake failed");
        }
    }

    private sealed class SendingCaptureInvoiceEmailService : InvoiceEmailService
    {
        public SendingCaptureInvoiceEmailService(IInvoiceEmailSettingsService settingsService)
            : base(NullLogger<InvoiceEmailService>.Instance, settingsService)
        {
        }

        public string? ConnectedHost { get; private set; }
        public int ConnectedPort { get; private set; }
        public SecureSocketOptions ConnectedSocketOptions { get; private set; }
        public string? AuthenticatedUsername { get; private set; }
        public string? AuthenticatedPassword { get; private set; }
        public bool? CreatedClientRevocationCheck { get; private set; }
        public MimeKit.MimeMessage? SentMessage { get; private set; }

        protected override SmtpClient CreateSmtpClient(InvoiceEmailOptions options)
        {
            var client = base.CreateSmtpClient(options);
            CreatedClientRevocationCheck = client.CheckCertificateRevocation;
            return client;
        }

        protected override Task ConnectSmtpClientAsync(
            SmtpClient smtpClient,
            InvoiceEmailOptions options,
            SecureSocketOptions secureSocketOptions
        )
        {
            ConnectedHost = options.Host;
            ConnectedPort = options.Port;
            ConnectedSocketOptions = secureSocketOptions;
            return Task.CompletedTask;
        }

        protected override Task AuthenticateSmtpClientAsync(
            SmtpClient smtpClient,
            string username,
            string password
        )
        {
            AuthenticatedUsername = username;
            AuthenticatedPassword = password;
            return Task.CompletedTask;
        }

        protected override Task SendSmtpMessageAsync(SmtpClient smtpClient, MimeKit.MimeMessage email)
        {
            SentMessage = email;
            return Task.CompletedTask;
        }

        protected override Task DisconnectSmtpClientAsync(SmtpClient smtpClient)
        {
            return Task.CompletedTask;
        }
    }

    private async Task SeedStoreOrderGraphAsync()
    {
        await _db.Insertable(
            new Store
            {
                StoreGUID = "store-1",
                StoreCode = "S001",
                StoreName = "Test Store",
                Address = "1 Test Street",
                ContactEmail = "store@example.com",
                Phone = "123456",
                IsActive = true,
            }
        ).ExecuteCommandAsync();

        await _db.Insertable(
            new WareHouseOrder
            {
                OrderGUID = "order-1",
                OrderNo = "SO-001",
                StoreCode = "S001",
                FlowStatus = 1,
                OrderDate = new DateTime(2026, 6, 4),
                OEMTotalAmount = 25m,
                ShippingFee = 2m,
                Remarks = "test",
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        await _db.Insertable(
            new Product
            {
                ProductCode = "P001",
                ItemNumber = "ITEM-001",
                Barcode = "BAR-001",
                ProductName = "Product 1",
                ProductImage = "image.png",
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        await _db.Insertable(
            new WarehouseProduct
            {
                ProductCode = "P001",
                IsActive = true,
                IsDeleted = false,
                MinOrderQuantity = 1,
                ImportPrice = 3m,
            }
        ).ExecuteCommandAsync();

        await _db.Insertable(
            new DomesticProduct
            {
                ProductCode = "P001",
                UnitVolume = 1.5m,
                PackingQuantity = 3,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        await _db.Insertable(
            new WareHouseOrderDetails
            {
                DetailGUID = "detail-1",
                OrderGUID = "order-1",
                StoreCode = "S001",
                ProductCode = "P001",
                Quantity = 5,
                AllocQuantity = 4,
                OEMPrice = 5m,
                OEMAmount = 25m,
                ImportPrice = 3m,
                ImportAmount = 12m,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private static ClaimsPrincipal CreateUser(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        return new ClaimsPrincipal(
            new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                    new Claim(ClaimTypes.Name, username),
                },
                "TestAuth"
            )
        );
    }
}
