using System.Text.Json;
using Hbpos.Contracts.Linkly;

namespace Hbpos.Api.Tests;

public sealed class LinklyCloudTerminalContractsTests
{
    [Fact]
    public void Pos_terminal_list_contract_contains_only_safe_terminal_metadata()
    {
        var response = new LinklyCloudTerminalListResponse(
            "Production",
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            7,
            [
                new LinklyCloudTerminalSummary(
                    Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
                    1,
                    "Front Counter",
                    "Ready",
                    false,
                    true,
                    "Ready",
                    DateTimeOffset.Parse("2026-09-02T00:00:00Z"))
            ]);

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("selectedTerminalId", json, StringComparison.Ordinal);
        Assert.Contains("selectionRevision", json, StringComparison.Ordinal);
        Assert.Contains("\"mode\":\"Legacy\"", json, StringComparison.Ordinal);
        Assert.Contains("displayName", json, StringComparison.Ordinal);
        Assert.DoesNotContain("username", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("posId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pos_terminal_list_contract_can_identify_active_mode_without_terminals()
    {
        var response = new LinklyCloudTerminalListResponse(
            "Production",
            null,
            null,
            [],
            "Active");

        Assert.Equal("Active", response.Mode);
        Assert.Empty(response.Terminals);
    }

    [Fact]
    public void Pair_response_never_exposes_credentials_or_terminal_secret()
    {
        var response = new LinklyCloudTerminalPairResponse(
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            "Sandbox",
            "Lane 1",
            "Ready",
            true,
            "Linkly Cloud terminal paired successfully.");

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("terminalId", json, StringComparison.Ordinal);
        Assert.Contains("pairingState", json, StringComparison.Ordinal);
        Assert.DoesNotContain("username", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("posId", json, StringComparison.OrdinalIgnoreCase);
    }
}
