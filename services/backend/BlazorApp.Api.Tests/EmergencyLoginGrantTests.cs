using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.POSM;
using BlazorApp.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class EmergencyLoginGrantTests
{
    [Fact]
    public void TokenCodec_V2HasFixedLengthAndRoundTrips()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = new DateTime(2026, 7, 14, 2, 0, 0, DateTimeKind.Utc);
        var grantId = Guid.Parse("4460b7f9-3770-4806-b0cb-f77eca21cae0");
        var token = EmergencyLoginTokenCodec.SignV2(
            grantId,
            "  s001 ",
            now,
            now.AddHours(12),
            "K202607",
            key.ExportPkcs8PrivateKeyPem());

        Assert.Equal(158, token.Length);
        Assert.StartsWith("HBPOSE2-", token);
        Assert.Matches("^HBPOSE2-[A-Za-z0-9_-]{150}$", token);
        Assert.True(EmergencyLoginTokenCodec.HasSupportedPrefix(token));
        Assert.True(EmergencyLoginTokenCodec.TryVerify(
            token,
            new Dictionary<string, string> { ["K202607"] = key.ExportSubjectPublicKeyInfoPem() },
            "S001",
            now.AddMinutes(1),
            out var claims,
            out var error), error);
        Assert.Equal(grantId, claims!.GrantId);
        Assert.Equal("S001", claims.StoreCode);
        Assert.Equal(now, claims.NotBeforeUtc);
        Assert.Equal(now.AddHours(12), claims.ExpiresAtUtc);
    }

    [Fact]
    public void TokenCodec_V2UsesSpecifiedBinaryLayoutAndSignatureInput()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keyId = "K202607";
        var now = new DateTime(2026, 7, 14, 2, 0, 0, DateTimeKind.Utc);
        var grantId = Guid.Parse("4460b7f9-3770-4806-b0cb-f77eca21cae0");
        var token = EmergencyLoginTokenCodec.SignV2(
            grantId, "s001", now, now.AddHours(12), keyId, key.ExportPkcs8PrivateKeyPem());
        var encoded = token["HBPOSE2-".Length..].Replace('-', '+').Replace('_', '/');
        var bytes = Convert.FromBase64String(encoded + new string('=', (4 - encoded.Length % 4) % 4));

        Assert.Equal(SHA256.HashData(Encoding.ASCII.GetBytes(keyId))[..8], bytes[..8]);
        Assert.Equal(Convert.FromHexString("4460B7F937704806B0CBF77ECA21CAE0"), bytes[8..24]);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes("S001"))[..16], bytes[24..40]);
        Assert.Equal(new DateTimeOffset(now).ToUnixTimeSeconds(), BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(40, 4)));
        Assert.Equal(new DateTimeOffset(now.AddHours(12)).ToUnixTimeSeconds(), BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(44, 4)));
        Assert.True(key.VerifyData(
            Encoding.ASCII.GetBytes("HBPOSE2-").Concat(bytes[..48]).ToArray(),
            bytes[48..],
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void TokenCodec_UnifiedVerifierAcceptsLegacyV1()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = new DateTime(2026, 7, 14, 2, 0, 0, DateTimeKind.Utc);
        var grantId = Guid.NewGuid();
        var token = EmergencyLoginTokenCodec.SignLegacy(CreateLegacyPayload(grantId, "S001", now), "K1", key.ExportPkcs8PrivateKeyPem());

        Assert.True(EmergencyLoginTokenCodec.HasSupportedPrefix(token));
        Assert.True(EmergencyLoginTokenCodec.TryVerify(
            token,
            new Dictionary<string, string> { ["K1"] = key.ExportSubjectPublicKeyInfoPem() },
            "s001",
            now.AddMinutes(1),
            out var claims,
            out var error), error);
        Assert.Equal(grantId, claims!.GrantId);
        Assert.Equal("S001", claims.StoreCode);
    }

    [Fact]
    public void TokenCodec_V2RejectsTamperingWrongStoreAndTimeBounds()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = new DateTime(2026, 7, 14, 2, 0, 0, DateTimeKind.Utc);
        var publicKeys = new Dictionary<string, string> { ["K1"] = key.ExportSubjectPublicKeyInfoPem() };
        var active = EmergencyLoginTokenCodec.SignV2(Guid.NewGuid(), "S001", now, now.AddMinutes(5), "K1", key.ExportPkcs8PrivateKeyPem());
        var future = EmergencyLoginTokenCodec.SignV2(Guid.NewGuid(), "S001", now.AddMinutes(5), now.AddHours(1), "K1", key.ExportPkcs8PrivateKeyPem());
        var encoded = active["HBPOSE2-".Length..].Replace('-', '+').Replace('_', '/');
        var tamperedBytes = Convert.FromBase64String(encoded + new string('=', (4 - encoded.Length % 4) % 4));
        tamperedBytes[48] ^= 0x01;
        var tampered = "HBPOSE2-" + Convert.ToBase64String(tamperedBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        Assert.False(EmergencyLoginTokenCodec.TryVerify(tampered, publicKeys, "S001", now.AddMinutes(1), out _, out var tamperedError));
        Assert.Equal("EMERGENCY_TOKEN_SIGNATURE_INVALID", tamperedError);
        Assert.False(EmergencyLoginTokenCodec.TryVerify(active, publicKeys, "S002", now.AddMinutes(1), out _, out var storeError));
        Assert.Equal("EMERGENCY_TOKEN_WRONG_STORE", storeError);
        Assert.False(EmergencyLoginTokenCodec.TryVerify(future, publicKeys, "S001", now, out _, out var futureError));
        Assert.Equal("EMERGENCY_TOKEN_NOT_ACTIVE", futureError);
        Assert.False(EmergencyLoginTokenCodec.TryVerify(active, publicKeys, "S001", now.AddMinutes(6), out _, out var expiredError));
        Assert.Equal("EMERGENCY_TOKEN_EXPIRED", expiredError);
    }

    [Fact]
    public void TokenCodec_V2RejectsUnknownAmbiguousAndInvalidKeys()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var wrongCurve = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var now = new DateTime(2026, 7, 14, 2, 0, 0, DateTimeKind.Utc);
        var token = EmergencyLoginTokenCodec.SignV2(Guid.NewGuid(), "S001", now, now.AddHours(1), "K1", key.ExportPkcs8PrivateKeyPem());

        Assert.False(EmergencyLoginTokenCodec.TryVerify(token, new Dictionary<string, string>(), "S001", now, out _, out var unknownError));
        Assert.Equal("EMERGENCY_TOKEN_KEY_UNKNOWN", unknownError);
        // 测试辅助类型通过重复枚举同一 KeyId 覆盖 selector 多命中失败关闭分支，不模拟真实 64-bit 哈希碰撞。
        Assert.False(EmergencyLoginTokenCodec.TryVerify(token, new DuplicateEnumeratedKeyDictionary("K1", key.ExportSubjectPublicKeyInfoPem()), "S001", now, out _, out var ambiguousError));
        Assert.Equal("EMERGENCY_TOKEN_KEY_INVALID", ambiguousError);
        Assert.False(EmergencyLoginTokenCodec.TryVerify(token, new Dictionary<string, string> { ["K1"] = wrongCurve.ExportSubjectPublicKeyInfoPem() }, "S001", now, out _, out var keyError));
        Assert.Equal("EMERGENCY_TOKEN_KEY_INVALID", keyError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("HBPOSE2-A")]
    [InlineData("HBPOSE2-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void TokenCodec_V2RejectsInvalidLengthOrCharacters(string token)
    {
        Assert.False(EmergencyLoginTokenCodec.TryVerify(
            token,
            new Dictionary<string, string>(),
            "S001",
            DateTime.UtcNow,
            out _,
            out var error));
        Assert.Equal("EMERGENCY_TOKEN_FORMAT_INVALID", error);
    }

    [Fact]
    public void TokenCodec_V2RejectsNonCanonicalBase64Url()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = new DateTime(2026, 7, 14, 2, 0, 0, DateTimeKind.Utc);
        var token = EmergencyLoginTokenCodec.SignV2(
            Guid.NewGuid(), "S001", now, now.AddHours(1), "K1", key.ExportPkcs8PrivateKeyPem());
        var replacement = token[^1] switch
        {
            'A' => 'B',
            'Q' => 'R',
            'g' => 'h',
            'w' => 'x',
            _ => throw new InvalidOperationException("112 字节编码的末字符应只有四种规范值"),
        };
        var nonCanonical = token[..^1] + replacement;

        Assert.False(EmergencyLoginTokenCodec.TryVerify(
            nonCanonical,
            new Dictionary<string, string> { ["K1"] = key.ExportSubjectPublicKeyInfoPem() },
            "S001",
            now,
            out _,
            out var error));
        Assert.Equal("EMERGENCY_TOKEN_FORMAT_INVALID", error);
    }

    [Fact]
    public void TokenCodec_SignsAndVerifiesP1363Token()
    {
        using var privateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyPem = privateKey.ExportPkcs8PrivateKeyPem();
        var publicKeys = new Dictionary<string, string>
        {
            ["K202607"] = privateKey.ExportSubjectPublicKeyInfoPem(),
        };
        var now = new DateTime(2026, 7, 14, 2, 0, 0, DateTimeKind.Utc);
        var payload = new EmergencyLoginTokenPayload
        {
            GrantId = Guid.Parse("4460b7f9-3770-4806-b0cb-f77eca21cae0"),
            StoreCode = "001",
            BusinessDate = "2026-07-14",
            Issuer = "admin",
            IssuedAtUtc = now,
            NotBeforeUtc = now,
            ExpiresAtUtc = now.AddHours(12),
        };

        var token = EmergencyLoginTokenCodec.Sign(payload, "K202607", privateKeyPem);

        Assert.StartsWith("HBPOSE1-K202607-", token);
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.True(
            EmergencyLoginTokenCodec.TryVerify(
                token,
                publicKeys,
                now.AddMinutes(1),
                out var verified,
                out var error
            ),
            error
        );
        Assert.Equal(payload.GrantId, verified!.GrantId);
        Assert.Equal("001", verified.StoreCode);
        Assert.Equal(EmergencyLoginTokenCodec.AllPosTerminalProfile, verified.PermissionProfile);
        Assert.Equal(EmergencyLoginTokenCodec.WpfAudience, verified.Audience);
    }

    [Fact]
    public void TokenCodec_RejectsTamperedAndExpiredToken()
    {
        using var privateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyPem = privateKey.ExportPkcs8PrivateKeyPem();
        var publicKeys = new Dictionary<string, string>
        {
            ["K1"] = privateKey.ExportSubjectPublicKeyInfoPem(),
        };
        var now = new DateTime(2026, 7, 14, 2, 0, 0, DateTimeKind.Utc);
        var token = EmergencyLoginTokenCodec.Sign(
            new EmergencyLoginTokenPayload
            {
                GrantId = Guid.NewGuid(),
                StoreCode = "001",
                BusinessDate = "2026-07-14",
                Issuer = "admin",
                IssuedAtUtc = now,
                NotBeforeUtc = now,
                ExpiresAtUtc = now.AddMinutes(5),
            },
            "K1",
            privateKeyPem
        );
        var tampered = token[..^1] + (token[^1] == 'A' ? "B" : "A");

        Assert.False(
            EmergencyLoginTokenCodec.TryVerify(
                tampered,
                publicKeys,
                now.AddMinutes(1),
                out _,
                out _
            )
        );
        Assert.False(
            EmergencyLoginTokenCodec.TryVerify(
                token,
                publicKeys,
                now.AddMinutes(6),
                out _,
                out var expiredError
            )
        );
        Assert.Equal("EMERGENCY_TOKEN_EXPIRED", expiredError);

        Assert.False(
            EmergencyLoginTokenCodec.TryVerify(
                token.Replace("-K1-", "-UNKNOWN-", StringComparison.Ordinal),
                publicKeys,
                now.AddMinutes(1),
                out _,
                out var keyError
            )
        );
        Assert.Equal("EMERGENCY_TOKEN_KEY_UNKNOWN", keyError);
    }

    [Fact]
    public void TokenCodec_RejectsUnsafeKeyId()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Throws<ArgumentException>(() => EmergencyLoginTokenCodec.Sign(
            new EmergencyLoginTokenPayload
            {
                GrantId = Guid.NewGuid(),
                StoreCode = "001",
                BusinessDate = "2026-07-14",
                Issuer = "admin",
                IssuedAtUtc = DateTime.UtcNow,
                NotBeforeUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            },
            "KEY_1",
            key.ExportPkcs8PrivateKeyPem()
        ));
    }

    [Theory]
    [InlineData("WrongAudience", EmergencyLoginTokenCodec.AllPosTerminalProfile)]
    [InlineData(EmergencyLoginTokenCodec.WpfAudience, "SystemAdmin")]
    public void TokenCodec_RejectsWrongAudienceOrPermissionProfile(
        string audience,
        string permissionProfile)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = new DateTime(2026, 7, 14, 2, 0, 0, DateTimeKind.Utc);
        var payload = new EmergencyLoginTokenPayload
        {
            GrantId = Guid.NewGuid(),
            StoreCode = "001",
            BusinessDate = "2026-07-14",
            PermissionProfile = permissionProfile,
            Issuer = "admin",
            Audience = audience,
            IssuedAtUtc = now,
            NotBeforeUtc = now,
            ExpiresAtUtc = now.AddHours(1),
        };
        var token = SignRawPayload(payload, "K1", key);

        Assert.False(EmergencyLoginTokenCodec.TryVerify(
            token,
            new Dictionary<string, string> { ["K1"] = key.ExportSubjectPublicKeyInfoPem() },
            now.AddMinutes(1),
            out _,
            out var error));
        Assert.Equal("EMERGENCY_TOKEN_PAYLOAD_INVALID", error);
    }

    [Fact]
    public void TokenCodec_RejectsNotActiveAndOverlongTokens()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = new DateTime(2026, 7, 14, 2, 0, 0, DateTimeKind.Utc);
        var token = EmergencyLoginTokenCodec.Sign(
            new EmergencyLoginTokenPayload
            {
                GrantId = Guid.NewGuid(),
                StoreCode = "001",
                BusinessDate = "2026-07-14",
                Issuer = "admin",
                IssuedAtUtc = now,
                NotBeforeUtc = now.AddMinutes(5),
                ExpiresAtUtc = now.AddHours(1),
            },
            "K1",
            key.ExportPkcs8PrivateKeyPem());
        var publicKeys = new Dictionary<string, string> { ["K1"] = key.ExportSubjectPublicKeyInfoPem() };

        Assert.False(EmergencyLoginTokenCodec.TryVerify(
            token, publicKeys, now, out _, out var notActiveError));
        Assert.Equal("EMERGENCY_TOKEN_NOT_ACTIVE", notActiveError);

        Assert.False(EmergencyLoginTokenCodec.TryVerify(
            token.PadRight(EmergencyLoginTokenCodec.MaxTokenLength + 1, 'A'),
            publicKeys,
            now,
            out _,
            out _));
    }

    [Fact]
    public void BusinessWindow_ExpiresAtNextBrisbaneMidnight()
    {
        var utcNow = new DateTime(2026, 7, 14, 15, 30, 0, DateTimeKind.Utc);

        var (businessDate, expiresAtUtc) = EmergencyLoginGrantService.ResolveBusinessWindow(utcNow);

        Assert.Equal(new DateOnly(2026, 7, 15), businessDate);
        Assert.Equal(new DateTime(2026, 7, 15, 14, 0, 0, DateTimeKind.Utc), expiresAtUtc);
    }

    private static string SignRawPayload(
        EmergencyLoginTokenPayload payload,
        string keyId,
        ECDsa key)
    {
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(
            payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var headerBytes = Encoding.ASCII.GetBytes($"{EmergencyLoginTokenCodec.TokenPrefix}-{keyId}-");
        var signedBytes = new byte[headerBytes.Length + payloadBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, signedBytes, 0, headerBytes.Length);
        Buffer.BlockCopy(payloadBytes, 0, signedBytes, headerBytes.Length, payloadBytes.Length);
        var signature = key.SignData(
            signedBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{EmergencyLoginTokenCodec.TokenPrefix}-{keyId}-{Convert.ToHexString(payloadBytes)}-{Convert.ToHexString(signature)}";
    }

    private static EmergencyLoginTokenPayload CreateLegacyPayload(Guid grantId, string storeCode, DateTime now) => new()
    {
        GrantId = grantId,
        StoreCode = storeCode,
        BusinessDate = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        Issuer = "admin",
        IssuedAtUtc = now,
        NotBeforeUtc = now,
        ExpiresAtUtc = now.AddHours(1),
    };

    private sealed class DuplicateEnumeratedKeyDictionary(string keyId, string publicKeyPem)
        : IReadOnlyDictionary<string, string>
    {
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            yield return new KeyValuePair<string, string>(keyId, publicKeyPem);
            yield return new KeyValuePair<string, string>(keyId, publicKeyPem);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public int Count => 2;
        public IEnumerable<string> Keys => [keyId, keyId];
        public IEnumerable<string> Values => [publicKeyPem, publicKeyPem];
        public string this[string key] => key == keyId ? publicKeyPem : throw new KeyNotFoundException();
        public bool ContainsKey(string key) => key == keyId;
        public bool TryGetValue(string key, out string value)
        {
            value = publicKeyPem;
            return key == keyId;
        }
    }

    [Fact]
    public void SchemaScripts_CreateSummaryOnlyTableAndUniqueActiveGrant()
    {
        var sql = string.Join("\n", EmergencyLoginGrantSchemaMigrator.SqlScriptsForTests);

        Assert.Contains("POSM_EmergencyLoginGrant", sql);
        Assert.Contains("GrantId", sql);
        Assert.Contains("IssuedReason", sql);
        Assert.Contains("RevokedAtUtc", sql);
        Assert.Contains("[IssuedReason] NVARCHAR(200)", sql);
        Assert.Contains("[RevokedReason] NVARCHAR(200)", sql);
        Assert.Contains("UX_POSM_EmergencyLoginGrant_StoreDate_Active", sql);
        Assert.Contains("WHERE [RevokedAtUtc] IS NULL", sql);
        Assert.DoesNotContain("[Token]", sql);
        Assert.DoesNotContain("PrivateKey", sql);
    }

    [Fact]
    public void Controller_AllEndpointsRequireBothManagementPermissions()
    {
        var actions = typeof(EmergencyLoginGrantsController)
            .GetMethods()
            .Where(method => method.DeclaringType == typeof(EmergencyLoginGrantsController))
            .Where(method => method.GetCustomAttributes(true).Any(attribute =>
                attribute.GetType().Name.StartsWith("Http", StringComparison.Ordinal)
            ));

        foreach (var action in actions)
        {
            var policies = action
                .GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>()
                .Select(attribute => attribute.Policy)
                .ToList();

            Assert.Contains(Permissions.DeviceRegistration.Manage, policies);
            Assert.Contains(Permissions.System.ManageSettings, policies);
        }
    }
}

public sealed class EmergencyLoginGrantServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"{Guid.NewGuid():N}.emergency-grants.db"
    );
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;
    private readonly DateTime _now = new(2026, 7, 14, 2, 0, 0, DateTimeKind.Utc);
    private readonly string _privateKeyPem;
    private readonly IDataProtectionProvider _dataProtectionProvider =
        new EphemeralDataProtectionProvider();

    public EmergencyLoginGrantServiceTests()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables<POSM_设备注册信息表>();
        _db.Ado.ExecuteCommand("""
            CREATE TABLE POSM_EmergencyLoginGrant (
                GrantId TEXT PRIMARY KEY,
                StoreCode TEXT NOT NULL,
                BusinessDate TEXT NOT NULL,
                KeyId TEXT NOT NULL,
                PermissionProfile TEXT NOT NULL,
                IssuedBy TEXT NOT NULL,
                IssuedReason TEXT NOT NULL,
                IssuedAtUtc TEXT NOT NULL,
                NotBeforeUtc TEXT NOT NULL,
                ExpiresAtUtc TEXT NOT NULL,
                RevokedAtUtc TEXT NULL,
                RevokedBy TEXT NULL,
                RevokedReason TEXT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _privateKeyPem = key.ExportPkcs8PrivateKeyPem();
        var protectedPrivateKey = _dataProtectionProvider
            .CreateProtector(EmergencyLoginKeyManagementService.PrivateKeyProtectionPurpose)
            .Protect(_privateKeyPem);
        _db.Ado.ExecuteCommand("""
            CREATE TABLE POSM_EmergencyLoginKey (
                KeyId TEXT PRIMARY KEY,
                Status TEXT NOT NULL,
                PublicKeyPem TEXT NOT NULL,
                PublicKeyFingerprint TEXT NOT NULL,
                ProtectedPrivateKey TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                CreatedBy TEXT NOT NULL,
                CreatedReason TEXT NOT NULL,
                ActivatedAtUtc TEXT NULL,
                ActivatedBy TEXT NULL,
                RetiredAtUtc TEXT NULL,
                RetiredBy TEXT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            CREATE TABLE POSM_EmergencyLoginKeySetState (
                StateId INTEGER PRIMARY KEY,
                Version INTEGER NOT NULL,
                ActiveKeyId TEXT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """);
        _db.Insertable(new EmergencyLoginKeyEntity
        {
            KeyId = "K1",
            Status = EmergencyLoginKeyStatus.Active,
            PublicKeyPem = key.ExportSubjectPublicKeyInfoPem(),
            PublicKeyFingerprint = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())),
            ProtectedPrivateKey = protectedPrivateKey,
            CreatedAtUtc = _now,
            CreatedBy = "admin",
            CreatedReason = "测试密钥",
            ActivatedAtUtc = _now,
            ActivatedBy = "admin",
            UpdatedAtUtc = _now,
        }).ExecuteCommand();
        _db.Insertable(new EmergencyLoginKeySetStateEntity
        {
            StateId = 1,
            Version = 1,
            ActiveKeyId = "K1",
            UpdatedAtUtc = _now,
        }).ExecuteCommand();
    }

    [Fact]
    public async Task Create_Revoke_Recreate_PersistsOnlyGrantSummary()
    {
        await _db.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = "hardware-1",
            系统设备编号 = "POS-001",
            分店代码 = "001",
            设备类型 = "POS",
            设备系统 = "Windows",
            设备状态 = 1,
            设备授权码 = "authorization-code",
        }).ExecuteCommandAsync();
        var service = CreateService();

        var first = await service.CreateAsync(
            new EmergencyLoginGrantCreateRequestDto { StoreCode = "001", Reason = "网络中断" },
            "admin"
        );
        var duplicate = await service.CreateAsync(
            new EmergencyLoginGrantCreateRequestDto { StoreCode = "001", Reason = "重复签发" },
            "admin"
        );
        var revoked = await service.RevokeAsync(
            first.Data!.Grant.GrantId,
            new EmergencyLoginGrantRevokeRequestDto { Reason = "故障解除" },
            "admin"
        );
        var recreated = await service.CreateAsync(
            new EmergencyLoginGrantCreateRequestDto { StoreCode = "001", Reason = "再次中断" },
            "admin"
        );
        var columns = await _db.Ado.SqlQueryAsync<SqliteColumnRow>(
            "PRAGMA table_info('POSM_EmergencyLoginGrant')"
        );

        Assert.True(first.Success);
        Assert.StartsWith("HBPOSE2-", first.Data.Token);
        Assert.Equal(EmergencyLoginTokenCodec.V2TokenLength, first.Data.Token.Length);
        Assert.False(duplicate.Success);
        Assert.Equal("EMERGENCY_GRANT_ALREADY_ACTIVE", duplicate.ErrorCode);
        Assert.True(revoked.Success);
        Assert.Equal("Revoked", revoked.Data!.Status);
        Assert.True(recreated.Success);
        Assert.NotEqual(first.Data.Grant.GrantId, recreated.Data!.Grant.GrantId);
        Assert.DoesNotContain(
            columns,
            column => column.Name.Equals("Token", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task Create_RejectsReasonLongerThanTwoHundredCharacters()
    {
        var result = await CreateService().CreateAsync(
            new EmergencyLoginGrantCreateRequestDto
            {
                StoreCode = "001",
                Reason = new string('x', 201),
            },
            "admin"
        );

        Assert.False(result.Success);
        Assert.Equal("EMERGENCY_GRANT_REASON_INVALID", result.ErrorCode);
    }

    [Fact]
    public async Task Create_FailsClosedWhenNoActiveDatabaseKeyExists()
    {
        await AddEnabledPosAsync();
        await _db.Deleteable<EmergencyLoginKeyEntity>().ExecuteCommandAsync();
        await _db.Updateable<EmergencyLoginKeySetStateEntity>()
            .SetColumns(item => item.ActiveKeyId == null)
            .Where(item => item.StateId == 1)
            .ExecuteCommandAsync();

        var result = await CreateService().CreateAsync(
            new EmergencyLoginGrantCreateRequestDto { StoreCode = "001", Reason = "网络中断" },
            "admin"
        );

        Assert.False(result.Success);
        Assert.Equal("EMERGENCY_GRANT_ACTIVE_SIGNING_KEY_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task Create_FailsClosedWhenActivePrivateKeyCannotBeDecrypted()
    {
        await AddEnabledPosAsync();
        await _db.Updateable<EmergencyLoginKeyEntity>()
            .SetColumns(item => item.ProtectedPrivateKey == "not-a-data-protection-payload")
            .Where(item => item.KeyId == "K1")
            .ExecuteCommandAsync();

        var result = await CreateService().CreateAsync(
            new EmergencyLoginGrantCreateRequestDto { StoreCode = "001", Reason = "网络中断" },
            "admin"
        );

        Assert.False(result.Success);
        Assert.Equal("EMERGENCY_GRANT_SIGNING_KEY_DECRYPT_FAILED", result.ErrorCode);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private EmergencyLoginGrantService CreateService()
    {
        var scope = new Mock<ICurrentUserManageableStoreScopeService>();
        scope
            .Setup(service => service.CanAccessStoreCodeAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        var context = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(POSMSqlSugarContext)
        );
        typeof(POSMSqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, _db);

        return new EmergencyLoginGrantService(
            context,
            scope.Object,
            _dataProtectionProvider,
            NullLogger<EmergencyLoginGrantService>.Instance,
            new FixedTimeProvider(_now)
        );
    }

    private Task<int> AddEnabledPosAsync() => _db.Insertable(new POSM_设备注册信息表
    {
        设备硬件识别码 = Guid.NewGuid().ToString("N"),
        系统设备编号 = $"POS-{Guid.NewGuid():N}",
        分店代码 = "001",
        设备类型 = "POS",
        设备系统 = "Windows",
        设备状态 = 1,
        设备授权码 = "authorization-code",
    }).ExecuteReturnIdentityAsync();

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class SqliteColumnRow
    {
        public string Name { get; set; } = string.Empty;
    }
}
