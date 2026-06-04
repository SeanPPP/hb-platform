using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using ClosedXML.Excel;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using Xunit;

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
            typeof(UserStore)
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
            Options.Create(new InvoiceEmailOptions())
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
            Options.Create(new InvoiceEmailOptions
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
    public void InvoiceEmailOptions_DefaultsToCheckingCertificateRevocation()
    {
        var options = new InvoiceEmailOptions();

        Assert.True(options.CheckCertificateRevocation);
    }

    [Fact]
    public void InvoiceEmailService_CreateSmtpClient_AppliesRevocationOption()
    {
        var service = new TestableInvoiceEmailService(
            Options.Create(new InvoiceEmailOptions
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
            Options.Create(new InvoiceEmailOptions
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

    private sealed class TestableInvoiceEmailService : InvoiceEmailService
    {
        public TestableInvoiceEmailService(IOptions<InvoiceEmailOptions> options)
            : base(NullLogger<InvoiceEmailService>.Instance, options)
        {
        }

        public SmtpClient CreateConfiguredClientForTest()
        {
            return CreateSmtpClient();
        }

        public MimeKit.MimeMessage BuildMimeMessageForTest(StoreOrderInvoiceEmailMessage message)
        {
            return BuildMimeMessage(message);
        }
    }

    private sealed class TlsFailingInvoiceEmailService : InvoiceEmailService
    {
        public TlsFailingInvoiceEmailService(IOptions<InvoiceEmailOptions> options)
            : base(NullLogger<InvoiceEmailService>.Instance, options)
        {
        }

        protected override Task ConnectSmtpClientAsync(
            SmtpClient smtpClient,
            SecureSocketOptions secureSocketOptions
        )
        {
            throw new SslHandshakeException("handshake failed");
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
