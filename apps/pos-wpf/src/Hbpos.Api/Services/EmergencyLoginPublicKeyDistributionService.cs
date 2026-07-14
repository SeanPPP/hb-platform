using BlazorApp.Shared.Models.POSM;
using Hbpos.Api.Data;
using Hbpos.Contracts.EmergencyLogin;
using SqlSugar;

namespace Hbpos.Api.Services;

public sealed record EmergencyLoginDeviceIdentity(
    string DeviceCode,
    string StoreCode,
    string HardwareId);

public sealed record EmergencyLoginPublicKeyRecord(
    string KeyId,
    string Algorithm,
    string PublicKeyPem,
    string Fingerprint,
    string Status);

public sealed record EmergencyLoginPublicKeySetSnapshot(
    long Version,
    string? ActiveKeyId,
    DateTime UpdatedAtUtc,
    IReadOnlyList<EmergencyLoginPublicKeyRecord> Keys);

public enum EmergencyLoginPublicKeyAckResult
{
    Acknowledged,
    StaleIgnored,
    FutureVersion,
    DeviceNotFound
}

public interface IEmergencyLoginPublicKeyRepository
{
    Task<EmergencyLoginPublicKeySetSnapshot> GetKeySetAsync(CancellationToken cancellationToken);

    Task<int?> FindDeviceRegistrationIdAsync(
        EmergencyLoginDeviceIdentity device,
        CancellationToken cancellationToken);

    Task UpsertAcknowledgementAsync(
        int deviceRegistrationId,
        long version,
        string keyId,
        DateTime acknowledgedAtUtc,
        CancellationToken cancellationToken);
}

public interface IEmergencyLoginPublicKeyDistributionService
{
    Task<EmergencyLoginPublicKeyPackage> GetAsync(CancellationToken cancellationToken);

    Task<EmergencyLoginPublicKeyAckResult> AcknowledgeAsync(
        EmergencyLoginDeviceIdentity device,
        long version,
        CancellationToken cancellationToken);
}

public sealed class EmergencyLoginPublicKeyDistributionService(
    IEmergencyLoginPublicKeyRepository repository,
    TimeProvider? timeProvider = null) : IEmergencyLoginPublicKeyDistributionService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<EmergencyLoginPublicKeyPackage> GetAsync(CancellationToken cancellationToken)
    {
        var snapshot = await repository.GetKeySetAsync(cancellationToken);
        var keys = snapshot.Keys
            .Where(key => EmergencyLoginKeyStatuses.IsDistributable(key.Status))
            .Select(key => new EmergencyLoginPublicKey(
                key.KeyId,
                key.Algorithm,
                key.PublicKeyPem,
                key.Fingerprint))
            .ToArray();

        return new EmergencyLoginPublicKeyPackage(
            snapshot.Version,
            snapshot.ActiveKeyId,
            DateTime.SpecifyKind(snapshot.UpdatedAtUtc, DateTimeKind.Utc),
            keys);
    }

    public async Task<EmergencyLoginPublicKeyAckResult> AcknowledgeAsync(
        EmergencyLoginDeviceIdentity device,
        long version,
        CancellationToken cancellationToken)
    {
        var snapshot = await repository.GetKeySetAsync(cancellationToken);
        if (version > snapshot.Version)
        {
            return EmergencyLoginPublicKeyAckResult.FutureVersion;
        }

        // 关键逻辑：旧版本确认不写库，避免落后的客户端干扰当前密钥覆盖率。
        if (version < snapshot.Version)
        {
            return EmergencyLoginPublicKeyAckResult.StaleIgnored;
        }

        var coverageKeyId = snapshot.Keys
            .FirstOrDefault(key => string.Equals(key.Status, EmergencyLoginKeyStatuses.Staged, StringComparison.Ordinal))
            ?.KeyId
            ?? snapshot.Keys.FirstOrDefault(key =>
                string.Equals(key.Status, EmergencyLoginKeyStatuses.Active, StringComparison.Ordinal))?.KeyId;
        if (string.IsNullOrWhiteSpace(coverageKeyId))
        {
            throw new InvalidOperationException("当前公钥包没有可确认的 Staged 或 Active 密钥。");
        }

        var deviceRegistrationId = await repository.FindDeviceRegistrationIdAsync(device, cancellationToken);
        if (deviceRegistrationId is null)
        {
            return EmergencyLoginPublicKeyAckResult.DeviceNotFound;
        }

        await repository.UpsertAcknowledgementAsync(
            deviceRegistrationId.Value,
            version,
            coverageKeyId,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
        return EmergencyLoginPublicKeyAckResult.Acknowledged;
    }
}

public sealed class SqlSugarEmergencyLoginPublicKeyRepository(HbposSqlSugarContext dbContext)
    : IEmergencyLoginPublicKeyRepository
{
    public async Task<EmergencyLoginPublicKeySetSnapshot> GetKeySetAsync(CancellationToken cancellationToken)
    {
        var state = await dbContext.PosmDb.Queryable<EmergencyLoginKeySetStateEntity>()
            .Where(item => item.StateId == 1)
            .FirstAsync(cancellationToken)
            ?? throw new InvalidOperationException("紧急登录公钥版本状态不存在。");
        var keys = await dbContext.PosmDb.Queryable<EmergencyLoginKeyEntity>()
            .Where(item =>
                item.Status == EmergencyLoginKeyStatuses.Staged ||
                item.Status == EmergencyLoginKeyStatuses.Active ||
                item.Status == EmergencyLoginKeyStatuses.Retiring)
            .OrderBy(item => item.CreatedAtUtc, OrderByType.Asc)
            .ToListAsync(cancellationToken);

        return new EmergencyLoginPublicKeySetSnapshot(
            state.Version,
            state.ActiveKeyId,
            state.UpdatedAtUtc,
            keys.Select(item => new EmergencyLoginPublicKeyRecord(
                item.KeyId,
                "ES256",
                item.PublicKeyPem,
                item.PublicKeyFingerprint,
                item.Status)).ToArray());
    }

    public async Task<int?> FindDeviceRegistrationIdAsync(
        EmergencyLoginDeviceIdentity device,
        CancellationToken cancellationToken)
    {
        var registration = await dbContext.PosmDb.Queryable<POSM_设备注册信息表>()
            .Where(item =>
                item.系统设备编号 == device.DeviceCode &&
                item.分店代码 == device.StoreCode &&
                item.设备硬件识别码 == device.HardwareId)
            .Select(item => new POSM_设备注册信息表 { ID = item.ID })
            .FirstAsync(cancellationToken);
        return registration?.ID;
    }

    public async Task UpsertAcknowledgementAsync(
        int deviceRegistrationId,
        long version,
        string keyId,
        DateTime acknowledgedAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
            MERGE [dbo].[POSM_EmergencyLoginKeyDeviceSync] WITH (HOLDLOCK) AS target
            USING (VALUES (@DeviceRegistrationId, @KeySetVersion, @KeyId, @AcknowledgedAtUtc))
                AS source ([DeviceRegistrationId], [KeySetVersion], [KeyId], [AcknowledgedAtUtc])
            ON target.[DeviceRegistrationId] = source.[DeviceRegistrationId]
               AND target.[KeySetVersion] = source.[KeySetVersion]
            WHEN MATCHED THEN
                UPDATE SET
                    [KeyId] = source.[KeyId],
                    [AcknowledgedAtUtc] = source.[AcknowledgedAtUtc],
                    [LastSeenAtUtc] = source.[AcknowledgedAtUtc]
            WHEN NOT MATCHED THEN
                INSERT ([DeviceRegistrationId], [KeySetVersion], [KeyId], [AcknowledgedAtUtc], [LastSeenAtUtc])
                VALUES (source.[DeviceRegistrationId], source.[KeySetVersion], source.[KeyId],
                    source.[AcknowledgedAtUtc], source.[AcknowledgedAtUtc]);
            """;
        // 关键逻辑：HOLDLOCK 让同设备同版本并发 ACK 保持幂等，避免复合主键竞争。
        await dbContext.PosmDb.Ado.ExecuteCommandAsync(
            sql,
            new SugarParameter("@DeviceRegistrationId", deviceRegistrationId),
            new SugarParameter("@KeySetVersion", version),
            new SugarParameter("@KeyId", keyId),
            new SugarParameter("@AcknowledgedAtUtc", acknowledgedAtUtc));
    }
}

internal static class EmergencyLoginKeyStatuses
{
    internal const string Staged = "Staged";
    internal const string Active = "Active";
    internal const string Retiring = "Retiring";
    internal const string Retired = "Retired";

    internal static bool IsDistributable(string status) =>
        status is Staged or Active or Retiring;
}

[SugarTable("POSM_EmergencyLoginKey")]
internal sealed class EmergencyLoginKeyEntity
{
    [SugarColumn(IsPrimaryKey = true)]
    public string KeyId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PublicKeyPem { get; set; } = string.Empty;
    public string PublicKeyFingerprint { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

[SugarTable("POSM_EmergencyLoginKeySetState")]
internal sealed class EmergencyLoginKeySetStateEntity
{
    [SugarColumn(IsPrimaryKey = true)]
    public int StateId { get; set; }
    public long Version { get; set; }
    public string? ActiveKeyId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

[SugarTable("POSM_EmergencyLoginKeyDeviceSync")]
internal sealed class EmergencyLoginKeyDeviceSyncEntity
{
    [SugarColumn(IsPrimaryKey = true)]
    public int DeviceRegistrationId { get; set; }
    [SugarColumn(IsPrimaryKey = true)]
    public long KeySetVersion { get; set; }
    public string KeyId { get; set; } = string.Empty;
    public DateTime AcknowledgedAtUtc { get; set; }
    public DateTime? LastSeenAtUtc { get; set; }
}
