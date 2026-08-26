using BlazorApp.Api.Authentication;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Shared.Constants;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class PerformanceControllerContractTests
{
    [Fact]
    public void 权限和ServiceTokenScope_使用独立最小权限()
    {
        Assert.Equal(
            "System.ViewPerformanceBaseline",
            Permissions.System.ViewPerformanceBaseline
        );
        Assert.Equal(
            "System.ManagePerformanceBaseline",
            Permissions.System.ManagePerformanceBaseline
        );
        Assert.Equal(
            "Service.WritePerformanceMetrics",
            ServiceApiScopes.WritePerformanceMetrics
        );
        Assert.Equal(
            "Service.WriteReleaseEvents",
            ServiceApiScopes.WriteReleaseEvents
        );
        Assert.Equal(
            "quality-ci-reporter",
            ServiceApiTokenPurposes.QualityCiReporter
        );
        Assert.Equal(
            "deployment-acceptance-reporter",
            ServiceApiTokenPurposes.DeploymentAcceptanceReporter
        );
    }

    [Fact]
    public void Controller_客户端匿名项目密钥_自动化和发布仅ServiceToken_查询冻结走用户权限()
    {
        var type = typeof(SystemPerformanceController);

        AssertRoute(type.GetMethod(nameof(SystemPerformanceController.ClientBatches))!, "client-batches");
        Assert.NotNull(type.GetMethod(nameof(SystemPerformanceController.ClientBatches))!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).SingleOrDefault());

        AssertServiceTokenPolicy(
            type.GetMethod(nameof(SystemPerformanceController.AutomationBatches))!,
            ServiceApiScopes.WritePerformanceMetrics
        );
        AssertServiceTokenPolicy(
            type.GetMethod(nameof(SystemPerformanceController.ReleaseEvents))!,
            ServiceApiScopes.WriteReleaseEvents
        );
        AssertUserPolicy(
            type.GetMethod(nameof(SystemPerformanceController.Overview))!,
            Permissions.System.ViewPerformanceBaseline
        );
        AssertUserPolicy(
            type.GetMethod(nameof(SystemPerformanceController.FreezeBaseline))!,
            Permissions.System.ManagePerformanceBaseline
        );
    }

    [Fact]
    public async Task Program_注册性能服务并调用独立幂等SchemaInitializer()
    {
        var root = FindRepoRoot();
        var source = await File.ReadAllTextAsync(
            Path.Combine(root, "services/backend/BlazorApp.Api/Program.cs")
        );

        Assert.Contains("Configure<PerformanceMetricsOptions>", source, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<PerformanceMetricBuffer>", source, StringComparison.Ordinal);
        Assert.Contains("AddScoped<PerformanceMetricService>", source, StringComparison.Ordinal);
        Assert.Contains("AddScoped<PerformanceClientIngestRateLimiter>", source, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<PerformanceMetricFlushService>", source, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<AspNetCoreRequestMetricListener>", source, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<SqlPerformanceAttachmentService>", source, StringComparison.Ordinal);
        Assert.Contains("AddHttpClient<SentryReleaseHealthClient>", source, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<SentryReleaseHealthSyncService>", source, StringComparison.Ordinal);
        Assert.Contains(
            "UseMiddleware<PerformanceMetricsEndpointExclusionMiddleware>",
            source,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "await PerformanceBaselineSchemaMigrator.EnsureAsync(dbContext.Db, app.Logger);",
            source,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task SchemaMigrator_SQLServer样本索引覆盖保留和精确冻结查询()
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(
                FindRepoRoot(),
                "services/backend/BlazorApp.Api/Data/PerformanceBaselineSchemaMigrator.cs"
            )
        );

        Assert.Contains("IX_PerformanceMetricSample_ObservedAtUtc", source, StringComparison.Ordinal);
        Assert.Contains("ON [dbo].[PerformanceMetricSample]([ObservedAtUtc])", source, StringComparison.Ordinal);
        Assert.Contains("IX_PerformanceMetricSample_ExactWebBundle", source, StringComparison.Ordinal);
        Assert.Contains(
            "ON [dbo].[PerformanceMetricSample]([Environment], [SourceType], [MetricName], [ObservedAtUtc])",
            source,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Client入口_使用数据库共享预算并以429和RetryAfter拒绝超额批次()
    {
        var root = FindRepoRoot();
        var source = await File.ReadAllTextAsync(
            Path.Combine(
                root,
                "services/backend/BlazorApp.Api/Controllers/SystemPerformanceController.cs"
            )
        );

        Assert.Contains("PerformanceClientIngestRateLimiter", source, StringComparison.Ordinal);
        Assert.Contains("Status429TooManyRequests", source, StringComparison.Ordinal);
        Assert.Contains("Response.Headers.RetryAfter", source, StringComparison.Ordinal);
        Assert.Contains("PERFORMANCE_METRIC_RATE_LIMITED", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client入口_仅基线管理员WebSubject和已验证POS设备进入可信预算()
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(
                FindRepoRoot(),
                "services/backend/BlazorApp.Api/Controllers/SystemPerformanceController.cs"
            )
        );

        Assert.Contains("IClientIpResolver", source, StringComparison.Ordinal);
        Assert.Contains("IDeviceRegistrationService", source, StringComparison.Ordinal);
        Assert.Contains("IAuthorizationService", source, StringComparison.Ordinal);
        Assert.Contains("FindFirstValue", source, StringComparison.Ordinal);
        Assert.Contains("ValidateDeviceAuthCodeAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Connection.RemoteIpAddress", source, StringComparison.Ordinal);
        Assert.Contains(
            "Permissions.System.ManagePerformanceBaseline",
            source,
            StringComparison.Ordinal
        );
        Assert.Contains("hasSignedManageClaim", source, StringComparison.Ordinal);
        Assert.Contains("Permissions.SuperAdminRoleNames.Any", source, StringComparison.Ordinal);
        Assert.Contains("web-baseline-manager", source, StringComparison.Ordinal);
        Assert.DoesNotContain("web-authenticated", source, StringComparison.Ordinal);
        Assert.Contains("pos-device-authenticated", source, StringComparison.Ordinal);
        Assert.Contains("client-public", source, StringComparison.Ordinal);
        Assert.Contains("RateLimitNamespace", source, StringComparison.Ordinal);
        Assert.Contains("$\"{project.ProjectCode}:{source.RateLimitNamespace}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overview入口_把范围和结果上限错误转换为明确的BadRequest()
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(
                FindRepoRoot(),
                "services/backend/BlazorApp.Api/Controllers/SystemPerformanceController.cs"
            )
        );

        Assert.Contains("catch (PerformanceOverviewQueryException ex)", source, StringComparison.Ordinal);
        Assert.Contains(
            "ApiResponse<PerformanceOverviewDto>.Error(ex.Message, ex.ErrorCode)",
            source,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void 长连接流式端点_显式禁用内建Http指标()
    {
        var method = typeof(ReactStoreProductPricesController).GetMethod(
            nameof(ReactStoreProductPricesController.CopyStoreDataStream)
        )!;

        Assert.Single(method.GetCustomAttributes<DisableHttpMetricsAttribute>());
    }

    private static void AssertRoute(System.Reflection.MethodInfo method, string template)
    {
        var attribute = Assert.Single(method.GetCustomAttributes<HttpPostAttribute>());
        Assert.Equal(template, attribute.Template);
    }

    private static void AssertServiceTokenPolicy(
        System.Reflection.MethodInfo method,
        string policy
    )
    {
        var attribute = Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(ServiceApiTokenAuthenticationDefaults.AuthenticationScheme, attribute.AuthenticationSchemes);
        Assert.Equal(policy, attribute.Policy);
    }

    private static void AssertUserPolicy(System.Reflection.MethodInfo method, string policy)
    {
        var attribute = Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(policy, attribute.Policy);
        Assert.True(string.IsNullOrWhiteSpace(attribute.AuthenticationSchemes));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var gitMarker = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitMarker) || File.Exists(gitMarker))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("找不到仓库根目录");
    }
}
