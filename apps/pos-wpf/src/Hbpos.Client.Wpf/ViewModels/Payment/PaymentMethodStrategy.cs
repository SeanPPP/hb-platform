using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Wpf.ViewModels;

/// <summary>
/// Strategy for payment-method-specific logic — remaining amount calculation,
/// refund reference resolution, default tender amounts, and CanAddTender guards.
/// Eliminates the last scattered <c>if (method == PaymentMethodKind.X)</c> branches.
/// </summary>
internal interface IPaymentMethodStrategy
{
    /// <summary>Refund remaining amount for this method, or null if not applicable.</summary>
    decimal? GetRefundRemainingAmount(
        decimal workflowRemainingAmount,
        Func<PaymentMethodKind, decimal?> getNextCardCapacityRemainingAmount);

    /// <summary>Refund reference for this method, or null if not needed.</summary>
    string? GetRefundReference(Func<string?> getNextCardCapacityReference);

    /// <summary>Default tender amount text when user hasn't typed anything.</summary>
    decimal ResolveDefaultAmount(
        bool isRefundMode,
        decimal actualAmount,
        Func<decimal> getCashRemainingAmount,
        Func<decimal> getExternalRemainingAmount,
        Func<PaymentMethodKind, decimal> getRefundRemainingAmount);

    /// <summary>Additional CanAddTender checks beyond the common guards.</summary>
    /// <returns>null if allowed; a status key string if blocked.</returns>
    string? CanAddTenderAdditionalCheck(
        bool isRefundMode,
        bool allowDefaultAmount,
        string voucherCodeText,
        string? refundReference,
        decimal remainingAmount);
}

internal sealed class CashStrategy : IPaymentMethodStrategy
{
    public static CashStrategy Instance { get; } = new();

    public decimal? GetRefundRemainingAmount(
        decimal workflowRemainingAmount,
        Func<PaymentMethodKind, decimal?> getNextCardCapacityRemainingAmount)
    {
        return new CashRoundingPolicy().NormalizeCashTender(
            Math.Abs(decimal.Round(workflowRemainingAmount, 2, MidpointRounding.AwayFromZero)));
    }

    public string? GetRefundReference(Func<string?> getNextCardCapacityReference) => null;

    public decimal ResolveDefaultAmount(
        bool isRefundMode,
        decimal actualAmount,
        Func<decimal> getCashRemainingAmount,
        Func<decimal> getExternalRemainingAmount,
        Func<PaymentMethodKind, decimal> getRefundRemainingAmount)
    {
        return isRefundMode
            ? getRefundRemainingAmount(PaymentMethodKind.Cash)
            : getCashRemainingAmount();
    }

    public string? CanAddTenderAdditionalCheck(
        bool isRefundMode,
        bool allowDefaultAmount,
        string voucherCodeText,
        string? refundReference,
        decimal remainingAmount)
    {
        // Cash always allows any amount; no upper limit.
        return null;
    }
}

internal sealed class CardStrategy : IPaymentMethodStrategy
{
    public static CardStrategy Instance { get; } = new();

    public decimal? GetRefundRemainingAmount(
        decimal workflowRemainingAmount,
        Func<PaymentMethodKind, decimal?> getNextCardCapacityRemainingAmount)
    {
        var netRemaining = Math.Abs(decimal.Round(workflowRemainingAmount, 2, MidpointRounding.AwayFromZero));
        if (netRemaining <= 0m) return 0m;

        var nextCapacity = getNextCardCapacityRemainingAmount(PaymentMethodKind.Card);
        return nextCapacity is null ? 0m : Math.Min(netRemaining, nextCapacity.Value);
    }

    public string? GetRefundReference(Func<string?> getNextCardCapacityReference)
    {
        return getNextCardCapacityReference();
    }

    public decimal ResolveDefaultAmount(
        bool isRefundMode,
        decimal actualAmount,
        Func<decimal> getCashRemainingAmount,
        Func<decimal> getExternalRemainingAmount,
        Func<PaymentMethodKind, decimal> getRefundRemainingAmount)
    {
        return isRefundMode
            ? getRefundRemainingAmount(PaymentMethodKind.Card)
            : getExternalRemainingAmount();
    }

    public string? CanAddTenderAdditionalCheck(
        bool isRefundMode,
        bool allowDefaultAmount,
        string voucherCodeText,
        string? refundReference,
        decimal remainingAmount)
    {
        if (isRefundMode && string.IsNullOrWhiteSpace(refundReference))
        {
            return "payment.refund.status.noCardReference";
        }

        // Card has no upper limit (terminal enforces it).
        return null;
    }
}

internal sealed class VoucherStrategy : IPaymentMethodStrategy
{
    public static VoucherStrategy Instance { get; } = new();

    public decimal? GetRefundRemainingAmount(
        decimal workflowRemainingAmount,
        Func<PaymentMethodKind, decimal?> getNextCardCapacityRemainingAmount)
    {
        var netRemaining = Math.Abs(decimal.Round(workflowRemainingAmount, 2, MidpointRounding.AwayFromZero));
        return netRemaining <= 0m ? 0m : netRemaining;
    }

    public string? GetRefundReference(Func<string?> getNextCardCapacityReference) => null;

    public decimal ResolveDefaultAmount(
        bool isRefundMode,
        decimal actualAmount,
        Func<decimal> getCashRemainingAmount,
        Func<decimal> getExternalRemainingAmount,
        Func<PaymentMethodKind, decimal> getRefundRemainingAmount)
    {
        return isRefundMode
            ? getRefundRemainingAmount(PaymentMethodKind.Voucher)
            : getExternalRemainingAmount();
    }

    public string? CanAddTenderAdditionalCheck(
        bool isRefundMode,
        bool allowDefaultAmount,
        string voucherCodeText,
        string? refundReference,
        decimal remainingAmount)
    {
        if (!isRefundMode && !allowDefaultAmount && string.IsNullOrWhiteSpace(voucherCodeText))
        {
            return "payment.status.voucherCodeRequired";
        }

        // Voucher has no upper limit; amount is validated against voucher value server-side.
        return null;
    }
}
