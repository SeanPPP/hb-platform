using System.Reflection;
using BlazorApp.Api.Controllers.React;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class StoreOrderRouteContractTests
{
    private const string BaseRoute = "api/react/v1/store-order";

    [Fact]
    public void StoreOrder端点保持HTTP路径与授权元数据兼容()
    {
        string[] expected =
        [
            "DELETE api/react/v1/store-order/{orderGuid} | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "GET api/react/v1/store-order/accessible-branches | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "GET api/react/v1/store-order/cart/{storeCode} | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "GET api/react/v1/store-order/cart/{storeCode}/summary | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "GET api/react/v1/store-order/detail/{orderGuid} | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "GET api/react/v1/store-order/detail/{orderGuid}/full | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "GET api/react/v1/store-order/detail/{orderGuid}/product-codes | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "GET api/react/v1/store-order/hq-sync/jobs/{jobId} | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "GET api/react/v1/store-order/invoice/email/jobs/{jobId} | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "GET api/react/v1/store-order/line/paste-replace/jobs/{jobId} | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "GET api/react/v1/store-order/sync-missing-orders/jobs/{jobId} | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "GET api/react/v1/store-order/unmatched-store-groups | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "GET api/react/v1/store-order/used-branches | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/batch-map-store-code | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/batch-status | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/cart/add | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/cart/clear | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/cart/remove | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/cart/scan-add | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/cart/scan-lookup-add | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/cart/scan-update | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/cart/update | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/complete/{orderGuid} | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/copy | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/create | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/dynamic-data | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/header/update | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/hq-sync/full/jobs | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/hq-sync/incremental/jobs | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/import-price-variance | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/import-price-variance/details | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/import-price-variance/domestic-price | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/import-price-variance/warehouse-import-price | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/import-price-variance/warehouse-import-price/batch | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/invoice/email | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/invoice/email/translate-text | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/line/add | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/line/batch-add | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/line/batch-update | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/line/paste-replace | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/line/paste-replace/jobs | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/line/refresh-import-prices | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/line/remove | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/line/update | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/list | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/outbound-date | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/product-activity-history | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/product-order-history | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/product/batch-status | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/product/status | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/products | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/products/batch-lookup | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/products/scan-lookup | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/sales-since-last-arrival | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/sales-since-last-arrival/summary | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/start-picking/{orderGuid} | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/status | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/store-contact/update | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/submit | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/sync-missing-orders | authorize(policy=<none>;roles=<none>;schemes=<none>)",
            "POST api/react/v1/store-order/sync-missing-orders/jobs | authorize(policy=<none>;roles=<none>;schemes=<none>)"
        ];

        var actual = typeof(ReactStoreOrderController).Assembly
            .GetTypes()
            .Where(IsStoreOrderController)
            .SelectMany(GetEndpointMetadata)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(61, actual.Length);
        Assert.Equal(
            expected.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            actual
        );
    }

    [Fact]
    public void 旧StoreOrderController必须保留完整反射路由元数据但不得重复注册()
    {
        var compatibilityType = typeof(ReactStoreOrderController);
        Assert.False(compatibilityType.IsSealed);
        Assert.True(
            compatibilityType.IsDefined(typeof(ApiControllerAttribute), inherit: true)
        );
        Assert.True(
            compatibilityType.IsDefined(typeof(NonControllerAttribute), inherit: true)
        );
        Assert.Contains(
            compatibilityType.GetCustomAttributes<RouteAttribute>(inherit: true),
            attribute => string.Equals(attribute.Template, BaseRoute, StringComparison.Ordinal)
        );

        var compatibilityMetadata = GetEndpointMetadata(compatibilityType)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var registeredMetadata = compatibilityType.Assembly
            .GetTypes()
            .Where(IsStoreOrderController)
            .SelectMany(GetEndpointMetadata)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(61, compatibilityMetadata.Length);
        Assert.Equal(registeredMetadata, compatibilityMetadata);
    }

    private static bool IsStoreOrderController(Type type)
    {
        return !type.IsAbstract
            && !type.IsDefined(typeof(NonControllerAttribute), inherit: true)
            && typeof(ControllerBase).IsAssignableFrom(type)
            && type.GetCustomAttributes<RouteAttribute>(inherit: true)
                .Any(attribute => string.Equals(attribute.Template, BaseRoute, StringComparison.Ordinal));
    }

    private static IEnumerable<string> GetEndpointMetadata(Type controllerType)
    {
        return controllerType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(method => method
                .GetCustomAttributes<HttpMethodAttribute>(inherit: true)
                .SelectMany(attribute => attribute.HttpMethods.Select(httpMethod =>
                    $"{httpMethod} {CombineRoute(attribute.Template)} | {GetAuthorizationMetadata(controllerType, method)}"
                )));
    }

    private static string CombineRoute(string? actionRoute)
    {
        return string.IsNullOrWhiteSpace(actionRoute)
            ? BaseRoute
            : $"{BaseRoute}/{actionRoute.Trim('/')}";
    }

    private static string GetAuthorizationMetadata(Type controllerType, MethodInfo method)
    {
        var attributes = controllerType.GetCustomAttributes(inherit: true)
            .Concat(method.GetCustomAttributes(inherit: true))
            .ToArray();

        if (attributes.OfType<AllowAnonymousAttribute>().Any())
        {
            return "anonymous";
        }

        var authorizeMetadata = attributes
            .OfType<AuthorizeAttribute>()
            .Select(attribute =>
                $"authorize(policy={SnapshotValue(attribute.Policy)};roles={SnapshotValue(attribute.Roles)};schemes={SnapshotValue(attribute.AuthenticationSchemes)})"
            )
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return authorizeMetadata.Length == 0
            ? "none"
            : string.Join("&", authorizeMetadata);
    }

    private static string SnapshotValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<none>" : value.Trim();
    }
}
