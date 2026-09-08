using System.Security.Cryptography;
using System.Text;
using System.Linq;
using BlazorApp.Api.Data;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.POSM;

namespace BlazorApp.Api.Services;

/// <summary>
/// 将远程维护快照解析到当前仍可证明的 Windows POS 注册行。
/// </summary>
public static class RemoteMaintenanceRegistrationResolver
{
    private const int EnabledDeviceStatus = 1;
    private const string PosDeviceType = "POS";
    private const string WindowsDeviceSystem = "Windows";
    private const int QueryBatchSize = 1000;
    private const string RebindKind = "Rebind";

    /// <summary>
    /// 解析快照对应的注册身份。无法从当前注册表和已消费 rebind 记录建立完整证明链时，省略该快照。
    /// </summary>
    public static async Task<Dictionary<Guid, POSM_设备注册信息表>> ResolveAsync(
        POSMSqlSugarContext context,
        IReadOnlyList<RemoteMaintenanceDevice> snapshots,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshots);

        var candidates = snapshots
            .Where(snapshot => snapshot.Id != Guid.Empty && !string.IsNullOrWhiteSpace(snapshot.HardwareId))
            .GroupBy(snapshot => snapshot.Id)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .ToArray();
        if (candidates.Length == 0)
            return new Dictionary<Guid, POSM_设备注册信息表>();

        var hardwareIds = candidates
            .Select(snapshot => Normalize(snapshot.HardwareId))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (hardwareIds.Length == 0)
            return new Dictionary<Guid, POSM_设备注册信息表>();

        var registrations = new List<RegistrationRow>();

        foreach (var hardwareBatch in Batch(hardwareIds, QueryBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 仅取证明所需的注册字段；授权码只留在内部行中用于最后一步哈希校验。
            var registrationBatch = await context.Db.Queryable<POSM_设备注册信息表>()
                .Where(row => hardwareBatch.Contains(row.设备硬件识别码))
                .Select(row => new RegistrationRow
                {
                    Id = row.ID,
                    HardwareId = row.设备硬件识别码,
                    DeviceCode = row.系统设备编号,
                    StoreCode = row.分店代码,
                    DeviceType = row.设备类型,
                    DeviceSystem = row.设备系统,
                    Status = row.设备状态,
                })
                .ToListAsync();
            registrations.AddRange(registrationBatch);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = new Dictionary<Guid, POSM_设备注册信息表>();
        var unresolved = new List<(RemoteMaintenanceDevice Snapshot, RegistrationRow[] Rows)>();
        foreach (var snapshot in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hardwareId = Normalize(snapshot.HardwareId);
            var rows = registrations
                .Where(row => string.Equals(Normalize(row.HardwareId), hardwareId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(row => row.Id)
                .ToArray();
            if (rows.Length == 0)
                continue;

            var direct = ResolveDirect(snapshot, rows);
            if (direct is not null)
            {
                result[snapshot.Id] = ToRegistration(direct);
                continue;
            }

            unresolved.Add((snapshot, rows));
        }

        if (unresolved.Count == 0)
            return result;

        var unresolvedHardwareIds = unresolved
            .Select(item => Normalize(item.Snapshot.HardwareId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var earliestUnresolvedSnapshot = unresolved
            .Select(item => AsUtc(item.Snapshot.RegisteredAtUtc))
            .Min();
        var grants = new List<RebindGrantRow>();
        foreach (var hardwareBatch in Batch(unresolvedHardwareIds, QueryBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 正常直接匹配已经在上面返回；只有未解出的硬件才读取 rebind 证据，且不读取 SecretHash。
            var grantBatch = await context.Db.Queryable<DeviceActivationCodeGrant>()
                .Where(grant =>
                    hardwareBatch.Contains(grant.ConsumedHardwareId)
                    && grant.ConsumedAtUtc.HasValue
                    && grant.ConsumedAtUtc.Value > earliestUnresolvedSnapshot)
                .Select(grant => new RebindGrantRow
                {
                    GrantId = grant.GrantId,
                    StoreCode = grant.StoreCode,
                    DeviceSystem = grant.DeviceSystem,
                    RevokedAtUtc = grant.RevokedAtUtc,
                    ConsumedAtUtc = grant.ConsumedAtUtc,
                    ConsumedHardwareId = grant.ConsumedHardwareId,
                    ConsumedDeviceCode = grant.ConsumedDeviceCode,
                    ConsumedDeviceRegistrationId = grant.ConsumedDeviceRegistrationId,
                    ConsumedAuthorizationHash = grant.ConsumedAuthorizationHash,
                    ConsumedDeviceSystem = grant.ConsumedDeviceSystem,
                    ConsumptionKind = grant.ConsumptionKind,
                    PreviousStoreCode = grant.PreviousStoreCode,
                    PreviousDeviceCode = grant.PreviousDeviceCode,
                })
                .ToListAsync();
            grants.AddRange(grantBatch);
        }

        var pending = new List<PendingResolution>();
        foreach (var (snapshot, rows) in unresolved)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = ResolveThroughRebind(snapshot, rows, grants);
            if (resolved is not null)
                pending.Add(new PendingResolution(snapshot, resolved.Value.Target, resolved.Value.AuthorizationHash));
        }

        if (pending.Count == 0)
            return result;

        // 只有元数据证明链已闭合的最终目标 ID 才读取授权码，用于最后的 SHA-256 校验。
        var authorizationCodes = new Dictionary<int, string>();
        foreach (var targetIdBatch in Batch(pending.Select(item => item.Target.Id).Distinct().ToArray(), QueryBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var authBatch = await context.Db.Queryable<POSM_设备注册信息表>()
                .Where(row =>
                    targetIdBatch.Contains(row.ID)
                    && row.设备状态 == EnabledDeviceStatus
                    && row.设备类型 == PosDeviceType
                    && row.设备系统 == WindowsDeviceSystem)
                .Select(row => new RegistrationAuthorizationRow
                {
                    Id = row.ID,
                    AuthorizationCode = row.设备授权码,
                })
                .ToListAsync();
            foreach (var row in authBatch)
                authorizationCodes[row.Id] = row.AuthorizationCode;
        }

        foreach (var item in pending)
        {
            if (!authorizationCodes.TryGetValue(item.Target.Id, out var authorizationCode)
                || string.IsNullOrEmpty(authorizationCode))
                continue;
            var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(authorizationCode));
            if (CryptographicOperations.FixedTimeEquals(actualHash, item.AuthorizationHash))
                result[item.Snapshot.Id] = ToRegistration(item.Target);
        }

        return result;
    }

    private static RegistrationRow? ResolveDirect(
        RemoteMaintenanceDevice snapshot,
        IReadOnlyList<RegistrationRow> rows)
    {
        var latest = rows[^1];
        return latest.Id == snapshot.DeviceRegistrationId && IsEnabledWindowsPos(latest)
            ? latest
            : null;
    }

    private static RebindResolution? ResolveThroughRebind(
        RemoteMaintenanceDevice snapshot,
        IReadOnlyList<RegistrationRow> rows,
        IReadOnlyList<RebindGrantRow> allGrants)
    {
        var hardwareId = Normalize(snapshot.HardwareId);
        var cutoff = AsUtc(snapshot.RegisteredAtUtc);
        var chainGrants = allGrants
            .Where(grant =>
                grant.ConsumedAtUtc.HasValue
                && AsUtc(grant.ConsumedAtUtc.Value) > cutoff
                && string.Equals(Normalize(grant.ConsumedHardwareId), hardwareId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(grant.ConsumptionKind?.Trim(), RebindKind, StringComparison.Ordinal))
            .OrderBy(grant => AsUtc(grant.ConsumedAtUtc!.Value))
            .ThenBy(grant => grant.GrantId)
            .ToArray();
        if (chainGrants.Length == 0)
            return null;

        // 同一设备同一消费时刻无法证明顺序；拒绝整个映射，避免任意选择一条链。
        if (chainGrants
            .GroupBy(grant => AsUtc(grant.ConsumedAtUtc!.Value))
            .Any(group => group.Count() > 1))
            return null;

        var currentId = snapshot.DeviceRegistrationId;
        var visitedIds = new HashSet<int>();
        RebindGrantRow? lastGrant = null;
        RegistrationRow? finalTarget = null;

        foreach (var grant in chainGrants)
        {
            if (!HasCompleteEvidence(grant, hardwareId))
                return null;

            var sourceCandidates = rows
                .Where(row =>
                    string.Equals(Normalize(row.HardwareId), hardwareId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Normalize(row.StoreCode), Normalize(grant.PreviousStoreCode), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Normalize(row.DeviceCode), Normalize(grant.PreviousDeviceCode), StringComparison.Ordinal)
                    && IsWindowsPos(row))
                .ToArray();
            if (sourceCandidates.Length != 1 || sourceCandidates[0].Id != currentId)
                return null;

            var target = rows.SingleOrDefault(row => row.Id == grant.ConsumedDeviceRegistrationId!.Value);
            if (target is null || !MatchesTarget(target, grant, hardwareId))
                return null;

            visitedIds.Add(currentId);
            visitedIds.Add(target.Id);
            currentId = target.Id;
            finalTarget = target;
            lastGrant = grant;
        }

        if (finalTarget is null || lastGrant is null || !IsEnabledWindowsPos(finalTarget))
            return null;

        // 允许回到更早的注册行，但链后任何全局最大 ID 都必须已被访问。
        var globalLatest = rows[^1];
        if (!visitedIds.Contains(globalLatest.Id))
            return null;

        if (rows.Count(IsEnabledWindowsPos) != 1)
            return null;

        return new RebindResolution(finalTarget, lastGrant.ConsumedAuthorizationHash!);
    }

    private static bool HasCompleteEvidence(RebindGrantRow grant, string hardwareId) =>
        grant.RevokedAtUtc is null
        && !string.IsNullOrWhiteSpace(grant.StoreCode)
        && string.Equals(Normalize(grant.ConsumedHardwareId), hardwareId, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(grant.ConsumedDeviceCode)
        && grant.ConsumedDeviceRegistrationId is > 0
        && grant.ConsumedAuthorizationHash is { Length: 32 }
        && string.Equals(Normalize(grant.ConsumedDeviceSystem), WindowsDeviceSystem, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Normalize(grant.DeviceSystem), WindowsDeviceSystem, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Normalize(grant.ConsumedDeviceSystem), Normalize(grant.DeviceSystem), StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(grant.PreviousStoreCode)
        && !string.IsNullOrWhiteSpace(grant.PreviousDeviceCode);

    private static bool MatchesTarget(RegistrationRow target, RebindGrantRow grant, string hardwareId) =>
        IsWindowsPos(target)
        && string.Equals(Normalize(target.HardwareId), hardwareId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Normalize(target.StoreCode), Normalize(grant.StoreCode), StringComparison.OrdinalIgnoreCase)
        && string.Equals(Normalize(target.DeviceCode), Normalize(grant.ConsumedDeviceCode), StringComparison.Ordinal)
        && string.Equals(Normalize(target.DeviceSystem), Normalize(grant.ConsumedDeviceSystem), StringComparison.OrdinalIgnoreCase);

    private static bool IsEnabledWindowsPos(RegistrationRow row) =>
        row.Status == EnabledDeviceStatus && IsWindowsPos(row);

    private static bool IsWindowsPos(RegistrationRow row) =>
        string.Equals(Normalize(row.DeviceType), PosDeviceType, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Normalize(row.DeviceSystem), WindowsDeviceSystem, StringComparison.OrdinalIgnoreCase);

    private static POSM_设备注册信息表 ToRegistration(RegistrationRow row) => new()
    {
        ID = row.Id,
        设备硬件识别码 = row.HardwareId,
        系统设备编号 = row.DeviceCode,
        分店代码 = row.StoreCode,
        设备类型 = row.DeviceType,
        设备系统 = row.DeviceSystem,
        设备状态 = row.Status,
    };

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static IEnumerable<IReadOnlyList<T>> Batch<T>(IReadOnlyList<T> values, int size)
    {
        for (var offset = 0; offset < values.Count; offset += size)
            yield return values.Skip(offset).Take(size).ToArray();
    }

    private sealed class RegistrationRow
    {
        public int Id { get; set; }
        public string HardwareId { get; set; } = string.Empty;
        public string DeviceCode { get; set; } = string.Empty;
        public string? StoreCode { get; set; }
        public string DeviceType { get; set; } = string.Empty;
        public string DeviceSystem { get; set; } = string.Empty;
        public int Status { get; set; }
    }

    private sealed class RegistrationAuthorizationRow
    {
        public int Id { get; set; }
        public string AuthorizationCode { get; set; } = string.Empty;
    }

    private readonly record struct RebindResolution(RegistrationRow Target, byte[] AuthorizationHash);

    private readonly record struct PendingResolution(
        RemoteMaintenanceDevice Snapshot,
        RegistrationRow Target,
        byte[] AuthorizationHash);

    private sealed class RebindGrantRow
    {
        public Guid GrantId { get; set; }
        public string StoreCode { get; set; } = string.Empty;
        public string DeviceSystem { get; set; } = string.Empty;
        public DateTime? RevokedAtUtc { get; set; }
        public DateTime? ConsumedAtUtc { get; set; }
        public string? ConsumedHardwareId { get; set; }
        public string? ConsumedDeviceCode { get; set; }
        public int? ConsumedDeviceRegistrationId { get; set; }
        public byte[]? ConsumedAuthorizationHash { get; set; }
        public string? ConsumedDeviceSystem { get; set; }
        public string? ConsumptionKind { get; set; }
        public string? PreviousStoreCode { get; set; }
        public string? PreviousDeviceCode { get; set; }
    }
}
