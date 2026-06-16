namespace Hbpos.Client.Wpf.Services.Facades;

public sealed class PosCoreServices : IPosCoreServices
{
    public LocalSellableItemIndex PriceIndex { get; }
    public PosCartService Cart { get; }
    public CashCheckoutService Checkout { get; }
    public ILocalSchemaService Schema { get; }

    public PosCoreServices(
        LocalSellableItemIndex priceIndex,
        PosCartService cart,
        CashCheckoutService checkout,
        ILocalSchemaService schema)
    {
        PriceIndex = priceIndex;
        Cart = cart;
        Checkout = checkout;
        Schema = schema;
    }
}
