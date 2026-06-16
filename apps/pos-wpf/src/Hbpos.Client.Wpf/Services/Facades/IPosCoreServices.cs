namespace Hbpos.Client.Wpf.Services.Facades;

public interface IPosCoreServices
{
    LocalSellableItemIndex PriceIndex { get; }
    PosCartService Cart { get; }
    CashCheckoutService Checkout { get; }
    ILocalSchemaService Schema { get; }
}
