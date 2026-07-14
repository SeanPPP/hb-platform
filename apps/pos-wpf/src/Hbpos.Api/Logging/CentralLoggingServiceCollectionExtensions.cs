using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hbpos.Api.Logging;

internal static class CentralLoggingServiceCollectionExtensions
{
    public static IServiceCollection AddHbposCentralLogging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = CentralLoggingOptions.FromConfiguration(configuration);
        services.AddSingleton(options);
        services.AddSingleton(new CentralLogQueue(options.QueueCapacity));
        services.AddHttpContextAccessor();
        services.AddHttpClient(CentralLogUploader.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
        });
        services.TryAddSingleton<ICentralLogDelay, SystemCentralLogDelay>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<CentralLoggerProvider>();
        services.AddSingleton<ILoggerProvider>(provider => provider.GetRequiredService<CentralLoggerProvider>());
        services.AddSingleton(provider => new CentralLogUploader(
            options,
            provider.GetRequiredService<CentralLogQueue>(),
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<ILogger<CentralLogUploadDiagnostic>>(),
            provider.GetRequiredService<ICentralLogDelay>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddHostedService(provider => provider.GetRequiredService<CentralLogUploader>());
        return services;
    }
}
