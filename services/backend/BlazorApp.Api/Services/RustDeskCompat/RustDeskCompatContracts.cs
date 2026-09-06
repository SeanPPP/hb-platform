using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BlazorApp.Api.Services.RustDeskCompat;

public sealed class RustDeskLoginRequest
{
    [Required, StringLength(100)]
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;
    [Required, StringLength(256)]
    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
    [StringLength(100)]
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    [StringLength(200)]
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }
    [StringLength(20)]
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    [JsonPropertyName("autoLogin")]
    public bool AutoLogin { get; set; }
}

public sealed record RustDeskAuthenticatedUser(string UserGuid, string UserName, string DisplayName);
public sealed record RustDeskLoginResult(string AccessToken, RustDeskAuthenticatedUser User);

/// <summary>通讯录不携带远程密码。在线状态由官方客户端通过 hbbs 查询。</summary>
public sealed record RustDeskPeer(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("hostname")] string Hostname,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("alias")] string Alias,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags);

public interface IRustDeskCompatService
{
    Task<RustDeskLoginResult?> LoginAsync(RustDeskLoginRequest request, string remoteIp, CancellationToken cancellationToken);
    Task<RustDeskAuthenticatedUser?> AuthenticateAsync(string token, CancellationToken cancellationToken);
    Task LogoutAsync(string token, CancellationToken cancellationToken);
    Task<IReadOnlyList<RustDeskPeer>> GetPeersAsync(RustDeskAuthenticatedUser user, CancellationToken cancellationToken);
}
