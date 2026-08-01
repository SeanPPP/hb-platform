using Hbpos.Api;
using Hbpos.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Hbpos.Api.Tests;

public sealed class LinklySettlementServiceRegistrationTests
{
    [Fact]
    public void AddHbposApiServices_registers_settlement_sync_and_schema_services()
    {
        var services = new ServiceCollection();

        services.AddHbposApiServices();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ILinklySettlementSyncService) &&
            descriptor.ImplementationType == typeof(LinklySettlementSyncService));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ILinklySettlementSchemaInitializer) &&
            descriptor.ImplementationType == typeof(SqlSugarLinklySettlementSchemaInitializer));
    }
}
