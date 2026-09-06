using System.Security.Cryptography;
using System.Text;
using BlazorApp.Api.Data;
using BlazorApp.Api.Utils;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.POSM;
using SqlSugar;

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
    private const int EnabledDeviceStatus = 1;
    private const string PosDeviceType = "POS";
    private const string WindowsDeviceSystem = "Windows";
    private const string SchemaNotReadyMessage = "RustDesk 专用数据库尚未准备就绪。";
    private static readonly string[] AdministratorRoles =
        ["Admin", "管理员", "SuperAdmin", "超级管理员"];

    private readonly SqlSugarContext _dbContext;
    private readonly POSMSqlSugarContext _posmContext;
    private readonly RustDeskCompatSchemaReadiness _schemaReadiness;
    private readonly TimeProvider _timeProvider;

    public RustDeskCompatService(
        SqlSugarContext dbContext,
        POSMSqlSugarContext posmContext,
        RustDeskCompatSchemaReadiness schemaReadiness,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _posmContext = posmContext;
        _schemaReadiness = schemaReadiness;
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
        CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Controller 传入的身份也必须重新从数据库核验，避免 stale principal 继续读通讯录。
        var activeUser = await GetActiveUserAsync(user.UserGuid, cancellationToken);
        if (activeUser is null
            || !await IsActiveAdministratorAsync(activeUser.UserGUID, cancellationToken))
            return [];

        var managed = await _dbContext.Db.Queryable<RustDeskManagedDevice>()
            .Where(x => !x.IsDisabled && x.RustdeskId != "")
            .Select(x => new RustDeskManagedDevice
            {
                Id = x.Id, RustdeskId = x.RustdeskId, Alias = x.Alias,
                Hostname = x.Hostname, Platform = x.Platform,
            })
            .ToListAsync();
        var peers = new List<RustDeskPeer>(managed.Count);
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
                StoreCode = x.StoreCode,
                DeviceCode = x.DeviceCode,
                ComputerName = x.ComputerName,
                RustdeskId = x.RustdeskId,
            })
            .ToListAsync();
        var hardwareIds = snapshots
            .Select(x => NormalizeBounded(x.HardwareId, 100))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (hardwareIds.Length == 0) return SortPeers(peers);

        // SQL Server 参数上限约 2100；硬件号列表按 1000 分批，避免大批量通讯录请求失败。
        var registrations = new List<POSM_设备注册信息表>();
        foreach (var batch in hardwareIds.Chunk(1000))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchRows = await _posmContext.Db.Queryable<POSM_设备注册信息表>()
                .Where(x => batch.Contains(x.设备硬件识别码))
                .Select(x => new POSM_设备注册信息表
                {
                    ID = x.ID,
                    设备硬件识别码 = x.设备硬件识别码,
                    设备状态 = x.设备状态,
                    设备类型 = x.设备类型,
                    设备系统 = x.设备系统,
                })
                .OrderByDescending(x => x.ID)
                .ToListAsync();
            registrations.AddRange(batchRows);
        }
        var latestRegistrations = registrations
            .GroupBy(x => NormalizeBounded(x.设备硬件识别码, 100), StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(row => row.ID).First())
            .Where(IsEnabledWindowsPos)
            .ToDictionary(x => NormalizeBounded(x.设备硬件识别码, 100), StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in snapshots)
        {
            var hardwareId = NormalizeBounded(snapshot.HardwareId, 100);
            if (string.IsNullOrWhiteSpace(hardwareId)
                || !latestRegistrations.TryGetValue(hardwareId, out var registration)
                || registration.ID != snapshot.DeviceRegistrationId)
                continue;

            var id = NormalizeBounded(snapshot.RustdeskId, MaxRustdeskIdLength);
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) continue;
            var hostname = NormalizeDisplay(snapshot.ComputerName);
            var username = NormalizeDisplay(snapshot.DeviceCode);
            var tags = string.IsNullOrWhiteSpace(snapshot.StoreCode)
                ? Array.Empty<string>()
                : [snapshot.StoreCode];
            peers.Add(new RustDeskPeer(
                id,
                username,
                hostname,
                WindowsDeviceSystem,
                hostname,
                tags));
        }

        return SortPeers(peers);
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

    private static bool IsEnabledWindowsPos(POSM_设备注册信息表 registration) =>
        registration.设备状态 == EnabledDeviceStatus
        && string.Equals(registration.设备类型, PosDeviceType, StringComparison.OrdinalIgnoreCase)
        && string.Equals(registration.设备系统, WindowsDeviceSystem, StringComparison.OrdinalIgnoreCase);

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
