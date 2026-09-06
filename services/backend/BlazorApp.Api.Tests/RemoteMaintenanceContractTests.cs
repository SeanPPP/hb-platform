using System.Reflection;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class RemoteMaintenanceContractTests
{
    [Fact]
    public void ListDto_DoesNotExposeSecrets()
    {
        var names = typeof(RemoteMaintenanceDeviceListItemDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(names, name => name.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("cipher", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ServiceStatus_OnlyAllowsAgentContractValues()
    {
        Assert.All(
            new[] { "notInstalled", "running", "stopped", "starting", "stopping", "checkFailed" },
            value => Assert.True(RemoteMaintenanceServiceStatuses.IsValid(value)));
        Assert.False(RemoteMaintenanceServiceStatuses.IsValid("installed"));
    }

    [Fact]
    public void AdminEndpoints_RequireSystemAdministratorRole()
    {
        var attributes = typeof(RemoteMaintenanceController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.DeclaringType == typeof(RemoteMaintenanceController))
            .Select(method => method.GetCustomAttribute<AuthorizeAttribute>())
            .Where(attribute => attribute is not null)
            .ToArray();

        Assert.NotEmpty(attributes);
        Assert.All(attributes, attribute => Assert.Contains("Admin", attribute!.Roles, StringComparison.Ordinal));
    }

    [Fact]
    public void SecretModel_ContainsPersistenceOnlyFields()
    {
        Assert.Contains(nameof(RemoteMaintenanceDevice.MonitorTokenHash), typeof(RemoteMaintenanceDevice).GetProperties().Select(x => x.Name));
        Assert.Contains(nameof(RemoteMaintenanceDevice.CredentialCiphertext), typeof(RemoteMaintenanceDevice).GetProperties().Select(x => x.Name));
        Assert.Contains(nameof(RemoteMaintenanceDevice.CommitResponseCiphertext), typeof(RemoteMaintenanceDevice).GetProperties().Select(x => x.Name));
    }

    [Fact]
    public void SqlDateTime2_UnspecifiedIsTreatedAsUtcWithoutLocalTimezoneShift()
    {
        var unspecified = new DateTime(2026, 9, 6, 12, 34, 56, DateTimeKind.Unspecified);

        var normalized = RemoteMaintenanceService.EnsureUtc(unspecified);

        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(unspecified.Ticks, normalized.Ticks);
    }

    [Fact]
    public void DeviceList_EmitsExplicitUtcForSqlDateTime2()
    {
        var timestamp = new DateTime(2026, 9, 6, 12, 34, 56, DateTimeKind.Unspecified);
        var dto = RemoteMaintenanceService.MapListItem(new RemoteMaintenanceDevice
        {
            Id = Guid.NewGuid(), LastSeenAtUtc = timestamp, RegisteredAtUtc = timestamp
        }, DateTime.SpecifyKind(timestamp.AddSeconds(-60), DateTimeKind.Utc));
        var json = System.Text.Json.JsonSerializer.Serialize(dto, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(DateTimeKind.Utc, dto.LastSeenAtUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, dto.RegisteredAtUtc.Kind);
        Assert.Contains("2026-09-06T12:34:56Z", json, StringComparison.Ordinal);
        Assert.Equal("online", dto.OnlineStatus);
    }

    [Fact]
    public void HeartbeatUpdateDoesNotTouchCommitIdentityFields()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "services/backend/BlazorApp.Api/Services/RemoteMaintenanceService.cs"));
        var heartbeat = source[(source.IndexOf("public async Task<RemoteMaintenanceResult<object>> HeartbeatAsync", StringComparison.Ordinal))..];
        var update = heartbeat[..heartbeat.IndexOf("private async Task<POSM_", StringComparison.Ordinal)];

        Assert.Contains("LastAcceptedSequence", update, StringComparison.Ordinal);
        Assert.Contains("LastSeenAtUtc", update, StringComparison.Ordinal);
        Assert.DoesNotContain("RustdeskId =", update, StringComparison.Ordinal);
        Assert.DoesNotContain("ClientVersion =", update, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "services/backend/BlazorApp.Api/Program.cs")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("无法定位仓库根目录");
    }
}
