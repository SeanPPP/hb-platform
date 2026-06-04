using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using ClosedXML.Excel;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 分店订货发票邮件附件生成服务。
    /// </summary>
    public class StoreOrderInvoiceAttachmentService : IStoreOrderInvoiceAttachmentService
    {
        private const string PdfContentType = "application/pdf";
        private const string ExcelContentType =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        private readonly IStoreOrderReactService _storeOrderService;
        private readonly ILogger<StoreOrderInvoiceAttachmentService> _logger;

        public StoreOrderInvoiceAttachmentService(
            IStoreOrderReactService storeOrderService,
            ILogger<StoreOrderInvoiceAttachmentService> logger
        )
        {
            _storeOrderService = storeOrderService;
            _logger = logger;
        }

        public async Task<ApiResponse<StoreOrderInvoiceAttachmentBundle>> GenerateAttachmentsAsync(
            string orderGuid,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                var normalizedOrderGuid = orderGuid.Trim();
                var orderResult = await _storeOrderService.GetOrderDetailFullAsync(
                    normalizedOrderGuid
                );
                if (!orderResult.Success || orderResult.Data == null)
                {
                    return ApiResponse<StoreOrderInvoiceAttachmentBundle>.Error(
                        string.IsNullOrWhiteSpace(orderResult.Message)
                            ? "订单不存在"
                            : orderResult.Message,
                        "STORE_ORDER_INVOICE_ATTACHMENT_ORDER_NOT_FOUND"
                    );
                }

                var order = orderResult.Data;
                var baseFileName = BuildBaseFileName(order);
                var pdfBytes = GeneratePdf(order);
                var excelBytes = GenerateExcel(order);

                return ApiResponse<StoreOrderInvoiceAttachmentBundle>.OK(
                    new StoreOrderInvoiceAttachmentBundle
                    {
                        OrderGUID = order.OrderGUID,
                        OrderNo = order.OrderNo,
                        StoreCode = order.StoreCode,
                        Attachments = new List<StoreOrderInvoiceEmailAttachment>
                        {
                            new()
                            {
                                FileName = $"{baseFileName}.pdf",
                                ContentType = PdfContentType,
                                Bytes = pdfBytes,
                            },
                            new()
                            {
                                FileName = $"{baseFileName}.xlsx",
                                ContentType = ExcelContentType,
                                Bytes = excelBytes,
                            },
                        },
                    },
                    "发票附件生成成功"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成分店订货发票邮件附件失败: {OrderGuid}", orderGuid);
                return ApiResponse<StoreOrderInvoiceAttachmentBundle>.Error(
                    "生成发票附件失败",
                    "STORE_ORDER_INVOICE_ATTACHMENT_GENERATION_FAILED"
                );
            }
        }

        private static byte[] GenerateExcel(StoreOrderCartDto order)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Invoice");
            worksheet.Cell(1, 1).Value = "#";
            worksheet.Cell(1, 2).Value = "Item No";
            worksheet.Cell(1, 3).Value = "Name";
            worksheet.Cell(1, 4).Value = "Barcode";
            worksheet.Cell(1, 5).Value = "Cost";
            worksheet.Cell(1, 6).Value = "Order Qty";
            worksheet.Cell(1, 7).Value = "Ship Qty";
            worksheet.Cell(1, 8).Value = "Subtotal";
            worksheet.Range(1, 1, 1, 8).Style.Font.Bold = true;

            var rowIndex = 2;
            foreach (var item in SortItems(order.Items))
            {
                var orderQuantity = item.Quantity;
                var allocQuantity = item.AllocQuantity ?? 0m;
                var subtotal = decimal.Round(allocQuantity * item.ImportPrice, 2);

                worksheet.Cell(rowIndex, 1).Value = rowIndex - 1;
                worksheet.Cell(rowIndex, 2).Value = item.ItemNumber ?? string.Empty;
                worksheet.Cell(rowIndex, 3).Value = item.ProductName ?? string.Empty;
                worksheet.Cell(rowIndex, 4).Value = item.Barcode ?? item.ProductCode;
                worksheet.Cell(rowIndex, 5).Value = item.ImportPrice;
                worksheet.Cell(rowIndex, 6).Value = orderQuantity;
                worksheet.Cell(rowIndex, 7).Value = allocQuantity;
                worksheet.Cell(rowIndex, 8).Value = subtotal;
                rowIndex += 1;
            }

            var subTotal = order.TotalImportAmount;
            var gst = decimal.Round(subTotal * 0.1m, 2);
            var freight = order.ShippingFee ?? 0m;
            var total = decimal.Round(subTotal + gst + freight, 2);

            rowIndex += 1;
            worksheet.Cell(rowIndex++, 3).Value = "Sub-Total:";
            worksheet.Cell(rowIndex - 1, 8).Value = subTotal;
            worksheet.Cell(rowIndex++, 3).Value = "GST 10%:";
            worksheet.Cell(rowIndex - 1, 8).Value = gst;
            worksheet.Cell(rowIndex++, 3).Value = "Freight:";
            worksheet.Cell(rowIndex - 1, 8).Value = freight;
            worksheet.Cell(rowIndex++, 3).Value = "Total:";
            worksheet.Cell(rowIndex - 1, 8).Value = total;
            worksheet.Cell(rowIndex + 1, 3).Value = "Remarks";
            worksheet.Cell(rowIndex + 1, 4).Value = string.IsNullOrWhiteSpace(order.Remarks)
                ? "Image for reference only, actual product may vary"
                : order.Remarks.Trim();

            worksheet.Columns().AdjustToContents();
            worksheet.Column(5).Style.NumberFormat.Format = "$#,##0.00";
            worksheet.Column(8).Style.NumberFormat.Format = "$#,##0.00";

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static byte[] GeneratePdf(StoreOrderCartDto order)
        {
            using var stream = new MemoryStream();
            using var document = new Document(PageSize.A4.Rotate(), 24, 24, 24, 24);
            PdfWriter.GetInstance(document, stream);
            document.Open();

            var baseFont = BaseFont.CreateFont("STSong-Light", "UniGB-UCS2-H", BaseFont.NOT_EMBEDDED);
            var titleFont = new iTextSharp.text.Font(baseFont, 16, iTextSharp.text.Font.BOLD);
            var headerFont = new iTextSharp.text.Font(baseFont, 9, iTextSharp.text.Font.BOLD);
            var bodyFont = new iTextSharp.text.Font(baseFont, 8, iTextSharp.text.Font.NORMAL);
            var boldFont = new iTextSharp.text.Font(baseFont, 8, iTextSharp.text.Font.BOLD);

            var title = new Paragraph($"INVOICE NO. {order.OrderNo ?? order.OrderGUID}", titleFont)
            {
                Alignment = Element.ALIGN_CENTER,
                SpacingAfter = 12,
            };
            document.Add(title);

            var infoTable = new PdfPTable(4) { WidthPercentage = 100, SpacingAfter = 10 };
            infoTable.SetWidths(new float[] { 1.2f, 2.2f, 1.2f, 2.2f });
            AddLabelCell(infoTable, "CUSTOMER:", boldFont);
            AddValueCell(infoTable, order.StoreCode ?? "--", bodyFont);
            AddLabelCell(infoTable, "INVOICE DATE:", boldFont);
            AddValueCell(infoTable, DateTime.Now.ToString("yyyy/M/d"), bodyFont);
            AddLabelCell(infoTable, "CUSTOMER CONTACT:", boldFont);
            AddValueCell(infoTable, order.StoreContactEmail ?? "--", bodyFont);
            AddLabelCell(infoTable, "ADDRESS:", boldFont);
            AddValueCell(infoTable, order.StoreAddress ?? "--", bodyFont);
            document.Add(infoTable);

            var detailTable = new PdfPTable(8) { WidthPercentage = 100, SpacingAfter = 10 };
            detailTable.SetWidths(new float[] { 0.5f, 1.2f, 1.6f, 3.2f, 1f, 1f, 1f, 1.2f });
            foreach (var header in new[]
                     {
                         "#",
                         "Item No.",
                         "Barcode",
                         "Name",
                         "Cost",
                         "Order Qty",
                         "Ship Qty",
                         "Subtotal",
                     })
            {
                AddHeaderCell(detailTable, header, headerFont);
            }

            var itemIndex = 1;
            foreach (var item in SortItems(order.Items))
            {
                var allocQuantity = item.AllocQuantity ?? 0m;
                var subtotal = decimal.Round(allocQuantity * item.ImportPrice, 2);
                AddValueCell(detailTable, itemIndex.ToString(), bodyFont);
                AddValueCell(detailTable, item.ItemNumber ?? string.Empty, bodyFont);
                AddValueCell(detailTable, item.Barcode ?? item.ProductCode, bodyFont);
                AddValueCell(detailTable, item.ProductName ?? string.Empty, bodyFont);
                AddValueCell(detailTable, FormatCurrency(item.ImportPrice), bodyFont);
                AddValueCell(detailTable, item.Quantity.ToString("0.##"), bodyFont);
                AddValueCell(detailTable, allocQuantity.ToString("0.##"), bodyFont);
                AddValueCell(detailTable, FormatCurrency(subtotal), bodyFont);
                itemIndex += 1;
            }
            document.Add(detailTable);

            var subTotal = order.TotalImportAmount;
            var gst = decimal.Round(subTotal * 0.1m, 2);
            var freight = order.ShippingFee ?? 0m;
            var total = decimal.Round(subTotal + gst + freight, 2);
            var totalTable = new PdfPTable(2)
            {
                WidthPercentage = 36,
                HorizontalAlignment = Element.ALIGN_RIGHT,
                SpacingAfter = 10,
            };
            totalTable.SetWidths(new float[] { 1.5f, 1f });
            AddLabelCell(totalTable, "Sub-Total:", boldFont);
            AddValueCell(totalTable, FormatCurrency(subTotal), bodyFont);
            AddLabelCell(totalTable, "GST 10%:", boldFont);
            AddValueCell(totalTable, FormatCurrency(gst), bodyFont);
            AddLabelCell(totalTable, "Freight:", boldFont);
            AddValueCell(totalTable, FormatCurrency(freight), bodyFont);
            AddLabelCell(totalTable, "Total Before Discount:", boldFont);
            AddValueCell(totalTable, FormatCurrency(total), boldFont);
            document.Add(totalTable);

            document.Add(new Paragraph("PAYMENT DETAIL: DIRECT DEBIT", boldFont));
            document.Add(
                new Paragraph(
                    string.IsNullOrWhiteSpace(order.Remarks)
                        ? "Image for reference only, actual product may vary"
                        : $"REMARKS: {order.Remarks.Trim()}",
                    bodyFont
                )
            );
            document.Close();
            return stream.ToArray();
        }

        private static List<StoreOrderCartItemDto> SortItems(IEnumerable<StoreOrderCartItemDto>? items)
        {
            return (items ?? Enumerable.Empty<StoreOrderCartItemDto>())
                .OrderBy(item => (item.AllocQuantity ?? 0m) == 0m ? 1 : 0)
                .ThenBy(item => item.ItemNumber ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ProductCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string BuildBaseFileName(StoreOrderCartDto order)
        {
            var storePart = SanitizeFileNamePart(order.StoreCode ?? "UnknownStore");
            var orderPart = SanitizeFileNamePart(order.OrderNo ?? order.OrderGUID);
            return $"Invoice_{storePart}_{orderPart}";
        }

        private static string SanitizeFileNamePart(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var cleaned = new string(
                (value ?? string.Empty)
                    .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
                    .ToArray()
            );
            cleaned = string.Join("_", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrWhiteSpace(cleaned) ? "Unknown" : cleaned;
        }

        private static string FormatCurrency(decimal value)
        {
            return $"${value:0.00}";
        }

        private static void AddHeaderCell(PdfPTable table, string text, iTextSharp.text.Font font)
        {
            table.AddCell(new PdfPCell(new Phrase(text, font))
            {
                BackgroundColor = new BaseColor(240, 240, 240),
                Padding = 4,
            });
        }

        private static void AddLabelCell(PdfPTable table, string text, iTextSharp.text.Font font)
        {
            table.AddCell(new PdfPCell(new Phrase(text, font))
            {
                Padding = 4,
                BorderColor = new BaseColor(220, 220, 220),
            });
        }

        private static void AddValueCell(PdfPTable table, string text, iTextSharp.text.Font font)
        {
            table.AddCell(new PdfPCell(new Phrase(text ?? string.Empty, font))
            {
                Padding = 4,
                BorderColor = new BaseColor(220, 220, 220),
            });
        }
    }
}
