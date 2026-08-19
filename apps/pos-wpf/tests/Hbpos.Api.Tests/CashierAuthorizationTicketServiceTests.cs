using System.Text.Json;
using Hbpos.Api.Auth;
using Microsoft.AspNetCore.DataProtection;

namespace Hbpos.Api.Tests;

public sealed class CashierAuthorizationTicketServiceTests
{
    [Fact]
    public void Issue_stamps_server_time_and_validate_keeps_it()
    {
        var now = new DateTimeOffset(2026, 8, 18, 2, 0, 0, TimeSpan.Zero);
        var provider = new EphemeralDataProtectionProvider();
        var service = new CashierAuthorizationTicketService(provider, new FixedTimeProvider(now));

        var issued = service.Issue("C001", "U001", "1042", "POS-01");
        var ticket = service.Validate(issued.Token);

        Assert.NotNull(ticket);
        Assert.Equal(now, ticket.IssuedAtUtc);
        Assert.Equal(now, ticket.BarcodeAuthenticatedAtUtc);
        Assert.Equal(now.AddHours(24), ticket.ExpiresAtUtc);
    }

    [Fact]
    public void Issue_can_bind_a_ticket_to_the_authenticated_hardware()
    {
        var now = new DateTimeOffset(2026, 8, 18, 2, 0, 0, TimeSpan.Zero);
        var provider = new EphemeralDataProtectionProvider();
        var service = new CashierAuthorizationTicketService(provider, new FixedTimeProvider(now));
        var issued = service.Issue("C001", "U001", "1042", "POS-01", "HW-01");
        var ticket = service.Validate(issued.Token);

        Assert.NotNull(ticket);
        Assert.Equal("HW-01", ticket.HardwareId);
        Assert.Equal(now, ticket.BarcodeAuthenticatedAtUtc);
    }

    [Fact]
    public void Refreshed_issue_preserves_the_original_barcode_authentication_time()
    {
        var now = new DateTimeOffset(2026, 8, 18, 2, 5, 0, TimeSpan.Zero);
        var barcodeAuthenticatedAtUtc = now.AddMinutes(-3);
        var provider = new EphemeralDataProtectionProvider();
        var service = new CashierAuthorizationTicketService(provider, new FixedTimeProvider(now));

        var issued = service.Issue(
            "C001",
            "U001",
            "1042",
            "POS-01",
            "HW-01",
            barcodeAuthenticatedAtUtc);
        var ticket = service.Validate(issued.Token);

        Assert.NotNull(ticket);
        Assert.Equal(now, ticket.IssuedAtUtc);
        Assert.Equal(barcodeAuthenticatedAtUtc, ticket.BarcodeAuthenticatedAtUtc);
    }

    [Fact]
    public void Validate_accepts_legacy_ticket_without_issued_time_for_existing_endpoints()
    {
        var now = new DateTimeOffset(2026, 8, 18, 2, 0, 0, TimeSpan.Zero);
        var provider = new EphemeralDataProtectionProvider();
        var legacyJson = JsonSerializer.Serialize(new
        {
            cashierId = "C001",
            userGuid = "U001",
            storeCode = "1042",
            deviceCode = "POS-01",
            expiresAtUtc = now.AddHours(1)
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var token = provider
            .CreateProtector("Hbpos.CashierAuthorization.v1")
            .Protect(legacyJson);
        var service = new CashierAuthorizationTicketService(provider, new FixedTimeProvider(now));

        var ticket = service.Validate(token);

        Assert.NotNull(ticket);
        Assert.Null(ticket.IssuedAtUtc);
        Assert.Null(ticket.BarcodeAuthenticatedAtUtc);
        Assert.Null(ticket.HardwareId);
    }

    [Fact]
    public void Hardware_issue_default_implementation_fails_closed()
    {
        ICashierAuthorizationTicketService service = new HardwareDefaultingTicketService();

        Assert.Throws<NotSupportedException>(() =>
            service.Issue("C001", "U001", "1042", "POS-01", "HW-01"));
    }

    private sealed class HardwareDefaultingTicketService : ICashierAuthorizationTicketService
    {
        public (string Token, DateTimeOffset ExpiresAtUtc) Issue(
            string cashierId,
            string userGuid,
            string storeCode,
            string deviceCode) => ("test-ticket", DateTimeOffset.UtcNow.AddHours(1));

        public CashierAuthorizationTicket? Validate(string? token) => null;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
