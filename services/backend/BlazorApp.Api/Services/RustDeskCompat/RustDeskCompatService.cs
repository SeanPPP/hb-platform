using System.Security.Cryptography;
using System.Text;
using BlazorApp.Api.Data;
using BlazorApp.Api.Security;
using BlazorApp.Api.Utils;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using SqlSugar;
using Microsoft.Extensions.Options;

namespace BlazorApp.Api.Services.RustDeskCompat;

/// <summary>
/// RustDesk 专用兼容服务。
///
/// 该服务故意不调用普通 AuthService 的 token 流程：RustDesk 只得到本用途的
/// opaque session，且每次使用都会重新检查用户状态、管理员角色和当前密码指纹。
/// </summary>
public sealed class RustDeskCompatService : IRustDeskCompatService
{
    private const int SessionLifetimeDays = 30;
    private const int TokenBytes = 32;
    private const int MaxUsernameLength = 100;
    private const int MaxPasswordLength = 256;
    private const int MaxRustdeskIdLength = 100;
    private const string WindowsDeviceSystem = "Windows";
    private const string SchemaNotReadyMessage = "RustDesk 专用数据库尚未准备就绪。";
    private static readonly string[] AdministratorRoles =
        ["Admin", "管理员", "SuperAdmin", "超级管理员"];

    private readonly SqlSugarContext _dbContext;
    private readonly POSMSqlSugarContext _posmContext;
    private readonly RustDeskCompatSchemaReadiness _schemaReadiness;
    private readonly TimeProvider _timeProvider;
    private readonly RemoteMaintenanceSecretProtector _secretProtector;
    private readonly IOptions<RemoteMaintenanceOptions> _remoteMaintenanceOptions;
    private readonly ILogger<RustDeskCompatService> _logger;

    public RustDeskCompatService(
        SqlSugarContext dbContext,
        POSMSqlSugarContext posmContext,
        RustDeskCompatSchemaReadiness schemaReadiness,
        RemoteMaintenanceSecretProtector secretProtector,
        IOptions<RemoteMaintenanceOptions> remoteMaintenanceOptions,
        ILogger<RustDeskCompatService> logger,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _posmContext = posmContext;
        _schemaReadiness = schemaReadiness;
        _secretProtector = secretProtector;
        _remoteMaintenanceOptions = remoteMaintenanceOptions;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<RustDeskLoginResult?> LoginAsync(
        RustDeskLoginRequest request,
        string remoteIp,
        CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var username = Normalize(request.Username);
        if (string.IsNullOrWhiteSpace(username)
            || username.Length > MaxUsernameLength
            || string.IsNullOrEmpty(request.Password)
            || request.Password.Length > MaxPasswordLength)
            return null;
        username = username.ToLowerInvariant();

        // RustDesk 必须提交原始密码；不接受 LoginRequest 的 clientSha256 兼容路径。
        var user = await _dbContext.Db.Queryable<User>()
            .Where(x => x.Username.ToLower() == username && x.IsActive && !x.IsDeleted)
            .FirstAsync();
        if (user is null
            || !PasswordHasher.VerifyPassword(
                request.Password,
                user.PasswordHash,
                PasswordHasher.PasswordFormatRaw,
                out _))
            return null;

        if (!await IsActiveAdministratorAsync(user.UserGUID, cancellationToken))
            return null;

        var now = UtcNow();
        var token = GenerateToken();
        var session = new RustDeskClientSession
        {
            Id = Guid.NewGuid(),
            UserGuid = user.UserGUID,
            TokenHash = Hash(token),
            PasswordFingerprint = Fingerprint(user.PasswordHash),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(SessionLifetimeDays),
        };

        cancellationToken.ThrowIfCancellationRequested();
        await _dbContext.Db.Insertable(session).ExecuteCommandAsync();
        return new RustDeskLoginResult(token, ToAuthenticatedUser(user));
    }

    public async Task<RustDeskAuthenticatedUser?> AuthenticateAsync(
        string token,
        CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedToken = token?.Trim();
        if (!IsOpaqueToken(normalizedToken)) return null;
        var tokenHash = Hash(normalizedToken!);
        var now = UtcNow();
        var session = await _dbContext.Db.Queryable<RustDeskClientSession>()
            .Where(x => x.TokenHash == tokenHash
                && x.RevokedAtUtc == null
                && x.ExpiresAtUtc > now)
            .FirstAsync();
        if (session is null) return null;

        var user = await GetActiveUserAsync(session.UserGuid, cancellationToken);
        if (user is null
            || !FixedTimeEquals(session.PasswordFingerprint, Fingerprint(user.PasswordHash))
            || !await IsActiveAdministratorAsync(user.UserGUID, cancellationToken))
            return null;

        return ToAuthenticatedUser(user);
    }

    public async Task LogoutAsync(string token, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedToken = token?.Trim();
        if (!IsOpaqueToken(normalizedToken)) return;

        var tokenHash = Hash(normalizedToken!);
        var revokedAtUtc = UtcNow();
        await _dbContext.Db.Updateable<RustDeskClientSession>()
            .SetColumns(x => new RustDeskClientSession { RevokedAtUtc = revokedAtUtc })
            .Where(x => x.TokenHash == tokenHash && x.RevokedAtUtc == null)
            .ExecuteCommandAsync();
    }

    public async Task<IReadOnlyList<RustDeskPeer>> GetPeersAsync(
        RustDeskAuthenticatedUser user,
        CancellationToken cancellationToken) =>
        (await GetPeerPageAsync(user, null, 100, cancellationToken)).Data;

    public Task<RustDeskPeerPage> GetAddressBookPeersAsync(
        RustDeskAuthenticatedUser user, int current, int pageSize, CancellationToken cancellationToken)
    {
        if (current < 1 || pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(current), "Invalid address book page");
        return GetPeerPageAsync(user, current, pageSize, cancellationToken);
    }

    private async Task<RustDeskPeerPage> GetPeerPageAsync(
        RustDeskAuthenticatedUser user, int? current, int pageSize, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Controller 传入的身份也必须重新从数据库核验，避免 stale principal 继续读通讯录。
        var activeUser = await GetActiveUserAsync(user.UserGuid, cancellationToken);
        if (activeUser is null
            || !await IsActiveAdministratorAsync(activeUser.UserGUID, cancellationToken))
            return new(0, []);

        var managed = await _dbContext.Db.Queryable<RustDeskManagedDevice>()
            .Where(x => !x.IsDisabled && x.RustdeskId != "")
            .Select(x => new RustDeskManagedDevice
            {
                Id = x.Id, RustdeskId = x.RustdeskId, Alias = x.Alias,
                Hostname = x.Hostname, Platform = x.Platform,
            })
            .ToListAsync();
        var peers = new List<RustDeskPeer>(managed.Count);
        var credentialSources = new Dictionary<string, RemoteMaintenanceDevice>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in managed)
        {
            var id = NormalizeBounded(device.RustdeskId, MaxRustdeskIdLength);
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) continue;
            peers.Add(new RustDeskPeer(
                id,
                id,
                NormalizeDisplay(device.Hostname),
                NormalizeDisplay(device.Platform),
                NormalizeDisplay(device.Alias),
                []));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var snapshots = await _dbContext.Db.Queryable<RemoteMaintenanceDevice>()
            .Where(x => !x.IsDeleted && x.RustdeskId != "")
            .Select(x => new RemoteMaintenanceDevice
            {
                Id = x.Id,
                DeviceRegistrationId = x.DeviceRegistrationId,
                HardwareId = x.HardwareId,
                RegisteredAtUtc = x.RegisteredAtUtc,
                RustdeskId = x.RustdeskId,
            })
            .ToListAsync();
        var registrations = await RemoteMaintenanceRegistrationResolver.ResolveAsync(_posmContext, snapshots, cancellationToken);

        // 名称按当前注册身份联查分店，避免重注册或分店改名后继续展示远程维护的旧快照。
        var storeCodes = registrations.Values.Select(x => NormalizeBounded(x.分店代码, 50))
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var stores = new List<Store>();
        foreach (var batch in storeCodes.Chunk(1000))
        {
            cancellationToken.ThrowIfCancellationRequested();
            stores.AddRange(await _dbContext.Db.Queryable<Store>()
                .Where(x => !x.IsDeleted && batch.Contains(x.StoreCode))
                .Select(x => new Store { StoreCode = x.StoreCode, StoreName = x.StoreName })
                .ToListAsync());
        }
        var storeNames = stores.GroupBy(x => NormalizeBounded(x.StoreCode, 50), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => NormalizeDisplay(x.First().StoreName), StringComparer.OrdinalIgnoreCase);

        var candidates = snapshots.Where(snapshot => registrations.ContainsKey(snapshot.Id));
        foreach (var group in candidates.GroupBy(x => NormalizeBounded(x.RustdeskId, MaxRustdeskIdLength), StringComparer.OrdinalIgnoreCase))
        {
            // 重复 RustDesk ID 无法唯一绑定设备名称及凭据，整组拒绝，避免无序选中错误的 POS。
            if (group.Count() != 1)
            {
                _logger.LogWarning("RustDesk duplicate POS identity excluded. RustdeskId={RustdeskId}", group.Key);
                continue;
            }
            var snapshot = group.First();
            var registration = registrations[snapshot.Id];

            var id = NormalizeBounded(snapshot.RustdeskId, MaxRustdeskIdLength);
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) continue;
            var storeCode = NormalizeBounded(registration.分店代码, 50);
            var storeName = storeNames.GetValueOrDefault(storeCode);
            var deviceCode = NormalizeBounded(registration.系统设备编号, 50);
            // RustDesk 卡片拼接 username@hostname；用完整显示名和空用户名避免重复设备代码。
            var displayName = string.Join(" ", new[]
            {
                string.IsNullOrWhiteSpace(storeName) ? storeCode : storeName,
                string.IsNullOrWhiteSpace(deviceCode) ? id : deviceCode,
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var tags = string.IsNullOrWhiteSpace(storeCode)
                ? Array.Empty<string>()
                : [storeCode];
            peers.Add(new RustDeskPeer(
                id,
                "",
                displayName,
                WindowsDeviceSystem,
                displayName,
                tags));
            credentialSources.Add(id, snapshot);
        }

        return await CompletePeerPageAsync(peers, credentialSources, activeUser.UserGUID, current, pageSize, cancellationToken);
    }

    private async Task<RustDeskPeerPage> CompletePeerPageAsync(
        List<RustDeskPeer> peers, Dictionary<string, RemoteMaintenanceDevice> credentialSources,
        string actor, int? current, int pageSize, CancellationToken cancellationToken)
    {
        var sorted = SortPeers(peers);
        // 普通列表只返回元数据；共享通讯录先排序分页，再读取本页的有效 POS 密文。
        if (current is null) return new(sorted.Length, sorted);
        var page = sorted.Skip((int)Math.Min(int.MaxValue, ((long)current.Value - 1) * pageSize))
            .Take(pageSize).ToArray();
        if (!_remoteMaintenanceOptions.Value.Enabled) return new(sorted.Length, page);
        var sourceIds = page.Where(peer => credentialSources.ContainsKey(peer.Id))
            .Select(peer => credentialSources[peer.Id].Id).ToArray();
        if (sourceIds.Length == 0) return new(sorted.Length, page);

        cancellationToken.ThrowIfCancellationRequested();
        var credentials = await _dbContext.Db.Queryable<RemoteMaintenanceDevice>()
            .Where(x => sourceIds.Contains(x.Id) && !x.IsDeleted)
            .Select(x => new RemoteMaintenanceDevice
            {
                Id = x.Id, RustdeskId = x.RustdeskId, DeviceRegistrationId = x.DeviceRegistrationId,
                CredentialCiphertext = x.CredentialCiphertext,
            }).ToListAsync();
        var byId = credentials.ToDictionary(x => x.Id);
        for (var index = 0; index < page.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var peer = page[index];
            if (!credentialSources.TryGetValue(peer.Id, out var source)
                || !byId.TryGetValue(source.Id, out var credential)
                || credential.DeviceRegistrationId != source.DeviceRegistrationId
                || !string.Equals(NormalizeBounded(credential.RustdeskId, MaxRustdeskIdLength), peer.Id, StringComparison.OrdinalIgnoreCase))
                continue;
            page[index] = peer with { Password = ReadAddressBookPassword(credential, actor) };
        }
        return new(sorted.Length, page);
    }

    private string? ReadAddressBookPassword(RemoteMaintenanceDevice device, string actor)
    {
        if (string.IsNullOrWhiteSpace(device.CredentialCiphertext)) return null;
        try
        {
            // 1.4.9 共享地址簿使用原始密码；客户端收到目标 salt/challenge 后自行完成认证。
            var password = _secretProtector.UnprotectPassword(device.CredentialCiphertext);
            if (string.IsNullOrEmpty(password)) return null;
            _logger.LogInformation("RustDesk address book credential synchronized. DeviceId={DeviceId}, Actor={Actor}", device.Id, actor);
            return password;
        }
        catch (CryptographicException)
        {
            // 单台设备凭据损坏时继续显示设备，但不下发任何密文或错误详情。
            _logger.LogWarning("RustDesk address book credential unavailable. DeviceId={DeviceId}", device.Id);
            return null;
        }
    }

    private async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (!await _schemaReadiness.IsReadyAsync(cancellationToken))
            throw new InvalidOperationException(SchemaNotReadyMessage);
    }

    private async Task<User?> GetActiveUserAsync(string userGuid, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userGuid)) return null;
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.Db.Queryable<User>()
            .Where(x => x.UserGUID == userGuid && x.IsActive && !x.IsDeleted)
            .FirstAsync();
    }

    private async Task<bool> IsActiveAdministratorAsync(string userGuid, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.Db.Queryable<UserRole, Role>((userRole, role) =>
                new JoinQueryInfos(JoinType.Inner, userRole.RoleGUID == role.RoleGUID))
            .Where((userRole, role) => userRole.UserGUID == userGuid
                && !userRole.IsDeleted
                && role.IsActive
                && !role.IsDeleted
                && (role.RoleName == AdministratorRoles[0]
                    || role.RoleName == AdministratorRoles[1]
                    || role.RoleName == AdministratorRoles[2]
                    || role.RoleName == AdministratorRoles[3]))
            .AnyAsync();
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static RustDeskAuthenticatedUser ToAuthenticatedUser(User user) =>
        new(user.UserGUID, user.Username, string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName);

    private static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Fingerprint(string passwordHash) => Hash(passwordHash);

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private static string Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized;
    }

    private static string NormalizeBounded(string? value, int maxLength)
    {
        var normalized = Normalize(value);
        return normalized.Length <= maxLength ? normalized : string.Empty;
    }

    private static string NormalizeDisplay(string? value) => value?.Trim() ?? string.Empty;

    private static bool IsOpaqueToken(string? token) =>
        token is { Length: 43 }
        && token.All(static character =>
            character is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '-' or '_');

    private static RustDeskPeer[] SortPeers(IEnumerable<RustDeskPeer> peers) => peers
        .OrderBy(x => x.Alias, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
