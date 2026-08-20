using Hbpos.Client.Wpf;
using Hbpos.Client.Wpf.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Resources;

namespace Hbpos.Client.Tests;

public sealed class ServiceRegistrationRuntimeEndpointTests
{
    [Fact]
    public void Persisted_user_api_address_overrides_stale_process_address()
    {
        var address = ServiceRegistration.ResolveApiBaseAddress(
            " https://saved.example.test/pos-api ",
            "https://stale.example.test/pos-api/");

        Assert.Equal("https://saved.example.test/pos-api/", address.AbsoluteUri);
    }

    [Fact]
    public void Process_api_address_is_used_when_user_address_is_blank()
    {
        var address = ServiceRegistration.ResolveApiBaseAddress(
            " ",
            " https://launcher.example.test/pos-api ");

        Assert.Equal("https://launcher.example.test/pos-api/", address.AbsoluteUri);
    }

    [Fact]
    public void Default_api_address_is_used_when_no_address_is_configured()
    {
        var address = ServiceRegistration.ResolveApiBaseAddress(null, null);

#if DEBUG
        Assert.Equal(ApiServerSettingsService.DevelopmentApiBaseAddress, address.AbsoluteUri);
#else
        Assert.Equal(ApiServerSettingsService.ReleaseApiBaseAddress, address.AbsoluteUri);
#endif
    }

    [Fact]
    public void Preview_api_address_ignores_user_and_process_configuration()
    {
        var address = ServiceRegistration.ResolveInitialApiBaseAddress(
            previewMode: true,
            "https://saved.example.test/pos-api/",
            "https://launcher.example.test/pos-api/");

        Assert.Equal(ServiceRegistration.PreviewApiBaseAddress, address.AbsoluteUri);
    }

    [Fact]
    public void Normal_startup_preserves_user_over_process_address_priority()
    {
        var address = ServiceRegistration.ResolveInitialApiBaseAddress(
            previewMode: false,
            "https://saved.example.test/pos-api/",
            "https://launcher.example.test/pos-api/");

        Assert.Equal("https://saved.example.test/pos-api/", address.AbsoluteUri);
    }

    [Theory]
    [InlineData("not-a-uri")]
    [InlineData("/relative")]
    [InlineData("ftp://localhost/pos-api/")]
    [InlineData("http://api.example.test/pos-api/")]
    [InlineData("https://user:password@api.example.test/pos-api/")]
    [InlineData("https://api.example.test/pos-api/?token=secret")]
    [InlineData("https://api.example.test/pos-api/#fragment")]
    public void Invalid_normal_startup_api_address_throws_safe_configuration_exception(string configuredAddress)
    {
        var exception = Assert.Throws<ApiBaseAddressConfigurationException>(() =>
            ServiceRegistration.ResolveInitialApiBaseAddress(
                previewMode: false,
                configuredAddress,
                "https://valid-process.example.test/pos-api/"));

        Assert.IsType<ArgumentException>(exception.InnerException);
        Assert.Contains("HBPOS_API_BASE_URL", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(configuredAddress, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_api_configuration_has_localized_safe_startup_prompt()
    {
        var exception = Assert.Throws<ApiBaseAddressConfigurationException>(() =>
            ServiceRegistration.ResolveApiBaseAddress("not-a-uri", null));
        var resources = new ResourceManager(
            "Hbpos.Client.Wpf.Resources.Strings",
            typeof(App).Assembly);

        foreach (var cultureName in new[] { "en-US", "zh-CN" })
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            var presentation = App.CreateStartupFailurePresentation(
                exception,
                key => resources.GetString(key, culture) ?? $"[[{key}]]");

            Assert.NotNull(presentation);
            Assert.DoesNotContain("[[", presentation!.Title, StringComparison.Ordinal);
            Assert.Contains("HBPOS_API_BASE_URL", presentation.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("not-a-uri", presentation.Message, StringComparison.Ordinal);
        }

        Assert.Null(App.CreateStartupFailurePresentation(
            new InvalidOperationException("unrelated"),
            static key => key));
    }

    [Fact]
    public void Runtime_endpoint_starts_with_the_resolved_api_address()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CentralLogging:Enabled"] = "true",
                    ["CentralLogging:ApiKey"] = "preview-test-key",
                    ["CentralLogging:IngestUrl"] = "https://logs.example.test/api/system/logs/ingest"
                })
                .Build());
        services.AddHbposClientServices(new AppStartupOptions([], PreviewMode: true, InitialScreen: null, InitialCulture: null));
        using var provider = services.BuildServiceProvider();

        var expectedAddress = new Uri(ServiceRegistration.PreviewApiBaseAddress);

        Assert.Equal(
            expectedAddress,
            provider.GetRequiredService<ApiRuntimeEndpointState>().CurrentAddress);
        Assert.Equal(
            expectedAddress,
            provider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(nameof(IAppUpdateApiClient))
                .BaseAddress);
        var applicationLogOptions = provider.GetRequiredService<ApplicationLogOptions>();
        Assert.False(applicationLogOptions.Enabled);
        Assert.Null(applicationLogOptions.IngestUri);
        Assert.False(applicationLogOptions.IsConfigured);
    }

    [Fact]
    public void App_update_registers_dedicated_cache_and_credential_services()
    {
        var services = new ServiceCollection();
        services.AddHbposClientServices(new AppStartupOptions([], PreviewMode: true, InitialScreen: null, InitialCulture: null));
        using var provider = services.BuildServiceProvider();

        Assert.IsType<AppUpdateDeviceCacheInitializer>(provider.GetRequiredService<IAppUpdateDeviceCacheInitializer>());
        Assert.IsType<AppUpdateDeviceCredentialProvider>(provider.GetRequiredService<IAppUpdateDeviceCredentialProvider>());
        Assert.NotNull(provider.GetRequiredService<IAppUpdateApiClient>());
    }

    [Fact]
    public void Api_endpoint_switch_keeps_the_single_local_database_path()
    {
        var services = new ServiceCollection();
        services.AddHbposClientServices(new AppStartupOptions([], PreviewMode: true, InitialScreen: null, InitialCulture: null));
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<LocalSqliteStore>();
        var endpoint = provider.GetRequiredService<ApiRuntimeEndpointState>();
        var original = store.ActiveDatabasePath;

        endpoint.Switch("http://127.0.0.1:5159/");

        Assert.Equal(original, store.ActiveDatabasePath);
        Assert.EndsWith("hbpos_client.db", original, StringComparison.OrdinalIgnoreCase);
        Assert.Null(provider.GetService<ApiEndpointDatabasePartitionResolver>());
    }

    [Fact]
    public async Task External_application_log_request_is_not_cancelled_by_api_server_switch()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddHbposClientServices(new AppStartupOptions([], PreviewMode: true, InitialScreen: null, InitialCulture: null));
        services.AddHttpClient("HbposApplicationLogUpload")
            .ConfigurePrimaryHttpMessageHandler(() => new WaitingHandler(started, complete));
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("HbposApplicationLogUpload");
        var endpoint = provider.GetRequiredService<ApiRuntimeEndpointState>();

        var request = client.GetAsync("https://logs.example.test/ingest");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        endpoint.Switch("https://new.example.test/pos-api/");
        await Task.Delay(50);

        Assert.False(request.IsCompleted);
        complete.SetResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        using var response = await request.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(response.IsSuccessStatusCode);
    }

    private sealed class WaitingHandler(
        TaskCompletionSource started,
        TaskCompletionSource<HttpResponseMessage> complete) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            started.TrySetResult();
            return await complete.Task.WaitAsync(cancellationToken);
        }
    }
}
