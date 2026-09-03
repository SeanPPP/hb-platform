using System.Text.Json;
using BlazorApp.Shared.DTOs;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ContainerDetailCollaborationContractTests
{
    [Fact]
    public void PresenceUser_公开最近活动字段必须是lastActiveAt()
    {
        var json = JsonSerializer.Serialize(
            new ContainerDetailPresenceUserDto { UserGuid = "U", UserName = "用户", LastActiveAt = DateTime.UtcNow },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );

        Assert.Contains("\"lastActiveAt\"", json);
        Assert.DoesNotContain("lastActiveAtUtc", json, StringComparison.Ordinal);
    }
}
