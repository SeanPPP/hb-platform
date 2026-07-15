using Hbpos.Api.Auth;
using Hbpos.Api.Data;
using Hbpos.Api.Services;
using Microsoft.AspNetCore.DataProtection;

namespace Hbpos.Api;

public static class ServiceRegistration
{
    public static IServiceCollection AddHbposApiServices(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var dataProtection = services.AddDataProtection();
        if (configuration is not null)
        {
            var keysPath = configuration["DataProtection:KeysPath"];
            if (string.IsNullOrWhiteSpace(keysPath))
            {
                keysPath = Path.Combine(AppContext.BaseDirectory, "App_Data", "DataProtectionKeys");
            }
            else if (!Path.IsPathRooted(keysPath))
            {
                keysPath = Path.GetFullPath(keysPath, AppContext.BaseDirectory);
            }

            Directory.CreateDirectory(keysPath);
            // 关键逻辑：24 小时收银员票据必须跨进程重启、多实例共用同一密钥环。
            dataProtection
                .SetApplicationName(configuration["DataProtection:ApplicationName"] ?? "Hbpos.Api")
                .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }
        services.AddSingleton<ICashierAuthorizationTicketService, CashierAuthorizationTicketService>();
        services.AddMemoryCache();
        services.AddScoped<IEmergencyLoginPublicKeyRepository, SqlSugarEmergencyLoginPublicKeyRepository>();
        services.AddScoped<IEmergencyLoginPublicKeyDistributionService, EmergencyLoginPublicKeyDistributionService>();
        services.AddScoped<IEmergencyLoginPublicKeyProvider, EmergencyLoginPublicKeyProvider>();
        services.AddScoped<IEmergencyGrantAuthorizationService, EmergencyGrantAuthorizationService>();
        services.AddOptions<SquareWebhookOptions>();
        services.AddOptions<AppUpdateOptions>();
        services.AddOptions<SquareTerminalRestOptions>()
            .Validate(
                options => SquareTerminalRestOptions.IsValidApiVersion(options.ApiVersion),
                "Square:ApiVersion must use yyyy-MM-dd.");
        if (configuration is not null)
        {
            services.Configure<LinklyCloudBackendAsyncOptions>(options =>
            {
                // 旧配置只作为兜底来源；新配置后应用，且空字符串不能覆盖已有有效值。
                ApplyLinklyCloudBackendAsyncOptionsSection(
                    options,
                    configuration.GetSection("LinklyCloudBackend"));
                ApplyLinklyCloudBackendAsyncOptionsSection(
                    options,
                    configuration.GetSection("LinklyCloudBackendAsync"));
            });
            services.Configure<SquareWebhookOptions>(configuration.GetSection("Square"));
            services.Configure<SquareTerminalRestOptions>(configuration.GetSection("Square"));
            services.Configure<AppUpdateOptions>(configuration.GetSection("AppUpdate"));
        }

        services.AddScoped<HbposSqlSugarContext>();
        services.AddScoped<IOperationAuditIngestService, SqlSugarOperationAuditIngestService>();
        services.AddScoped<IOperationAuditSchemaInitializer, SqlSugarOperationAuditSchemaInitializer>();
        services.AddScoped<IDeviceRegistrationRepository, SqlSugarDeviceRegistrationRepository>();
        services.AddScoped<IDeviceService, DeviceService>();
        services.AddScoped<IDeviceAuthorizationService, DeviceAuthorizationService>();
        services.AddScoped<IDeviceRuntimeStatusSchemaSqlExecutor, SqlSugarDeviceRuntimeStatusSchemaSqlExecutor>();
        services.AddScoped<IDeviceRuntimeStatusSchemaInitializer, SqlSugarDeviceRuntimeStatusSchemaInitializer>();
        services.AddScoped<ICashierService, CashierService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IPromotionRuleService, PromotionRuleService>();
        services.AddScoped<IAdvertisementPlaybackService, AdvertisementPlaybackService>();
        services.AddScoped<IStoreSchemaSqlExecutor, SqlSugarStoreSchemaSqlExecutor>();
        services.AddScoped<IStoreSchemaInitializer, SqlSugarStoreSchemaInitializer>();
        services.AddScoped<IAdvertisementSchemaInitializer, SqlSugarAdvertisementSchemaInitializer>();
        services.AddScoped<IOrderRepository, SqlSugarOrderRepository>();
        services.AddScoped<IOrderSyncService, OrderSyncService>();
        services.AddScoped<IOrderHistoryRepository, SqlSugarOrderHistoryRepository>();
        services.AddScoped<IOrderHistoryService, OrderHistoryService>();
        services.AddScoped<IOrderReturnRepository, SqlSugarOrderReturnRepository>();
        services.AddScoped<IOrderReturnService, OrderReturnService>();
        services.AddScoped<IInstallmentRepository, SqlSugarInstallmentRepository>();
        services.AddScoped<InstallmentService>();
        services.AddScoped<IInstallmentService>(sp => sp.GetRequiredService<InstallmentService>());
        services.AddScoped<IInstallmentHistoryService>(sp => sp.GetRequiredService<InstallmentService>());
        services.AddScoped<IStoreVoucherRepository, SqlSugarStoreVoucherRepository>();
        services.AddScoped<IStoreVoucherService, StoreVoucherService>();
        services.AddScoped<ILinklyCloudCredentialRepository, SqlSugarLinklyCloudCredentialRepository>();
        services.AddScoped<ILinklyCloudCredentialService, LinklyCloudCredentialService>();
        services.AddScoped<ILinklyCloudCredentialSchemaSqlExecutor, SqlSugarLinklyCloudCredentialSchemaSqlExecutor>();
        services.AddScoped<ILinklyCloudCredentialSchemaInitializer, SqlSugarLinklyCloudCredentialSchemaInitializer>();
        services.AddScoped<ILinklyCloudBackendAsyncRepository, SqlSugarLinklyCloudBackendAsyncRepository>();
        services.AddScoped<ILinklyCloudBackendTerminalCredentialRepository, SqlSugarLinklyCloudBackendTerminalCredentialRepository>();
        services.AddHttpClient<ILinklyCloudBackendAsyncTransport, HttpLinklyCloudBackendAsyncTransport>(client =>
        {
            client.Timeout = LinklyCloudBackendTimeoutPolicy.HttpTimeout;
        });
        services.AddHttpClient<ILinklyCloudBackendTokenProvider, HttpLinklyCloudBackendTokenProvider>(client =>
        {
            client.Timeout = LinklyCloudBackendTimeoutPolicy.HttpTimeout;
        });
        services.AddScoped<ILinklyCloudBackendAsyncService, LinklyCloudBackendAsyncService>();
        services.AddScoped<ILinklyCloudBackendAsyncSchemaSqlExecutor, SqlSugarLinklyCloudBackendAsyncSchemaSqlExecutor>();
        services.AddScoped<ILinklyCloudBackendAsyncSchemaInitializer, SqlSugarLinklyCloudBackendAsyncSchemaInitializer>();
        services.AddScoped<ISquareTokenRepository, SqlSugarSquareTokenRepository>();
        services.AddScoped<ISquareTokenService, SquareTokenService>();
        services.AddScoped<ISquareTokenSchemaSqlExecutor, SqlSugarSquareTokenSchemaSqlExecutor>();
        services.AddScoped<ISquareTokenSchemaInitializer, SqlSugarSquareTokenSchemaInitializer>();
        services.AddScoped<ISquareWebhookVerifier, SquareWebhookVerifier>();
        services.AddScoped<ISquareCheckoutSessionRepository, SqlSugarSquareCheckoutSessionRepository>();
        services.AddScoped<ISquareWebhookSchemaSqlExecutor, SqlSugarSquareWebhookSchemaSqlExecutor>();
        services.AddScoped<ISquareWebhookSchemaInitializer, SqlSugarSquareWebhookSchemaInitializer>();
        services.AddHttpClient<ISquareTerminalRestClient, HttpSquareTerminalRestClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<ISquareTerminalBackendService, SquareTerminalBackendService>();
        services.AddSingleton<ICatalogIndexCache, CatalogIndexCache>();
        services.AddSingleton<IPriceIndexBuilder, PriceIndexBuilder>();
        services.AddSingleton<IOrderSyncPlanner, OrderSyncPlanner>();
        services.AddScoped<IStoreVoucherReservationService, SqlSugarStoreVoucherReservationService>();
        services.AddHttpClient<ILocalAppUpdateService, LocalAppUpdateService>(client =>
        {
            // 本地 WPF 更新检查不应继承 HttpClient 默认 100 秒超时，避免 POS API 线程长时间挂起。
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        return services;
    }

    private static void ApplyLinklyCloudBackendAsyncOptionsSection(
        LinklyCloudBackendAsyncOptions options,
        IConfigurationSection section)
    {
        AssignNonEmpty(
            section,
            nameof(LinklyCloudBackendAsyncOptions.ProductionNotificationBearer),
            value => options.ProductionNotificationBearer = value);
        AssignNonEmpty(
            section,
            nameof(LinklyCloudBackendAsyncOptions.SandboxNotificationBearer),
            value => options.SandboxNotificationBearer = value);
        AssignNonEmpty(
            section,
            nameof(LinklyCloudBackendAsyncOptions.PublicNotificationBaseUrl),
            value => options.PublicNotificationBaseUrl = value);
        AssignNonEmpty(
            section,
            nameof(LinklyCloudBackendAsyncOptions.ProductionAuthBaseUrl),
            value => options.ProductionAuthBaseUrl = value);
        AssignNonEmpty(
            section,
            nameof(LinklyCloudBackendAsyncOptions.SandboxAuthBaseUrl),
            value => options.SandboxAuthBaseUrl = value);
        AssignNonEmpty(
            section,
            nameof(LinklyCloudBackendAsyncOptions.ProductionRestBaseUrl),
            value => options.ProductionRestBaseUrl = value);
        AssignNonEmpty(
            section,
            nameof(LinklyCloudBackendAsyncOptions.SandboxRestBaseUrl),
            value => options.SandboxRestBaseUrl = value);
        AssignNonEmpty(
            section,
            nameof(LinklyCloudBackendAsyncOptions.PosName),
            value => options.PosName = value);
        AssignNonEmpty(
            section,
            nameof(LinklyCloudBackendAsyncOptions.PosVersion),
            value => options.PosVersion = value);
        AssignNonEmpty(
            section,
            nameof(LinklyCloudBackendAsyncOptions.ProductionPosVendorId),
            value => options.ProductionPosVendorId = value);
        AssignNonEmpty(
            section,
            nameof(LinklyCloudBackendAsyncOptions.SandboxPosVendorId),
            value => options.SandboxPosVendorId = value);
    }

    private static void AssignNonEmpty(
        IConfiguration section,
        string key,
        Action<string> assign)
    {
        var value = section[key];
        if (!string.IsNullOrWhiteSpace(value))
        {
            assign(value.Trim());
        }
    }

}
