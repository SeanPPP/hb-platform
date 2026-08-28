using BlazorApp.Api.Data;
using Microsoft.Extensions.Configuration;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.Orders.Infrastructure;

internal interface IStoreOrderOrdersHqConnectionFactory
{
    ISqlSugarClient Create();
}

internal sealed class StoreOrderOrdersHqConnectionFactory(IConfiguration configuration)
    : IStoreOrderOrdersHqConnectionFactory
{
    public ISqlSugarClient Create()
    {
        return HqSqlSugarContext.CreateConcurrentConnection(configuration);
    }
}
