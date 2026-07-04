using System.Globalization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Contracts.Installments;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Wpf.Services;

internal static class InstallmentReceiptMapper
{
    public static ReceiptDetails CreateReceipt(LocalInstallmentOrder order)
    {
        var recordedPayments = order.Payments
            .Where(payment => payment.Status == InstallmentPaymentStatus.Recorded)
            .OrderBy(payment => payment.RecordedAt)
            .ToList();
        var payments = recordedPayments
            .Select(payment => new ReceiptPaymentLine(
                payment.Method,
                payment.Amount,
                payment.Reference,
                payment.CardTransactions))
            .ToList();
        var extraInfoLines = new List<string>
        {
            $"Customer: {order.CustomerName}",
            $"Phone: {order.CustomerPhone}",
            $"Deposit paid: {Money(order.DownPaymentAmount)}",
            $"Balance due: {Money(order.BalanceAmount)}"
        };

        if (recordedPayments.Count > 0)
        {
            // 分期补录打印要展示完整还款历史；时间只存在于分期 payment DTO，不能放到通用 ReceiptPaymentLine。
            extraInfoLines.Add("Payment history:");
            extraInfoLines.AddRange(recordedPayments.Select(FormatPaymentHistoryLine));
        }

        return new ReceiptDetails(
            order.OrderGuid,
            order.StoreCode,
            order.DeviceCode,
            order.CashierName,
            order.CreatedAt,
            order.TotalAmount,
            0m,
            order.TotalAmount,
            order.Lines.Select(line => new ReceiptPreviewLine(
                line.DisplayName,
                line.LookupCode,
                line.Quantity,
                line.UnitPrice,
                line.DiscountAmount,
                line.ActualAmount)).ToList(),
            payments,
            DocumentTitle: "===== INSTALLMENT ORDER =====",
            StatusText: "*** Deposit Received ***",
            OrderDisplay: order.InstallmentNumber,
            ExtraInfoLines: extraInfoLines);
    }

    private static string FormatPaymentHistoryLine(InstallmentPaymentDto payment)
    {
        var timeText = payment.RecordedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var paymentLine = new ReceiptPaymentLine(payment.Method, payment.Amount, payment.Reference, payment.CardTransactions);
        var line = $"{timeText} {paymentLine.MethodLabel} {Money(payment.Amount)}";
        return string.IsNullOrWhiteSpace(paymentLine.DisplayReference)
            ? line
            : $"{line} Ref: {paymentLine.DisplayReference}";
    }

    private static string Money(decimal amount)
    {
        return string.Create(CultureInfo.InvariantCulture, $"${amount:0.00}");
    }
}
