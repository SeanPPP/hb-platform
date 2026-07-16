using System.Security.Cryptography;
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

public sealed class AttendanceSigningKeyRegistrationService(
    HbposSqlSugarContext dbContext,
    AttendanceQrKeyProtector keyProtector,
    TimeProvider? timeProvider = null) : IAttendanceSigningKeyRegistrationService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

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
            var now = _timeProvider.GetUtcNow().UtcDateTime;
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
                    new SugarParameter("@HardwareId", identity.HardwareId),
                    new SugarParameter("@RegisteredAtUtc", now));
            }
            catch (Exception exception) when (ContainsRegistrationConflictSqlError(exception))
            {
                throw new AttendanceSigningKeyConflictException("kid conflict");
            }

            if (result == null)
            {
                throw new InvalidOperationException("考勤二维码密钥登记未返回结果");
            }

            try
            {
                storedKey = keyProtector.Unprotect(result.ProtectedKey);
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException)
            {
                throw new InvalidOperationException("考勤二维码密钥保护数据无效");
            }
            if (!CryptographicOperations.FixedTimeEquals(storedKey, key))
            {
                throw new AttendanceSigningKeyConflictException("kid conflict");
            }

            return new AttendanceSigningKeyRegistrationResponse(
                request.Kid,
                DateTime.SpecifyKind(result.RegisteredAtUtc, DateTimeKind.Utc),
                now);
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
        number is 51000 or 2601 or 2627 or 1205;

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

    internal const string RegistrationSql = """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;

        DECLARE @ExistingRegisteredAtUtc datetime2 = NULL;
        DECLARE @KidExists bit = 0;
        DECLARE @KidMismatch bit = 0;

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
            UPDATE [dbo].[AttendancePosQrKey]
            SET [Status] = N'Revoked', [RevokedAtUtc] = @RegisteredAtUtc
            WHERE ([DeviceCode] = @DeviceCode OR [HardwareId] = @HardwareId)
              AND [Status] = N'Active';

            INSERT [dbo].[AttendancePosQrKey]
                ([Kid], [Algorithm], [ProtectedKey], [StoreCode], [DeviceCode], [HardwareId], [Status], [RegisteredAtUtc])
            VALUES
                (@Kid, @Algorithm, @ProtectedKey, @StoreCode, @DeviceCode, @HardwareId, N'Active', @RegisteredAtUtc);
        END;

        COMMIT TRANSACTION;
        SELECT [RegisteredAtUtc], [ProtectedKey]
        FROM [dbo].[AttendancePosQrKey]
        WHERE [Kid] = @Kid;
        """;

    private sealed class AttendanceSigningKeyRegistrationRow
    {
        public DateTime RegisteredAtUtc { get; set; }
        public string ProtectedKey { get; set; } = string.Empty;
    }
}
