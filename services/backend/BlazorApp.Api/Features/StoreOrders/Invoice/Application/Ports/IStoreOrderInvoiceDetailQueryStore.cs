using BlazorApp.Api.Features.StoreOrders.Invoice.Domain;

namespace BlazorApp.Api.Features.StoreOrders.Invoice.Application.Ports;

internal interface IStoreOrderInvoiceDetailQueryStore
{
    Task<StoreOrderInvoiceDetailReadResult> GetAsync(
        string orderGuid,
        CancellationToken cancellationToken = default
    );
}
