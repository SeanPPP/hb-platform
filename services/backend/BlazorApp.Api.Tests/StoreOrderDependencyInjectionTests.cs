using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Controllers.React.StoreOrders;
using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders;
using BlazorApp.Api.Features.StoreOrders.Invoice;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class StoreOrderDependencyInjectionTests
{
    [Fact]
    public void StoreOrder兼容Facade必须保留基线公开构造签名()
    {
        var constructor = typeof(StoreOrderReactService).GetConstructor(
            [
                typeof(SqlSugarContext),
                typeof(ILogger<StoreOrderReactService>),
                typeof(IHttpContextAccessor),
                typeof(IOrderNumberGenerator),
                typeof(IConfiguration),
                typeof(IMapper),
                typeof(IInvoiceEmailService),
                typeof(IStoreOrderLocationProductLookupService),
                typeof(IWarehouseProductChangeHistoryService),
                typeof(TimeProvider),
            ]
        );

        Assert.NotNull(constructor);
        var timeProvider = constructor!.GetParameters()[^1];
        Assert.True(timeProvider.HasDefaultValue);
        Assert.Null(timeProvider.DefaultValue);
    }

    [Fact]
    public void StoreOrder生产依赖图可以完整构建并解析全部Controller()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(CreateSqlSugarContext());
        services.AddSingleton(Mock.Of<IMapper>());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped(_ => Mock.Of<IAuthorizationService>());
        services.AddScoped(_ => Mock.Of<ICurrentUserManageableStoreScopeService>());
        services.AddScoped(_ => Mock.Of<IUserService>());
        services.AddScoped(_ => Mock.Of<IStoreOrderLocationProductLookupService>());
        services.AddScoped(_ => Mock.Of<IWarehouseProductChangeHistoryService>());
        services.AddScoped(_ => Mock.Of<IOrderNumberGenerator>());
        services.AddScoped(_ => Mock.Of<IPreorderGateService>());
        services.AddScoped(_ => Mock.Of<IStoreOrderSyncJobService>());
        services.AddScoped(_ => Mock.Of<IStoreOrderInvoiceEmailJobService>());
        services.AddScoped(_ => Mock.Of<IStoreOrderInvoiceEmailTextTranslationService>());
        services.AddScoped(_ => Mock.Of<IStoreOrderPasteReplaceJobService>());

        services.AddStoreOrderFeatures();
        services.AddScoped<IStoreOrderReactService, StoreOrderReactService>();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            }
        );
        using var scope = provider.CreateScope();
        var scopedProvider = scope.ServiceProvider;

        Assert.NotNull(scopedProvider.GetRequiredService<IStoreOrderReactService>());
        Assert.NotNull(scopedProvider.GetRequiredService<IStoreOrderInvoiceDetailReader>());

        Type[] controllerTypes =
        [
            typeof(StoreOrderProductController),
            typeof(StoreOrderCartController),
            typeof(StoreOrderHistoryController),
            typeof(StoreOrderQueryController),
            typeof(StoreOrderManagementController),
            typeof(StoreOrderImportPriceVarianceController),
            typeof(StoreOrderInvoiceController),
            typeof(StoreOrderSyncController),
            typeof(StoreOrderLifecycleController),
        ];

        foreach (var controllerType in controllerTypes)
        {
            Assert.NotNull(ActivatorUtilities.CreateInstance(scopedProvider, controllerType));
        }
    }

    private static SqlSugarContext CreateSqlSugarContext()
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(SqlSugarContext)
        );
        var dbField = typeof(SqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, Mock.Of<ISqlSugarClient>());
        return context;
    }
}
