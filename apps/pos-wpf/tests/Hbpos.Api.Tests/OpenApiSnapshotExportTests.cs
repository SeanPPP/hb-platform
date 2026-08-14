using System.Text;
using System.Text.Json;
using Hbpos.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hbpos.Api.Tests;

public sealed class OpenApiSnapshotExportTests
{
    [Fact]
    public async Task Swagger_document_can_be_exported_from_the_database_free_development_test_host()
    {
        using var factory = new OpenApiExportFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/v1/devices/register", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/app-updates/pos-ipad", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/app-updates/pos-handheld", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/app-updates/pos-handheld/ota", document, StringComparison.Ordinal);
        Assert.Contains("deviceSystem", document, StringComparison.Ordinal);
        Assert.Contains("catalogVersion", document, StringComparison.Ordinal);
        Assert.Contains("pageChecksum", document, StringComparison.Ordinal);
        Assert.Contains("LinklyCloudBackendCardTransactionDto", document, StringComparison.Ordinal);
        Assert.Contains("cardTransaction", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/held-orders/capabilities", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/held-orders/{holdGuid}/claims/prepare", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/held-orders/claims/mine", document, StringComparison.Ordinal);
        Assert.Contains("HeldOrderSourceDto", document, StringComparison.Ordinal);
        Assert.Contains("heldOrderDisposition", document, StringComparison.Ordinal);

        AssertSharedSaleCartOpenApiContract(document);

        var outputPath = Environment.GetEnvironmentVariable("HBPOS_OPENAPI_SNAPSHOT_OUTPUT");
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            await File.WriteAllTextAsync(outputPath, document, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static void AssertSharedSaleCartOpenApiContract(string document)
    {
        using var json = JsonDocument.Parse(document);
        var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("SharedSaleCartV1", out _), "OpenAPI 必须保留 SharedSaleCartV1 schema");
        Assert.True(schemas.TryGetProperty("SharedSaleCartV2", out _), "OpenAPI 必须生成 SharedSaleCartV2 schema");
        Assert.True(schemas.TryGetProperty("SharedPricingStateV1", out _), "OpenAPI 必须保留 SharedPricingStateV1 schema");
        Assert.True(schemas.TryGetProperty("SharedSaleLineV1", out _), "OpenAPI 必须保留 SharedSaleLineV1 schema");
        var v2Line = schemas.GetProperty("SharedSaleLineV2");
        Assert.Contains(
            "catalogDiscountBasisPoints",
            v2Line.GetProperty("required")
                .EnumerateArray()
                .Select(value => value.GetString()));

        AssertPayloadPropertyIsV1OrV2OneOf(json.RootElement, "SharedHeldOrderClaimPrepareResponse", "payload");
        AssertPayloadPropertyIsV1OrV2OneOf(json.RootElement, "SharedHeldOrderPublishRequest", "cart");
        AssertPayloadPropertyIsV1OrV2OneOf(json.RootElement, "SharedHeldOrderRecoveryClaimDto", "payload");
    }

    private static void AssertPayloadPropertyIsV1OrV2OneOf(JsonElement documentRoot, string schemaName, string propertyName)
    {
        var schema = documentRoot.GetProperty("components").GetProperty("schemas").GetProperty(schemaName);
        var property = schema.GetProperty("properties").GetProperty(propertyName);
        var oneOf = property.GetProperty("oneOf");
        Assert.False(
            property.TryGetProperty("nullable", out var nullable) && nullable.GetBoolean(),
            $"{schemaName}.{propertyName} 不得声明 nullable");
        Assert.Equal(2, oneOf.GetArrayLength());
        var refs = oneOf.EnumerateArray().Select(e => e.GetProperty("$ref").GetString()).OrderBy(r => r, StringComparer.Ordinal).ToArray();
        Assert.Equal("#/components/schemas/SharedSaleCartV1", refs[0]);
        Assert.Equal("#/components/schemas/SharedSaleCartV2", refs[1]);
    }

    private sealed class OpenApiExportFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                // 测试宿主必须覆盖 Development User Secrets，避免 OpenAPI 导出误连本机业务数据库。
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MainConnection"] = string.Empty,
                    ["ConnectionStrings:DefaultConnection"] = string.Empty,
                    ["ConnectionStrings:PosmConnection"] = string.Empty,
                    ["ConnectionStrings:HBPOSMConnection"] = string.Empty
                });
            });
            builder.ConfigureServices(services =>
            {
                // 快照只能从测试宿主导出，所有启动期 schema 初始化均替换为无副作用实现。
                services.RemoveAll<IStoreSchemaInitializer>();
                services.AddSingleton<IStoreSchemaInitializer>(new NoOp());
                services.RemoveAll<IAttendanceQrKeySchemaInitializer>();
                services.AddSingleton<IAttendanceQrKeySchemaInitializer>(new NoOp());
                services.RemoveAll<IAdvertisementSchemaInitializer>();
                services.AddSingleton<IAdvertisementSchemaInitializer>(new NoOp());
                services.RemoveAll<ILinklyCloudCredentialSchemaInitializer>();
                services.AddSingleton<ILinklyCloudCredentialSchemaInitializer>(new NoOp());
                services.RemoveAll<IOperationAuditSchemaInitializer>();
                services.AddSingleton<IOperationAuditSchemaInitializer>(new NoOp());
                services.RemoveAll<IDeviceRuntimeStatusSchemaInitializer>();
                services.AddSingleton<IDeviceRuntimeStatusSchemaInitializer>(new NoOp());
                services.RemoveAll<ILinklyCloudBackendAsyncSchemaInitializer>();
                services.AddSingleton<ILinklyCloudBackendAsyncSchemaInitializer>(new NoOp());
                services.RemoveAll<ILinklySettlementSchemaInitializer>();
                services.AddSingleton<ILinklySettlementSchemaInitializer>(new NoOp());
                services.RemoveAll<IInstallmentRepaymentClaimSchemaInitializer>();
                services.AddSingleton<IInstallmentRepaymentClaimSchemaInitializer>(new NoOp());
                services.RemoveAll<IInstallmentCancelClaimSchemaInitializer>();
                services.AddSingleton<IInstallmentCancelClaimSchemaInitializer>(new NoOp());
                services.RemoveAll<ISquareWebhookSchemaInitializer>();
                services.AddSingleton<ISquareWebhookSchemaInitializer>(new NoOp());
                services.RemoveAll<ISquareTokenSchemaInitializer>();
                services.AddSingleton<ISquareTokenSchemaInitializer>(new NoOp());
            });
        }
    }

    private sealed class NoOp : IStoreSchemaInitializer,
        IAttendanceQrKeySchemaInitializer,
        IAdvertisementSchemaInitializer,
        ILinklyCloudCredentialSchemaInitializer,
        IOperationAuditSchemaInitializer,
        IDeviceRuntimeStatusSchemaInitializer,
        ILinklyCloudBackendAsyncSchemaInitializer,
        ILinklySettlementSchemaInitializer,
        IInstallmentRepaymentClaimSchemaInitializer,
        IInstallmentCancelClaimSchemaInitializer,
        ISquareWebhookSchemaInitializer,
        ISquareTokenSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
