using Hbpos.Api.Services;
using Hbpos.Contracts.Installments;
using Hbpos.Contracts.Orders;
using Microsoft.Extensions.Configuration;

namespace Hbpos.Api.Tests;

public sealed class InstallmentRepaymentClaimContractsTests
{
    [Fact]
    public void Release_keeps_cross_device_disabled_while_development_enables_local_testing()
    {
        var releaseConfiguration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var releaseOptions = new InstallmentRepaymentClaimOptions();
        releaseConfiguration.GetSection("InstallmentRepaymentClaims").Bind(releaseOptions);

        var developmentConfiguration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: false)
            .Build();
        var developmentOptions = new InstallmentRepaymentClaimOptions();
        developmentConfiguration.GetSection("InstallmentRepaymentClaims").Bind(developmentOptions);

        Assert.False(releaseOptions.CrossDeviceEnabled);
        Assert.True(developmentOptions.CrossDeviceEnabled);
    }

    [Fact]
    public void Capabilities_expose_locked_repayment_claim_contract()
    {
        var capabilities = new InstallmentRepaymentCapabilitiesResponse(
            RepaymentClaimsSupported: true,
            RepaymentClaimsRequired: false,
            CrossDeviceRepaymentEnabled: false,
            PreparedClaimTtlSeconds: 120);

        Assert.True(capabilities.RepaymentClaimsSupported);
        Assert.False(capabilities.RepaymentClaimsRequired);
        Assert.False(capabilities.CrossDeviceRepaymentEnabled);
        Assert.Equal(120, capabilities.PreparedClaimTtlSeconds);
    }

    [Fact]
    public void Claim_contract_has_locked_states_and_identity_free_request_body()
    {
        var operationGuid = Guid.NewGuid();
        var paymentGuid = Guid.NewGuid();
        var request = new InstallmentRepaymentClaimCreateRequest(
            operationGuid,
            paymentGuid,
            12.34m,
            PaymentMethodKind.Card,
            "installment-action-1");

        Assert.Equal(operationGuid, request.OperationGuid);
        Assert.Equal(paymentGuid, request.PaymentGuid);
        Assert.Equal(
        [
            InstallmentRepaymentClaimStatus.Prepared,
            InstallmentRepaymentClaimStatus.ProviderPending,
            InstallmentRepaymentClaimStatus.Committed,
            InstallmentRepaymentClaimStatus.Released,
            InstallmentRepaymentClaimStatus.Declined,
            InstallmentRepaymentClaimStatus.Unknown
        ],
            Enum.GetValues<InstallmentRepaymentClaimStatus>());
        Assert.DoesNotContain(
            typeof(InstallmentRepaymentClaimCreateRequest).GetProperties(),
            property => property.Name is "StoreCode" or "DeviceCode" or "CashierId" or "CashierName");
    }
}
