using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.RustDeskCompat;
using BlazorApp.Api.Utils;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Data.Sqlite;
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

    public RustDeskCompatServiceTests()
    {
        _mainConnection = new SqliteConnection($"Data Source={_mainPath}");
        _posmConnection = new SqliteConnection($"Data Source={_posmPath}");
        _mainConnection.Open();
        _posmConnection.Open();
        _mainDb = CreateSqliteClient(_mainConnection);
        _posmDb = CreateSqliteClient(_posmConnection);
        _mainDb.CodeFirst.InitTables<User, Role, UserRole>();
        _mainDb.CodeFirst.InitTables<RustDeskClientSession, RustDeskManagedDevice, RemoteMaintenanceDevice>();
        _posmDb.CodeFirst.InitTables<POSM_设备注册信息表>();
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
            设备硬件识别码 = "hw-1", 系统设备编号 = "POS-01", 设备类型 = "POS", 设备系统 = "Windows", 设备状态 = 1,
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

    private RustDeskCompatService CreateService() => new(
        CreateMainContext(_mainDb),
        CreatePosmContext(_posmDb),
        new RustDeskCompatSchemaReadiness(CreateMainContext(_mainDb)),
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
}
