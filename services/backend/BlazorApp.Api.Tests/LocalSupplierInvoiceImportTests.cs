using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using ClosedXML.Excel;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public sealed class LocalSupplierInvoiceImportTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _sqliteConnection;
        private readonly SqlSugarClient _db;

        public LocalSupplierInvoiceImportTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
            _sqliteConnection = new SqliteConnection($"Data Source={_dbPath}");
            _sqliteConnection.Open();

            _db = new SqlSugarClient(
                new ConnectionConfig
                {
                    ConnectionString = _sqliteConnection.ConnectionString,
                    DbType = DbType.Sqlite,
                    IsAutoCloseConnection = false,
                    InitKeyType = InitKeyType.Attribute,
                    MoreSettings = new ConnMoreSettings(),
                }
            );

            _db.CodeFirst.InitTables(
                typeof(Store),
                typeof(HBLocalSupplier),
                typeof(StoreLocalSupplierInvoice),
                typeof(StoreLocalSupplierInvoiceDetails)
            );
        }

        [Fact]
        public async Task PreviewAsync_XlsxPreview_ReturnsSourceColumnsRecommendedMappingAndLines()
        {
            await SeedStoreAndSupplierAsync();
            var service = CreateImportService();
            var file = CreateExcelFile(
                "invoice.xlsx",
                workbook =>
                {
                    var sheet = workbook.AddWorksheet("Sheet1");
                    sheet.Cell(1, 1).Value = "分店代码";
                    sheet.Cell(1, 2).Value = "S01";
                    sheet.Cell(2, 1).Value = "供应商代码";
                    sheet.Cell(2, 2).Value = "SUP01";
                    sheet.Cell(3, 1).Value = "单号";
                    sheet.Cell(3, 2).Value = "INV-IMPORT-001";
                    sheet.Cell(5, 1).Value = "货号";
                    sheet.Cell(5, 2).Value = "条码";
                    sheet.Cell(5, 3).Value = "名称";
                    sheet.Cell(5, 4).Value = "数量";
                    sheet.Cell(5, 5).Value = "价格";
                    sheet.Cell(6, 1).Value = "ITEM-001";
                    sheet.Cell(6, 2).Value = "9300000000012";
                    sheet.Cell(6, 3).Value = "测试商品一";
                    sheet.Cell(6, 4).Value = 2;
                    sheet.Cell(6, 5).Value = 3.50m;
                }
            );

            var result = await service.PreviewAsync(file);

            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.Data);
            Assert.Equal("S01", result.Data!.Header.StoreCode);
            Assert.Equal("SUP01", result.Data.Header.SupplierCode);
            Assert.Equal("INV-IMPORT-001", result.Data.Header.InvoiceNo);
            Assert.Equal("col_1", result.Data.RecommendedMapping.ItemNumberColumnKey);
            Assert.Equal("col_2", result.Data.RecommendedMapping.BarcodeColumnKey);
            Assert.Equal("col_3", result.Data.RecommendedMapping.ProductNameColumnKey);
            Assert.Equal("col_4", result.Data.RecommendedMapping.QuantityColumnKey);
            Assert.Equal("col_5", result.Data.RecommendedMapping.PriceColumnKey);
            Assert.Single(result.Data.Lines);
            Assert.Equal("ITEM-001", result.Data.Lines[0].RawValues["col_1"]);
            Assert.Equal(LocalSupplierInvoiceImportMatchStatus.Matched, result.Data.Header.StoreMatch.Status);
            Assert.Equal(
                LocalSupplierInvoiceImportMatchStatus.Matched,
                result.Data.Header.SupplierMatch.Status
            );
        }

        [Fact]
        public async Task PreviewAsync_XlsFile_ReturnsExpectedError()
        {
            var service = CreateImportService();
            var file = CreateFormFile("legacy.xls", new byte[] { 1, 2, 3 });

            var result = await service.PreviewAsync(file);

            Assert.False(result.Success);
            Assert.Equal("旧版 Excel 不支持，请另存为 .xlsx 后上传", result.Message);
            Assert.Equal("UNSUPPORTED_XLS", result.ErrorCode);
        }

        [Fact]
        public async Task PreviewAsync_TextPdf_ReturnsSourceColumnsRecommendedMappingAndLines()
        {
            await SeedStoreAndSupplierAsync();
            var service = CreateImportService();
            var file = CreatePdfFile(
                "invoice.pdf",
                (document, _) =>
                {
                    document.Add(new Paragraph("分店代码：S01"));
                    document.Add(new Paragraph("供应商代码：SUP01"));
                    document.Add(new Paragraph("单号：INV-PDF-001"));
                    document.Add(new Paragraph("货号    条码    名称    数量    价格"));
                    document.Add(new Paragraph("ITEM-PDF-001    9300000000123    测试PDF商品    3    4.50"));
                }
            );

            var result = await service.PreviewAsync(file);

            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.Data);
            Assert.Equal("S01", result.Data!.Header.StoreCode);
            Assert.Equal("SUP01", result.Data.Header.SupplierCode);
            Assert.Equal("INV-PDF-001", result.Data.Header.InvoiceNo);
            Assert.Equal("col_1", result.Data.RecommendedMapping.ItemNumberColumnKey);
            Assert.Equal("col_2", result.Data.RecommendedMapping.BarcodeColumnKey);
            Assert.Equal("col_3", result.Data.RecommendedMapping.ProductNameColumnKey);
            Assert.Equal("col_4", result.Data.RecommendedMapping.QuantityColumnKey);
            Assert.Equal("col_5", result.Data.RecommendedMapping.PriceColumnKey);
            Assert.Single(result.Data.Lines);
            Assert.Equal("ITEM-PDF-001", result.Data.Lines[0].RawValues["col_1"]);
            Assert.Equal("9300000000123", result.Data.Lines[0].RawValues["col_2"]);
        }

        [Fact]
        public async Task PreviewAsync_InvalidXlsx_ReturnsStableParseError()
        {
            var service = CreateImportService();
            var file = CreateFormFile("broken.xlsx", new byte[] { 1, 2, 3 });

            var result = await service.PreviewAsync(file);

            Assert.False(result.Success);
            Assert.Equal("IMPORT_PREVIEW_ERROR", result.ErrorCode);
            Assert.Equal("文件解析失败，请检查文件格式或重新导出后再上传", result.Message);
        }

        [Fact]
        public async Task PreviewAsync_WhenXlsxHasTooManyRows_ReturnsExpectedError()
        {
            var service = CreateImportService();
            var file = CreateExcelFile(
                "too-many-rows.xlsx",
                workbook =>
                {
                    var sheet = workbook.AddWorksheet("Sheet1");
                    sheet.Cell(1, 1).Value = "货号";
                    sheet.Cell(1, 2).Value = "条码";
                    sheet.Cell(1, 3).Value = "名称";
                    sheet.Cell(1, 4).Value = "数量";
                    sheet.Cell(1, 5).Value = "价格";
                    for (var index = 0; index < 2001; index++)
                    {
                        var rowNumber = index + 2;
                        sheet.Cell(rowNumber, 1).Value = $"ITEM-{index:D4}";
                        sheet.Cell(rowNumber, 2).Value = $"930000{index:D7}";
                        sheet.Cell(rowNumber, 3).Value = $"测试商品{index:D4}";
                        sheet.Cell(rowNumber, 4).Value = 1;
                        sheet.Cell(rowNumber, 5).Value = 2.5m;
                    }
                }
            );

            var result = await service.PreviewAsync(file);

            Assert.False(result.Success);
            Assert.Equal("TOO_MANY_LINES", result.ErrorCode);
            Assert.Equal("导入明细不能超过 2000 行", result.Message);
        }

        [Fact]
        public async Task ConfirmAsync_WhenRequiredMappingMissing_ReturnsValidationError()
        {
            await SeedStoreAndSupplierAsync();
            var service = CreateImportService();

            var result = await service.ConfirmAsync(
                new LocalSupplierInvoiceImportConfirmRequest
                {
                    SourceColumns = CreateSourceColumns(),
                    Header = new LocalSupplierInvoiceImportHeaderDto
                    {
                        StoreCode = "S01",
                        SupplierCode = "SUP01",
                        InvoiceNo = "INV-CONFIRM-001",
                    },
                    Mapping = new LocalSupplierInvoiceImportConfirmMappingDto
                    {
                        ItemNumberColumnKey = "col_1",
                        BarcodeColumnKey = "col_2",
                        ProductNameColumnKey = "col_3",
                        QuantityColumnKey = "col_4",
                        PriceColumnKey = string.Empty,
                    },
                    Lines = CreateConfirmLines()
                }
            );

            Assert.False(result.Success);
            Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
            var detailMessages = Assert.IsType<List<string>>(result.Details);
            Assert.Contains("价格字段映射为必填", detailMessages);
        }

        [Fact]
        public async Task ConfirmAsync_WhenMappingsPointToSameColumn_ReturnsValidationError()
        {
            await SeedStoreAndSupplierAsync();
            var service = CreateImportService();

            var result = await service.ConfirmAsync(
                new LocalSupplierInvoiceImportConfirmRequest
                {
                    SourceColumns = CreateSourceColumns(),
                    Header = new LocalSupplierInvoiceImportHeaderDto
                    {
                        StoreCode = "S01",
                        SupplierCode = "SUP01",
                        InvoiceNo = "INV-CONFIRM-DUP-MAPPING",
                    },
                    Mapping = new LocalSupplierInvoiceImportConfirmMappingDto
                    {
                        ItemNumberColumnKey = "col_1",
                        BarcodeColumnKey = "col_2",
                        ProductNameColumnKey = "col_3",
                        QuantityColumnKey = "col_4",
                        PriceColumnKey = "col_4",
                    },
                    Lines = CreateConfirmLines()
                }
            );

            Assert.False(result.Success);
            Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
            var detailMessages = Assert.IsType<List<string>>(result.Details);
            Assert.Contains("货号、条码、名称、数量、价格必须分别选择不同来源列", detailMessages);
        }

        [Fact]
        public async Task ConfirmAsync_WhenBarcodeValueMissing_ReturnsValidationError()
        {
            await SeedStoreAndSupplierAsync();
            var service = CreateImportService();
            var lines = CreateConfirmLines();
            lines[0].RawValues["col_2"] = string.Empty;

            var result = await service.ConfirmAsync(
                new LocalSupplierInvoiceImportConfirmRequest
                {
                    SourceColumns = CreateSourceColumns(),
                    Header = new LocalSupplierInvoiceImportHeaderDto
                    {
                        StoreCode = "S01",
                        SupplierCode = "SUP01",
                        InvoiceNo = "INV-CONFIRM-MISSING-BARCODE",
                    },
                    Mapping = CreateConfirmMapping(),
                    Lines = lines
                }
            );

            Assert.False(result.Success);
            Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
            var detailMessages = Assert.IsType<List<string>>(result.Details);
            Assert.Contains("第 2 行缺少条码", detailMessages);
        }

        [Fact]
        public async Task ConfirmAsync_WhenQuantityExceedsIntRange_ReturnsValidationError()
        {
            await SeedStoreAndSupplierAsync();
            var service = CreateImportService();
            var lines = CreateConfirmLines();
            lines[0].RawValues["col_4"] = "999999999999999999999";

            var result = await service.ConfirmAsync(
                new LocalSupplierInvoiceImportConfirmRequest
                {
                    SourceColumns = CreateSourceColumns(),
                    Header = new LocalSupplierInvoiceImportHeaderDto
                    {
                        StoreCode = "S01",
                        SupplierCode = "SUP01",
                        InvoiceNo = "INV-CONFIRM-QTY-OVERFLOW",
                    },
                    Mapping = CreateConfirmMapping(),
                    Lines = lines
                }
            );

            Assert.False(result.Success);
            Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
            var detailMessages = Assert.IsType<List<string>>(result.Details);
            Assert.Contains("第 2 行数量无效: 999999999999999999999", detailMessages);
        }

        [Fact]
        public async Task ConfirmAsync_WhenInvoiceNoDuplicated_DoesNotCreateNewInvoice()
        {
            await SeedStoreAndSupplierAsync();
            await _db.Insertable(
                new StoreLocalSupplierInvoice
                {
                    InvoiceGUID = "existing-invoice",
                    StoreCode = "S01",
                    SupplierCode = "SUP01",
                    InvoiceNo = "INV-DUPLICATE",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    IsDeleted = false,
                }
            ).ExecuteCommandAsync();

            var service = CreateImportService();
            var beforeCount = await _db.Queryable<StoreLocalSupplierInvoice>().CountAsync();

            var result = await service.ConfirmAsync(
                new LocalSupplierInvoiceImportConfirmRequest
                {
                    SourceColumns = CreateSourceColumns(),
                    Header = new LocalSupplierInvoiceImportHeaderDto
                    {
                        StoreCode = "S01",
                        SupplierCode = "SUP01",
                        InvoiceNo = "INV-DUPLICATE",
                    },
                    Mapping = CreateConfirmMapping(),
                    Lines = CreateConfirmLines()
                }
            );

            var afterCount = await _db.Queryable<StoreLocalSupplierInvoice>().CountAsync();

            Assert.False(result.Success);
            Assert.Equal("DUPLICATE_INVOICE", result.ErrorCode);
            Assert.Equal(beforeCount, afterCount);
        }

        [Fact]
        public async Task CreateAsync_WhenHeaderTotalUpdateFails_RollsBackHeaderAndDetails()
        {
            await SeedStoreAndSupplierAsync();
            await _db.Ado.ExecuteCommandAsync(
                """
                CREATE TRIGGER trg_local_supplier_invoice_total_fail
                BEFORE UPDATE ON StoreLocalSupplierInvoice
                WHEN NEW.InvoiceNo = 'INV-ROLLBACK'
                BEGIN
                    SELECT RAISE(ABORT, 'forced total update failure');
                END;
                """
            );

            var service = CreateInvoiceService();
            var result = await service.CreateAsync(
                new CreateInvoiceRequest
                {
                    StoreCode = "S01",
                    SupplierCode = "SUP01",
                    InvoiceNo = "INV-ROLLBACK",
                    Items =
                    {
                        new PastedDetailItem
                        {
                            ItemNumber = "ITEM-ROLLBACK",
                            Barcode = "9300000000099",
                            ProductName = "回滚商品",
                            Quantity = 2,
                            Price = 4.25m,
                        }
                    }
                }
            );

            var headerCount = await _db.Queryable<StoreLocalSupplierInvoice>()
                .Where(invoice => invoice.InvoiceNo == "INV-ROLLBACK")
                .CountAsync();
            var detailCount = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .Where(detail => detail.ItemNumber == "ITEM-ROLLBACK")
                .CountAsync();

            Assert.False(result.Success);
            Assert.Equal(0, headerCount);
            Assert.Equal(0, detailCount);
        }

        [Fact]
        public async Task PreviewAsync_WhenPdfHasNoTextAndOcrIsNotConfigured_ReturnsError()
        {
            var service = CreateImportService();
            var file = CreatePdfFile(
                "empty.pdf",
                (document, writer) =>
                {
                    document.NewPage();
                    writer.DirectContent.Rectangle(10, 10, 20, 20);
                    writer.DirectContent.Stroke();
                }
            );

            var result = await service.PreviewAsync(file);

            Assert.False(result.Success);
            Assert.Equal("OCR_NOT_CONFIGURED", result.ErrorCode);
            Assert.Equal("PDF 文本为空，且未配置 OCR 服务，无法继续导入", result.Message);
        }

        private async Task SeedStoreAndSupplierAsync()
        {
            await _db.Insertable(
                new Store
                {
                    StoreGUID = "store-guid-1",
                    StoreCode = "S01",
                    StoreName = "Sydney Store",
                    IsDeleted = false,
                }
            ).ExecuteCommandAsync();

            await _db.Insertable(
                new HBLocalSupplier
                {
                    Guid = "supplier-guid-1",
                    LocalSupplierCode = "SUP01",
                    Name = "Local Supplier",
                    IsDeleted = false,
                }
            ).ExecuteCommandAsync();
        }

        private LocalSupplierInvoiceImportService CreateImportService()
        {
            return new LocalSupplierInvoiceImportService(
                CreateSqlSugarContext(_db),
                CreateInvoiceService(),
                new NoopLocalSupplierInvoiceOcrService(),
                NullLogger<LocalSupplierInvoiceImportService>.Instance
            );
        }

        private LocalSupplierInvoicesReactService CreateInvoiceService()
        {
            var autoPricing = new Mock<IAutoPricingService>();
            autoPricing.Setup(x => x.GetAllActiveStrategiesAsync())
                .ReturnsAsync(new List<BlazorApp.Shared.Models.HBweb.PricingStrategy>());
            autoPricing.Setup(
                    x =>
                        x.FindStrategyForPriceAsync(
                            It.IsAny<decimal>(),
                            It.IsAny<string?>(),
                            It.IsAny<string?>()
                        )
                )
                .ReturnsAsync((BlazorApp.Shared.Models.HBweb.PricingStrategy?)null);
            autoPricing.Setup(x => x.CalculateRate(It.IsAny<decimal>(), It.IsAny<BlazorApp.Shared.Models.HBweb.PricingStrategy?>()))
                .Returns(250m);
            autoPricing.Setup(x => x.CalculateRetailPrice(It.IsAny<decimal>(), It.IsAny<BlazorApp.Shared.Models.HBweb.PricingStrategy?>()))
                .Returns<decimal, BlazorApp.Shared.Models.HBweb.PricingStrategy?>((price, _) => price * 2.5m);

            return new LocalSupplierInvoicesReactService(
                CreateSqlSugarContext(_db),
                CreateHqSqlSugarContext(),
                Mock.Of<IMapper>(),
                NullLogger<LocalSupplierInvoicesReactService>.Instance,
                autoPricing.Object
            );
        }

        private static List<LocalSupplierInvoiceImportSourceColumnDto> CreateSourceColumns()
        {
            return
            [
                new LocalSupplierInvoiceImportSourceColumnDto { Key = "col_1", Header = "货号" },
                new LocalSupplierInvoiceImportSourceColumnDto { Key = "col_2", Header = "条码" },
                new LocalSupplierInvoiceImportSourceColumnDto { Key = "col_3", Header = "名称" },
                new LocalSupplierInvoiceImportSourceColumnDto { Key = "col_4", Header = "数量" },
                new LocalSupplierInvoiceImportSourceColumnDto { Key = "col_5", Header = "价格" }
            ];
        }

        private static LocalSupplierInvoiceImportConfirmMappingDto CreateConfirmMapping()
        {
            return new LocalSupplierInvoiceImportConfirmMappingDto
            {
                ItemNumberColumnKey = "col_1",
                BarcodeColumnKey = "col_2",
                ProductNameColumnKey = "col_3",
                QuantityColumnKey = "col_4",
                PriceColumnKey = "col_5",
            };
        }

        private static List<LocalSupplierInvoiceImportLineDto> CreateConfirmLines()
        {
            return
            [
                new LocalSupplierInvoiceImportLineDto
                {
                    RowNumber = 2,
                    RawValues = new Dictionary<string, string?>
                    {
                        ["col_1"] = "ITEM-001",
                        ["col_2"] = "9300000000012",
                        ["col_3"] = "测试商品一",
                        ["col_4"] = "2",
                        ["col_5"] = "3.50"
                    }
                }
            ];
        }

        private static FormFile CreateExcelFile(string fileName, Action<XLWorkbook> buildWorkbook)
        {
            using var workbook = new XLWorkbook();
            buildWorkbook(workbook);
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return CreateFormFile(fileName, stream.ToArray());
        }

        private static FormFile CreatePdfFile(
            string fileName,
            Action<Document, PdfWriter> writeDocument
        )
        {
            using var stream = new MemoryStream();
            using (var document = new Document())
            {
                var writer = PdfWriter.GetInstance(document, stream);
                document.Open();
                writeDocument(document, writer);
                document.Close();
            }

            return CreateFormFile(fileName, stream.ToArray());
        }

        private static FormFile CreateFormFile(string fileName, byte[] bytes)
        {
            var stream = new MemoryStream(bytes);
            return new FormFile(stream, 0, stream.Length, "file", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/octet-stream",
            };
        }

        private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
        {
            var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(
                typeof(SqlSugarContext)
            );

            var dbField = typeof(SqlSugarContext).GetField(
                "_db",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            dbField!.SetValue(context, db);

            return context;
        }

        private static HqSqlSugarContext CreateHqSqlSugarContext()
        {
            var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
                typeof(HqSqlSugarContext)
            );

            var dbField = typeof(HqSqlSugarContext).GetField(
                "_db",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            dbField!.SetValue(context, new Mock<ISqlSugarClient>().Object);

            return context;
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
    }
}
