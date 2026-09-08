using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Security;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.RustDeskCompat;
using BlazorApp.Api.Utils;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class RustDeskCompatServiceTests : IDisposable
{
    private readonly string _mainPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
    private readonly string _posmPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
    private readonly SqliteConnection _mainConnection;
    private readonly SqliteConnection _posmConnection;
    private readonly SqlSugarClient _mainDb;
    private readonly SqlSugarClient _posmDb;
    private readonly TestTimeProvider _time = new(DateTimeOffset.Parse("2026-09-06T00:00:00Z"));
    private readonly RemoteMaintenanceSecretProtector _protector =
        RemoteMaintenanceDataProtection.CreateProtector(new EphemeralDataProtectionProvider());
    private readonly RecordingLogger _logger = new();

    public RustDeskCompatServiceTests()
    {
        _mainConnection = new SqliteConnection($"Data Source={_mainPath}");
        _posmConnection = new SqliteConnection($"Data Source={_posmPath}");
        _mainConnection.Open();
        _posmConnection.Open();
        _mainDb = CreateSqliteClient(_mainConnection);
        _posmDb = CreateSqliteClient(_posmConnection);
        _mainDb.CodeFirst.InitTables<User, Role, UserRole>();
        _mainDb.CodeFirst.InitTables<Store>();
        _mainDb.CodeFirst.InitTables<RustDeskClientSession, RustDeskManagedDevice, RemoteMaintenanceDevice>();
        _posmDb.CodeFirst.InitTables<POSM_设备注册信息表>();
        // 测试保留开通码表的可空消费/撤销字段，避免 SQLite CodeFirst 改变证明链语义。
        _posmDb.Ado.ExecuteCommand("""
            CREATE TABLE POSM_DeviceActivationGrant (
                GrantId TEXT PRIMARY KEY, SecretHash BLOB NOT NULL,
                StoreCode TEXT NOT NULL, DeviceSystem TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL, CreatedBy TEXT NOT NULL, Reason TEXT NOT NULL,
                ExpiresAtUtc TEXT NOT NULL, RevokedAtUtc TEXT NULL, RevokedBy TEXT NULL, RevokeReason TEXT NULL,
                ConsumedAtUtc TEXT NULL, ConsumedHardwareId TEXT NULL, ConsumedDeviceCode TEXT NULL,
                ConsumedDeviceRegistrationId INTEGER NULL, ConsumedAuthorizationHash BLOB NULL,
                ConsumedDeviceSystem TEXT NULL, ConsumptionKind TEXT NULL,
                PreviousStoreCode TEXT NULL, PreviousDeviceCode TEXT NULL, RowVersion BLOB NULL
            )
            """);
    }

    [Fact]
    public async Task LoginStoresOnlyHashAndAuthenticateRechecksAdminAndPasswordFingerprint()
    {
        var user = await SeedAdminAsync();
        var service = CreateService();

        var login = await service.LoginAsync(new RustDeskLoginRequest
        {
            Username = " ADMIN ", Password = "Secret123",
        }, "198.51.100.7", CancellationToken.None);

        Assert.NotNull(login);
        Assert.NotEqual(login!.AccessToken, (await _mainDb.Queryable<RustDeskClientSession>().SingleAsync()).TokenHash);
        Assert.Equal(user.UserGUID, login.User.UserGuid);
        Assert.Equal(login.User, await service.AuthenticateAsync(login.AccessToken, CancellationToken.None));

        user.PasswordHash = PasswordHasher.HashPassword("Changed123");
        await _mainDb.Updateable(user).ExecuteCommandAsync();
        Assert.Null(await service.AuthenticateAsync(login.AccessToken, CancellationToken.None));
    }

    [Fact]
    public async Task LoginRejectsNonAdminAndPasswordFormatsOtherThanRaw()
    {
        var user = await SeedUserAsync("manager", "Manager", "Manager");
        var service = CreateService();

        Assert.Null(await service.LoginAsync(new RustDeskLoginRequest
        {
            Username = user.Username, Password = "Secret123",
        }, "127.0.0.1", CancellationToken.None));

        await AddAdminRoleAsync(user.UserGUID);
        var clientHash = PasswordHasher.ComputeSha256("Secret123");
        Assert.Null(await service.LoginAsync(new RustDeskLoginRequest
        {
            Username = user.Username, Password = clientHash,
        }, "127.0.0.1", CancellationToken.None));
    }

    [Fact]
    public async Task AuthenticateFailsWhenUserIsDisabledRoleRemovedOrSessionExpiresAndLogoutRevokes()
    {
        var user = await SeedAdminAsync();
        var service = CreateService();
        var login = await service.LoginAsync(new RustDeskLoginRequest { Username = user.Username, Password = "Secret123" }, "127.0.0.1", CancellationToken.None);
        Assert.NotNull(login);

        user.IsActive = false;
        await _mainDb.Updateable(user).ExecuteCommandAsync();
        Assert.Null(await service.AuthenticateAsync(login!.AccessToken, CancellationToken.None));

        user.IsActive = true;
        await _mainDb.Updateable(user).ExecuteCommandAsync();
        await _mainDb.Updateable<UserRole>().SetColumns(x => new UserRole { IsDeleted = true }).Where(x => x.UserGUID == user.UserGUID).ExecuteCommandAsync();
        Assert.Null(await service.AuthenticateAsync(login.AccessToken, CancellationToken.None));

        await service.LogoutAsync(login.AccessToken, CancellationToken.None);
        var session = await _mainDb.Queryable<RustDeskClientSession>().SingleAsync();
        Assert.NotNull(session.RevokedAtUtc);

        var second = await service.LoginAsync(new RustDeskLoginRequest { Username = user.Username, Password = "Secret123" }, "127.0.0.1", CancellationToken.None);
        Assert.Null(second);

        // 重新加回角色只为验证独立过期行为，不复用已撤销会话。
        await AddAdminRoleAsync(user.UserGUID);
        second = await service.LoginAsync(new RustDeskLoginRequest { Username = user.Username, Password = "Secret123" }, "127.0.0.1", CancellationToken.None);
        Assert.NotNull(second);
        _time.Advance(TimeSpan.FromDays(30));
        Assert.Null(await service.AuthenticateAsync(second!.AccessToken, CancellationToken.None));
    }

    [Fact]
    public async Task GetPeersReturnsManagedDevicesAndOnlyLatestEnabledWindowsPos()
    {
        var user = await SeedAdminAsync();
        await _mainDb.Insertable(new RustDeskManagedDevice
        {
            Id = Guid.NewGuid(), RustdeskId = "1615245593", Alias = "Mac office", Hostname = "Mac-1", Platform = "Mac",
            CreatedAtUtc = _time.UtcDateTime,
        }).ExecuteCommandAsync();
        var registration = new POSM_设备注册信息表
        {
            设备硬件识别码 = "hw-1", 系统设备编号 = "POS-01", 分店代码 = "S001", 设备类型 = "POS", 设备系统 = "Windows", 设备状态 = 1,
            设备授权码 = "auth", 创建时间 = _time.UtcDateTime,
        };
        registration.ID = await _posmDb.Insertable(registration).ExecuteReturnIdentityAsync();
        await _mainDb.Insertable(new RemoteMaintenanceDevice
        {
            Id = Guid.NewGuid(), DeviceRegistrationId = registration.ID, HardwareId = "hw-1", DeviceCode = "POS-01",
            ComputerName = "POS-01", StoreCode = "S001", RustdeskId = "123456789", RegisteredAtUtc = _time.UtcDateTime,
        }).ExecuteCommandAsync();
        var peers = await CreateService().GetPeersAsync(new RustDeskAuthenticatedUser(user.UserGUID, user.Username, user.FullName!), CancellationToken.None);

        Assert.Equal(2, peers.Count);
        Assert.Contains(peers, x => x.Id == "1615245593" && x.Platform == "Mac");
        Assert.Contains(peers, x => x.Id == "123456789" && x.Platform == "Windows" && x.Tags.Contains("S001"));
        // 同一硬件重新登记但被停用后，不能回退到旧的 enabled 行。
        registration.ID = 0;
        registration.设备状态 = 0;
        await _posmDb.Insertable(registration).ExecuteReturnIdentityAsync();
        peers = await CreateService().GetPeersAsync(new RustDeskAuthenticatedUser(user.UserGUID, user.Username, user.FullName!), CancellationToken.None);
        Assert.Single(peers);
        Assert.Equal("1615245593", peers[0].Id);
    }

    [Fact]
    public async Task SharedAddressBookDecryptsRegisteredPasswordOnlyWhenExplicitlyRequestedAndEnabled()
    {
        var user = await SeedAdminAsync();
        var principal = new RustDeskAuthenticatedUser(user.UserGUID, user.Username, user.FullName!);
        var device = await SeedPosAsync("sync", _protector.ProtectPassword("test-device-password"));
        var service = CreateService();

        var ordinary = Assert.Single(await service.GetPeersAsync(principal, CancellationToken.None));
        Assert.Null(ordinary.Password);
        var shared = Assert.Single((await service.GetAddressBookPeersAsync(principal, 1, 100, CancellationToken.None)).Data);
        Assert.Equal("test-device-password", shared.Password);
        Assert.DoesNotContain("test-device-password", shared.ToString());
        Assert.DoesNotContain(device.CredentialCiphertext!, System.Text.Json.JsonSerializer.Serialize(shared));
        var disabled = Assert.Single((await CreateService(enabled: false).GetAddressBookPeersAsync(principal, 1, 100, CancellationToken.None)).Data);
        Assert.Null(disabled.Password);
    }

    [Fact]
    public async Task PosNamesFollowCurrentStoreAndDeviceCodeAfterReregistrationWithoutChangingRemoteCredentials()
    {
        var user = await SeedAdminAsync();
        await _mainDb.Insertable(new[]
        {
            new Store { StoreGUID = Guid.NewGuid().ToString(), StoreCode = "1042", StoreName = "分店 A" },
            new Store { StoreGUID = Guid.NewGuid().ToString(), StoreCode = "1043", StoreName = "分店 B" },
        }).ExecuteCommandAsync();
        var device = await SeedPosAsync("238424213", _protector.ProtectPassword("unchanged-device-test-password"));
        var registration = await _posmDb.Queryable<POSM_设备注册信息表>().SingleAsync();
        registration.分店代码 = "1042";
        registration.系统设备编号 = "POS_1042_0200";
        await _posmDb.Updateable(registration).ExecuteCommandAsync();
        var service = CreateService();
        var principal = new RustDeskAuthenticatedUser(user.UserGUID, user.Username, user.FullName!);

        var first = Assert.Single(await service.GetPeersAsync(principal, CancellationToken.None));
        Assert.Equal("分店 A POS_1042_0200", first.Alias);
        Assert.Equal(first.Alias, first.Hostname);
        Assert.Equal(string.Empty, first.Username);
        Assert.Equal(new[] { "1042" }, first.Tags);

        // 模拟同一台 POS 在注册流程中更新分店及系统设备编号；远程维护安装快照故意保持旧值。
        registration.分店代码 = "1043";
        registration.系统设备编号 = "POS_1043_0200";
        await _posmDb.Updateable(registration).ExecuteCommandAsync();
        var renamed = Assert.Single(await service.GetPeersAsync(principal, CancellationToken.None));
        var shared = Assert.Single((await service.GetAddressBookPeersAsync(principal, 1, 100, CancellationToken.None)).Data);
        Assert.Equal("分店 B POS_1043_0200", renamed.Alias);
        Assert.Equal(renamed.Alias, shared.Alias);
        Assert.Equal(renamed.Alias, shared.Hostname);
        Assert.Equal(device.RustdeskId, renamed.Id);
        Assert.Equal(new[] { "1043" }, shared.Tags);
        Assert.Equal("unchanged-device-test-password", shared.Password);
        var snapshot = await _mainDb.Queryable<RemoteMaintenanceDevice>().SingleAsync();
        Assert.Equal(device.StoreCode, snapshot.StoreCode);
        Assert.Equal(device.DeviceCode, snapshot.DeviceCode);
        Assert.Equal(device.CredentialCiphertext, snapshot.CredentialCiphertext);

        await _mainDb.Updateable<Store>().SetColumns(x => new Store { StoreName = "分店 B 新名称" })
            .Where(x => x.StoreCode == "1043").ExecuteCommandAsync();
        Assert.Equal("分店 B 新名称 POS_1043_0200", Assert.Single(await service.GetPeersAsync(principal, CancellationToken.None)).Alias);
    }

    [Fact]
    public async Task AddressBookFollowsProvenWpfStoreRebindWithTheExistingRustDeskInstallation()
    {
        var user = await SeedAdminAsync();
        var snapshot = await SeedPosAsync("238424213", _protector.ProtectPassword("existing-install-test-password"));
        var source = await _posmDb.Queryable<POSM_设备注册信息表>().SingleAsync();
        await _posmDb.Updateable<POSM_设备注册信息表>().SetColumns(x => new POSM_设备注册信息表 { 设备状态 = 0 })
            .Where(x => x.ID == source.ID).ExecuteCommandAsync();
        var target = new POSM_设备注册信息表
        {
            设备硬件识别码 = source.设备硬件识别码, 分店代码 = "1043", 系统设备编号 = "POS_1043_0200",
            设备类型 = "POS", 设备系统 = "Windows", 设备状态 = 1, 设备授权码 = "new-identity-test-auth",
        };
        target.ID = await _posmDb.Insertable(target).ExecuteReturnIdentityAsync();
        await _mainDb.Insertable(new Store { StoreGUID = Guid.NewGuid().ToString(), StoreCode = "1043", StoreName = "新分店" }).ExecuteCommandAsync();
        var service = CreateService();
        var principal = new RustDeskAuthenticatedUser(user.UserGUID, user.Username, user.FullName!);
        Assert.Empty(await service.GetPeersAsync(principal, CancellationToken.None));

        // 重放 WPF 开通码换店事务持久化的证据；不改绑或重建远程维护安装快照。
        await _posmDb.Insertable(new DeviceActivationCodeGrant
        {
            GrantId = Guid.NewGuid(), SecretHash = new byte[32], StoreCode = target.分店代码!, DeviceSystem = "Windows",
            CreatedAtUtc = _time.UtcDateTime, ExpiresAtUtc = _time.UtcDateTime.AddDays(1),
            ConsumedAtUtc = snapshot.RegisteredAtUtc.AddMinutes(1), ConsumptionKind = "Rebind",
            PreviousStoreCode = source.分店代码, PreviousDeviceCode = source.系统设备编号,
            ConsumedHardwareId = target.设备硬件识别码, ConsumedDeviceRegistrationId = target.ID,
            ConsumedDeviceCode = target.系统设备编号, ConsumedDeviceSystem = "Windows",
            ConsumedAuthorizationHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(target.设备授权码)),
        }).ExecuteCommandAsync();

        var ordinary = Assert.Single(await service.GetPeersAsync(principal, CancellationToken.None));
        var shared = Assert.Single((await service.GetAddressBookPeersAsync(principal, 1, 100, CancellationToken.None)).Data);
        Assert.Equal("新分店 POS_1043_0200", ordinary.Hostname);
        Assert.Equal(ordinary.Hostname, shared.Alias);
        Assert.Equal(string.Empty, shared.Username);
        Assert.Equal("238424213", shared.Id);
        Assert.Equal("existing-install-test-password", shared.Password);
        Assert.Equal(source.ID, (await _mainDb.Queryable<RemoteMaintenanceDevice>().SingleAsync()).DeviceRegistrationId);
    }

    [Fact]
    public async Task PosWithoutStoreRecordKeepsCurrentStoreCodeAndCompleteDeviceCode()
    {
        var user = await SeedAdminAsync();
        await SeedPosAsync("238424213", null);
        await _posmDb.Updateable<POSM_设备注册信息表>()
            .SetColumns(x => new POSM_设备注册信息表 { 分店代码 = "1042", 系统设备编号 = "POS_1042_0200" })
            .Where(x => x.设备硬件识别码 == "hw-238424213").ExecuteCommandAsync();
        var peer = Assert.Single(await CreateService().GetPeersAsync(new(user.UserGUID, user.Username, user.FullName!), CancellationToken.None));
        Assert.Equal("1042 POS_1042_0200", peer.Alias);
        Assert.Equal(string.Empty, peer.Username);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("disabled")]
    [InlineData("reregistered")]
    [InlineData("non-pos")]
    [InlineData("wrong-registration")]
    [InlineData("role-revoked")]
    [InlineData("user-disabled")]
    public async Task SharedAddressBookNeverDisclosesCredentialsForIneligibleDevicesOrUsers(string scenario)
    {
        var user = await SeedAdminAsync();
        var device = await SeedPosAsync("guard", _protector.ProtectPassword("excluded-device-password"));
        var registration = await _posmDb.Queryable<POSM_设备注册信息表>().SingleAsync();
        switch (scenario)
        {
            case "deleted":
                device.IsDeleted = true;
                await _mainDb.Updateable(device).ExecuteCommandAsync();
                break;
            case "wrong-registration":
                device.DeviceRegistrationId++;
                await _mainDb.Updateable(device).ExecuteCommandAsync();
                break;
            case "disabled":
                registration.设备状态 = 0;
                await _posmDb.Updateable(registration).ExecuteCommandAsync();
                break;
            case "reregistered":
                registration.ID = 0;
                await _posmDb.Insertable(registration).ExecuteReturnIdentityAsync();
                break;
            case "non-pos":
                registration.设备类型 = "PDA";
                await _posmDb.Updateable(registration).ExecuteCommandAsync();
                break;
            case "role-revoked":
                await _mainDb.Updateable<UserRole>().SetColumns(x => new UserRole { IsDeleted = true })
                    .Where(x => x.UserGUID == user.UserGUID).ExecuteCommandAsync();
                break;
            case "user-disabled":
                user.IsActive = false;
                await _mainDb.Updateable(user).ExecuteCommandAsync();
                break;
        }

        var peers = (await CreateService().GetAddressBookPeersAsync(new(user.UserGUID, user.Username, user.FullName!), 1, 100, CancellationToken.None)).Data;
        Assert.Empty(peers);
    }

    [Fact]
    public async Task MissingOrCorruptCredentialDoesNotBreakOtherAddressBookDevices()
    {
        var user = await SeedAdminAsync();
        await SeedPosAsync("missing", null);
        await SeedPosAsync("corrupt", "invalid-test-ciphertext");
        await SeedPosAsync("valid", _protector.ProtectPassword("valid-test-password"));
        var peers = (await CreateService().GetAddressBookPeersAsync(new(user.UserGUID, user.Username, user.FullName!), 1, 100, CancellationToken.None)).Data;
        Assert.Equal(3, peers.Count);
        Assert.Null(peers.Single(x => x.Id == "missing").Password);
        Assert.Null(peers.Single(x => x.Id == "corrupt").Password);
        Assert.Equal("valid-test-password", peers.Single(x => x.Id == "valid").Password);
    }

    [Fact]
    public async Task SharedAddressBookReadsAndAuditsOnlyTheRequestedPageCredentials()
    {
        var user = await SeedAdminAsync();
        var first = await SeedPosAsync("a", _protector.ProtectPassword("first-page-test-password"));
        var second = await SeedPosAsync("z", "corrupt-other-page-test-ciphertext");
        var credentialQueries = new List<string>();
        _mainDb.Aop.OnLogExecuting = (sql, parameters) =>
        {
            if (sql.Contains("CredentialCiphertext", StringComparison.OrdinalIgnoreCase))
                credentialQueries.Add(sql + string.Join(",", parameters.Select(x => x.Value)));
        };
        var service = CreateService();
        var principal = new RustDeskAuthenticatedUser(user.UserGUID, user.Username, user.FullName!);
        var page = await service.GetAddressBookPeersAsync(principal, 1, 1, CancellationToken.None);

        Assert.Equal(2, page.Total);
        Assert.Equal("first-page-test-password", Assert.Single(page.Data).Password);
        var query = Assert.Single(credentialQueries);
        Assert.Contains(first.Id.ToString(), query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(second.Id.ToString(), query, StringComparison.OrdinalIgnoreCase);
        var audit = Assert.Single(_logger.Messages);
        Assert.Contains(first.Id.ToString(), audit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("first-page-test-password", audit);
        Assert.DoesNotContain(second.Id.ToString(), audit, StringComparison.OrdinalIgnoreCase);

        var outside = await service.GetAddressBookPeersAsync(principal, int.MaxValue, 100, CancellationToken.None);
        Assert.Equal(2, outside.Total);
        Assert.Empty(outside.Data);
        Assert.Single(credentialQueries);
        Assert.Single(_logger.Messages);
    }

    [Fact]
    public async Task DuplicateRustDeskIdsAcrossEligiblePosDevicesNeverChooseAnArbitraryNameOrPassword()
    {
        var user = await SeedAdminAsync();
        var first = await SeedPosAsync("first", _protector.ProtectPassword("first-test-password"));
        var second = await SeedPosAsync("second", _protector.ProtectPassword("second-test-password"));
        await _mainDb.Updateable<RemoteMaintenanceDevice>()
            .SetColumns(x => new RemoteMaintenanceDevice { RustdeskId = "238424213" })
            .Where(x => x.Id == first.Id || x.Id == second.Id).ExecuteCommandAsync();
        var principal = new RustDeskAuthenticatedUser(user.UserGUID, user.Username, user.FullName!);
        var service = CreateService();

        Assert.Empty(await service.GetPeersAsync(principal, CancellationToken.None));
        Assert.Empty((await service.GetAddressBookPeersAsync(principal, 1, 100, CancellationToken.None)).Data);
        Assert.DoesNotContain(_logger.Messages, message => message.Contains("test-password", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ManagedDeviceNeverInheritsPosPasswordFromAnOverlappingRustDeskId()
    {
        var user = await SeedAdminAsync();
        await SeedPosAsync("same-id", _protector.ProtectPassword("pos-only-test-password"));
        await _mainDb.Insertable(new RustDeskManagedDevice
        {
            Id = Guid.NewGuid(), RustdeskId = "same-id", Alias = "Managed", Hostname = "Mac", Platform = "Mac OS",
            CreatedAtUtc = _time.UtcDateTime,
        }).ExecuteCommandAsync();
        var peer = Assert.Single((await CreateService().GetAddressBookPeersAsync(new(user.UserGUID, user.Username, user.FullName!), 1, 100, CancellationToken.None)).Data);
        Assert.Equal("Mac OS", peer.Platform);
        Assert.Null(peer.Password);
    }

    private async Task<RemoteMaintenanceDevice> SeedPosAsync(string id, string? ciphertext)
    {
        var registration = new POSM_设备注册信息表
        {
            设备硬件识别码 = "hw-" + id, 系统设备编号 = "POS-" + id, 分店代码 = "S001", 设备类型 = "POS", 设备系统 = "Windows", 设备状态 = 1,
            设备授权码 = "test-auth", 创建时间 = _time.UtcDateTime,
        };
        registration.ID = await _posmDb.Insertable(registration).ExecuteReturnIdentityAsync();
        var device = new RemoteMaintenanceDevice
        {
            Id = Guid.NewGuid(), DeviceRegistrationId = registration.ID, HardwareId = registration.设备硬件识别码,
            DeviceCode = registration.系统设备编号, ComputerName = "POS-" + id, StoreCode = "S001", RustdeskId = id,
            RegisteredAtUtc = _time.UtcDateTime, CredentialCiphertext = ciphertext,
        };
        await _mainDb.Insertable(device).ExecuteCommandAsync();
        return device;
    }

    private RustDeskCompatService CreateService(bool enabled = true) => new(
        CreateMainContext(_mainDb),
        CreatePosmContext(_posmDb),
        new RustDeskCompatSchemaReadiness(CreateMainContext(_mainDb)),
        _protector,
        Options.Create(new RemoteMaintenanceOptions { Enabled = enabled }),
        _logger,
        _time);

    private async Task<User> SeedAdminAsync()
    {
        var user = await SeedUserAsync("admin", "Admin", "Admin User");
        await AddAdminRoleAsync(user.UserGUID);
        return user;
    }

    private async Task<User> SeedUserAsync(string username, string roleName, string fullName)
    {
        var user = new User
        {
            UserGUID = Guid.NewGuid().ToString(), Username = username, Email = username + "@example.test",
            PasswordHash = PasswordHasher.HashPassword("Secret123"), FullName = fullName, IsActive = true,
            IsDeleted = false, CreatedAt = _time.UtcDateTime, UpdatedAt = _time.UtcDateTime,
        };
        await _mainDb.Insertable(user).ExecuteCommandAsync();
        var role = new Role { RoleGUID = Guid.NewGuid().ToString(), RoleName = roleName, IsActive = true, IsDeleted = false, CreatedAt = _time.UtcDateTime };
        await _mainDb.Insertable(role).ExecuteCommandAsync();
        await _mainDb.Insertable(new UserRole { UserRoleGUID = Guid.NewGuid().ToString(), UserGUID = user.UserGUID, RoleGUID = role.RoleGUID, IsDeleted = false, CreatedAt = _time.UtcDateTime }).ExecuteCommandAsync();
        return user;
    }

    private async Task AddAdminRoleAsync(string userGuid)
    {
        var role = new Role { RoleGUID = Guid.NewGuid().ToString(), RoleName = "Admin", IsActive = true, IsDeleted = false, CreatedAt = _time.UtcDateTime };
        await _mainDb.Insertable(role).ExecuteCommandAsync();
        await _mainDb.Insertable(new UserRole { UserRoleGUID = Guid.NewGuid().ToString(), UserGUID = userGuid, RoleGUID = role.RoleGUID, IsDeleted = false, CreatedAt = _time.UtcDateTime }).ExecuteCommandAsync();
    }

    private static SqlSugarClient CreateSqliteClient(SqliteConnection connection) => new(new ConnectionConfig
    {
        ConnectionString = connection.ConnectionString, DbType = DbType.Sqlite, IsAutoCloseConnection = false, InitKeyType = InitKeyType.Attribute,
        ConfigureExternalServices = new ConfigureExternalServices
        {
            EntityService = (_, column) =>
            {
                // 测试数据库以 TEXT 表达 SQL Server 的大文本列，保留真实实体查询字段。
                if (column.DataType?.Contains("(max)", StringComparison.OrdinalIgnoreCase) == true) column.DataType = "TEXT";
            },
        },
    });

    private static SqlSugarContext CreateMainContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
        return context;
    }

    private static POSMSqlSugarContext CreatePosmContext(ISqlSugarClient db)
    {
        var context = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(POSMSqlSugarContext));
        typeof(POSMSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
        return context;
    }

    public void Dispose()
    {
        _mainDb.Dispose();
        _posmDb.Dispose();
        _mainConnection.Dispose();
        _posmConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_mainPath);
        SqliteTempFileCleanup.DeleteIfExists(_posmPath);
    }

    private sealed class TestTimeProvider(DateTimeOffset current) : TimeProvider
    {
        private DateTimeOffset _current = current;
        public DateTime UtcDateTime => _current.UtcDateTime;
        public void Advance(TimeSpan amount) => _current = _current.Add(amount);
        public override DateTimeOffset GetUtcNow() => _current;
    }

    private sealed class RecordingLogger : ILogger<RustDeskCompatService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
