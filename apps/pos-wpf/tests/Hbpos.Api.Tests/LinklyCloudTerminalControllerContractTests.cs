using System.Security.Claims;
using Hbpos.Api.Auth;
using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.Linkly;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hbpos.Api.Tests;

public sealed class LinklyCloudTerminalControllerContractTests
{
    [Theory]
    [InlineData(nameof(LinklyController.GetCloudBackendTerminals), "cloud-backend/terminals", CashierAuthorizationPolicies.PaymentTerminalSelection)]
    [InlineData(nameof(LinklyController.SelectCloudBackendTerminal), "cloud-backend/terminal-selection", CashierAuthorizationPolicies.PaymentTerminalSelection)]
    [InlineData(nameof(LinklyController.GetCloudBackendHealth), "cloud-backend/health", CashierAuthorizationPolicies.PaymentTerminalSelection)]
    [InlineData(nameof(LinklyController.PairCloudBackendTerminal), "cloud-backend/terminals/{terminalId:guid}/pair", CashierAuthorizationPolicies.PaymentSettings)]
    [InlineData(nameof(LinklyController.TestCloudBackendTerminal), "cloud-backend/terminals/{terminalId:guid}/connection-test", CashierAuthorizationPolicies.PaymentSettings)]
    [InlineData(nameof(LinklyController.AssignCloudBackendTerminal), "cloud-backend/terminals/{terminalId:guid}/assignment", CashierAuthorizationPolicies.PaymentSettings)]
    [InlineData(nameof(LinklyController.PairCloudBackend), "cloud-backend/pair", CashierAuthorizationPolicies.PaymentSettings)]
    [InlineData(nameof(LinklyController.RunCloudBackendLogonTest), "cloud-backend/logon-test", CashierAuthorizationPolicies.PaymentSettings)]
    public void Multi_terminal_routes_use_claim_scoped_permissions(
        string methodName,
        string expectedTemplate,
        string expectedPolicy)
    {
        var method = typeof(LinklyController).GetMethod(methodName);

        Assert.NotNull(method);
        var route = method!.GetCustomAttributes(inherit: true)
            .OfType<HttpMethodAttribute>()
            .Single();
        var authorization = method.GetCustomAttributes(inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();
        Assert.Equal(expectedTemplate, route.Template);
        Assert.Equal(expectedPolicy, authorization.Policy);
    }

    [Theory]
    [InlineData(nameof(LinklyController.TestCloudBackendTerminal), typeof(LinklyCloudTerminalConnectionTestResponse))]
    [InlineData(nameof(LinklyController.AssignCloudBackendTerminal), typeof(LinklyCloudTerminalListResponse))]
    public void New_terminal_management_routes_declare_typed_success_and_error_responses(string methodName, Type responseType)
    {
        var method = typeof(LinklyController).GetMethod(methodName)!;
        var responseTypes = method.GetCustomAttributes(inherit: true).OfType<ProducesResponseTypeAttribute>().ToArray();
        Assert.Contains(responseTypes, item => item.StatusCode == 200 && item.Type == typeof(ApiResult<>).MakeGenericType(responseType));
        Assert.Contains(responseTypes, item => item.StatusCode == 400);
        Assert.Contains(responseTypes, item => item.StatusCode == 404);
        Assert.Contains(responseTypes, item => item.StatusCode == 409);
    }

    [Fact]
    public async Task New_terminal_management_routes_reject_null_body_as_bad_request()
    {
        var controller = CreateController(new CredentialFailureTerminalService(new LinklyCloudTerminalSelectionConflictException()));
        var id = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var connection = await controller.TestCloudBackendTerminal(id, null!, CancellationToken.None);
        var assignment = await controller.AssignCloudBackendTerminal(id, null!, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(connection.Result);
        Assert.IsType<BadRequestObjectResult>(assignment.Result);
    }

    [Fact]
    public async Task New_terminal_management_routes_map_busy_to_conflict()
    {
        var controller = CreateController(new CredentialFailureTerminalService(new LinklyCloudTerminalSelectionConflictException("busy")));
        var id = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var connection = await controller.TestCloudBackendTerminal(id,
            new LinklyCloudTerminalConnectionTestRequest("Production", "1", null, 0), CancellationToken.None);
        var assignment = await controller.AssignCloudBackendTerminal(id,
            new LinklyCloudTerminalAssignmentRequest("Production", "1", null, 0, null, null, 0), CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(connection.Result);
        Assert.IsType<ConflictObjectResult>(assignment.Result);
    }

    [Fact]
    public void Transaction_contract_keeps_terminal_selection_explicit_and_optional_for_legacy()
    {
        var request = new LinklyCloudBackendTransactionRequest(
            "Production",
            "P",
            100,
            null,
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            5);

        Assert.Equal(5, request.SelectionRevision);
        Assert.NotNull(request.TerminalId);

        var legacy = new LinklyCloudBackendTransactionRequest("Production", "P", 100, null);
        Assert.Null(legacy.TerminalId);
        Assert.Null(legacy.SelectionRevision);
    }

    [Fact]
    public async Task SelectCloudBackendTerminal_returns_stable_conflict_when_terminal_is_assigned()
    {
        var requestServices = new ServiceCollection()
            .AddSingleton<ILinklyCloudTerminalService>(new AssignedTerminalService())
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = requestServices,
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(DeviceAuthConstants.StoreCodeClaim, "S01"),
                new Claim(DeviceAuthConstants.DeviceCodeClaim, "POS-01")
            ], "Test"))
        };
        var controller = new LinklyController(
            new NoOpLinklyCloudCredentialService(),
            new NoOpLinklyCloudBackendAsyncService(),
            new NoOpLinklyCloudPairingService())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var action = await controller.SelectCloudBackendTerminal(
            new LinklyCloudTerminalSelectionRequest(
                "Production",
                Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
                0),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(action.Result);
        var envelope = Assert.IsType<ApiResult<LinklyCloudTerminalSelectionResponse>>(conflict.Value);
        Assert.False(envelope.Success);
        Assert.Equal("LINKLY_CLOUD_TERMINAL_ASSIGNED", envelope.ErrorCode);
    }

    [Theory]
    [InlineData(true, "LINKLY_CLOUD_TERMINAL_CREDENTIAL_REENTRY_REQUIRED", "Linkly Cloud terminal credentials must be re-entered in the management portal.")]
    [InlineData(false, "LINKLY_CLOUD_TERMINAL_CREDENTIAL_UNAVAILABLE", "Linkly Cloud terminal credentials are unavailable. Re-enter them in the management portal.")]
    public async Task Terminal_selection_returns_fixed_safe_credential_failure(
        bool reentryRequired,
        string expectedCode,
        string expectedMessage)
    {
        var controller = CreateController(new CredentialFailureTerminalService(
            reentryRequired
                ? new LinklyCloudTerminalCredentialReentryRequiredException()
                : new LinklyCloudTerminalCredentialUnavailableException()));

        var action = await controller.SelectCloudBackendTerminal(
            new LinklyCloudTerminalSelectionRequest(
                "Production",
                Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
                0),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(action.Result);
        var envelope = Assert.IsType<ApiResult<LinklyCloudTerminalSelectionResponse>>(conflict.Value);
        Assert.Equal(expectedCode, envelope.ErrorCode);
        Assert.Equal(expectedMessage, envelope.Message);
    }

    [Theory]
    [InlineData(true, "LINKLY_CLOUD_TERMINAL_CREDENTIAL_REENTRY_REQUIRED", "Linkly Cloud terminal credentials must be re-entered in the management portal.")]
    [InlineData(false, "LINKLY_CLOUD_TERMINAL_CREDENTIAL_UNAVAILABLE", "Linkly Cloud terminal credentials are unavailable. Re-enter them in the management portal.")]
    public async Task Terminal_pairing_returns_fixed_safe_credential_failure(
        bool reentryRequired,
        string expectedCode,
        string expectedMessage)
    {
        var controller = CreateController(new CredentialFailureTerminalService(
            reentryRequired
                ? new LinklyCloudTerminalCredentialReentryRequiredException()
                : new LinklyCloudTerminalCredentialUnavailableException()));

        var action = await controller.PairCloudBackendTerminal(
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            new LinklyCloudBackendPairRequest("Production", "123456"),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(action.Result);
        var envelope = Assert.IsType<ApiResult<LinklyCloudTerminalPairResponse>>(conflict.Value);
        Assert.Equal(expectedCode, envelope.ErrorCode);
        Assert.Equal(expectedMessage, envelope.Message);
    }

    private static LinklyController CreateController(ILinklyCloudTerminalService terminalService)
    {
        var requestServices = new ServiceCollection()
            .AddSingleton(terminalService)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = requestServices,
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(DeviceAuthConstants.StoreCodeClaim, "S01"),
                new Claim(DeviceAuthConstants.DeviceCodeClaim, "POS-01")
            ], "Test"))
        };
        return new LinklyController(
            new NoOpLinklyCloudCredentialService(),
            new NoOpLinklyCloudBackendAsyncService(),
            new NoOpLinklyCloudPairingService())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private sealed class AssignedTerminalService : ILinklyCloudTerminalService
    {
        public Task<LinklyCloudTerminalListResponse> GetTerminalsAsync(
            string storeCode,
            string deviceCode,
            string environment,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> GetConfigurationModeAsync(
            string environment,
            string storeCode,
            CancellationToken cancellationToken) => Task.FromResult("Active");

        public Task<LinklyCloudTerminalSelectionResponse> SelectTerminalAsync(
            string storeCode,
            string deviceCode,
            LinklyCloudTerminalSelectionRequest request,
            string? updatedBy,
            CancellationToken cancellationToken) => throw new LinklyCloudTerminalAssignedException();

        public Task<LinklyCloudTerminalPairResponse> PairTerminalAsync(
            string storeCode,
            string deviceCode,
            Guid terminalId,
            LinklyCloudBackendPairRequest request,
            string? updatedBy,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LinklyCloudTerminalPaymentContext?> ResolvePaymentTerminalAsync(
            string environment,
            string storeCode,
            string deviceCode,
            Guid? terminalId,
            long? selectionRevision,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LinklyCloudTerminalRecord?> GetTerminalAsync(
            string environment,
            string storeCode,
            Guid terminalId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LinklyCloudTerminalOperationLease> AcquireOperationLeaseAsync(
            string environment,
            string storeCode,
            string deviceCode,
            LinklyCloudTerminalPaymentContext terminalContext,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ReleaseOperationLeaseAsync(
            string environment,
            string storeCode,
            Guid terminalId,
            Guid leaseId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RecordHealthAsync(
            LinklyCloudTerminalPaymentContext terminalContext,
            string healthStatus,
            DateTime checkedAt,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CredentialFailureTerminalService(Exception exception)
        : ILinklyCloudTerminalService
    {
        public Task<LinklyCloudTerminalListResponse> GetTerminalsAsync(
            string storeCode, string deviceCode, string environment,
            CancellationToken cancellationToken) => throw exception;

        public Task<string> GetConfigurationModeAsync(
            string environment, string storeCode, CancellationToken cancellationToken) =>
            Task.FromResult("Active");

        public Task<LinklyCloudTerminalSelectionResponse> SelectTerminalAsync(
            string storeCode, string deviceCode, LinklyCloudTerminalSelectionRequest request,
            string? updatedBy, CancellationToken cancellationToken) => throw exception;

        public Task<LinklyCloudTerminalPairResponse> PairTerminalAsync(
            string storeCode, string deviceCode, Guid terminalId,
            LinklyCloudBackendPairRequest request, string? updatedBy,
            CancellationToken cancellationToken) => throw exception;

        public Task<LinklyCloudTerminalPaymentContext?> ResolvePaymentTerminalAsync(
            string environment, string storeCode, string deviceCode, Guid? terminalId,
            long? selectionRevision, CancellationToken cancellationToken) => throw exception;

        public Task<LinklyCloudTerminalRecord?> GetTerminalAsync(
            string environment, string storeCode, Guid terminalId,
            CancellationToken cancellationToken) => throw exception;

        public Task<LinklyCloudTerminalOperationLease> AcquireOperationLeaseAsync(
            string environment, string storeCode, string deviceCode,
            LinklyCloudTerminalPaymentContext terminalContext,
            CancellationToken cancellationToken) => throw exception;

        public Task ReleaseOperationLeaseAsync(
            string environment, string storeCode, Guid terminalId, Guid leaseId,
            CancellationToken cancellationToken) => throw exception;

        public Task<bool> RecordHealthAsync(
            LinklyCloudTerminalPaymentContext terminalContext,
            string healthStatus,
            DateTime checkedAt,
            CancellationToken cancellationToken) => throw exception;

        public Task<LinklyCloudTerminalConnectionTestResponse> ConnectionTestAsync(
            string storeCode, string deviceCode, Guid terminalId,
            LinklyCloudTerminalConnectionTestRequest request,
            CancellationToken cancellationToken) => throw exception;

        public Task<LinklyCloudTerminalListResponse> AssignTerminalAsync(
            string storeCode, string deviceCode, Guid terminalId,
            LinklyCloudTerminalAssignmentRequest request, string? updatedBy,
            CancellationToken cancellationToken) => throw exception;
    }
}
