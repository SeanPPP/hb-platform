using System.Reflection;
using BlazorApp.Api.Controllers.React;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BlazorApp.Api.Tests;

public class StoreVoucherReactRouteTests
{
    [Fact]
    public void Controller_ExposesStoreVoucherReactRoute()
    {
        var route = typeof(StoreVoucherController).GetCustomAttribute<RouteAttribute>();

        Assert.NotNull(route);
        Assert.Equal("api/react/v1/store-vouchers", route!.Template);
    }
}
