using System.Security.Claims;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.Security;
using Hbpos.Api.Auth;
using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Hbpos.Contracts.Cashiers;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hbpos.Api.Tests;

public sealed class CashierPermissionAuthorizationTests
{
    [Fact]
    public async Task Handler_rechecks_live_permission_for_device_bound_ticket()
    {
        var cashierService = new FakeCashierService(true);
        var httpContext = CreateHttpContext("ticket", cashierService, new FakeEmergencyGrantService(null));
        var requirement = new CashierPermissionRequirement([Permissions.PosTerminal.Payment.Confirm]);
        var context = new AuthorizationHandlerContext([requirement], httpContext.User, null);
        var handler = new CashierPermissionAuthorizationHandler(
            new HttpContextAccessor { HttpContext = httpContext },
            new FakeTicketService(new CashierAuthorizationTicket(
                "C001", "U001", "S001", "POS-01", DateTimeOffset.UtcNow.AddHours(1))));

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        Assert.Equal(Permissions.PosTerminal.Payment.Confirm, Assert.Single(cashierService.CheckedPermissions));
        Assert.Equal("C001", httpContext.Items[CashierAuthorizationContext.CashierIdItemKey]);
    }

    [Fact]
    public async Task Handler_rejects_ticket_from_another_device_before_database_check()
    {
        var cashierService = new FakeCashierService(true);
        var httpContext = CreateHttpContext("ticket", cashierService, new FakeEmergencyGrantService(null));
        var requirement = new CashierPermissionRequirement([Permissions.PosTerminal.Payment.Confirm]);
        var context = new AuthorizationHandlerContext([requirement], httpContext.User, null);
        var handler = new CashierPermissionAuthorizationHandler(
            new HttpContextAccessor { HttpContext = httpContext },
            new FakeTicketService(new CashierAuthorizationTicket(
                "C001", "U001", "S001", "POS-02", DateTimeOffset.UtcNow.AddHours(1))));

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.Empty(cashierService.CheckedPermissions);
    }

    [Theory]
    [InlineData("HBPOSE1-test-token")]
    [InlineData("HBPOSE2-test-token")]
    public async Task Handler_accepts_both_emergency_token_versions_without_cashier_database_identity(string token)
    {
        var cashierService = new FakeCashierService(false);
        var emergencyGrantService = new FakeEmergencyGrantService(new EmergencyLoginVerifiedClaims(
            Guid.NewGuid(),
            "S001",
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddHours(1)));
        var httpContext = CreateHttpContext(token, cashierService, emergencyGrantService);
        var requirement = new CashierPermissionRequirement([Permissions.PosTerminal.Returns.Confirm]);
        var context = new AuthorizationHandlerContext([requirement], httpContext.User, null);
        var handler = new CashierPermissionAuthorizationHandler(
            new HttpContextAccessor { HttpContext = httpContext },
            new FakeTicketService(null));

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Handler_audit_mode_records_boundary_without_blocking_device_authenticated_request()
    {
        var httpContext = CreateHttpContext(
            string.Empty,
            new FakeCashierService(false),
            new FakeEmergencyGrantService(null));
        var requirement = new CashierPermissionRequirement([Permissions.PosTerminal.Payment.Confirm]);
        var context = new AuthorizationHandlerContext([requirement], httpContext.User, null);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CashierAuthorization:Mode"] = "Audit"
            })
            .Build();
        var handler = new CashierPermissionAuthorizationHandler(
            new HttpContextAccessor { HttpContext = httpContext },
            new FakeTicketService(null),
            configuration);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public void Take_card_policy_requires_take_card_and_confirm_permissions_separately()
    {
        var options = new AuthorizationOptions();
        CashierAuthorizationPolicies.AddPolicies(options);

        var policy = options.GetPolicy(CashierAuthorizationPolicies.TakeCard);
        Assert.NotNull(policy);
        var requirements = policy.Requirements
            .OfType<CashierPermissionRequirement>()
            .ToArray();

        Assert.Equal(2, requirements.Length);
        Assert.Contains(requirements, requirement =>
            requirement.PermissionCodes.SequenceEqual([Permissions.PosTerminal.Payment.TakeCard]));
        Assert.Contains(requirements, requirement =>
            requirement.PermissionCodes.SequenceEqual([Permissions.PosTerminal.Payment.Confirm]));
    }

    [Fact]
    public async Task Take_card_policy_rejects_ticket_with_only_take_card_permission()
    {
        var options = new AuthorizationOptions();
        CashierAuthorizationPolicies.AddPolicies(options);
        var policy = options.GetPolicy(CashierAuthorizationPolicies.TakeCard);
        Assert.NotNull(policy);
        var cashierService = new SelectiveCashierService(Permissions.PosTerminal.Payment.TakeCard);
        var httpContext = CreateHttpContext("ticket", cashierService, new FakeEmergencyGrantService(null));
        var context = new AuthorizationHandlerContext(policy.Requirements, httpContext.User, null);
        var handler = new CashierPermissionAuthorizationHandler(
            new HttpContextAccessor { HttpContext = httpContext },
            new FakeTicketService(new CashierAuthorizationTicket(
                "C001", "U001", "S001", "POS-01", DateTimeOffset.UtcNow.AddHours(1))));

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.Contains(Permissions.PosTerminal.Payment.TakeCard, cashierService.CheckedPermissions);
        Assert.Contains(Permissions.PosTerminal.Payment.Confirm, cashierService.CheckedPermissions);
    }

    [Fact]
    public void Sensitive_policies_map_to_their_specific_pos_permissions()
    {
        var options = new AuthorizationOptions();
        CashierAuthorizationPolicies.AddPolicies(options);

        AssertPolicyPermissions(
            options,
            CashierAuthorizationPolicies.VoucherRefund,
            Permissions.PosTerminal.Returns.Confirm,
            Permissions.PosTerminal.Installments.Cancel);
        AssertPolicyPermissions(
            options,
            CashierAuthorizationPolicies.InstallmentView,
            Permissions.PosTerminal.Installments.View);
        AssertPolicyPermissions(
            options,
            CashierAuthorizationPolicies.DeviceRegistration,
            Permissions.PosTerminal.Settings.DeviceRegistration);

        Assert.Equal(
            CashierAuthorizationPolicies.InstallmentView,
            typeof(InstallmentsController).GetMethod(nameof(InstallmentsController.History))?
                .GetCustomAttributes(typeof(AuthorizeAttribute), false)
                .Cast<AuthorizeAttribute>()
                .Single()
                .Policy);
        Assert.Equal(
            CashierAuthorizationPolicies.InstallmentView,
            typeof(InstallmentsController).GetMethod(nameof(InstallmentsController.Details))?
                .GetCustomAttributes(typeof(AuthorizeAttribute), false)
                .Cast<AuthorizeAttribute>()
                .Single()
                .Policy);
    }

    private static void AssertPolicyPermissions(
        AuthorizationOptions options,
        string policyName,
        params string[] permissions)
    {
        var policy = options.GetPolicy(policyName);
        Assert.NotNull(policy);
        var requirement = Assert.Single(policy.Requirements.OfType<CashierPermissionRequirement>());
        Assert.Equal(permissions, requirement.PermissionCodes);
    }

    private static DefaultHttpContext CreateHttpContext(
        string token,
        ICashierService cashierService,
        IEmergencyGrantAuthorizationService emergencyGrantService)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = new ServiceCollection()
            .AddSingleton(cashierService)
            .AddSingleton(emergencyGrantService)
            .BuildServiceProvider();
        httpContext.Request.Headers[CashierAuthorizationConstants.HeaderName] = token;
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(DeviceAuthConstants.StoreCodeClaim, "S001"),
            new Claim(DeviceAuthConstants.DeviceCodeClaim, "POS-01")
        ], "test"));
        return httpContext;
    }

    private sealed class FakeTicketService(CashierAuthorizationTicket? ticket)
        : ICashierAuthorizationTicketService
    {
        public (string Token, DateTimeOffset ExpiresAtUtc) Issue(
            string cashierId,
            string userGuid,
            string storeCode,
            string deviceCode) => throw new NotSupportedException();

        public CashierAuthorizationTicket? Validate(string? token) => token == "ticket" ? ticket : null;
    }

    private sealed class FakeCashierService(bool allowed) : ICashierService
    {
        public List<string> CheckedPermissions { get; } = [];

        public Task<CashierSessionDto?> BarcodeLoginAsync(
            CashierBarcodeLoginRequest request,
            CancellationToken cancellationToken) => Task.FromResult<CashierSessionDto?>(null);

        public Task<CashierSessionDto?> RefreshSessionAsync(
            CashierAuthorizationTicket ticket,
            CancellationToken cancellationToken) => Task.FromResult<CashierSessionDto?>(null);

        public Task<bool> HasAnyPermissionAsync(
            string userGuid,
            string storeCode,
            IReadOnlyCollection<string> permissionCodes,
            CancellationToken cancellationToken)
        {
            CheckedPermissions.AddRange(permissionCodes);
            return Task.FromResult(allowed);
        }
    }

    private sealed class SelectiveCashierService(params string[] allowedPermissions) : ICashierService
    {
        public List<string> CheckedPermissions { get; } = [];

        public Task<CashierSessionDto?> BarcodeLoginAsync(
            CashierBarcodeLoginRequest request,
            CancellationToken cancellationToken) => Task.FromResult<CashierSessionDto?>(null);

        public Task<CashierSessionDto?> RefreshSessionAsync(
            CashierAuthorizationTicket ticket,
            CancellationToken cancellationToken) => Task.FromResult<CashierSessionDto?>(null);

        public Task<bool> HasAnyPermissionAsync(
            string userGuid,
            string storeCode,
            IReadOnlyCollection<string> permissionCodes,
            CancellationToken cancellationToken)
        {
            CheckedPermissions.AddRange(permissionCodes);
            return Task.FromResult(permissionCodes.Any(permission =>
                allowedPermissions.Contains(permission, StringComparer.OrdinalIgnoreCase)));
        }
    }

    private sealed class FakeEmergencyGrantService(EmergencyLoginVerifiedClaims? claims)
        : IEmergencyGrantAuthorizationService
    {
        public Task<EmergencyLoginVerifiedClaims?> ValidateAsync(
            string? token,
            string deviceStoreCode,
            CancellationToken cancellationToken) => Task.FromResult(claims);
    }
}
