using Hbpos.Client.Wpf;
using Hbpos.Client.Wpf.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;

namespace Hbpos.Client.Tests;

public sealed class ServiceRegistrationSharedHeldOrderTests
{
    [Fact]
    public void AddHbposClientServices_registers_shared_held_order_runtime_as_singletons()
    {
        var services = new ServiceCollection();
        services.AddHbposClientServices(
            new AppStartupOptions([], PreviewMode: true, InitialScreen: null, InitialCulture: null));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<SharedHeldOrderApiClient>(provider.GetRequiredService<ISharedHeldOrderApiClient>());
        Assert.IsType<SharedHeldOrderMapper>(provider.GetRequiredService<ISharedHeldOrderMapper>());
        Assert.IsType<SharedHeldOrderReverseMapper>(provider.GetRequiredService<ISharedHeldOrderReverseMapper>());
        Assert.IsType<SharedHeldOrderPaymentSourceResolver>(
            provider.GetRequiredService<ISharedHeldOrderPaymentSourceResolver>());
        Assert.IsType<SharedHeldOrderCoordinator>(provider.GetRequiredService<ISharedHeldOrderCoordinator>());
        Assert.IsType<SharedHeldOrderPublicationGate>(
            provider.GetRequiredService<ISharedHeldOrderPublicationGate>());
        Assert.IsType<SharedHeldOrderPublicationWorker>(
            provider.GetRequiredService<ISharedHeldOrderPublicationWorker>());

        var hosted = provider.GetServices<IHostedService>()
            .OfType<SharedHeldOrderPublicationHostedService>()
            .Single();
        Assert.Same(hosted, provider.GetRequiredService<SharedHeldOrderPublicationHostedService>());
    }

    [Fact]
    public void AddHbposClientServices_configures_shared_held_order_client_with_runtime_endpoint_and_device_auth()
    {
        var services = new ServiceCollection();
        services.AddHbposClientServices(
            new AppStartupOptions([], PreviewMode: true, InitialScreen: null, InitialCulture: null));

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(ISharedHeldOrderApiClient));
        Assert.Equal(ServiceRegistration.GetApiBaseAddress(), client.BaseAddress);

        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(nameof(ISharedHeldOrderApiClient));
        var handlerTypes = new List<Type>();
        for (HttpMessageHandler? current = handler;
             current is not null;
             current = current is DelegatingHandler delegating ? delegating.InnerHandler : null)
        {
            handlerTypes.Add(current.GetType());
        }

        Assert.Contains(typeof(DeviceAuthorizationMessageHandler), handlerTypes);
    }
}
