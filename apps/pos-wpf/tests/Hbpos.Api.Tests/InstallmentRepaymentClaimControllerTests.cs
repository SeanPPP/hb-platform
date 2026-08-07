using System.Reflection;
using System.Security.Claims;
using Hbpos.Api.Auth;
using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.Installments;
using Hbpos.Contracts.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Tests;

public sealed class InstallmentRepaymentClaimControllerTests
{
    private static readonly Guid InstallmentGuid = Guid.Parse("8052ff65-bc21-4c76-a126-dadff4b7d14e");
    private static readonly Guid OperationGuid = Guid.Parse("a35f0832-6cc7-40f8-8011-c7da72d820f0");
    private static readonly Guid PaymentGuid = Guid.Parse("90d3c9c7-71c2-45d4-939e-9e6d6311e7aa");
    private static readonly InstallmentRepaymentClaimIdentity TrustedIdentity =
        new("S01", "POS-02", "C01", "Trusted Cashier");

    [Fact]
    public void Capabilities_and_claim_routes_are_stable_and_authorized()
    {
        AssertRoute(nameof(InstallmentsController.Capabilities), "capabilities", isGet: true, policy: null);
        AssertRoute(nameof(InstallmentsController.CreateRepaymentClaim), "{installmentGuid:guid}/repayment-claims", false, CashierAuthorizationPolicies.InstallmentPayment);
        AssertRoute(nameof(InstallmentsController.BeginRepaymentProvider), "{installmentGuid:guid}/repayment-claims/{operationGuid:guid}/begin-provider", false, CashierAuthorizationPolicies.InstallmentPayment);
        AssertRoute(nameof(InstallmentsController.PrepareRepaymentProvider), "{installmentGuid:guid}/repayment-claims/{operationGuid:guid}/prepare-provider", false, policy: null);
        AssertRoute(nameof(InstallmentsController.GetRepaymentClaim), "{installmentGuid:guid}/repayment-claims/{operationGuid:guid}", true, policy: null);
        AssertRoute(nameof(InstallmentsController.ResolveRepaymentClaim), "{installmentGuid:guid}/repayment-claims/{operationGuid:guid}/resolve", false, policy: null);
        AssertRoute(nameof(InstallmentsController.CommitRepaymentClaim), "{installmentGuid:guid}/repayment-claims/{operationGuid:guid}/commit", false, policy: null);
    }

    [Fact]
    public async Task Capabilities_and_all_claim_actions_return_200_api_result_envelopes()
    {
        var claims = new FakeClaimService();
        var controller = CreateController(claims, new FakeIdentityResolver(TrustedIdentity));

        AssertOkEnvelope(
            controller.Capabilities(),
            data => Assert.Equal(120, data.PreparedClaimTtlSeconds));
        AssertOkEnvelope(
            await controller.CreateRepaymentClaim(
                InstallmentGuid,
                CreateRequest(),
                CancellationToken.None),
            data => Assert.Equal(InstallmentRepaymentClaimStatus.Prepared, data.Status));
        AssertOkEnvelope(
            await controller.BeginRepaymentProvider(
                InstallmentGuid,
                OperationGuid,
                new InstallmentRepaymentClaimBeginProviderRequest("linkly", "attempt-1"),
                CancellationToken.None),
            data => Assert.Equal(OperationGuid, data.OperationGuid));
        AssertOkEnvelope(
            await controller.PrepareRepaymentProvider(
                InstallmentGuid,
                OperationGuid,
                new InstallmentRepaymentClaimPrepareProviderRequest(
                    PaymentGuid,
                    10m,
                    PaymentMethodKind.Card,
                    "claim-action-1",
                    "linkly",
                    "attempt-1"),
                CancellationToken.None),
            data => Assert.Equal(OperationGuid, data.OperationGuid));
        AssertOkEnvelope(
            await controller.GetRepaymentClaim(
                InstallmentGuid,
                OperationGuid,
                CancellationToken.None),
            data => Assert.Equal(OperationGuid, data.OperationGuid));
        AssertOkEnvelope(
            await controller.ResolveRepaymentClaim(
                InstallmentGuid,
                OperationGuid,
                new InstallmentRepaymentClaimResolveRequest(InstallmentRepaymentClaimResolveOutcome.Released),
                CancellationToken.None),
            data => Assert.Equal(OperationGuid, data.OperationGuid));
        AssertOkEnvelope(
            await controller.CommitRepaymentClaim(
                InstallmentGuid,
                OperationGuid,
                new InstallmentRepaymentClaimCommitRequest(Reference: "APPROVED"),
                CancellationToken.None),
            data => Assert.Equal(OperationGuid, data.OperationGuid));

        Assert.Equal(TrustedIdentity, claims.LastIdentity);
    }

    [Theory]
    [InlineData(InstallmentRepaymentClaimErrorCodes.Busy)]
    [InlineData(InstallmentRepaymentClaimErrorCodes.Mismatch)]
    [InlineData(InstallmentRepaymentClaimErrorCodes.ClaimRequired)]
    public async Task Claim_conflict_errors_map_to_http_409(string errorCode)
    {
        var claims = new FakeClaimService { ErrorCode = errorCode };
        var controller = CreateController(claims, new FakeIdentityResolver(TrustedIdentity));

        var action = await controller.CreateRepaymentClaim(
            InstallmentGuid,
            CreateRequest(),
            CancellationToken.None);

        AssertError(action, StatusCodes.Status409Conflict, errorCode);
    }

    [Fact]
    public async Task Prepare_provider_conflict_errors_map_to_http_409()
    {
        var claims = new FakeClaimService { ErrorCode = InstallmentRepaymentClaimErrorCodes.Mismatch };
        var controller = CreateController(claims, new FakeIdentityResolver(TrustedIdentity));

        var action = await controller.PrepareRepaymentProvider(
            InstallmentGuid,
            OperationGuid,
            new InstallmentRepaymentClaimPrepareProviderRequest(
                PaymentGuid,
                10m,
                PaymentMethodKind.Cash,
                "claim-action-1",
                "cash",
                "attempt-1"),
            CancellationToken.None);

        AssertError(action, StatusCodes.Status409Conflict, InstallmentRepaymentClaimErrorCodes.Mismatch);
    }

    [Fact]
    public async Task Missing_method_permission_maps_to_http_403()
    {
        var claims = new FakeClaimService
        {
            ErrorCode = InstallmentRepaymentClaimErrorCodes.PermissionDenied
        };
        var controller = CreateController(claims, new FakeIdentityResolver(TrustedIdentity));

        var action = await controller.CreateRepaymentClaim(
            InstallmentGuid,
            CreateRequest(),
            CancellationToken.None);

        AssertError(action, StatusCodes.Status403Forbidden, InstallmentRepaymentClaimErrorCodes.PermissionDenied);
    }

    [Theory]
    [InlineData(InstallmentRepaymentClaimErrorCodes.Invalid)]
    [InlineData(InstallmentRepaymentClaimErrorCodes.PaymentMethodUnsupported)]
    public async Task Boundary_validation_errors_map_to_http_400(string errorCode)
    {
        var claims = new FakeClaimService
        {
            ErrorCode = errorCode
        };
        var controller = CreateController(claims, new FakeIdentityResolver(TrustedIdentity));

        var action = await controller.CreateRepaymentClaim(
            InstallmentGuid,
            CreateRequest(),
            CancellationToken.None);

        AssertError(action, StatusCodes.Status400BadRequest, errorCode);
    }

    [Fact]
    public async Task Missing_verified_identity_returns_401_and_body_has_no_identity_fields()
    {
        var claims = new FakeClaimService();
        var controller = CreateController(claims, new FakeIdentityResolver(null));

        var action = await controller.CreateRepaymentClaim(
            InstallmentGuid,
            CreateRequest(),
            CancellationToken.None);

        AssertError(action, StatusCodes.Status401Unauthorized, "CASHIER_AUTH_REQUIRED");
        Assert.Null(claims.LastIdentity);
        var bodyProperties = typeof(InstallmentRepaymentClaimCreateRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("storeCode", bodyProperties);
        Assert.DoesNotContain("deviceCode", bodyProperties);
        Assert.DoesNotContain("cashierId", bodyProperties);
        Assert.DoesNotContain("cashierName", bodyProperties);
    }

    [Fact]
    public async Task Recovery_routes_still_require_a_verified_cashier_identity()
    {
        var claims = new FakeClaimService();
        var controller = CreateController(claims, new FakeIdentityResolver(null));

        AssertError(
            await controller.GetRepaymentClaim(InstallmentGuid, OperationGuid, CancellationToken.None),
            StatusCodes.Status401Unauthorized,
            "CASHIER_AUTH_REQUIRED");
        AssertError(
            await controller.ResolveRepaymentClaim(
                InstallmentGuid,
                OperationGuid,
                new InstallmentRepaymentClaimResolveRequest(InstallmentRepaymentClaimResolveOutcome.Unknown),
                CancellationToken.None),
            StatusCodes.Status401Unauthorized,
            "CASHIER_AUTH_REQUIRED");
        AssertError(
            await controller.CommitRepaymentClaim(
                InstallmentGuid,
                OperationGuid,
                new InstallmentRepaymentClaimCommitRequest(),
                CancellationToken.None),
            StatusCodes.Status401Unauthorized,
            "CASHIER_AUTH_REQUIRED");
        Assert.Null(claims.LastIdentity);
    }

    [Fact]
    public async Task Device_claims_override_forged_legacy_body_scope_and_return_403()
    {
        var installmentService = new FakeInstallmentService();
        var claims = new FakeClaimService();
        var controller = CreateController(claims, new FakeIdentityResolver(TrustedIdentity), installmentService);
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(DeviceAuthConstants.StoreCodeClaim, "S02"),
            new Claim(DeviceAuthConstants.DeviceCodeClaim, "POS-99")
        ], "test"));

        var action = await controller.AppendPayment(
            InstallmentGuid,
            LegacyPaymentRequest(),
            CancellationToken.None);

        AssertError(action, StatusCodes.Status403Forbidden, "DEVICE_SCOPE_FORBIDDEN");
        Assert.Equal(0, installmentService.AppendCalls);
        Assert.Equal(0, claims.LegacyChecks);
    }

    [Fact]
    public async Task Required_mode_rejects_legacy_payment_before_installment_service_runs()
    {
        var installmentService = new FakeInstallmentService();
        var claims = new FakeClaimService { LegacyErrorCode = InstallmentRepaymentClaimErrorCodes.ClaimRequired };
        var controller = CreateController(claims, new FakeIdentityResolver(TrustedIdentity), installmentService);

        var action = await controller.AppendPayment(
            InstallmentGuid,
            LegacyPaymentRequest(),
            CancellationToken.None);

        AssertError(action, StatusCodes.Status409Conflict, InstallmentRepaymentClaimErrorCodes.ClaimRequired);
        Assert.Equal(0, installmentService.AppendCalls);
        Assert.Equal(1, claims.LegacyChecks);
    }

    [Fact]
    public async Task Blocking_claim_rejects_cancel_pickup_and_void_before_installment_service_runs()
    {
        var installmentService = new FakeInstallmentService();
        var claims = new FakeClaimService
        {
            BlockingErrorCode = InstallmentRepaymentClaimErrorCodes.Busy
        };
        var controller = CreateController(claims, new FakeIdentityResolver(TrustedIdentity), installmentService);

        var cancel = await controller.Cancel(
            InstallmentGuid,
            new InstallmentCancelRequest(
                InstallmentGuid,
                "S01",
                "POS-02",
                "C01",
                "Trusted Cashier",
                DateTimeOffset.Parse("2026-08-04T00:00:00Z"),
                Array.Empty<InstallmentRefundPaymentCommandDto>()),
            CancellationToken.None);
        var pickup = await controller.ConfirmPickup(
            InstallmentGuid,
            new InstallmentConfirmPickupRequest(
                InstallmentGuid,
                "S01",
                "POS-02",
                "C01",
                "Trusted Cashier",
                DateTimeOffset.Parse("2026-08-04T00:00:00Z")),
            CancellationToken.None);
        var @void = await controller.Void(
            InstallmentGuid,
            new InstallmentVoidRequest(
                InstallmentGuid,
                "S01",
                "POS-02",
                "C01",
                "Trusted Cashier",
                DateTimeOffset.Parse("2026-08-04T00:00:00Z")),
            CancellationToken.None);

        AssertError(cancel, StatusCodes.Status409Conflict, InstallmentRepaymentClaimErrorCodes.Busy);
        AssertError(pickup, StatusCodes.Status409Conflict, InstallmentRepaymentClaimErrorCodes.Busy);
        AssertError(@void, StatusCodes.Status409Conflict, InstallmentRepaymentClaimErrorCodes.Busy);
        Assert.Equal(3, claims.BlockingChecks);
        Assert.Equal(0, installmentService.CancelCalls);
        Assert.Equal(0, installmentService.ConfirmPickupCalls);
        Assert.Equal(0, installmentService.VoidCalls);
    }

    private static InstallmentsController CreateController(
        FakeClaimService claims,
        IInstallmentRepaymentClaimIdentityResolver resolver,
        FakeInstallmentService? installments = null)
    {
        var controller = new InstallmentsController(
            installments ?? new FakeInstallmentService(),
            new FakeHistoryService(),
            claims,
            resolver,
            new PassThroughCancelClaimService())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return controller;
    }

    private static InstallmentRepaymentClaimCreateRequest CreateRequest() => new(
        OperationGuid,
        PaymentGuid,
        10m,
        PaymentMethodKind.Card,
        "claim-action-1");

    private static InstallmentAppendPaymentRequest LegacyPaymentRequest() => new(
        InstallmentGuid,
        PaymentGuid,
        "S01",
        "POS-02",
        "forged-cashier",
        "Forged Cashier",
        10m,
        PaymentMethodKind.Card,
        "LEGACY",
        IdempotencyKey: "legacy-action-1");

    private static void AssertRoute(string methodName, string template, bool isGet, string? policy)
    {
        var method = typeof(InstallmentsController).GetMethod(methodName);
        Assert.NotNull(method);
        var routeTemplate = isGet
            ? method!.GetCustomAttribute<HttpGetAttribute>()?.Template
            : method!.GetCustomAttribute<HttpPostAttribute>()?.Template;
        Assert.Equal(template, routeTemplate);
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal(policy, authorize!.Policy);
    }

    private static void AssertOkEnvelope<T>(ActionResult<ApiResult<T>> action, Action<T> assertData)
    {
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var envelope = Assert.IsType<ApiResult<T>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        assertData(envelope.Data!);
    }

    private static void AssertError<T>(ActionResult<ApiResult<T>> action, int statusCode, string errorCode)
    {
        var result = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(statusCode, result.StatusCode);
        var envelope = Assert.IsType<ApiResult<T>>(result.Value);
        Assert.False(envelope.Success);
        Assert.Equal(errorCode, envelope.ErrorCode);
    }

    private sealed class FakeIdentityResolver(InstallmentRepaymentClaimIdentity? identity)
        : IInstallmentRepaymentClaimIdentityResolver
    {
        public Task<InstallmentRepaymentClaimIdentity?> ResolveAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken) => Task.FromResult(identity);
    }

    private sealed class FakeClaimService : IInstallmentRepaymentClaimService
    {
        public string? ErrorCode { get; set; }

        public string? LegacyErrorCode { get; set; }

        public string? BlockingErrorCode { get; set; }

        public InstallmentRepaymentClaimIdentity? LastIdentity { get; private set; }

        public int LegacyChecks { get; private set; }

        public int BlockingChecks { get; private set; }

        public InstallmentRepaymentCapabilitiesResponse GetCapabilities() => new(true, false, true, 120);

        public Task<InstallmentRepaymentClaimDto> CreateAsync(Guid installmentGuid, InstallmentRepaymentClaimCreateRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => Respond(identity);

        public Task<InstallmentRepaymentClaimDto> BeginProviderAsync(Guid installmentGuid, Guid operationGuid, InstallmentRepaymentClaimBeginProviderRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => Respond(identity);

        public Task<InstallmentRepaymentClaimDto> PrepareProviderAsync(Guid installmentGuid, Guid operationGuid, InstallmentRepaymentClaimPrepareProviderRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => Respond(identity);

        public Task<InstallmentRepaymentClaimDto> GetAsync(Guid installmentGuid, Guid operationGuid, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => Respond(identity);

        public Task<InstallmentRepaymentClaimDto> ResolveAsync(Guid installmentGuid, Guid operationGuid, InstallmentRepaymentClaimResolveRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => Respond(identity);

        public Task<InstallmentRepaymentClaimDto> CommitAsync(Guid installmentGuid, Guid operationGuid, InstallmentRepaymentClaimCommitRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => Respond(identity);

        public Task EnsureLegacyAppendAllowedAsync(Guid installmentGuid, CancellationToken cancellationToken)
        {
            LegacyChecks++;
            ThrowIfConfigured(LegacyErrorCode);
            return Task.CompletedTask;
        }

        public Task EnsureNoBlockingClaimAsync(Guid installmentGuid, CancellationToken cancellationToken)
        {
            BlockingChecks++;
            ThrowIfConfigured(BlockingErrorCode);
            return Task.CompletedTask;
        }

        private Task<InstallmentRepaymentClaimDto> Respond(InstallmentRepaymentClaimIdentity identity)
        {
            LastIdentity = identity;
            ThrowIfConfigured(ErrorCode);
            return Task.FromResult(new InstallmentRepaymentClaimDto(
                InstallmentGuid,
                OperationGuid,
                PaymentGuid,
                10m,
                PaymentMethodKind.Card,
                "claim-action-1",
                InstallmentRepaymentClaimStatus.Prepared,
                null,
                null,
                DateTimeOffset.Parse("2026-08-04T00:00:00Z"),
                DateTimeOffset.Parse("2026-08-04T00:00:00Z"),
                DateTimeOffset.Parse("2026-08-04T00:02:00Z")));
        }

        private static void ThrowIfConfigured(string? errorCode)
        {
            if (errorCode is not null)
            {
                throw new InstallmentRepaymentClaimException(errorCode, "configured claim failure");
            }
        }
    }

    private sealed class FakeInstallmentService : IInstallmentService
    {
        public int AppendCalls { get; private set; }

        public int ConfirmPickupCalls { get; private set; }

        public int CancelCalls { get; private set; }

        public int VoidCalls { get; private set; }

        public Task<InstallmentCreateResponse> CreateAsync(InstallmentCreateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<InstallmentAppendPaymentResponse> AppendPaymentAsync(InstallmentAppendPaymentRequest request, CancellationToken cancellationToken)
        {
            AppendCalls++;
            throw new NotSupportedException();
        }

        public Task<InstallmentConfirmPickupResponse> ConfirmPickupAsync(InstallmentConfirmPickupRequest request, CancellationToken cancellationToken)
        {
            ConfirmPickupCalls++;
            throw new NotSupportedException();
        }

        public Task<InstallmentCancelResponse> CancelAsync(InstallmentCancelRequest request, CancellationToken cancellationToken)
        {
            CancelCalls++;
            throw new NotSupportedException();
        }

        public Task<InstallmentVoidResponse> VoidAsync(InstallmentVoidRequest request, CancellationToken cancellationToken)
        {
            VoidCalls++;
            throw new NotSupportedException();
        }
    }

    private sealed class PassThroughCancelClaimService : IInstallmentCancelClaimService
    {
        public Task EnsureLegacyCancelAllowedAsync(Guid installmentGuid, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task EnsureNoBlockingClaimAsync(Guid installmentGuid, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<InstallmentCancelClaimDto> CreateAsync(Guid installmentGuid, InstallmentCancelClaimCreateRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelClaimDto> BeginRefundAsync(Guid installmentGuid, Guid operationGuid, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelClaimDto> GetAsync(Guid installmentGuid, Guid operationGuid, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelClaimDto> ResolveAsync(Guid installmentGuid, Guid operationGuid, InstallmentCancelClaimResolveRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallmentCancelClaimDto> CommitAsync(Guid installmentGuid, Guid operationGuid, InstallmentCancelClaimCommitRequest request, InstallmentRepaymentClaimIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeHistoryService : IInstallmentHistoryService
    {
        public Task<InstallmentHistoryQueryResponse> QueryAsync(InstallmentHistoryQueryRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<InstallmentDetailsDto?> GetDetailsAsync(Guid installmentGuid, CancellationToken cancellationToken) => Task.FromResult<InstallmentDetailsDto?>(null);
    }
}
