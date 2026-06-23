using Hbpos.Api;
using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Tests;

public sealed class SquareServiceRegistrationTests
{
    [Fact]
    public void AddHbposApiServices_RegistersSquareBackendServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MainConnection"] = "Server=(localdb)\\MSSQLLocalDB;Database=hbpos-main-test;Trusted_Connection=True;",
                ["ConnectionStrings:PosmConnection"] = "Server=(localdb)\\MSSQLLocalDB;Database=hbpos-posm-test;Trusted_Connection=True;"
            })
            .Build());
        services.AddHbposApiServices();

        var backendService = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISquareTerminalBackendService));
        Assert.Equal(typeof(SquareTerminalBackendService), backendService.ImplementationType);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<ISquareTerminalBackendService>());
        var restClient = provider.GetRequiredService<ISquareTerminalRestClient>();
        Assert.IsType<HttpSquareTerminalRestClient>(restClient);
    }

    [Fact]
    public void AddHbposApiServices_BindsSquareTerminalRestOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Square:ApiVersion"] = "2026-05-20"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddHbposApiServices(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SquareTerminalRestOptions>>().Value;
        Assert.Equal("2026-05-20", options.GetApiVersion());
    }

    [Fact]
    public void AddHbposApiServices_ConfiguresSquareRestClientTimeout()
    {
        var services = new ServiceCollection();

        services.AddHbposApiServices();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        Assert.Equal(
            TimeSpan.FromSeconds(30),
            factory.CreateClient(nameof(ISquareTerminalRestClient)).Timeout);
    }

    [Fact]
    public void AddHbposApiServices_RegistersSquareWebhookInfrastructure()
    {
        var services = new ServiceCollection();

        services.AddHbposApiServices();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISquareWebhookVerifier));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISquareCheckoutSessionRepository));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISquareWebhookSchemaInitializer));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISquareWebhookSchemaSqlExecutor));

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IOptions<SquareWebhookOptions>>().Value);
    }
}
