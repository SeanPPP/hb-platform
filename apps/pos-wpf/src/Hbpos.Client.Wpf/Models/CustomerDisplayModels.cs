namespace Hbpos.Client.Wpf.Models;

public sealed record CustomerDisplayLine(
    string DisplayName,
    string LookupCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal ActualAmount)
{
    public string QuantityDisplay => Quantity.ToString("0.##");
}
