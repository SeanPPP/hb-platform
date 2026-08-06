using System.Reflection;
using Hbpos.Api;
using Hbpos.Api.Auth;
using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Installments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Tests;

public sealed class InstallmentCancelClaimContractsTests
{
    [Fact]
    public void Release_configuration_requires_cancel_claims()
    {
        Assert.True(new InstallmentCancelClaimOptions().Required);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var services = new ServiceCollection();
        services.AddHbposApiServices(configuration);

        using var provider = services.BuildServiceProvider();
        var cancelOptions = provider.GetRequiredService<IOptions<InstallmentCancelClaimOptions>>();
        var service = new InstallmentRepaymentClaimService(
            null!,
            null!,
            null!,
            Options.Create(new InstallmentRepaymentClaimOptions()),
            cancelOptions: cancelOptions);

        Assert.True(cancelOptions.Value.Required);
        Assert.True(service.GetCapabilities().CancelClaimsRequired);
    }

    [Fact]
    public void Capabilities_and_cancel_claim_statuses_match_the_iPad_wire_contract()
    {
        var capabilities = new InstallmentRepaymentCapabilitiesResponse(
            RepaymentClaimsSupported: true,
            RepaymentClaimsRequired: false,
            CrossDeviceRepaymentEnabled: false,
            PreparedClaimTtlSeconds: 120,
            CancelClaimsSupported: true,
            CancelClaimsRequired: false,
            CancelPreparedClaimTtlSeconds: 120);

        Assert.True(capabilities.CancelClaimsSupported);
        Assert.False(capabilities.CancelClaimsRequired);
        Assert.Equal(120, capabilities.CancelPreparedClaimTtlSeconds);
        Assert.Equal(1, (int)InstallmentCancelClaimStatus.Prepared);
        Assert.Equal(2, (int)InstallmentCancelClaimStatus.RefundPending);
        Assert.Equal(3, (int)InstallmentCancelClaimStatus.Committed);
        Assert.Equal(4, (int)InstallmentCancelClaimStatus.Released);
        Assert.Equal(5, (int)InstallmentCancelClaimStatus.Declined);
        Assert.Equal(6, (int)InstallmentCancelClaimStatus.Unknown);
    }

    [Fact]
    public void Cancel_claim_routes_are_stable_and_use_the_installment_cancel_policy()
    {
        AssertRoute(nameof(InstallmentsController.CreateCancelClaim), "{installmentGuid:guid}/cancel-claims", false);
        AssertRoute(nameof(InstallmentsController.BeginCancelClaimRefund), "{installmentGuid:guid}/cancel-claims/{operationGuid:guid}/begin-refund", false);
        AssertRoute(nameof(InstallmentsController.GetCancelClaim), "{installmentGuid:guid}/cancel-claims/{operationGuid:guid}", true);
        AssertRoute(nameof(InstallmentsController.ResolveCancelClaim), "{installmentGuid:guid}/cancel-claims/{operationGuid:guid}/resolve", false);
        AssertRoute(nameof(InstallmentsController.CommitCancelClaim), "{installmentGuid:guid}/cancel-claims/{operationGuid:guid}/commit", false);
    }

    [Fact]
    public async Task Dynamic_permission_denial_maps_to_403_before_any_claim_mutation()
    {
        var cancelClaims = new PermissionDeniedCancelClaimService();
        var controller = new InstallmentsController(
            new NoOpInstallmentService(),
            new NoOpInstallmentHistoryService(),
            repaymentClaimIdentityResolver: new FixedIdentityResolver(),
            cancelClaimService: cancelClaims)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.CreateCancelClaim(
            Guid.NewGuid(),
            new InstallmentCancelClaimCreateRequest(
                Guid.NewGuid(),
                "operation",
                null,
                $"sha256:{new string('a', 64)}"),
            CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        var envelope = Assert.IsType<ApiResult<InstallmentCancelClaimDto>>(forbidden.Value);
        Assert.Equal(InstallmentCancelClaimErrorCodes.PermissionDenied, envelope.ErrorCode);
        Assert.Equal(0, cancelClaims.Mutations);
    }

    [Fact]
    public async Task Shared_lock_repayment_busy_maps_to_a_stable_cancel_conflict()
    {
        var controller = new InstallmentsController(
            new NoOpInstallmentService(),
            new NoOpInstallmentHistoryService(),
            repaymentClaimIdentityResolver: new FixedIdentityResolver(),
            cancelClaimService: new RepaymentBusyCancelClaimService())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var create = await controller.CreateCancelClaim(
            KnownGuid,
            new InstallmentCancelClaimCreateRequest(
                Guid.NewGuid(),
                "operation",
                null,
                $"sha256:{new string('a', 64)}"),
            CancellationToken.None);
        var commit = await controller.CommitCancelClaim(
            KnownGuid,
            Guid.NewGuid(),
            new InstallmentCancelClaimCommitRequest([]),
            CancellationToken.None);

        AssertError(create, InstallmentCancelClaimErrorCodes.Busy);
        AssertError(commit, InstallmentCancelClaimErrorCodes.Busy);
    }

    [Fact]
    public async Task Legacy_cancel_required_and_blocking_claims_fail_before_legacy_mutations()
    {
        var required = CreateController(new GateCancelClaimService(legacyRequired: true));
        var requiredResult = await required.Cancel(
            KnownGuid,
            CancelRequest(),
            CancellationToken.None);
        AssertError(requiredResult, InstallmentCancelClaimErrorCodes.ClaimRequired);

        var blocking = CreateController(new GateCancelClaimService(blocking: true));
        AssertError(
            await blocking.AppendPayment(KnownGuid, PaymentRequest(), CancellationToken.None),
            InstallmentCancelClaimErrorCodes.Busy);
        AssertError(
            await blocking.ConfirmPickup(KnownGuid, PickupRequest(), CancellationToken.None),
            InstallmentCancelClaimErrorCodes.Busy);
        AssertError(
            await blocking.Cancel(KnownGuid, CancelRequest(), CancellationToken.None),
            InstallmentCancelClaimErrorCodes.Busy);
        AssertError(
            await blocking.Void(KnownGuid, VoidRequest(), CancellationToken.None),
            InstallmentCancelClaimErrorCodes.Busy);
    }

    [Fact]
    public async Task Missing_cancel_claim_service_fails_closed_before_all_legacy_mutations()
    {
        var installments = new CountingInstallmentService();
        var controller = new InstallmentsController(
            installments,
            new NoOpInstallmentHistoryService())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsType<BadRequestObjectResult>((await controller.AppendPayment(
                KnownGuid,
                PaymentRequest(),
                CancellationToken.None)).Result).StatusCode);
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsType<BadRequestObjectResult>((await controller.ConfirmPickup(
                KnownGuid,
                PickupRequest(),
                CancellationToken.None)).Result).StatusCode);
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsType<BadRequestObjectResult>((await controller.Cancel(
                KnownGuid,
                CancelRequest(),
                CancellationToken.None)).Result).StatusCode);
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsType<BadRequestObjectResult>((await controller.Void(
                KnownGuid,
                VoidRequest(),
                CancellationToken.None)).Result).StatusCode);
        Assert.Equal(0, installments.Mutations);
    }

    private static readonly Guid KnownGuid = Guid.Parse("11111111-1111-4111-8111-111111111111");

    private static InstallmentsController CreateController(IInstallmentCancelClaimService service) => new(
        new NoOpInstallmentService(),
        new NoOpInstallmentHistoryService(),
        cancelClaimService: service)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    private static InstallmentAppendPaymentRequest PaymentRequest() => new(
        KnownGuid,
        Guid.NewGuid(),
        "S01",
        "POS-01",
        "C01",
        "Cashier",
        1m,
        Hbpos.Contracts.Orders.PaymentMethodKind.Cash,
        null);

    private static InstallmentConfirmPickupRequest PickupRequest() => new(
        KnownGuid,
        "S01",
        "POS-01",
        "C01",
        "Cashier",
        DateTimeOffset.UtcNow);

    private static InstallmentCancelRequest CancelRequest() => new(
        KnownGuid,
        "S01",
        "POS-01",
        "C01",
        "Cashier",
        DateTimeOffset.UtcNow,
        []);

    private static InstallmentVoidRequest VoidRequest() => new(
        KnownGuid,
        "S01",
        "POS-01",
        "C01",
        "Cashier",
        DateTimeOffset.UtcNow);

    private static void AssertError<T>(ActionResult<ApiResult<T>> action, string code)
    {
        var conflict = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal(code, Assert.IsType<ApiResult<T>>(conflict.Value).ErrorCode);
    }

    private static void AssertRoute(string methodName, string template, bool isGet)
    {
        var method = typeof(InstallmentsController).GetMethod(methodName);
        Assert.NotNull(method);
        Assert.Equal(
            template,
            isGet
                ? method!.GetCustomAttribute<HttpGetAttribute>()?.Template
                : method!.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.Equal(
            CashierAuthorizationPolicies.InstallmentCancel,
            method.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
    }

    private sealed class FixedIdentityResolver : IInstallmentRepaymentClaimIdentityResolver
    {
        public Task<InstallmentRepaymentClaimIdentity?> ResolveAsync(HttpContext httpContext, CancellationToken cancellationToken) =>
            Task.FromResult<InstallmentRepaymentClaimIdentity?>(new(
                "S01",
                "POS-01",
                "C01",
                "Cashier",
                [],
                "U01"));
    }

    private sealed class PermissionDeniedCancelClaimService : IInstallmentCancelClaimService
    {
        public int Mutations { get; private set; }

        public Task<InstallmentCancelClaimDto> CreateAsync(Guid installmentGuid, InstallmentCancelClaimCreateRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) =>
            throw new InstallmentCancelClaimException(
                InstallmentCancelClaimErrorCodes.PermissionDenied,
                "denied");
        public Task<InstallmentCancelClaimDto> BeginRefundAsync(Guid installmentGuid, Guid operationGuid, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelClaimDto> GetAsync(Guid installmentGuid, Guid operationGuid, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelClaimDto> ResolveAsync(Guid installmentGuid, Guid operationGuid, InstallmentCancelClaimResolveRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelClaimDto> CommitAsync(Guid installmentGuid, Guid operationGuid, InstallmentCancelClaimCommitRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnsureLegacyCancelAllowedAsync(Guid installmentGuid, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EnsureNoBlockingClaimAsync(Guid installmentGuid, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RepaymentBusyCancelClaimService : IInstallmentCancelClaimService
    {
        private static InstallmentRepaymentClaimException Busy() => new(
            InstallmentRepaymentClaimErrorCodes.Busy,
            "shared installment mutation lock is busy");

        public Task<InstallmentCancelClaimDto> CreateAsync(Guid installmentGuid, InstallmentCancelClaimCreateRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw Busy();
        public Task<InstallmentCancelClaimDto> BeginRefundAsync(Guid installmentGuid, Guid operationGuid, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelClaimDto> GetAsync(Guid installmentGuid, Guid operationGuid, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelClaimDto> ResolveAsync(Guid installmentGuid, Guid operationGuid, InstallmentCancelClaimResolveRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelClaimDto> CommitAsync(Guid installmentGuid, Guid operationGuid, InstallmentCancelClaimCommitRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw Busy();
        public Task EnsureLegacyCancelAllowedAsync(Guid installmentGuid, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EnsureNoBlockingClaimAsync(Guid installmentGuid, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class GateCancelClaimService(bool legacyRequired = false, bool blocking = false)
        : IInstallmentCancelClaimService
    {
        public Task EnsureLegacyCancelAllowedAsync(Guid installmentGuid, CancellationToken cancellationToken) =>
            legacyRequired
                ? Task.FromException(new InstallmentCancelClaimException(
                    InstallmentCancelClaimErrorCodes.ClaimRequired,
                    "required"))
                : EnsureNoBlockingClaimAsync(installmentGuid, cancellationToken);

        public Task EnsureNoBlockingClaimAsync(Guid installmentGuid, CancellationToken cancellationToken) =>
            blocking
                ? Task.FromException(new InstallmentCancelClaimException(
                    InstallmentCancelClaimErrorCodes.Busy,
                    "busy"))
                : Task.CompletedTask;

        public Task<InstallmentCancelClaimDto> CreateAsync(Guid installmentGuid, InstallmentCancelClaimCreateRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelClaimDto> BeginRefundAsync(Guid installmentGuid, Guid operationGuid, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelClaimDto> GetAsync(Guid installmentGuid, Guid operationGuid, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelClaimDto> ResolveAsync(Guid installmentGuid, Guid operationGuid, InstallmentCancelClaimResolveRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelClaimDto> CommitAsync(Guid installmentGuid, Guid operationGuid, InstallmentCancelClaimCommitRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoOpInstallmentService : IInstallmentService
    {
        public Task<InstallmentCreateResponse> CreateAsync(InstallmentCreateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentAppendPaymentResponse> AppendPaymentAsync(InstallmentAppendPaymentRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentConfirmPickupResponse> ConfirmPickupAsync(InstallmentConfirmPickupRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelResponse> CancelAsync(InstallmentCancelRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentVoidResponse> VoidAsync(InstallmentVoidRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CountingInstallmentService : IInstallmentService
    {
        public int Mutations { get; private set; }

        public Task<InstallmentCreateResponse> CreateAsync(InstallmentCreateRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<InstallmentCreateResponse>(null!);

        public Task<InstallmentAppendPaymentResponse> AppendPaymentAsync(InstallmentAppendPaymentRequest request, CancellationToken cancellationToken)
        {
            Mutations++;
            return Task.FromResult<InstallmentAppendPaymentResponse>(null!);
        }

        public Task<InstallmentConfirmPickupResponse> ConfirmPickupAsync(InstallmentConfirmPickupRequest request, CancellationToken cancellationToken)
        {
            Mutations++;
            return Task.FromResult<InstallmentConfirmPickupResponse>(null!);
        }

        public Task<InstallmentCancelResponse> CancelAsync(InstallmentCancelRequest request, CancellationToken cancellationToken)
        {
            Mutations++;
            return Task.FromResult<InstallmentCancelResponse>(null!);
        }

        public Task<InstallmentVoidResponse> VoidAsync(InstallmentVoidRequest request, CancellationToken cancellationToken)
        {
            Mutations++;
            return Task.FromResult<InstallmentVoidResponse>(null!);
        }
    }

    private sealed class NoOpInstallmentHistoryService : IInstallmentHistoryService
    {
        public Task<InstallmentHistoryQueryResponse> QueryAsync(InstallmentHistoryQueryRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentDetailsDto?> GetDetailsAsync(Guid installmentGuid, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
