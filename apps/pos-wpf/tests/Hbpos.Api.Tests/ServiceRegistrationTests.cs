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
    public void AddHbposApiServices_AllowsAppReviewGateWithStoreScopedEnforcementInAuditMode()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PosIpadAppReview:Enabled"] = "true",
                ["PosIpadAppReview:StoreCode"] = " 1042 ",
                ["PosIpadAppReview:ExpiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(1).ToString("O"),
                ["PosIpadAppReview:MaxActiveDevices"] = "1",
                ["PosIpadAppReview:GrantId"] = "4baf31b5-792d-49ef-8cc2-d38b486a28a7",
                ["PosIpadAppReview:RegistrationCodeSha256"] = new string('A', 64),
                ["CashierAuthorization:Mode"] = "Audit"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddHbposApiServices(configuration);

        Assert.NotEmpty(services);
    }

    [Fact]
    public void AddHbposApiServices_RejectsEnabledAppReviewGateWithoutStoreCode()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PosIpadAppReview:Enabled"] = "true",
                ["CashierAuthorization:Mode"] = "Audit"
            })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddHbposApiServices(configuration));

        Assert.Contains("PosIpadAppReview:StoreCode", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PosIpadAppReview:ExpiresAtUtc", null)]
    [InlineData("PosIpadAppReview:MaxActiveDevices", "0")]
    [InlineData("PosIpadAppReview:MaxActiveDevices", "2")]
    [InlineData("PosIpadAppReview:GrantId", "not-a-guid")]
    [InlineData("PosIpadAppReview:RegistrationCodeSha256", "ABCDEF")]
    public void AddHbposApiServices_RejectsIncompleteEnabledAppReviewGate(string invalidKey, string? invalidValue)
    {
        var values = ValidAppReviewConfiguration();
        values[invalidKey] = invalidValue;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddHbposApiServices(configuration));

        Assert.Contains(invalidKey, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddHbposApiServices_AllowsExpiredReviewWindowSoOrdinaryPosApiCanRestart()
    {
        var values = ValidAppReviewConfiguration();
        values["PosIpadAppReview:ExpiresAtUtc"] = "2000-01-01T00:00:00Z";

        var services = new ServiceCollection();
        services.AddHbposApiServices(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

        Assert.NotEmpty(services);
    }

    private static Dictionary<string, string?> ValidAppReviewConfiguration() => new()
    {
        ["PosIpadAppReview:Enabled"] = "true",
        ["PosIpadAppReview:StoreCode"] = "1042",
        ["PosIpadAppReview:ExpiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(1).ToString("O"),
        ["PosIpadAppReview:MaxActiveDevices"] = "1",
        ["PosIpadAppReview:GrantId"] = "4baf31b5-792d-49ef-8cc2-d38b486a28a7",
        ["PosIpadAppReview:RegistrationCodeSha256"] = new string('A', 64),
        ["CashierAuthorization:Mode"] = "Audit"
    };
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
    public void AddHbposApiServices_registers_shared_held_order_services_and_enables_cross_device_operations_by_default()
    {
        var services = new ServiceCollection();

        services.AddHbposApiServices();

        Assert.Equal(
            typeof(SqlSugarSharedHeldOrderRepository),
            Assert.Single(services, x => x.ServiceType == typeof(ISharedHeldOrderRepository)).ImplementationType);
        Assert.Equal(
            typeof(SharedHeldOrderService),
            Assert.Single(services, x => x.ServiceType == typeof(ISharedHeldOrderService)).ImplementationType);
        Assert.Equal(
            typeof(SharedHeldOrderPayloadProtector),
            Assert.Single(services, x => x.ServiceType == typeof(ISharedHeldOrderPayloadProtector)).ImplementationType);
        Assert.Equal(
            typeof(SharedHeldOrderIdentityResolver),
            Assert.Single(services, x => x.ServiceType == typeof(ISharedHeldOrderIdentityResolver)).ImplementationType);
        Assert.Equal(
            typeof(SqlSugarSharedHeldOrderSchemaInitializer),
            Assert.Single(services, x => x.ServiceType == typeof(ISharedHeldOrderSchemaInitializer)).ImplementationType);

        using var provider = services.BuildServiceProvider();
        Assert.True(provider.GetRequiredService<IOptions<SharedHeldOrderOptions>>().Value.Enabled);
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
