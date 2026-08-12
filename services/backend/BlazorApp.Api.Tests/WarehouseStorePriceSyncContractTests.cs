using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class WarehouseStorePriceSyncContractTests
{
    [Fact]
    public void 仓库价格同步控制器_公开固定路由与角色契约()
    {
        var controllerType = typeof(Program).Assembly.GetType(
            "BlazorApp.Api.Controllers.React.ReactProductWarehouseStorePriceSyncController"
        );

        Assert.NotNull(controllerType);
        Assert.Equal(
            "api/react/v1/product-warehouse/store-price-sync",
            controllerType!.GetCustomAttribute<RouteAttribute>()?.Template
        );

        AssertActionContract(controllerType, "GetTargetStores", "target-stores", typeof(HttpGetAttribute));
        AssertActionContract(controllerType, "GetProductCount", "product-count", typeof(HttpGetAttribute));
        AssertActionContract(controllerType, "StartJob", "jobs", typeof(HttpPostAttribute));
        AssertActionContract(controllerType, "GetJob", "jobs/{jobId}", typeof(HttpGetAttribute));
    }

    private static void AssertActionContract(
        Type controllerType,
        string methodName,
        string route,
        Type httpAttributeType
    )
    {
        var method = controllerType.GetMethod(methodName);

        Assert.NotNull(method);
        var httpAttribute = method!.GetCustomAttributes()
            .Single(attribute => attribute.GetType() == httpAttributeType);
        Assert.Equal(route, ((HttpMethodAttribute)httpAttribute).Template);
        Assert.Equal(
            "Admin,管理员,SuperAdmin,超级管理员,WarehouseManager,仓库经理",
            method.GetCustomAttribute<AuthorizeAttribute>()?.Roles
        );
    }
}
