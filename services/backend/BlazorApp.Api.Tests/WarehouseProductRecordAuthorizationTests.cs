using System.Linq;
using System.Reflection;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class WarehouseProductRecordAuthorizationTests
{
    [Theory]
    [InlineData(nameof(ReactWarehouseProductRecordsController.GetSummary), Permissions.Warehouse.ManageProducts)]
    [InlineData(nameof(ReactWarehouseProductRecordsController.QueryContainers), Permissions.Container.View)]
    [InlineData(nameof(ReactWarehouseProductRecordsController.QueryAllocations), Permissions.Container.View)]
    public void Endpoint_RequiresExpectedPolicyAndNoRoleGate(string methodName, string expectedPolicy)
    {
        var method = typeof(ReactWarehouseProductRecordsController).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public
        );
        Assert.NotNull(method);

        var attributes = method!.GetCustomAttributes<AuthorizeAttribute>(inherit: false).ToList();
        Assert.Contains(attributes, attribute => attribute.Policy == expectedPolicy);
        Assert.DoesNotContain(attributes, attribute => !string.IsNullOrWhiteSpace(attribute.Roles));
    }
}
