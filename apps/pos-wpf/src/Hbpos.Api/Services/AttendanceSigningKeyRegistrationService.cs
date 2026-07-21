using System.Security.Cryptography;
using System.Globalization;
using Hbpos.Api.Data;
using Hbpos.Api.Security;
using Hbpos.Contracts.Attendance;
using Microsoft.Data.SqlClient;
using SqlSugar;

namespace Hbpos.Api.Services;

public sealed record AttendanceSigningKeyDeviceIdentity(
    string DeviceCode,
    string StoreCode,
    string HardwareId);

public interface IAttendanceSigningKeyRegistrationService
{
    Task<AttendanceSigningKeyRegistrationResponse> RegisterAsync(
        AttendanceSigningKeyDeviceIdentity identity,
        AttendanceSigningKeyRegistrationRequest request,
        CancellationToken cancellationToken);
}

public sealed class AttendanceSigningKeyValidationException(string message) : Exception(message);
public sealed class AttendanceSigningKeyConflictException(string message) : Exception(message);
public sealed class AttendanceSigningKeyUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);

public sealed class AttendanceSigningKeyRegistrationService(
    HbposSqlSugarContext dbContext,
    AttendanceQrKeyProtector keyProtector,
    ILogger<AttendanceSigningKeyRegistrationService> logger,
    IConfiguration configuration) : IAttendanceSigningKeyRegistrationService
{
    internal const string ProtectedKeyDecryptionFailureLogMessage =
        "Attendance signing key protected data decryption failed.";

    public async Task<AttendanceSigningKeyRegistrationResponse> RegisterAsync(
        AttendanceSigningKeyDeviceIdentity identity,
        AttendanceSigningKeyRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var key = DecodeKeyMaterial(request);
        byte[]? storedKey = null;
        try
        {
            var protectedKey = keyProtector.Protect(key);
            AttendanceSigningKeyRegistrationRow? result;
            try
            {
                result = await dbContext.MainDb.Ado.SqlQuerySingleAsync<AttendanceSigningKeyRegistrationRow>(
                    RegistrationSql,
                    new SugarParameter("@Kid", request.Kid),
                    new SugarParameter("@Algorithm", request.Algorithm),
                    new SugarParameter("@ProtectedKey", protectedKey),
                    new SugarParameter("@StoreCode", identity.StoreCode),
                    new SugarParameter("@DeviceCode", identity.DeviceCode),
                    new SugarParameter("@HardwareId", identity.HardwareId));
            }
            catch (Exception exception) when (ContainsRegistrationConflictSqlError(exception))
            {
                throw new AttendanceSigningKeyConflictException("kid conflict");
            }
            catch (Exception exception) when (ContainsRegistrationTransientSqlError(exception))
            {
                // 仅记录阶段与 SQL 错误号，不记录密钥、请求体或设备身份。
                logger.LogWarning(
                    "Attendance signing key registration transient SQL failure. SqlErrorNumber={SqlErrorNumber}",
                    FindSqlErrorNumber(exception));
                throw new AttendanceSigningKeyUnavailableException(
                    "registration temporarily unavailable",
                    exception);
            }

            if (result == null)
            {
                throw new InvalidOperationException("考勤二维码密钥登记未返回结果");
            }

            storedKey = UnprotectStoredKey(
                keyProtector,
                result.ProtectedKey,
                result.RegisteredAtUtc,
                GetLegacyProtectedKeyCutoffUtc(configuration),
                logger);
            if (!CryptographicOperations.FixedTimeEquals(storedKey, key))
            {
                throw new AttendanceSigningKeyConflictException("kid conflict");
            }

            return new AttendanceSigningKeyRegistrationResponse(
                request.Kid,
                DateTime.SpecifyKind(result.RegisteredAtUtc, DateTimeKind.Utc),
                DateTime.SpecifyKind(result.ServerTimeUtc, DateTimeKind.Utc));
        }
        finally
        {
            // 关键逻辑：登记完成或异常退出时都清除已解码/解保护的 AES 密钥缓冲区。
            CryptographicOperations.ZeroMemory(key);
            if (storedKey != null)
            {
                CryptographicOperations.ZeroMemory(storedKey);
            }
        }
    }

    internal static void ValidateRequest(AttendanceSigningKeyRegistrationRequest request)
    {
        var key = DecodeKeyMaterial(request);
        CryptographicOperations.ZeroMemory(key);
    }

    private static byte[] DecodeKeyMaterial(AttendanceSigningKeyRegistrationRequest request)
    {
        // 先限制固定长度，避免对超长不可信输入做遍历、替换或 Base64 解码。
        if (!string.Equals(request.Algorithm, "A256GCM", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(request.Kid)
            || request.Kid.Length > 64
            || request.Kid.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_')
            || request.KeyMaterial is not { Length: 43 })
        {
            throw new AttendanceSigningKeyValidationException("考勤二维码密钥登记参数无效");
        }

        try
        {
            if (request.KeyMaterial.Any(character => character is not (>= 'A' and <= 'Z')
                    and not (>= 'a' and <= 'z')
                    and not (>= '0' and <= '9')
                    and not '-'
                    and not '_'))
            {
                throw new FormatException();
            }

            var padded = request.KeyMaterial.Replace('-', '+').Replace('_', '/') + "=";
            var key = Convert.FromBase64String(padded);
            var canonical = Convert.ToBase64String(key).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            if (key.Length != 32 || !canonical.Equals(request.KeyMaterial, StringComparison.Ordinal))
            {
                CryptographicOperations.ZeroMemory(key);
                throw new AttendanceSigningKeyValidationException("考勤二维码密钥必须为 32 字节 base64url");
            }

            return key;
        }
        catch (AttendanceSigningKeyValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new AttendanceSigningKeyValidationException("考勤二维码密钥格式无效");
        }
    }

    internal static bool IsRegistrationConflictSqlErrorNumber(int number) =>
        number is 51000 or 2601 or 2627;

    internal static bool IsRegistrationTransientSqlErrorNumber(int number) =>
        number is 51001 or 1205;

    internal static byte[] UnprotectStoredKey(
        AttendanceQrKeyProtector keyProtector,
        string protectedKey,
        DateTime registeredAtUtc,
        DateTimeOffset? legacyProtectedKeyCutoffUtc,
        ILogger<AttendanceSigningKeyRegistrationService> logger)
    {
        try
        {
            return keyProtector.Unprotect(protectedKey);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            var normalizedRegisteredAtUtc = DateTime.SpecifyKind(registeredAtUtc, DateTimeKind.Utc);
            if (legacyProtectedKeyCutoffUtc is { } cutoff
                && normalizedRegisteredAtUtc <= cutoff.UtcDateTime)
            {
                // 关键逻辑：仅迁移截止点前的旧 ring 密文允许降级为冲突，通知 WPF 生成新 kid。
                throw new AttendanceSigningKeyConflictException("kid conflict");
            }

            // 固定消息不携带 kid、密文、门店、设备或底层异常内容。
            logger.LogError(ProtectedKeyDecryptionFailureLogMessage);
            throw new InvalidOperationException("考勤二维码密钥保护数据无法解密", exception);
        }
    }

    private static DateTimeOffset? GetLegacyProtectedKeyCutoffUtc(IConfiguration configuration)
    {
        var configuredValue = configuration["AttendanceQrDataProtection:LegacyProtectedKeyCutoffUtc"];
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
                configuredValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var cutoff))
        {
            return cutoff;
        }

        throw new InvalidOperationException(
            "AttendanceQrDataProtection:LegacyProtectedKeyCutoffUtc must be a valid UTC timestamp.");
    }

    private static bool ContainsRegistrationConflictSqlError(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is SqlException sqlException
                && IsRegistrationConflictSqlErrorNumber(sqlException.Number))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsRegistrationTransientSqlError(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is SqlException sqlException
                && IsRegistrationTransientSqlErrorNumber(sqlException.Number))
            {
                return true;
            }
        }
        return false;
    }

    private static int? FindSqlErrorNumber(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is SqlException sqlException)
            {
                return sqlException.Number;
            }
        }
        return null;
    }

    internal const string RegistrationSql = """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;

        DECLARE @RegistrationTimeUtc datetime2 = NULL;
        DECLARE @ExistingRegisteredAtUtc datetime2 = NULL;
        DECLARE @KidExists bit = 0;
        DECLARE @KidMismatch bit = 0;
        DECLARE @RegistrationLockResult int;

        SELECT
            @KidExists = 1,
            @ExistingRegisteredAtUtc = [RegisteredAtUtc],
            @KidMismatch = CASE WHEN
                [StoreCode] <> @StoreCode
                OR [DeviceCode] <> @DeviceCode
                OR [HardwareId] <> @HardwareId
                OR [Algorithm] <> @Algorithm
                OR [Status] <> N'Active'
                THEN 1 ELSE 0 END
        FROM [dbo].[AttendancePosQrKey]
        WHERE [Kid] = @Kid;

        IF @KidExists = 0
        BEGIN
            -- 关键逻辑：新登记先串行化，再获取缺失 kid 的范围锁，避免并发换绑时锁顺序反转。
            EXEC @RegistrationLockResult = sys.sp_getapplock
                @Resource = N'AttendancePosQrKey_Registration',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 3000;
            IF @RegistrationLockResult < 0
            BEGIN
                ROLLBACK TRANSACTION;
                THROW 51001, 'Attendance QR registration lock timeout.', 1;
            END;
        END;

        SET @KidExists = 0;
        SET @KidMismatch = 0;
        SET @ExistingRegisteredAtUtc = NULL;

        -- 加锁复查，确保初次读取后发生的并发登记不会造成重复或交叉绑定。
        SELECT
            @KidExists = 1,
            @ExistingRegisteredAtUtc = [RegisteredAtUtc],
            @KidMismatch = CASE WHEN
                [StoreCode] <> @StoreCode
                OR [DeviceCode] <> @DeviceCode
                OR [HardwareId] <> @HardwareId
                OR [Algorithm] <> @Algorithm
                OR [Status] <> N'Active'
                THEN 1 ELSE 0 END
        FROM [dbo].[AttendancePosQrKey] WITH (UPDLOCK, HOLDLOCK)
        WHERE [Kid] = @Kid;

        IF @KidExists = 1 AND @KidMismatch = 1
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 51000, 'Attendance QR key id is immutable; generate a new kid.', 1;
        END;

        IF @KidExists = 0
        BEGIN
            SET @RegistrationTimeUtc = SYSUTCDATETIME();

            UPDATE [dbo].[AttendancePosQrKey]
            SET [Status] = N'Revoked', [RevokedAtUtc] = @RegistrationTimeUtc
            WHERE ([DeviceCode] = @DeviceCode OR [HardwareId] = @HardwareId)
              AND [Status] = N'Active';

            INSERT [dbo].[AttendancePosQrKey]
                ([Kid], [Algorithm], [ProtectedKey], [StoreCode], [DeviceCode], [HardwareId], [Status], [RegisteredAtUtc])
            VALUES
                (@Kid, @Algorithm, @ProtectedKey, @StoreCode, @DeviceCode, @HardwareId, N'Active', @RegistrationTimeUtc);
        END;

        COMMIT TRANSACTION;
        -- 响应时间必须在所有锁等待和写入完成后重新读取，供 POS 校准 15 秒二维码。
        SELECT [RegisteredAtUtc], [ProtectedKey], SYSUTCDATETIME() AS [ServerTimeUtc]
        FROM [dbo].[AttendancePosQrKey]
        WHERE [Kid] = @Kid;
        """;

    private sealed class AttendanceSigningKeyRegistrationRow
    {
        public DateTime RegisteredAtUtc { get; set; }
        public DateTime ServerTimeUtc { get; set; }
        public string ProtectedKey { get; set; } = string.Empty;
    }
}
