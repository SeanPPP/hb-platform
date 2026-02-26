using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using iTextSharp.text;
using iTextSharp.text.pdf;
using QRCoder;
using SqlSugar;
using PdfFont = iTextSharp.text.Font;
using PdfImage = iTextSharp.text.Image;
using PdfRectangle = iTextSharp.text.Rectangle;

namespace BlazorApp.Api.Services
{
    public interface ITaxInvoiceService
    {
        Task<byte[]> GenerateTaxInvoicePdfAsync(string orderGuid);
    }

    public class TaxInvoiceService : ITaxInvoiceService
    {
        private readonly POSMSqlSugarContext _posmContext;
        private readonly SqlSugarContext _context;
        private readonly ILogger<TaxInvoiceService> _logger;

        public TaxInvoiceService(
            POSMSqlSugarContext posmContext,
            SqlSugarContext context,
            ILogger<TaxInvoiceService> logger
        )
        {
            _posmContext = posmContext;
            _context = context;
            _logger = logger;
        }

        public async Task<byte[]> GenerateTaxInvoicePdfAsync(string orderGuid)
        {
            try
            {
                var order = await _posmContext.SalesOrderDb.GetFirstAsync(o =>
                    o.OrderGuid == orderGuid
                );
                if (order == null)
                {
                    throw new ArgumentException($"Order not found: {orderGuid}");
                }

                var orderDetails = await _posmContext.SalesOrderDetailDb.GetListAsync(d =>
                    d.OrderGuid == orderGuid
                );
                var paymentDetails = await _posmContext.PaymentDetailDb.GetListAsync(p =>
                    p.OrderGuid == orderGuid
                );

                CustomerInfo? customer = null;
                if (order.Status == 4 && !string.IsNullOrEmpty(order.Remark))
                {
                    customer = await _posmContext
                        .Db.Queryable<CustomerInfo>()
                        .Where(c => c.CustomerCode == order.Remark)
                        .FirstAsync();
                }

                Store? store = null;
                if (!string.IsNullOrEmpty(order.BranchCode))
                {
                    _logger.LogInformation($"查找分店信息: BranchCode = {order.BranchCode}");
                    store = await _context.StoreDb.GetFirstAsync(s =>
                        s.StoreCode == order.BranchCode && !s.IsDeleted
                    );
                    if (store != null)
                    {
                        _logger.LogInformation(
                            $"找到分店: StoreCode = {store.StoreCode}, StoreName = {store.StoreName}, BrandName = {store.BrandName}, ABN = {store.ABN}, Address = {store.Address}"
                        );
                    }
                    else
                    {
                        _logger.LogWarning($"未找到分店: BranchCode = {order.BranchCode}");
                    }
                }
                else
                {
                    _logger.LogWarning("订单的 BranchCode 为空");
                }

                using var ms = new MemoryStream();
                var document = new Document(PageSize.A4, 50, 50, 50, 50);
                var writer = PdfWriter.GetInstance(document, ms);
                document.Open();

                var blackColor = new BaseColor(0, 0, 0);
                var darkGrayColor = new BaseColor(64, 64, 64);
                var lightGrayColor = new BaseColor(211, 211, 211);

                var titleFont = FontFactory.GetFont(
                    FontFactory.HELVETICA_BOLD,
                    18,
                    PdfFont.NORMAL,
                    blackColor
                );
                var headerFont = FontFactory.GetFont(
                    FontFactory.HELVETICA_BOLD,
                    12,
                    PdfFont.NORMAL,
                    blackColor
                );
                var bodyFont = FontFactory.GetFont(
                    FontFactory.HELVETICA,
                    10,
                    PdfFont.NORMAL,
                    darkGrayColor
                );
                var boldFont = FontFactory.GetFont(
                    FontFactory.HELVETICA_BOLD,
                    10,
                    PdfFont.NORMAL,
                    blackColor
                );

                var qrCodeBytes = GenerateQRCodeImage(order.OrderGuid ?? "");

                var titleTable = new PdfPTable(2) { WidthPercentage = 100 };
                titleTable.SetWidths(new float[] { 3, 1 });

                titleTable.AddCell(
                    new PdfPCell(
                        new Paragraph("TAX INVOICE", titleFont) { Alignment = Element.ALIGN_CENTER }
                    )
                    {
                        Border = PdfRectangle.NO_BORDER,
                        VerticalAlignment = Element.ALIGN_MIDDLE,
                    }
                );

                var qrPdfImage = PdfImage.GetInstance(qrCodeBytes);
                qrPdfImage.ScaleAbsolute(100f, 100f);
                titleTable.AddCell(
                    new PdfPCell(qrPdfImage)
                    {
                        Border = PdfRectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        VerticalAlignment = Element.ALIGN_MIDDLE,
                        PaddingTop = 10,
                    }
                );

                document.Add(titleTable);

                _logger.LogInformation(
                    $"准备显示 ABN 和 Address: store = {(store != null ? "存在" : "null")}, ABN = {store?.ABN}, Address = {store?.Address}"
                );

                if (store != null && !string.IsNullOrEmpty(store.BrandName))
                {
                    document.Add(
                        new Paragraph(store.BrandName, titleFont)
                        {
                            Alignment = Element.ALIGN_CENTER,
                        }
                    );
                    _logger.LogInformation($"已显示 BrandName: {store.BrandName}");
                }

                if (store != null && !string.IsNullOrEmpty(store.ABN))
                {
                    document.Add(
                        new Paragraph($"ABN: {store.ABN}", bodyFont)
                        {
                            Alignment = Element.ALIGN_CENTER,
                        }
                    );
                    _logger.LogInformation($"已显示 ABN: {store.ABN}");
                }
                else
                {
                    _logger.LogWarning(
                        $"ABN 未显示: store = {(store != null ? "存在" : "null")}, ABN = {store?.ABN}"
                    );
                }

                if (store != null && !string.IsNullOrEmpty(store.Address))
                {
                    document.Add(
                        new Paragraph($"Address: {store.Address}", bodyFont)
                        {
                            Alignment = Element.ALIGN_CENTER,
                        }
                    );
                    _logger.LogInformation($"已显示 Address: {store.Address}");
                }
                else
                {
                    _logger.LogWarning(
                        $"Address 未显示: store = {(store != null ? "存在" : "null")}, Address = {store?.Address}"
                    );
                }

                document.Add(new Paragraph(" ", bodyFont));
                document.Add(new Paragraph(" ", bodyFont));

                var infoTable = new PdfPTable(2) { WidthPercentage = 100 };
                infoTable.SetWidths(new float[] { 1, 1 });

                infoTable.AddCell(
                    new PdfPCell(new Phrase("Order No:", boldFont))
                    {
                        Border = PdfRectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                    }
                );
                infoTable.AddCell(
                    new PdfPCell(new Phrase(order.OrderGuid ?? "", bodyFont))
                    {
                        Border = PdfRectangle.NO_BORDER,
                    }
                );
                infoTable.AddCell(
                    new PdfPCell(new Phrase("Date:", boldFont))
                    {
                        Border = PdfRectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                    }
                );
                infoTable.AddCell(
                    new PdfPCell(
                        new Phrase(order.OrderTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "", bodyFont)
                    )
                    {
                        Border = PdfRectangle.NO_BORDER,
                    }
                );
                infoTable.AddCell(
                    new PdfPCell(new Phrase("Branch:", boldFont))
                    {
                        Border = PdfRectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                    }
                );
                infoTable.AddCell(
                    new PdfPCell(new Phrase(store?.StoreName ?? order.BranchCode ?? "", bodyFont))
                    {
                        Border = PdfRectangle.NO_BORDER,
                    }
                );
               
                infoTable.AddCell(
                    new PdfPCell(new Phrase("Device:", boldFont))
                    {
                        Border = PdfRectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                    }
                );
                infoTable.AddCell(
                    new PdfPCell(new Phrase(order.DeviceCode ?? "", bodyFont))
                    {
                        Border = PdfRectangle.NO_BORDER,
                    }
                );

                document.Add(infoTable);
                document.Add(new Paragraph(" ", bodyFont));

                if (customer != null)
                {
                    document.Add(new Paragraph("Customer Information:", headerFont));
                    var customerTable = new PdfPTable(2) { WidthPercentage = 100 };
                    customerTable.SetWidths(new float[] { 1, 1 });

                    customerTable.AddCell(
                        new PdfPCell(new Phrase("Name:", boldFont))
                        {
                            Border = PdfRectangle.NO_BORDER,
                            HorizontalAlignment = Element.ALIGN_RIGHT,
                        }
                    );
                    customerTable.AddCell(
                        new PdfPCell(
                            new Phrase($"{customer.LastName} {customer.FirstName}".Trim(), bodyFont)
                        )
                        {
                            Border = PdfRectangle.NO_BORDER,
                        }
                    );
                    customerTable.AddCell(
                        new PdfPCell(new Phrase("Phone:", boldFont))
                        {
                            Border = PdfRectangle.NO_BORDER,
                            HorizontalAlignment = Element.ALIGN_RIGHT,
                        }
                    );
                    customerTable.AddCell(
                        new PdfPCell(new Phrase(customer.CustomerPhone ?? "", bodyFont))
                        {
                            Border = PdfRectangle.NO_BORDER,
                        }
                    );
                    customerTable.AddCell(
                        new PdfPCell(new Phrase("Email:", boldFont))
                        {
                            Border = PdfRectangle.NO_BORDER,
                            HorizontalAlignment = Element.ALIGN_RIGHT,
                        }
                    );
                    customerTable.AddCell(
                        new PdfPCell(new Phrase(customer.CustomerEmail ?? "", bodyFont))
                        {
                            Border = PdfRectangle.NO_BORDER,
                        }
                    );

                    document.Add(customerTable);
                    document.Add(new Paragraph(" ", bodyFont));
                }

                document.Add(new Paragraph("Order Details:", headerFont));
                document.Add(new Paragraph(" ", bodyFont));

                var detailTable = new PdfPTable(5) { WidthPercentage = 100 };
                detailTable.SetWidths(new float[] { 3, 3, 2, 2, 2 });

                detailTable.AddCell(
                    new PdfPCell(new Phrase("Product Name", headerFont))
                    {
                        BackgroundColor = lightGrayColor,
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        Padding = 5,
                    }
                );
                detailTable.AddCell(
                    new PdfPCell(new Phrase("Price", headerFont))
                    {
                        BackgroundColor = lightGrayColor,
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        Padding = 5,
                    }
                );
                detailTable.AddCell(
                    new PdfPCell(new Phrase("Qty", headerFont))
                    {
                        BackgroundColor = lightGrayColor,
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        Padding = 5,
                    }
                );
                detailTable.AddCell(
                    new PdfPCell(new Phrase("Discount", headerFont))
                    {
                        BackgroundColor = lightGrayColor,
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        Padding = 5,
                    }
                );
                detailTable.AddCell(
                    new PdfPCell(new Phrase("Subtotal", headerFont))
                    {
                        BackgroundColor = lightGrayColor,
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        Padding = 5,
                    }
                );

                foreach (var detail in orderDetails)
                {
                    detailTable.AddCell(
                        new PdfPCell(new Phrase(detail.ProductName ?? "", bodyFont)) { Padding = 5 }
                    );
                    detailTable.AddCell(
                        new PdfPCell(new Phrase($"${detail.Price ?? 0:F2}", bodyFont))
                        {
                            HorizontalAlignment = Element.ALIGN_RIGHT,
                            Padding = 5,
                        }
                    );
                    detailTable.AddCell(
                        new PdfPCell(new Phrase($"{detail.Quantity ?? 0}", bodyFont))
                        {
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            Padding = 5,
                        }
                    );
                    detailTable.AddCell(
                        new PdfPCell(new Phrase($"${detail.DiscountAmount ?? 0:F2}", bodyFont))
                        {
                            HorizontalAlignment = Element.ALIGN_RIGHT,
                            Padding = 5,
                        }
                    );
                    detailTable.AddCell(
                        new PdfPCell(new Phrase($"${detail.ActualAmount ?? 0:F2}", bodyFont))
                        {
                            HorizontalAlignment = Element.ALIGN_RIGHT,
                            Padding = 5,
                        }
                    );
                }

                document.Add(detailTable);
                document.Add(new Paragraph(" ", bodyFont));

                var summaryTable = new PdfPTable(2) { WidthPercentage = 100 };
                summaryTable.SetWidths(new float[] { 3, 1 });

                summaryTable.AddCell(
                    new PdfPCell(new Phrase("Total Amount:", boldFont))
                    {
                        Border = PdfRectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                    }
                );
                summaryTable.AddCell(
                    new PdfPCell(new Phrase($"${order.TotalAmount ?? 0:F2}", bodyFont))
                    {
                        Border = PdfRectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                    }
                );
                summaryTable.AddCell(
                    new PdfPCell(new Phrase("Discount:", boldFont))
                    {
                        Border = PdfRectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                    }
                );
                summaryTable.AddCell(
                    new PdfPCell(new Phrase($"${order.DiscountAmount ?? 0:F2}", bodyFont))
                    {
                        Border = PdfRectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                    }
                );
                summaryTable.AddCell(
                    new PdfPCell(new Phrase("Actual Amount:", boldFont))
                    {
                        Border = PdfRectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                        PaddingTop = 5,
                    }
                );
                summaryTable.AddCell(
                    new PdfPCell(new Phrase($"${order.ActualAmount ?? 0:F2}", boldFont))
                    {
                        Border = PdfRectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                        PaddingTop = 5,
                    }
                );
                var gstAmount = (order.ActualAmount ?? 0) * 10 / 110;
                summaryTable.AddCell(
                    new PdfPCell(new Phrase("GST Included:", boldFont))
                    {
                        Border = PdfRectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                    }
                );
                summaryTable.AddCell(
                    new PdfPCell(new Phrase($"${gstAmount:F2}", bodyFont))
                    {
                        Border = PdfRectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                    }
                );

                document.Add(summaryTable);
                document.Add(new Paragraph(" ", bodyFont));

                if (paymentDetails.Any())
                {
                    document.Add(new Paragraph("Payment Details:", headerFont));
                    document.Add(new Paragraph(" ", bodyFont));

                    var paymentTable = new PdfPTable(3) { WidthPercentage = 100 };
                    paymentTable.SetWidths(new float[] { 2, 2, 2 });

                    paymentTable.AddCell(
                        new PdfPCell(new Phrase("Payment Method", headerFont))
                        {
                            BackgroundColor = lightGrayColor,
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            Padding = 5,
                        }
                    );
                    paymentTable.AddCell(
                        new PdfPCell(new Phrase("Time", headerFont))
                        {
                            BackgroundColor = lightGrayColor,
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            Padding = 5,
                        }
                    );
                    paymentTable.AddCell(
                        new PdfPCell(new Phrase("Amount", headerFont))
                        {
                            BackgroundColor = lightGrayColor,
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            Padding = 5,
                        }
                    );

                    foreach (var payment in paymentDetails)
                    {
                        string methodName = payment.PaymentMethod switch
                        {
                            1 => "Cash",
                            2 => "Card",
                            3 => "Voucher",
                            _ => "Other",
                        };

                        var paymentTime = payment.UpdatedTime ?? payment.CreatedTime;

                        paymentTable.AddCell(
                            new PdfPCell(new Phrase(methodName, bodyFont)) { Padding = 5 }
                        );
                        paymentTable.AddCell(
                            new PdfPCell(
                                new Phrase(paymentTime?.ToString("HH:mm:ss") ?? "", bodyFont)
                            )
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                                Padding = 5,
                            }
                        );
                        paymentTable.AddCell(
                            new PdfPCell(new Phrase($"${payment.Amount ?? 0:F2}", bodyFont))
                            {
                                HorizontalAlignment = Element.ALIGN_RIGHT,
                                Padding = 5,
                            }
                        );
                    }

                    document.Add(paymentTable);
                }

                document.Add(new Paragraph(" ", bodyFont));
                document.Add(new Paragraph(" ", bodyFont));
                document.Add(new Paragraph($"Cashier: {order.CashierName ?? "N/A"}", bodyFont));
                document.Add(new Paragraph($"Status: {GetStatusText(order.Status)}", bodyFont));

                if (!string.IsNullOrEmpty(order.Remark))
                {
                    document.Add(new Paragraph($"Remark: {order.Remark}", bodyFont));
                }

                document.Close();

                return ms.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "GenerateTaxInvoicePdfAsync failed for order: {OrderGuid}",
                    orderGuid
                );
                throw;
            }
        }

        private string GetStatusText(int? status)
        {
            return status switch
            {
                0 => "Pending",
                1 => "Paid",
                2 => "Cancelled",
                3 => "Refunded",
                4 => "Installment",
                _ => "Unknown",
            };
        }

        private byte[] GenerateQRCodeImage(string content)
        {
            using var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
            var pngByteQrCode = new PngByteQRCode(qrCodeData);

            return pngByteQrCode.GetGraphic(20);
        }
    }
}
