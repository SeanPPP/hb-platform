using Hbpos.Api.Auth;
using Hbpos.Api.Data;
using Hbpos.Api.Services;
using Hbpos.Api.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Hbpos.Api;

public static class ServiceRegistration
{
    public static IServiceCollection AddHbposApiServices(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var globalKeysPath = ResolveDataProtectionKeysPath(
            configuration?["DataProtection:KeysPath"],
            "DataProtectionKeys");
        var configuredAttendanceKeysPath = configuration?["AttendanceQrDataProtection:KeysPath"];
        var environmentName = configuration?["ASPNETCORE_ENVIRONMENT"]
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(configuredAttendanceKeysPath))
        {
            // 关键逻辑：生产环境不得静默创建本地考勤 ring，否则会与主 backend 永久失配。
            throw new InvalidOperationException(
                "Production requires AttendanceQrDataProtection:KeysPath.");
        }

        var attendanceKeysPath = ResolveDataProtectionKeysPath(
            configuredAttendanceKeysPath,
            "AttendanceQrDataProtectionKeys");

        Directory.CreateDirectory(globalKeysPath);
        // 关键逻辑：POS 自身票据使用独立持久 ring，不能挂载主 backend 的全局 ring。
        services.AddDataProtection()
            .SetApplicationName(configuration?["DataProtection:ApplicationName"] ?? "Hbpos.Api")
            .PersistKeysToFileSystem(new DirectoryInfo(globalKeysPath));
        // 关键逻辑：考勤密钥使用单独目录及固定应用名/purpose，仅与主 backend 的考勤 ring 共享。
        services.AddSingleton(AttendanceQrKeyDataProtection.CreateProtector(attendanceKeysPath));
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
        services.AddScoped<IAppUpdateDeviceIdentityValidator, AppUpdateDeviceIdentityValidator>();
        services.AddScoped<IAttendanceSigningKeyRegistrationService, AttendanceSigningKeyRegistrationService>();
        services.AddScoped<IAttendanceQrKeySchemaSqlExecutor, SqlSugarAttendanceQrKeySchemaSqlExecutor>();
        services.AddScoped<IAttendanceQrKeySchemaInitializer, SqlSugarAttendanceQrKeySchemaInitializer>();
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
        })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .UseSocketsHttpHandler((handler, _) =>
            {
                // 固定工厂 Handler，并由连接生命周期定期刷新 DNS；REST 池始终最多一条真实连接。
                handler.PooledConnectionLifetime = TimeSpan.FromMinutes(15);
                handler.MaxConnectionsPerServer = 1;
            });
        services.AddHttpClient<ILinklyCloudBackendTokenProvider, HttpLinklyCloudBackendTokenProvider>(client =>
        {
            client.Timeout = LinklyCloudBackendTimeoutPolicy.HttpTimeout;
        })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .UseSocketsHttpHandler((handler, _) =>
            {
                // 固定工厂 Handler，并由连接生命周期定期刷新 DNS；Token 与 REST 合计最多两条连接。
                handler.PooledConnectionLifetime = TimeSpan.FromMinutes(15);
                handler.MaxConnectionsPerServer = 1;
            });
        services.AddHostedService<LinklyHttpConnectionMetricsService>();
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

    private static string ResolveDataProtectionKeysPath(string? configuredPath, string defaultDirectoryName)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.Combine(AppContext.BaseDirectory, "App_Data", defaultDirectoryName);
        }

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
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
