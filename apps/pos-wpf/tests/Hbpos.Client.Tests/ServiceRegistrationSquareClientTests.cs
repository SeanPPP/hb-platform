using System.Reflection;
using Hbpos.Client.Wpf;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Hbpos.Client.Tests;

public sealed class ServiceRegistrationSquareClientTests
{
    [Fact]
    public void AddHbposClientServices_registers_shared_api_server_settings_services()
    {
        var services = new ServiceCollection();
        services.AddHbposClientServices(new AppStartupOptions([], PreviewMode: true, InitialScreen: null, InitialCulture: null));

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ApiServerSettingsService>());
        var first = provider.GetRequiredService<ApiServerSettingsViewModel>();
        Assert.Same(first, provider.GetRequiredService<ApiServerSettingsViewModel>());

        var mainViewModel = provider.GetRequiredService<MainViewModel>();
        var field = typeof(MainViewModel).GetField(
            "_apiServerSettings",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.Same(first, field!.GetValue(mainViewModel));
    }

    [Fact]
    public void AddHbposClientServices_configures_square_terminal_clients_with_hbpos_api_base_and_device_auth_handler()
    {
        using var variables = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["HBPOS_API_BASE_URL"] = "http://127.0.0.1:55159"
        });
        var services = new ServiceCollection();
        services.AddHbposClientServices(new AppStartupOptions([], PreviewMode: true, InitialScreen: null, InitialCulture: null));

        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<ISquareTerminalSetupClient>();
        _ = provider.GetRequiredService<ISquareTerminalPaymentClient>();
        _ = provider.GetRequiredService<ICardTerminalClient>();
        Assert.Null(provider.GetService<ISquareAccessTokenProvider>());
        Assert.Null(provider.GetService<ISquareTokenResolver>());

        var clientFactory = provider.GetRequiredService<IHttpClientFactory>();
        var handlerFactory = provider.GetRequiredService<IHttpMessageHandlerFactory>();
        var expectedBaseAddress = new Uri("http://127.0.0.1:55159/");

        AssertSquareTerminalClientRegistration(
            clientFactory,
            handlerFactory,
            nameof(ISquareTerminalSetupClient),
            expectedBaseAddress);
        AssertSquareTerminalClientRegistration(
            clientFactory,
            handlerFactory,
            nameof(ISquareTerminalPaymentClient),
            expectedBaseAddress);
        AssertSquareTerminalClientRegistration(
            clientFactory,
            handlerFactory,
            nameof(ICardTerminalClient),
            expectedBaseAddress);
    }

    private static void AssertSquareTerminalClientRegistration(
        IHttpClientFactory clientFactory,
        IHttpMessageHandlerFactory handlerFactory,
        string clientName,
        Uri expectedBaseAddress)
    {
        var client = clientFactory.CreateClient(clientName);
        Assert.Equal(expectedBaseAddress, client.BaseAddress);

        var handlerTypes = DescribeHandlerChain(handlerFactory.CreateHandler(clientName));
        Assert.Contains(
            typeof(DeviceAuthorizationMessageHandler).FullName,
            handlerTypes);
    }

    private static IReadOnlyList<string?> DescribeHandlerChain(HttpMessageHandler handler)
    {
        var handlerTypes = new List<string?>();
        HttpMessageHandler? current = handler;
        while (current is not null)
        {
            handlerTypes.Add(current.GetType().FullName);
            current = current is DelegatingHandler delegatingHandler
                ? delegatingHandler.InnerHandler
                : null;
        }

        return handlerTypes;
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues = new(StringComparer.OrdinalIgnoreCase);

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var entry in values)
            {
                _originalValues[entry.Key] = Environment.GetEnvironmentVariable(entry.Key);
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }

        public void Dispose()
        {
            foreach (var entry in _originalValues)
            {
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }
    }
}
