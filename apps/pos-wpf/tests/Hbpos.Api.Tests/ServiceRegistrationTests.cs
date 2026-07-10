using Hbpos.Api;
using Hbpos.Api.Services;
using Hbpos.Contracts.Linkly;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net.Http;

namespace Hbpos.Api.Tests;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void AddHbposApiServices_prefers_async_section_and_ignores_empty_legacy_fallback()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LinklyCloudBackendAsync:PublicNotificationBaseUrl"] = "https://public.example/callback/",
                ["LinklyCloudBackend:PublicNotificationBaseUrl"] = ""
            })
            .Build();
        var services = new ServiceCollection();

        services.AddHbposApiServices(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<LinklyCloudBackendAsyncOptions>>().Value;
        Assert.Equal("https://public.example/callback/", options.PublicNotificationBaseUrl);
    }

    [Fact]
    public void AddHbposApiServices_uses_non_empty_legacy_section_as_fallback()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LinklyCloudBackend:PublicNotificationBaseUrl"] = "https://legacy-public.example/callback/"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddHbposApiServices(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<LinklyCloudBackendAsyncOptions>>().Value;
        Assert.Equal("https://legacy-public.example/callback/", options.PublicNotificationBaseUrl);
    }

    [Fact]
    public void AddHbposApiServices_RegistersAdvertisementSchemaInitializer()
    {
        var services = new ServiceCollection();

        services.AddHbposApiServices();

        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(IAdvertisementSchemaInitializer));
        Assert.Equal(typeof(SqlSugarAdvertisementSchemaInitializer), descriptor.ImplementationType);
    }

    [Fact]
    public void AddHbposApiServices_RegistersStoreSchemaInitializer()
    {
        var services = new ServiceCollection();

        services.AddHbposApiServices();

        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(IStoreSchemaInitializer));
        Assert.Equal(typeof(SqlSugarStoreSchemaInitializer), descriptor.ImplementationType);
    }

    [Fact]
    public void AddHbposApiServices_RegistersDeviceRuntimeStatusSchemaInitializer()
    {
        var services = new ServiceCollection();

        services.AddHbposApiServices();

        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(IDeviceRuntimeStatusSchemaInitializer));
        Assert.Equal(typeof(SqlSugarDeviceRuntimeStatusSchemaInitializer), descriptor.ImplementationType);
    }

    [Fact]
    public void AddHbposApiServices_configures_linkly_cloud_backend_http_clients_above_business_wait()
    {
        var services = new ServiceCollection();

        services.AddHbposApiServices();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        Assert.True(LinklyTimeoutConstants.HttpTimeout > LinklyTimeoutConstants.BusinessWait);
        Assert.Equal(LinklyTimeoutConstants.HttpTimeout, factory.CreateClient(nameof(ILinklyCloudBackendAsyncTransport)).Timeout);
        Assert.Equal(LinklyTimeoutConstants.HttpTimeout, factory.CreateClient(nameof(ILinklyCloudBackendTokenProvider)).Timeout);
    }

    [Fact]
    public void AddHbposApiServices_configures_local_app_update_service_timeout()
    {
        var services = new ServiceCollection();

        services.AddHbposApiServices();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        Assert.Equal(TimeSpan.FromSeconds(15), factory.CreateClient(nameof(ILocalAppUpdateService)).Timeout);
    }

    [Fact]
    public void AddHbposApiServices_RegistersPromotionRuleService()
    {
        var services = new ServiceCollection();

        services.AddHbposApiServices();

        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(IPromotionRuleService));
        Assert.Equal(typeof(PromotionRuleService), descriptor.ImplementationType);
    }

    [Fact]
    public void AddHbposApiServices_registers_operation_audit_ingest_and_schema_services()
    {
        var services = new ServiceCollection();

        services.AddHbposApiServices();

        var ingest = Assert.Single(services, x => x.ServiceType == typeof(IOperationAuditIngestService));
        Assert.Equal(typeof(SqlSugarOperationAuditIngestService), ingest.ImplementationType);
        var initializer = Assert.Single(services, x => x.ServiceType == typeof(IOperationAuditSchemaInitializer));
        Assert.Equal(typeof(SqlSugarOperationAuditSchemaInitializer), initializer.ImplementationType);
    }
}
