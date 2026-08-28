using BlazorApp.Api.Features.StoreOrders.Invoice.Application;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Invoice;

/// <summary>
/// 为发票附件提供所需的只读订单数据，不暴露订单管理用例。
/// </summary>
public interface IStoreOrderInvoiceDetailReader
{
    Task<ApiResponse<StoreOrderCartDto?>> GetInvoiceDetailAsync(
        string orderGuid,
        CancellationToken cancellationToken = default
    );
}

internal sealed class StoreOrderInvoiceDetailReader(
    GetStoreOrderInvoiceDetailHandler handler
) : IStoreOrderInvoiceDetailReader
{
    public Task<ApiResponse<StoreOrderCartDto?>> GetInvoiceDetailAsync(
        string orderGuid,
        CancellationToken cancellationToken = default
    )
    {
        return handler.HandleAsync(
            new GetStoreOrderInvoiceDetailQuery(orderGuid),
            cancellationToken
        );
    }
}
