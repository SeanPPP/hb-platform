using System.Security.Claims;
using Hbpos.Api.Auth;
using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.Installments;
using BlazorApp.Shared.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Tests;

public sealed class InstallmentsControllerTests
{
    [Fact]
    public async Task Cross_device_pickup_uses_verified_identity_and_requires_current_action_permission()
    {
        var installmentGuid = Guid.NewGuid();
        var installments = new CapturingInstallmentService();
        var trusted = new InstallmentRepaymentClaimIdentity(
            "S01",
            "POS-02",
            "TRUSTED-CASHIER",
            "Trusted Cashier",
            [Permissions.PosTerminal.Installments.ConfirmPickup],
            "USER-01");
        var controller = new InstallmentsController(
            installments,
            new FakeInstallmentHistoryService(CreateDetails(installmentGuid, "S01", "POS-01")),
            repaymentClaimIdentityResolver: new FixedIdentityResolver(trusted),
            cancelClaimService: new PassThroughCancelClaimService(),
            repaymentClaimService: new PassThroughRepaymentClaimService());
        SetAuthenticatedDevice(controller, "S01", "POS-02");

        await controller.ConfirmPickup(
            installmentGuid,
            new InstallmentConfirmPickupRequest(
                installmentGuid,
                "S01",
                "POS-02",
                "FORGED",
                "Forged Cashier",
                DateTimeOffset.Parse("2026-08-05T00:00:00Z"),
                OperationGuid: Guid.NewGuid(),
                IdempotencyKey: "pickup-operation"),
            CancellationToken.None);

        Assert.NotNull(installments.PickupRequest);
        Assert.Equal("TRUSTED-CASHIER", installments.PickupRequest!.CashierId);
        Assert.Equal("Trusted Cashier", installments.PickupRequest.CashierName);

        installments.PickupRequest = null;
        controller = new InstallmentsController(
            installments,
            new FakeInstallmentHistoryService(CreateDetails(installmentGuid, "S01", "POS-01")),
            repaymentClaimIdentityResolver: new FixedIdentityResolver(trusted with { PermissionCodes = [] }),
            cancelClaimService: new PassThroughCancelClaimService(),
            repaymentClaimService: new PassThroughRepaymentClaimService());
        SetAuthenticatedDevice(controller, "S01", "POS-02");

        var denied = await controller.ConfirmPickup(
            installmentGuid,
            new InstallmentConfirmPickupRequest(
                installmentGuid,
                "S01",
                "POS-02",
                "FORGED",
                "Forged Cashier",
                DateTimeOffset.Parse("2026-08-05T00:00:00Z"),
                OperationGuid: Guid.NewGuid(),
                IdempotencyKey: "pickup-denied"),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(denied.Result).StatusCode);
        Assert.Null(installments.PickupRequest);
    }

    [Fact]
    public async Task Details_allows_installment_from_another_device_in_the_same_store()
    {
        var installmentGuid = Guid.NewGuid();
        var controller = new InstallmentsController(
            null!,
            new FakeInstallmentHistoryService(CreateDetails(
                installmentGuid,
                storeCode: "S01",
                deviceCode: "POS-02")));
        SetAuthenticatedDevice(controller, storeCode: "S01", deviceCode: "POS-01");

        var result = await controller.Details(installmentGuid, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResult<InstallmentDetailsDto?>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Equal(installmentGuid, envelope.Data?.InstallmentGuid);
    }

    [Fact]
    public async Task Details_rejects_installment_from_another_store()
    {
        var installmentGuid = Guid.NewGuid();
        var controller = new InstallmentsController(
            null!,
            new FakeInstallmentHistoryService(CreateDetails(
                installmentGuid,
                storeCode: "S02",
                deviceCode: "POS-02")));
        SetAuthenticatedDevice(controller, storeCode: "S01", deviceCode: "POS-01");

        var result = await controller.Details(installmentGuid, CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        var envelope = Assert.IsType<ApiResult<InstallmentDetailsDto?>>(forbidden.Value);
        Assert.Equal("DEVICE_SCOPE_FORBIDDEN", envelope.ErrorCode);
    }

    private static InstallmentDetailsDto CreateDetails(
        Guid installmentGuid,
        string storeCode,
        string deviceCode)
    {
        return new InstallmentDetailsDto(
            installmentGuid,
            "INS-001",
            storeCode,
            deviceCode,
            "CASHIER-1",
            "Cashier One",
            "Customer One",
            "0400000000",
            DateTimeOffset.Parse("2026-08-03T00:00:00Z"),
            100m,
            20m,
            20m,
            20m,
            80m,
            InstallmentStatus.Active,
            [],
            [],
            null);
    }

    private static void SetAuthenticatedDevice(
        ControllerBase controller,
        string storeCode,
        string deviceCode)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(DeviceAuthConstants.StoreCodeClaim, storeCode),
            new Claim(DeviceAuthConstants.DeviceCodeClaim, deviceCode),
            new Claim(DeviceAuthConstants.HardwareIdClaim, "HW-001")
        ], DeviceAuthConstants.Scheme);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }

    private sealed class FakeInstallmentHistoryService(InstallmentDetailsDto? details)
        : IInstallmentHistoryService
    {
        public Task<InstallmentHistoryQueryResponse> QueryAsync(
            InstallmentHistoryQueryRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new InstallmentHistoryQueryResponse([]));
        }

        public Task<InstallmentDetailsDto?> GetDetailsAsync(
            Guid installmentGuid,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(details);
        }
    }

    private sealed class FixedIdentityResolver(InstallmentRepaymentClaimIdentity identity)
        : IInstallmentRepaymentClaimIdentityResolver
    {
        public Task<InstallmentRepaymentClaimIdentity?> ResolveAsync(HttpContext httpContext, CancellationToken cancellationToken) =>
            Task.FromResult<InstallmentRepaymentClaimIdentity?>(identity);
    }

    private sealed class CapturingInstallmentService : IInstallmentService
    {
        public InstallmentConfirmPickupRequest? PickupRequest { get; set; }

        public Task<InstallmentConfirmPickupResponse> ConfirmPickupAsync(InstallmentConfirmPickupRequest request, CancellationToken cancellationToken)
        {
            PickupRequest = request;
            return Task.FromResult(new InstallmentConfirmPickupResponse(
                request.InstallmentGuid,
                InstallmentStatus.PickedUp,
                request.ConfirmedAt,
                CreateDetails(request.InstallmentGuid, request.StoreCode, "POS-01")));
        }

        public Task<InstallmentCreateResponse> CreateAsync(InstallmentCreateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentAppendPaymentResponse> AppendPaymentAsync(InstallmentAppendPaymentRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelResponse> CancelAsync(InstallmentCancelRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentVoidResponse> VoidAsync(InstallmentVoidRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class PassThroughRepaymentClaimService : IInstallmentRepaymentClaimService
    {
        public InstallmentRepaymentCapabilitiesResponse GetCapabilities() => new(true, true, true, 120);
        public Task EnsureNoBlockingClaimAsync(Guid installmentGuid, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EnsureLegacyAppendAllowedAsync(Guid installmentGuid, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<InstallmentRepaymentClaimDto> CreateAsync(Guid installmentGuid, InstallmentRepaymentClaimCreateRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentRepaymentClaimDto> BeginProviderAsync(Guid installmentGuid, Guid operationGuid, InstallmentRepaymentClaimBeginProviderRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentRepaymentClaimDto> GetAsync(Guid installmentGuid, Guid operationGuid, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentRepaymentClaimDto> ResolveAsync(Guid installmentGuid, Guid operationGuid, InstallmentRepaymentClaimResolveRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentRepaymentClaimDto> CommitAsync(Guid installmentGuid, Guid operationGuid, InstallmentRepaymentClaimCommitRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class PassThroughCancelClaimService : IInstallmentCancelClaimService
    {
        public Task EnsureNoBlockingClaimAsync(Guid installmentGuid, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EnsureLegacyCancelAllowedAsync(Guid installmentGuid, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<InstallmentCancelClaimDto> CreateAsync(Guid installmentGuid, InstallmentCancelClaimCreateRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelClaimDto> BeginRefundAsync(Guid installmentGuid, Guid operationGuid, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelClaimDto> GetAsync(Guid installmentGuid, Guid operationGuid, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelClaimDto> ResolveAsync(Guid installmentGuid, Guid operationGuid, InstallmentCancelClaimResolveRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelClaimDto> CommitAsync(Guid installmentGuid, Guid operationGuid, InstallmentCancelClaimCommitRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
