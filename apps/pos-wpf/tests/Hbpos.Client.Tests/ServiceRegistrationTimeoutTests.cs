using Hbpos.Client.Wpf;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Linkly;
using Microsoft.Extensions.DependencyInjection;

namespace Hbpos.Client.Tests;

public sealed class ServiceRegistrationTimeoutTests
{
    [Fact]
    public void AddHbposClientServices_configures_catalog_http_client_without_hidden_timeout()
    {
        var services = new ServiceCollection();
        services.AddHbposClientServices(new AppStartupOptions([], PreviewMode: true, InitialScreen: null, InitialCulture: null));

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        Assert.Equal(Timeout.InfiniteTimeSpan, factory.CreateClient(nameof(ICatalogApiClient)).Timeout);
    }

    [Fact]
    public void AddHbposClientServices_configures_linkly_http_clients_above_business_wait()
    {
        var services = new ServiceCollection();
        services.AddHbposClientServices(new AppStartupOptions([], PreviewMode: true, InitialScreen: null, InitialCulture: null));

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        Assert.True(LinklyTimeoutConstants.HttpTimeout > LinklyTimeoutConstants.BusinessWait);
        Assert.Equal(LinklyTimeoutConstants.HttpTimeout, factory.CreateClient(nameof(ILinklyCloudApiClient)).Timeout);
        Assert.Equal(LinklyTimeoutConstants.HttpTimeout, factory.CreateClient(nameof(ILinklyBackendTerminalClient)).Timeout);
        Assert.Equal(LinklyTimeoutConstants.HttpTimeout, factory.CreateClient(nameof(ICardTerminalClient)).Timeout);
    }

    [Fact]
    public void AddHbposClientServices_reuses_one_linkly_backend_client_for_terminal_selection_and_payment()
    {
        var services = new ServiceCollection();
        services.AddHbposClientServices(new AppStartupOptions([], PreviewMode: true, InitialScreen: null, InitialCulture: null));

        using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<ILinklyBackendTerminalClient>(),
            provider.GetRequiredService<ILinklyBackendTerminalClient>());
    }
}
