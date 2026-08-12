using System.Reflection;
using Hbpos.Api.Auth;
using Hbpos.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Tests;

public sealed class LinklyCloudPairingSliceTests
{
    [Fact]
    public void Pair_endpoint_uses_payment_settings_and_expected_route()
    {
        var method = typeof(LinklyController).GetMethod("PairCloudBackend");

        Assert.NotNull(method);
        Assert.Equal(
            "cloud-backend/pair",
            method!.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.Equal(
            CashierAuthorizationPolicies.PaymentSettings,
            method.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
    }
}
