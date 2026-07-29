using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Wpf.ViewModels;

public enum CardRecoveryResultSeverity
{
    Success,
    Warning,
    Error
}

public sealed class CardRecoveryResultDialogViewModel : ObservableObject
{
    private string _refundReference = string.Empty;
    private string _refundEvidence = string.Empty;
    private string _refundSupervisorNote = string.Empty;
    private string _refundResolutionMessage = string.Empty;
    private bool _isRefundResolutionBusy;

    public CardRecoveryResultDialogViewModel(
        string title,
        string message,
        CardRecoveryResultSeverity severity,
        Guid? orderGuid,
        decimal? amount,
        string? sessionId,
        string? txnRef,
        string? responseCode,
        string? responseText,
        DateTimeOffset timestamp,
        IEnumerable<ReceiptPreviewRow>? receiptPreviewRows = null,
        bool canPrintReceipt = false,
        string printButtonText = "Print receipt",
        bool canRetryRecovery = false,
        string retryButtonText = "Retry recovery",
        bool canManualConfirm = false,
        string manualConfirmButtonText = "Confirm checked and continue",
        CardRefundRecoveryDetails? refundDetails = null,
        CardPaymentSupervisorDetails? paymentSupervisorDetails = null)
    {
        Title = title;
        Message = message;
        Severity = severity;
        OrderGuid = orderGuid;
        Amount = amount;
        SessionId = Normalize(sessionId);
        TxnRef = Normalize(txnRef);
        ResponseCode = Normalize(responseCode);
        ResponseText = Normalize(responseText);
        Timestamp = timestamp;
        CanPrintReceipt = canPrintReceipt;
        PrintButtonText = printButtonText;
        CanRetryRecovery = canRetryRecovery;
        RetryButtonText = retryButtonText;
        CanManualConfirm = canManualConfirm;
        ManualConfirmButtonText = manualConfirmButtonText;
        RefundDetails = refundDetails;
        PaymentSupervisorDetails = paymentSupervisorDetails;
        ReceiptPreviewRows = new ObservableCollection<ReceiptPreviewRow>(receiptPreviewRows ?? []);
    }

    public string Title { get; }

    public string Message { get; }

    public CardRecoveryResultSeverity Severity { get; }

    public Guid? OrderGuid { get; }

    public decimal? Amount { get; }

    public string? SessionId { get; }

    public string? TxnRef { get; }

    public string? ResponseCode { get; }

    public string? ResponseText { get; }

    public DateTimeOffset Timestamp { get; }

    public ObservableCollection<ReceiptPreviewRow> ReceiptPreviewRows { get; }

    public bool CanPrintReceipt { get; }

    public string PrintButtonText { get; }

    public bool CanRetryRecovery { get; }

    public string RetryButtonText { get; }

    public bool CanManualConfirm { get; }

    public string ManualConfirmButtonText { get; }

    public CardRefundRecoveryDetails? RefundDetails { get; }

    public CardPaymentSupervisorDetails? PaymentSupervisorDetails { get; }

    public bool CanResolveRefund => RefundDetails is not null;

    public bool CanResolvePayment => PaymentSupervisorDetails is not null;

    public bool CanResolveFinancialResult => CanResolveRefund || CanResolvePayment;

    public string RefundProcessorDisplay => RefundDetails?.Processor.ToString() ?? "-";

    public string OriginalReferenceDisplay =>
        string.IsNullOrWhiteSpace(RefundDetails?.OriginalReference) ? "-" : RefundDetails.OriginalReference;

    public string RefundReference
    {
        get => _refundReference;
        set => SetProperty(ref _refundReference, value ?? string.Empty);
    }

    public string RefundEvidence
    {
        get => _refundEvidence;
        set => SetProperty(ref _refundEvidence, value ?? string.Empty);
    }

    public string RefundSupervisorNote
    {
        get => _refundSupervisorNote;
        set => SetProperty(ref _refundSupervisorNote, value ?? string.Empty);
    }

    public string RefundResolutionMessage
    {
        get => _refundResolutionMessage;
        set
        {
            if (SetProperty(ref _refundResolutionMessage, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasRefundResolutionMessage));
            }
        }
    }

    public bool HasRefundResolutionMessage => !string.IsNullOrWhiteSpace(RefundResolutionMessage);

    public bool IsRefundResolutionBusy
    {
        get => _isRefundResolutionBusy;
        set => SetProperty(ref _isRefundResolutionBusy, value);
    }

    public bool HasReceiptPreview => ReceiptPreviewRows.Count > 0;

    public bool HasOrderGuid => OrderGuid is not null;

    public bool HasAmount => Amount is not null;

    public bool HasSessionId => !string.IsNullOrWhiteSpace(SessionId);

    public bool HasTxnRef => !string.IsNullOrWhiteSpace(TxnRef);

    public bool HasResponseCode => !string.IsNullOrWhiteSpace(ResponseCode);

    public bool HasResponseText => !string.IsNullOrWhiteSpace(ResponseText);

    public string AmountDisplay => Amount is { } amount ? string.Create(CultureInfo.InvariantCulture, $"${amount:0.00}") : "-";

    public string OrderGuidDisplay => OrderGuid?.ToString("D") ?? "-";

    public string TimestampDisplay => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.CurrentCulture);

    public bool IsSuccess => Severity == CardRecoveryResultSeverity.Success;

    public bool IsWarning => Severity == CardRecoveryResultSeverity.Warning;

    public bool IsError => Severity == CardRecoveryResultSeverity.Error;

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
