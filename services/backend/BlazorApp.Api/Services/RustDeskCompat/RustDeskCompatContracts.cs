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
public sealed record RustDeskPeerPage(int Total, IReadOnlyList<RustDeskPeer> Data);

/// <summary>只有管理员共享通讯录可携带连接密码；其他列表的 Password 保持为空。</summary>
public sealed record RustDeskPeer(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("hostname")] string Hostname,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("alias")] string Alias,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags)
{
    [JsonPropertyName("password")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Password { get; init; }

    // record 默认 ToString 会展开全部属性，禁止把同步凭据带入诊断文本。
    public override string ToString() => $"RustDeskPeer {{ Id = {Id} }}";
}

public interface IRustDeskCompatService
{
    Task<RustDeskLoginResult?> LoginAsync(RustDeskLoginRequest request, string remoteIp, CancellationToken cancellationToken);
    Task<RustDeskAuthenticatedUser?> AuthenticateAsync(string token, CancellationToken cancellationToken);
    Task LogoutAsync(string token, CancellationToken cancellationToken);
    Task<IReadOnlyList<RustDeskPeer>> GetPeersAsync(RustDeskAuthenticatedUser user, CancellationToken cancellationToken);
    Task<RustDeskPeerPage> GetAddressBookPeersAsync(RustDeskAuthenticatedUser user, int current, int pageSize, CancellationToken cancellationToken);
}
