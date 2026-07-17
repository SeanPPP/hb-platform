using Hbpos.Api;
using Hbpos.Api.Auth;
using Hbpos.Api.Logging;
using Hbpos.Api.Services;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddHbposFileLogging(builder.Configuration, builder.Environment);
builder.Services.AddHbposCentralLogging(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services
    .AddAuthentication(DeviceAuthConstants.Scheme)
    .AddScheme<AuthenticationSchemeOptions, DeviceAuthenticationHandler>(
        DeviceAuthConstants.Scheme,
        options => { });
builder.Services.AddAuthorization(CashierAuthorizationPolicies.AddPolicies);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuthorizationHandler, CashierPermissionAuthorizationHandler>();
builder.Services.AddHbposApiServices(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    // 启动时只检查 Square 后端注册存在，避免提前实例化 SQL 仓储并要求数据库连接串。
    var serviceRegistration = scope.ServiceProvider.GetRequiredService<IServiceProviderIsService>();
    if (!serviceRegistration.IsService(typeof(ISquareTerminalBackendService)))
    {
        throw new InvalidOperationException($"{nameof(ISquareTerminalBackendService)} is not registered.");
    }

    // REST client 不依赖本地数据库，真实解析可提前验证 Square:ApiVersion 配置。
    _ = scope.ServiceProvider.GetRequiredService<ISquareTerminalRestClient>();

    var storeSchemaInitializer = scope.ServiceProvider.GetRequiredService<IStoreSchemaInitializer>();
    await storeSchemaInitializer.InitializeAsync();

    // 关键逻辑：考勤签名服务依赖 MainDb 密钥表，初始化失败时直接阻止 API 启动。
    var attendanceQrKeySchemaInitializer = scope.ServiceProvider.GetRequiredService<IAttendanceQrKeySchemaInitializer>();
    await attendanceQrKeySchemaInitializer.InitializeAsync();

    var advertisementSchemaInitializer = scope.ServiceProvider.GetRequiredService<IAdvertisementSchemaInitializer>();
    await advertisementSchemaInitializer.InitializeAsync();

    var linklyCloudCredentialSchemaInitializer = scope.ServiceProvider.GetRequiredService<ILinklyCloudCredentialSchemaInitializer>();
    await linklyCloudCredentialSchemaInitializer.InitializeAsync();

    if (HasConnectionString(app.Configuration, "PosmConnection", "HBPOSMConnection"))
    {
        var operationAuditSchemaInitializer = scope.ServiceProvider.GetRequiredService<IOperationAuditSchemaInitializer>();
        await operationAuditSchemaInitializer.InitializeAsync();

        var deviceRuntimeStatusSchemaInitializer = scope.ServiceProvider.GetRequiredService<IDeviceRuntimeStatusSchemaInitializer>();
        await deviceRuntimeStatusSchemaInitializer.InitializeAsync();

        var linklyCloudBackendAsyncSchemaInitializer = scope.ServiceProvider.GetRequiredService<ILinklyCloudBackendAsyncSchemaInitializer>();
        await linklyCloudBackendAsyncSchemaInitializer.InitializeAsync();

        var squareWebhookSchemaInitializer = scope.ServiceProvider.GetRequiredService<ISquareWebhookSchemaInitializer>();
        await squareWebhookSchemaInitializer.InitializeAsync();
    }

    var squareTokenSchemaInitializer = scope.ServiceProvider.GetRequiredService<ISquareTokenSchemaInitializer>();
    await squareTokenSchemaInitializer.InitializeAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

static bool HasConnectionString(IConfiguration configuration, string primaryName, string fallbackName)
{
    return !string.IsNullOrWhiteSpace(configuration.GetConnectionString(primaryName)) ||
        !string.IsNullOrWhiteSpace(configuration.GetConnectionString(fallbackName));
}
