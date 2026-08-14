using BlazorApp.Shared.Constants;
using Hbpos.Api.Auth;
using Microsoft.AspNetCore.Authorization;

namespace Hbpos.Api.Tests;

public sealed class StoreReceiptProfileAuthorizationTests
{
    [Fact]
    public void Receipt_printer_policy_maps_to_settings_receipt_printer_permission()
    {
        var options = new AuthorizationOptions();
        CashierAuthorizationPolicies.AddPolicies(options);

        var policy = options.GetPolicy(CashierAuthorizationPolicies.ReceiptPrinter);
        Assert.NotNull(policy);
        var requirement = Assert.Single(policy.Requirements.OfType<CashierPermissionRequirement>());
        Assert.Equal([Permissions.PosTerminal.Settings.ReceiptPrinter], requirement.PermissionCodes);
    }
}
