using Hbpos.Client.Wpf.Models;

namespace Hbpos.Client.Wpf.ViewModels;

public interface IScreenNavigation
{
    void OpenCashPayment(PosCartServiceSnapshot cartSnapshot);

    void PaymentSuccess(LocalOrder order);
}

public sealed record PosCartServiceSnapshot(decimal TotalAmount, decimal DiscountAmount, decimal ActualAmount);

public sealed class PaymentCompletedEventArgs(LocalOrder order, decimal tenderedAmount, decimal changeAmount) : EventArgs
{
    public LocalOrder Order { get; } = order;

    public decimal TenderedAmount { get; } = tenderedAmount;

    public decimal ChangeAmount { get; } = changeAmount;
}
