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
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class EmergencyLoginGrantTests
{
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
        Assert.StartsWith("HBPOSE1-K1-", first.Data.Token);
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
            Options.Create(new EmergencyLoginSigningOptions
            {
                ActiveKeyId = "K1",
                PrivateKeys = new Dictionary<string, string> { ["K1"] = _privateKeyPem },
            }),
            NullLogger<EmergencyLoginGrantService>.Instance,
            new FixedTimeProvider(_now)
        );
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class SqliteColumnRow
    {
        public string Name { get; set; } = string.Empty;
    }
}
