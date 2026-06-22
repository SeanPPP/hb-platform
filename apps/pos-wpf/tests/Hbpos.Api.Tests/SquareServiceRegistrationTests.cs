using Hbpos.Api;
using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Tests;

public sealed class SquareServiceRegistrationTests
{
    [Fact]
    public void AddHbposApiServices_RegistersSquareBackendServices()
    {
        var services = new ServiceCollection();

        services.AddHbposApiServices();

        var backendService = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISquareTerminalBackendService));
        Assert.Equal(typeof(SquareTerminalBackendService), backendService.ImplementationType);

        using var provider = services.BuildServiceProvider();
        var restClient = provider.GetRequiredService<ISquareTerminalRestClient>();
        Assert.IsType<HttpSquareTerminalRestClient>(restClient);
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
