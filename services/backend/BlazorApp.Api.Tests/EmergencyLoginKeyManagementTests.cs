using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.POSM;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class EmergencyLoginKeySchemaMigratorTests
{
    [Fact]
    public void SchemaScripts_CreateAllKeyManagementTablesAndFilteredUniqueIndexes()
    {
        var sql = string.Join("\n", EmergencyLoginKeySchemaMigrator.SqlScriptsForTests);

        Assert.Contains("POSM_EmergencyLoginKey", sql);
        Assert.Contains("POSM_EmergencyLoginKeySetState", sql);
        Assert.Contains("POSM_EmergencyLoginKeyDeviceSync", sql);
        Assert.Contains("POSM_EmergencyLoginKeyAudit", sql);
        Assert.Contains("WHERE [Status] = N'Staged'", sql);
        Assert.Contains("WHERE [Status] = N'Active'", sql);
        Assert.Contains("CHECK ([Status] IN (N'Staged', N'Active', N'Retiring', N'Retired'))", sql);
        Assert.Contains("IF OBJECT_ID", sql);
        Assert.Contains("IF NOT EXISTS", sql);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Program_RegistersServiceAndRunsIdempotentMigratorAtStartup()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null
               && (!Directory.Exists(Path.Combine(directory.FullName, "apps"))
                   || !Directory.Exists(Path.Combine(directory.FullName, "services"))))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var program = await File.ReadAllTextAsync(Path.Combine(
            directory!.FullName,
            "services/backend/BlazorApp.Api/Program.cs"
        ));
        Assert.Contains("AddScoped<EmergencyLoginKeyManagementService>()", program);
        Assert.Contains(
            "await EmergencyLoginKeySchemaMigrator.EnsureAsync(posmDbContext.Db, app.Logger);",
            program
        );
    }
}

public sealed class EmergencyLoginKeyManagementContractTests
{
    [Fact]
    public void ResponseDtos_NeverExposeProtectedOrPrivateKeyMaterial()
    {
        var exposedNames = typeof(EmergencyLoginKeyListDto)
            .Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(EmergencyLoginKeyListDto).Namespace)
            .Where(type => type.Name.StartsWith("EmergencyLoginKey", StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .ToList();

        Assert.DoesNotContain(exposedNames, name =>
            name.Contains("Private", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Protected", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Cipher", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Controller_UsesExpectedRoutesAndOnlySystemManageSettings()
    {
        var controller = typeof(EmergencyLoginKeysController);
        var route = controller.GetCustomAttribute<Microsoft.AspNetCore.Mvc.RouteAttribute>();

        Assert.Equal("api/react/v1/emergency-login-keys", route!.Template);
        foreach (var action in controller.GetMethods().Where(method =>
                     method.DeclaringType == controller
                     && method.GetCustomAttributes(true).Any(attribute =>
                         attribute.GetType().Name.StartsWith("Http", StringComparison.Ordinal))))
        {
            var policies = action.GetCustomAttributes<AuthorizeAttribute>()
                .Select(attribute => attribute.Policy)
                .ToList();
            Assert.Single(policies);
            Assert.Equal(Permissions.System.ManageSettings, policies[0]);
        }

        Assert.Equal(
            "generate",
            controller.GetMethod(nameof(EmergencyLoginKeysController.Generate))!
                .GetCustomAttribute<HttpPostAttribute>()!.Template
        );
        Assert.Equal(
            "{kid}/activate",
            controller.GetMethod(nameof(EmergencyLoginKeysController.Activate))!
                .GetCustomAttribute<HttpPostAttribute>()!.Template
        );
        Assert.Equal(
            "{kid}/retire",
            controller.GetMethod(nameof(EmergencyLoginKeysController.Retire))!
                .GetCustomAttribute<HttpPostAttribute>()!.Template
        );
    }
}

public sealed class EmergencyLoginKeyManagementServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"{Guid.NewGuid():N}.emergency-keys.db"
    );
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;
    private readonly DateTime _now = new(2026, 7, 15, 1, 2, 3, DateTimeKind.Utc);
    private readonly IDataProtectionProvider _dataProtectionProvider =
        new EphemeralDataProtectionProvider();

    public EmergencyLoginKeyManagementServiceTests()
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
            CREATE UNIQUE INDEX UX_Key_Staged ON POSM_EmergencyLoginKey(Status) WHERE Status = 'Staged';
            CREATE UNIQUE INDEX UX_Key_Active ON POSM_EmergencyLoginKey(Status) WHERE Status = 'Active';
            CREATE TABLE POSM_EmergencyLoginKeySetState (
                StateId INTEGER PRIMARY KEY,
                Version INTEGER NOT NULL,
                ActiveKeyId TEXT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            INSERT INTO POSM_EmergencyLoginKeySetState VALUES (1, 0, NULL, '2026-07-15T01:02:03Z');
            CREATE TABLE POSM_EmergencyLoginKeyDeviceSync (
                DeviceRegistrationId INTEGER NOT NULL,
                KeySetVersion INTEGER NOT NULL,
                KeyId TEXT NOT NULL,
                AcknowledgedAtUtc TEXT NOT NULL,
                LastSeenAtUtc TEXT NULL,
                PRIMARY KEY (DeviceRegistrationId, KeySetVersion)
            );
            CREATE TABLE POSM_EmergencyLoginKeyAudit (
                AuditId INTEGER PRIMARY KEY AUTOINCREMENT,
                KeyId TEXT NULL,
                Action TEXT NOT NULL,
                Actor TEXT NOT NULL,
                Reason TEXT NOT NULL,
                ExpectedVersion INTEGER NOT NULL,
                ResultVersion INTEGER NOT NULL,
                Details TEXT NULL,
                CreatedAtUtc TEXT NOT NULL
            );
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
    }

    [Fact]
    public async Task Generate_CreatesEncryptedP256StagedKeyAndAdvancesVersion()
    {
        var result = await CreateService().GenerateAsync(
            new EmergencyLoginKeyGenerateRequestDto { ExpectedVersion = 0, Reason = "首次生成" },
            "admin"
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Data!.Version);
        Assert.Matches("^K[0-9]{14}[A-F0-9]+$", result.Data.Key.KeyId);
        Assert.True(result.Data.Key.KeyId.Length <= 32);
        Assert.Equal("Staged", result.Data.Key.Status);
        Assert.Equal(64, result.Data.Key.PublicKeyFingerprint.Length);
        Assert.Contains("BEGIN PUBLIC KEY", result.Data.Key.PublicKeyPem);
        var stored = await _db.Queryable<EmergencyLoginKeyEntity>().SingleAsync();
        Assert.DoesNotContain("PRIVATE KEY", stored.ProtectedPrivateKey, StringComparison.Ordinal);

        var stale = await CreateService().GenerateAsync(
            new EmergencyLoginKeyGenerateRequestDto { ExpectedVersion = 0, Reason = "并发生成" },
            "admin"
        );
        Assert.False(stale.Success);
        Assert.Equal("EMERGENCY_KEY_VERSION_CONFLICT", stale.ErrorCode);
    }

    [Fact]
    public async Task Mutations_RejectMissingExpectedVersion()
    {
        var result = await CreateService().GenerateAsync(
            new EmergencyLoginKeyGenerateRequestDto { Reason = "遗漏版本" },
            "admin"
        );

        Assert.False(result.Success);
        Assert.Equal("EMERGENCY_KEY_EXPECTED_VERSION_REQUIRED", result.ErrorCode);
    }

    [Fact]
    public async Task Activate_RequiresEveryEnabledPosAckUnlessForced()
    {
        var firstDeviceId = await AddPosDeviceAsync("001", "POS-001", "hardware-1", 1);
        var secondDeviceId = await AddPosDeviceAsync("002", "POS-002", "hardware-2", 1);
        await AddPosDeviceAsync("003", "POS-003", "hardware-3", 0);
        var service = CreateService();
        var generated = await service.GenerateAsync(
            new EmergencyLoginKeyGenerateRequestDto { ExpectedVersion = 0, Reason = "轮换" },
            "admin"
        );
        var keyId = generated.Data!.Key.KeyId;
        await InsertAckAsync(firstDeviceId, 1, keyId);

        var blocked = await service.ActivateAsync(
            keyId,
            new EmergencyLoginKeyActivateRequestDto
            {
                ExpectedVersion = 1,
                Reason = "正常激活",
            },
            "admin"
        );

        Assert.False(blocked.Success);
        Assert.Equal("EMERGENCY_KEY_DEVICE_ACK_INCOMPLETE", blocked.ErrorCode);
        var missing = Assert.IsType<List<EmergencyLoginKeyMissingDeviceDto>>(blocked.Details);
        Assert.Single(missing);
        Assert.Equal("POS-002", missing[0].DeviceNumber);

        var forced = await service.ActivateAsync(
            keyId,
            new EmergencyLoginKeyActivateRequestDto
            {
                ExpectedVersion = 1,
                Reason = "门店故障，强制激活",
                Force = true,
            },
            "admin"
        );

        Assert.True(forced.Success, forced.Message);
        Assert.Equal(2, forced.Data!.Version);
        Assert.Equal(keyId, forced.Data.ActiveKeyId);
        var audit = await _db.Queryable<EmergencyLoginKeyAuditEntity>()
            .Where(item => item.Action == "ForceActivate")
            .SingleAsync();
        Assert.Contains(secondDeviceId.ToString(), audit.Details);
        Assert.DoesNotContain("PRIVATE KEY", audit.Details ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Activate_SucceedsAfterEveryEnabledPosAcknowledgesCurrentVersion()
    {
        var firstDeviceId = await AddPosDeviceAsync("001", "POS-001", "hardware-1", 1);
        var secondDeviceId = await AddPosDeviceAsync("002", "POS-002", "hardware-2", 1);
        var service = CreateService();
        var generated = await service.GenerateAsync(
            new EmergencyLoginKeyGenerateRequestDto { ExpectedVersion = 0, Reason = "轮换" },
            "admin"
        );
        await InsertAckAsync(firstDeviceId, 1, generated.Data!.Key.KeyId);
        await InsertAckAsync(secondDeviceId, 1, generated.Data.Key.KeyId);

        var activated = await service.ActivateAsync(
            generated.Data.Key.KeyId,
            new EmergencyLoginKeyActivateRequestDto
            {
                ExpectedVersion = 1,
                Reason = "设备同步完成",
            },
            "admin"
        );

        Assert.True(activated.Success, activated.Message);
        Assert.Equal(2, activated.Data!.Version);
        var audit = await _db.Queryable<EmergencyLoginKeyAuditEntity>()
            .Where(item => item.Action == "Activate")
            .SingleAsync();
        Assert.Null(audit.Details);
    }

    [Fact]
    public async Task Retire_StagedKeyClearsProtectedPrivateKey()
    {
        var service = CreateService();
        var generated = await service.GenerateAsync(
            new EmergencyLoginKeyGenerateRequestDto { ExpectedVersion = 0, Reason = "临时生成" },
            "admin"
        );

        var retired = await service.RetireAsync(
            generated.Data!.Key.KeyId,
            new EmergencyLoginKeyRetireRequestDto { ExpectedVersion = 1, Reason = "放弃此密钥" },
            "admin"
        );

        Assert.True(retired.Success, retired.Message);
        Assert.Equal("Retired", retired.Data!.Key.Status);
        var stored = await _db.Queryable<EmergencyLoginKeyEntity>().SingleAsync();
        Assert.Null(stored.ProtectedPrivateKey);
    }

    [Fact]
    public async Task Rotation_PreservesCurrentActiveAndBlocksRetiringKeyWithLiveGrant()
    {
        var service = CreateService();
        var first = await service.GenerateAsync(
            new EmergencyLoginKeyGenerateRequestDto { ExpectedVersion = 0, Reason = "首把密钥" },
            "admin"
        );
        var firstActivated = await service.ActivateAsync(
            first.Data!.Key.KeyId,
            new EmergencyLoginKeyActivateRequestDto { ExpectedVersion = 1, Reason = "首次激活" },
            "admin"
        );
        var second = await service.GenerateAsync(
            new EmergencyLoginKeyGenerateRequestDto { ExpectedVersion = 2, Reason = "例行轮换" },
            "admin"
        );

        Assert.Equal(first.Data.Key.KeyId, second.Data!.ActiveKeyId);
        Assert.Equal(first.Data.Key.KeyId, (await service.ListAsync()).Data!.ActiveKeyId);
        var secondActivated = await service.ActivateAsync(
            second.Data.Key.KeyId,
            new EmergencyLoginKeyActivateRequestDto { ExpectedVersion = 3, Reason = "切换密钥" },
            "admin"
        );
        Assert.True(secondActivated.Success, secondActivated.Message);
        var oldKey = await _db.Queryable<EmergencyLoginKeyEntity>()
            .SingleAsync(item => item.KeyId == first.Data.Key.KeyId);
        Assert.Equal("Retiring", oldKey.Status);

        await _db.Insertable(new EmergencyLoginGrantEntity
        {
            GrantId = Guid.NewGuid(),
            StoreCode = "001",
            BusinessDate = DateTime.SpecifyKind(_now.Date, DateTimeKind.Unspecified),
            KeyId = oldKey.KeyId,
            PermissionProfile = "AllPosTerminalUsers",
            IssuedBy = "admin",
            IssuedReason = "网络中断",
            IssuedAtUtc = _now,
            NotBeforeUtc = _now,
            ExpiresAtUtc = _now.AddHours(1),
            UpdatedAtUtc = _now,
        }).ExecuteCommandAsync();

        var blocked = await service.RetireAsync(
            oldKey.KeyId,
            new EmergencyLoginKeyRetireRequestDto { ExpectedVersion = 4, Reason = "轮换完成" },
            "admin"
        );

        Assert.False(blocked.Success);
        Assert.Equal("EMERGENCY_KEY_ACTIVE_GRANTS_EXIST", blocked.ErrorCode);

        await _db.Updateable<EmergencyLoginGrantEntity>()
            .SetColumns(item => item.RevokedAtUtc == _now)
            .Where(item => item.KeyId == oldKey.KeyId)
            .ExecuteCommandAsync();
        var retired = await service.RetireAsync(
            oldKey.KeyId,
            new EmergencyLoginKeyRetireRequestDto { ExpectedVersion = 4, Reason = "授权已撤销" },
            "admin"
        );
        Assert.True(retired.Success, retired.Message);
        Assert.Equal(5, retired.Data!.Version);
        Assert.Equal("Retired", retired.Data.Key.Status);
    }

    [Fact]
    public async Task List_ReturnsCoverageAndDataProtectionHealthWithoutSecrets()
    {
        await AddPosDeviceAsync("001", "POS-001", "hardware-1", 1);
        var service = CreateService();
        await service.GenerateAsync(
            new EmergencyLoginKeyGenerateRequestDto { ExpectedVersion = 0, Reason = "准备轮换" },
            "admin"
        );

        var result = await service.ListAsync();

        Assert.True(result.Success);
        Assert.True(result.Data!.DataProtectionHealthy);
        Assert.Equal(1, result.Data.Coverage.TotalDevices);
        Assert.Equal(0, result.Data.Coverage.AcknowledgedDevices);
        Assert.Single(result.Data.MissingDevices);
    }

    [Fact]
    public async Task List_ReportsUnhealthyWhenStoredSigningKeyCannotBeDecrypted()
    {
        var service = CreateService();
        await service.GenerateAsync(
            new EmergencyLoginKeyGenerateRequestDto { ExpectedVersion = 0, Reason = "准备轮换" },
            "admin"
        );
        await _db.Updateable<EmergencyLoginKeyEntity>()
            .SetColumns(item => item.ProtectedPrivateKey == "invalid-protected-value")
            .Where(item => item.Status == EmergencyLoginKeyStatus.Staged)
            .ExecuteCommandAsync();

        var result = await service.ListAsync();

        Assert.True(result.Success);
        Assert.False(result.Data!.DataProtectionHealthy);
        Assert.Equal("StoredKeyDecryptFailed", result.Data.DataProtectionStatus);
    }

    [Fact]
    public async Task Controller_ReturnsConflictForExpectedVersionMismatch()
    {
        var service = CreateService();
        await service.GenerateAsync(
            new EmergencyLoginKeyGenerateRequestDto { ExpectedVersion = 0, Reason = "首次生成" },
            "admin"
        );
        var controller = new EmergencyLoginKeysController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        var result = await controller.Generate(new EmergencyLoginKeyGenerateRequestDto
        {
            ExpectedVersion = 0,
            Reason = "并发请求",
        });

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private EmergencyLoginKeyManagementService CreateService()
    {
        var context = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(POSMSqlSugarContext)
        );
        typeof(POSMSqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, _db);
        return new EmergencyLoginKeyManagementService(
            context,
            _dataProtectionProvider,
            NullLogger<EmergencyLoginKeyManagementService>.Instance,
            new FixedTimeProvider(_now)
        );
    }

    private async Task<int> AddPosDeviceAsync(
        string storeCode,
        string deviceNumber,
        string hardwareId,
        int status
    )
    {
        return await _db.Insertable(new POSM_设备注册信息表
        {
            分店代码 = storeCode,
            系统设备编号 = deviceNumber,
            设备硬件识别码 = hardwareId,
            设备类型 = "POS",
            设备系统 = "Windows",
            设备状态 = status,
            设备授权码 = "authorization-code",
            最后心跳时间 = _now.AddMinutes(-5),
        }).ExecuteReturnIdentityAsync();
    }

    private Task InsertAckAsync(int deviceId, long version, string keyId) =>
        _db.Insertable(new EmergencyLoginKeyDeviceSyncEntity
        {
            DeviceRegistrationId = deviceId,
            KeySetVersion = version,
            KeyId = keyId,
            AcknowledgedAtUtc = _now,
            LastSeenAtUtc = _now,
        }).ExecuteCommandAsync();

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
