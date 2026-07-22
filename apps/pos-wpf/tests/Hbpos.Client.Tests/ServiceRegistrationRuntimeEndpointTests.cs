using Hbpos.Client.Wpf;
using Hbpos.Client.Wpf.Services;
using Microsoft.Extensions.DependencyInjection;

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
    public void Runtime_endpoint_starts_with_the_resolved_api_address()
    {
        var services = new ServiceCollection();
        services.AddHbposClientServices(new AppStartupOptions([], PreviewMode: true, InitialScreen: null, InitialCulture: null));
        using var provider = services.BuildServiceProvider();

        Assert.Equal(
            ServiceRegistration.GetApiBaseAddress(),
            provider.GetRequiredService<ApiRuntimeEndpointState>().CurrentAddress);
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
