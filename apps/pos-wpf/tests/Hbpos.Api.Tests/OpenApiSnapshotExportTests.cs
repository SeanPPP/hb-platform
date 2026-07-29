using System.Text;
using Hbpos.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
        Assert.Contains("deviceSystem", document, StringComparison.Ordinal);
        Assert.Contains("catalogVersion", document, StringComparison.Ordinal);
        Assert.Contains("pageChecksum", document, StringComparison.Ordinal);
        Assert.Contains("LinklyCloudBackendCardTransactionDto", document, StringComparison.Ordinal);
        Assert.Contains("cardTransaction", document, StringComparison.Ordinal);

        var outputPath = Environment.GetEnvironmentVariable("HBPOS_OPENAPI_SNAPSHOT_OUTPUT");
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            await File.WriteAllTextAsync(outputPath, document, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private sealed class OpenApiExportFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
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
                services.RemoveAll<ISquareTokenSchemaInitializer>();
                services.AddSingleton<ISquareTokenSchemaInitializer>(new NoOp());
            });
        }
    }

    private sealed class NoOp : IStoreSchemaInitializer,
        IAttendanceQrKeySchemaInitializer,
        IAdvertisementSchemaInitializer,
        ILinklyCloudCredentialSchemaInitializer,
        ISquareTokenSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
