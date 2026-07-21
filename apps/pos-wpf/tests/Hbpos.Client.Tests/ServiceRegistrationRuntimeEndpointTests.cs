using Hbpos.Client.Wpf;
using Hbpos.Client.Wpf.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Hbpos.Client.Tests;

public sealed class ServiceRegistrationRuntimeEndpointTests
{
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
