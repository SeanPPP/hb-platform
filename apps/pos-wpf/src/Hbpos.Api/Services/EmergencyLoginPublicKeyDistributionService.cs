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

    Task<EmergencyLoginPublicKeyAckResult> AcknowledgeAsync(
        EmergencyLoginDeviceIdentity device,
        long version,
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
        // 版本、覆盖密钥和写入必须由仓储在同一数据库事务内完成，避免轮换期间 TOCTOU。
        return await repository.AcknowledgeAsync(
            device,
            version,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
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

    internal const string AckSqlForTests = """
        SET NOCOUNT ON;
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;

        DECLARE @CurrentVersion BIGINT;
        DECLARE @CoverageKeyId NVARCHAR(32);
        DECLARE @DeviceRegistrationId INT;
        DECLARE @ResultCode INT;

        SELECT @CurrentVersion = [Version]
        FROM [dbo].[POSM_EmergencyLoginKeySetState] WITH (UPDLOCK, HOLDLOCK)
        WHERE [StateId] = 1;

        IF @CurrentVersion IS NULL
            SET @ResultCode = 4;
        ELSE IF @RequestedVersion > @CurrentVersion
            SET @ResultCode = 2;
        ELSE IF @RequestedVersion < @CurrentVersion
            SET @ResultCode = 1;
        ELSE
        BEGIN
            SELECT @DeviceRegistrationId = [ID]
            FROM [dbo].[POSM_设备注册信息表] WITH (HOLDLOCK)
            WHERE [系统设备编号] = @DeviceCode
              AND [分店代码] = @StoreCode
              AND [设备硬件识别码] = @HardwareId;

            IF @DeviceRegistrationId IS NULL
                SET @ResultCode = 3;
            ELSE
            BEGIN
                SELECT TOP (1) @CoverageKeyId = [KeyId]
                FROM [dbo].[POSM_EmergencyLoginKey] WITH (HOLDLOCK)
                WHERE [Status] IN (N'Staged', N'Active')
                ORDER BY CASE WHEN [Status] = N'Staged' THEN 0 ELSE 1 END;

                IF @CoverageKeyId IS NULL
                    SET @ResultCode = 4;
                ELSE
                BEGIN
                    MERGE [dbo].[POSM_EmergencyLoginKeyDeviceSync] WITH (HOLDLOCK) AS target
                    USING (VALUES (
                        @DeviceRegistrationId,
                        @RequestedVersion,
                        @CoverageKeyId,
                        @AcknowledgedAtUtc))
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
                    SET @ResultCode = 0;
                END
            END
        END

        COMMIT TRANSACTION;
        SELECT @ResultCode AS [ResultCode];
        """;

    public async Task<EmergencyLoginPublicKeyAckResult> AcknowledgeAsync(
        EmergencyLoginDeviceIdentity device,
        long version,
        DateTime acknowledgedAtUtc,
        CancellationToken cancellationToken)
    {
        // 关键逻辑：锁定版本状态至 ACK 完成，保证版本、覆盖 KeyId 和幂等写入属于同一快照。
        var result = await dbContext.PosmDb.Ado.SqlQuerySingleAsync<EmergencyLoginPublicKeyAckSqlResult>(
            AckSqlForTests,
            new SugarParameter("@RequestedVersion", version),
            new SugarParameter("@DeviceCode", device.DeviceCode),
            new SugarParameter("@StoreCode", device.StoreCode),
            new SugarParameter("@HardwareId", device.HardwareId),
            new SugarParameter("@AcknowledgedAtUtc", acknowledgedAtUtc));
        return result?.ResultCode switch
        {
            0 => EmergencyLoginPublicKeyAckResult.Acknowledged,
            1 => EmergencyLoginPublicKeyAckResult.StaleIgnored,
            2 => EmergencyLoginPublicKeyAckResult.FutureVersion,
            3 => EmergencyLoginPublicKeyAckResult.DeviceNotFound,
            _ => throw new InvalidOperationException("紧急登录公钥 ACK 无法取得一致的版本与覆盖密钥。")
        };
    }

    private sealed class EmergencyLoginPublicKeyAckSqlResult
    {
        public int ResultCode { get; set; }
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
