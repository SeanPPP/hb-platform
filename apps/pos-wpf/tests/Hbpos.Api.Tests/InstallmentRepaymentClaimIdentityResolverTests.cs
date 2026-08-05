using System.Security.Claims;
using BlazorApp.Shared.Constants;
using Hbpos.Api.Auth;
using Hbpos.Api.Services;
using Hbpos.Contracts.Cashiers;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Http;

namespace Hbpos.Api.Tests;

public sealed class InstallmentRepaymentClaimIdentityResolverTests
{
    [Fact]
    public async Task Resolver_uses_device_claims_validated_ticket_and_refreshed_server_session_only()
    {
        var ticket = new CashierAuthorizationTicket(
            "C01",
            "U01",
            "S01",
            "POS-02",
            DateTimeOffset.UtcNow.AddHours(1));
        var session = new CashierSessionDto(
            "C01",
            "U01",
            "Server Cashier Name",
            "S01",
            "POS-02",
            [
                Permissions.PosTerminal.Payment.Confirm,
                Permissions.PosTerminal.Payment.TakeCash,
            ],
            [],
            ["S01"],
            false,
            false,
            false);
        var resolver = new InstallmentRepaymentClaimIdentityResolver(
            new FakeTicketService(ticket),
            new FakeCashierService(session));
        var context = CreateHttpContext("S01", "POS-02");

        var identity = await resolver.ResolveAsync(context, CancellationToken.None);

        Assert.NotNull(identity);
        Assert.Equal("S01", identity.StoreCode);
        Assert.Equal("POS-02", identity.DeviceCode);
        Assert.Equal("C01", identity.CashierId);
        Assert.Equal("Server Cashier Name", identity.CashierName);
        Assert.Equal(session.PermissionCodes, identity.PermissionCodes);
        Assert.Equal("U01", identity.CashierUserGuid);
    }

    [Fact]
    public async Task Resolver_fails_closed_when_refreshed_session_has_no_permission_snapshot()
    {
        var ticket = new CashierAuthorizationTicket(
            "C01",
            "U01",
            "S01",
            "POS-02",
            DateTimeOffset.UtcNow.AddHours(1));
        var session = new CashierSessionDto(
            "C01",
            "U01",
            "Server Cashier Name",
            "S01",
            "POS-02",
            [],
            null!,
            ["S01"],
            false,
            false,
            false);
        var resolver = new InstallmentRepaymentClaimIdentityResolver(
            new FakeTicketService(ticket),
            new FakeCashierService(session));

        var identity = await resolver.ResolveAsync(
            CreateHttpContext("S01", "POS-02"),
            CancellationToken.None);

        Assert.Null(identity);
    }

    [Theory]
    [InlineData(null, "POS-02")]
    [InlineData("S01", null)]
    [InlineData("S02", "POS-02")]
    [InlineData("S01", "POS-99")]
    public async Task Resolver_rejects_missing_or_mismatched_device_claims(
        string? storeCode,
        string? deviceCode)
    {
        var ticket = new CashierAuthorizationTicket(
            "C01",
            "U01",
            "S01",
            "POS-02",
            DateTimeOffset.UtcNow.AddHours(1));
        var resolver = new InstallmentRepaymentClaimIdentityResolver(
            new FakeTicketService(ticket),
            new FakeCashierService(null));
        var context = CreateHttpContext(storeCode, deviceCode);

        var identity = await resolver.ResolveAsync(context, CancellationToken.None);

        Assert.Null(identity);
    }

    private static DefaultHttpContext CreateHttpContext(string? storeCode, string? deviceCode)
    {
        var claims = new List<Claim>();
        if (storeCode is not null)
        {
            claims.Add(new Claim(DeviceAuthConstants.StoreCodeClaim, storeCode));
        }

        if (deviceCode is not null)
        {
            claims.Add(new Claim(DeviceAuthConstants.DeviceCodeClaim, deviceCode));
        }

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };
        context.Request.Headers[CashierAuthorizationConstants.HeaderName] = "validated-ticket";
        return context;
    }

    private sealed class FakeTicketService(CashierAuthorizationTicket? ticket)
        : ICashierAuthorizationTicketService
    {
        public (string Token, DateTimeOffset ExpiresAtUtc) Issue(string cashierId, string userGuid, string storeCode, string deviceCode) => throw new NotSupportedException();

        public CashierAuthorizationTicket? Validate(string? token) =>
            token == "validated-ticket" ? ticket : null;
    }

    private sealed class FakeCashierService(CashierSessionDto? session) : ICashierService
    {
        public Task<CashierSessionDto?> BarcodeLoginAsync(CashierBarcodeLoginRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> HasAnyPermissionAsync(string userGuid, string storeCode, IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CashierSessionDto?> RefreshSessionAsync(CashierAuthorizationTicket ticket, CancellationToken cancellationToken) => Task.FromResult(session);
    }
}
