using Hbpos.Api;
using Hbpos.Api.Auth;
using Hbpos.Api.Logging;
using Hbpos.Api.Services;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddHbposFileLogging(builder.Configuration, builder.Environment);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services
    .AddAuthentication(DeviceAuthConstants.Scheme)
    .AddScheme<AuthenticationSchemeOptions, DeviceAuthenticationHandler>(
        DeviceAuthConstants.Scheme,
        options => { });
builder.Services.AddAuthorization();
builder.Services.AddHbposApiServices(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var storeSchemaInitializer = scope.ServiceProvider.GetRequiredService<IStoreSchemaInitializer>();
    await storeSchemaInitializer.InitializeAsync();

    var advertisementSchemaInitializer = scope.ServiceProvider.GetRequiredService<IAdvertisementSchemaInitializer>();
    await advertisementSchemaInitializer.InitializeAsync();

    var linklyCloudCredentialSchemaInitializer = scope.ServiceProvider.GetRequiredService<ILinklyCloudCredentialSchemaInitializer>();
    await linklyCloudCredentialSchemaInitializer.InitializeAsync();

    if (HasConnectionString(app.Configuration, "PosmConnection", "HBPOSMConnection"))
    {
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
