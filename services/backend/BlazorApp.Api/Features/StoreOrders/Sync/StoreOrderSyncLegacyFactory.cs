using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Sync.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.Sync;

public static class StoreOrderSyncLegacyFactory
{
    public static IStoreOrderMissingOrdersSyncExecutor Create(
        SqlSugarContext context,
        IMapper mapper,
        IConfiguration configuration
    )
    {
        return Create(
            context,
            mapper,
            () => HqSqlSugarContext.CreateConcurrentConnection(configuration)
        );
    }

    public static IStoreOrderMissingOrdersSyncExecutor Create(
        SqlSugarContext context,
        IMapper mapper,
        Func<ISqlSugarClient> createHqConnection
    )
    {
        var infrastructure = new StoreOrderSyncInfrastructure(
            context.Db,
            mapper,
            NullLogger<StoreOrderSyncInfrastructure>.Instance,
            createHqConnection
        );

        return new SyncMissingOrdersHandler(
            new SyncMissingOrdersValidator(),
            new SyncMissingOrdersQuery(infrastructure),
            new SyncMissingOrdersCommand(infrastructure),
            NullLogger<SyncMissingOrdersHandler>.Instance
        );
    }
}
