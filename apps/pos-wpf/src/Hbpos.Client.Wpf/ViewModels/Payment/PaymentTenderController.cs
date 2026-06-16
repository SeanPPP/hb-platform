using Hbpos.Client.Wpf.Models;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Wpf.ViewModels;

internal sealed class PaymentTenderController
{
    public async Task ApplyQuickCashAsync(
        QuickCashOption? option,
        Action<string> setTenderAmountText,
        Func<PaymentMethodKind, Task> addTenderAsync)
    {
        if (option is null)
        {
            return;
        }

        setTenderAmountText(option.Amount.ToString("0.00"));
        await addTenderAsync(PaymentMethodKind.Cash);
    }

    public PaymentTenderAddPlan CreateAddTenderPlan(PaymentTenderAddRequest request)
    {
        if (request.IsInteractionBlocked)
        {
            return PaymentTenderAddPlan.Ignore();
        }

        if (request.HasPendingVoucherUpload)
        {
            return PaymentTenderAddPlan.Blocked("payment.status.retryVoucherUpload", notifyCommandStates: true);
        }

        if (request.IsOfflineVoucherRefundUnavailable)
        {
            return PaymentTenderAddPlan.Blocked("payment.refund.status.voucherOfflineUnavailable", notifyCommandStates: true);
        }

        if (request.HasDuplicateTender)
        {
            return PaymentTenderAddPlan.Blocked(
                "payment.status.duplicatePaymentMethod",
                selectedPaymentMethod: request.Method,
                notifyCommandStates: true);
        }

        if (request.BlockingCartIssueStatusKey is not null)
        {
            return PaymentTenderAddPlan.Blocked(request.BlockingCartIssueStatusKey, notifyCommandStates: true);
        }

        var selectedPaymentMethod = request.Method;
        if (!request.IsRefundMode &&
            request.Method == PaymentMethodKind.Voucher &&
            string.IsNullOrWhiteSpace(request.VoucherCodeText))
        {
            return PaymentTenderAddPlan.Blocked(
                "payment.status.voucherCodeRequired",
                selectedPaymentMethod: selectedPaymentMethod,
                notifyCommandStates: true);
        }

        // 中文注释：这里仅负责整理 tender 入口参数，不接管后续卡支付执行与恢复状态机。
        var amountText = request.ShouldUseMethodDefaultAmount
            ? request.ResolveDefaultTenderAmountText(request.Method)
            : request.ResolveTenderAmountText(request.Method);
        var referenceText = request.Method == PaymentMethodKind.Voucher
            ? request.VoucherCodeText
            : request.IsRefundMode && request.Method == PaymentMethodKind.Card
                ? request.GetRefundReference(request.Method)
                : null;
        return PaymentTenderAddPlan.Ready(selectedPaymentMethod, amountText, referenceText);
    }
}

internal readonly record struct PaymentTenderAddRequest(
    PaymentMethodKind Method,
    PaymentMethodKind SelectedPaymentMethod,
    bool IsInteractionBlocked,
    bool HasPendingVoucherUpload,
    bool IsRefundMode,
    bool IsOfflineVoucherRefundUnavailable,
    bool HasDuplicateTender,
    string? BlockingCartIssueStatusKey,
    string VoucherCodeText,
    Func<PaymentMethodKind, string> ResolveTenderAmountText,
    Func<PaymentMethodKind, string> ResolveDefaultTenderAmountText,
    Func<PaymentMethodKind, string?> GetRefundReference)
{
    public bool ShouldUseMethodDefaultAmount =>
        IsRefundMode &&
        Method == PaymentMethodKind.Card &&
        SelectedPaymentMethod != Method;
}

internal readonly record struct PaymentTenderAddPlan(
    bool ShouldProceed,
    PaymentMethodKind? SelectedPaymentMethod,
    string? AmountText,
    string? ReferenceText,
    string? StatusKey,
    bool NotifyCommandStates)
{
    public static PaymentTenderAddPlan Ignore()
    {
        return new PaymentTenderAddPlan(
            ShouldProceed: false,
            SelectedPaymentMethod: null,
            AmountText: null,
            ReferenceText: null,
            StatusKey: null,
            NotifyCommandStates: false);
    }

    public static PaymentTenderAddPlan Blocked(
        string statusKey,
        PaymentMethodKind? selectedPaymentMethod = null,
        bool notifyCommandStates = false)
    {
        return new PaymentTenderAddPlan(
            ShouldProceed: false,
            SelectedPaymentMethod: selectedPaymentMethod,
            AmountText: null,
            ReferenceText: null,
            StatusKey: statusKey,
            NotifyCommandStates: notifyCommandStates);
    }

    public static PaymentTenderAddPlan Ready(
        PaymentMethodKind selectedPaymentMethod,
        string amountText,
        string? referenceText)
    {
        return new PaymentTenderAddPlan(
            ShouldProceed: true,
            SelectedPaymentMethod: selectedPaymentMethod,
            AmountText: amountText,
            ReferenceText: referenceText,
            StatusKey: null,
            NotifyCommandStates: false);
    }
}
