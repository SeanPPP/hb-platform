using System.Text.Json;
using Hbpos.Api.Services;
using Hbpos.Contracts.Installments;
using Microsoft.Extensions.Configuration;

namespace Hbpos.Api.Tests;

public sealed class InstallmentCrossDeviceLifecycleTests
{
    [Fact]
    public void Code_defaults_keep_cross_device_lifecycle_fail_closed()
    {
        var options = new InstallmentCrossDeviceLifecycleOptions();

        Assert.False(options.CancelRefundEnabled);
        Assert.False(options.VoidEnabled);
        Assert.False(options.PickupEnabled);
    }

    [Fact]
    public void Release_defaults_enable_required_cross_device_and_lifecycle_switches()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var repayment = new InstallmentRepaymentClaimOptions();
        configuration.GetSection("InstallmentRepaymentClaims").Bind(repayment);
        var lifecycle = new InstallmentCrossDeviceLifecycleOptions();
        configuration.GetSection("InstallmentCrossDeviceLifecycle").Bind(lifecycle);

        Assert.True(repayment.Required);
        Assert.True(repayment.CrossDeviceEnabled);
        Assert.True(lifecycle.CancelRefundEnabled);
        Assert.True(lifecycle.VoidEnabled);
        Assert.True(lifecycle.PickupEnabled);
    }

    [Fact]
    public void Wire_contract_exposes_fail_closed_capabilities_and_idempotent_lifecycle_facts()
    {
        var capabilityProperties = typeof(InstallmentRepaymentCapabilitiesResponse)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("CrossDeviceCancelRefundEnabled", capabilityProperties);
        Assert.Contains("CrossDeviceVoidEnabled", capabilityProperties);
        Assert.Contains("CrossDevicePickupEnabled", capabilityProperties);

        Assert.NotNull(typeof(InstallmentConfirmPickupRequest).GetProperty("OperationGuid"));
        Assert.NotNull(typeof(InstallmentConfirmPickupRequest).GetProperty("IdempotencyKey"));
        Assert.NotNull(typeof(InstallmentVoidRequest).GetProperty("OperationGuid"));
        Assert.NotNull(typeof(InstallmentCancelClaimRecord).GetProperty("OriginalDeviceCode"));
        Assert.NotNull(typeof(InstallmentCancelClaimRecord).GetProperty("ClaimantDeviceCode"));
    }

    [Fact]
    public void Missing_capability_fields_and_old_lifecycle_payloads_remain_fail_closed_compatible()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var capabilities = JsonSerializer.Deserialize<InstallmentRepaymentCapabilitiesResponse>(
            """
            {
              "repaymentClaimsSupported": true,
              "repaymentClaimsRequired": true,
              "crossDeviceRepaymentEnabled": true,
              "preparedClaimTtlSeconds": 120
            }
            """,
            options);
        var pickup = JsonSerializer.Deserialize<InstallmentConfirmPickupRequest>(
            """
            {
              "installmentGuid": "11111111-1111-1111-1111-111111111111",
              "storeCode": "S01",
              "deviceCode": "POS-01",
              "cashierId": "C01",
              "cashierName": "Cashier One",
              "confirmedAt": "2026-08-05T00:00:00Z"
            }
            """,
            options);
        var oldPositionalVoid = new InstallmentVoidRequest(
            Guid.NewGuid(),
            "S01",
            "POS-01",
            "C01",
            "Cashier One",
            DateTimeOffset.Parse("2026-08-05T00:00:00Z"),
            "Legacy same-device void");

        Assert.NotNull(capabilities);
        Assert.False(capabilities!.CrossDeviceCancelRefundEnabled);
        Assert.False(capabilities.CrossDeviceVoidEnabled);
        Assert.False(capabilities.CrossDevicePickupEnabled);
        Assert.NotNull(pickup);
        Assert.Equal(Guid.Empty, pickup!.OperationGuid);
        Assert.Null(pickup.IdempotencyKey);
        Assert.Equal(Guid.Empty, oldPositionalVoid.OperationGuid);
        Assert.Null(oldPositionalVoid.IdempotencyKey);
    }
}
