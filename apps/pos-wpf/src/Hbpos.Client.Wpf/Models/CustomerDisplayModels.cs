namespace Hbpos.Client.Wpf.Models;

public sealed record CustomerDisplayLine(
    string DisplayName,
    string LookupCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal ActualAmount)
{
    public string QuantityDisplay => Quantity.ToString("0.##");

    public decimal GrossAmount { get; init; } = ActualAmount;

    public bool HasDiscount { get; init; }

    public string DiscountRateText { get; init; } = string.Empty;
}
