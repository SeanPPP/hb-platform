using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.Models;
using Hbpos.Api.Data;
using Hbpos.Api.Auth;
using Hbpos.Api.Services;
using Hbpos.Contracts.Cashiers;
using Microsoft.Data.Sqlite;
using SqlSugar;

namespace Hbpos.Api.Tests;

public sealed class CashierServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;

    public CashierServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _sqliteConnection = new SqliteConnection($"Data Source={_dbPath}");
        _sqliteConnection.Open();

        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _sqliteConnection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });

        _db.CodeFirst.InitTables(
            typeof(CashRegisterUser),
            typeof(User),
            typeof(Store),
            typeof(UserStore),
            typeof(Role),
            typeof(UserRole),
            typeof(SysPermission),
            typeof(SysRolePermission),
            typeof(SysUserPermission)
        );
    }

    [Fact]
    public async Task BarcodeLoginAsync_RejectsBlankBarcode()
    {
        await SeedStoreAsync("store-allowed", "S-ALLOWED");
        await SeedUserAsync("user-blank", "Blank User");
        await SeedUserStoreAsync("user-blank", "store-allowed");
        await SeedCashierAsync("cashier-blank", "user-blank", "S-OLD", string.Empty);

        var session = await CreateService().BarcodeLoginAsync(
            new CashierBarcodeLoginRequest("S-ALLOWED", "   ", "POS-1"),
            CancellationToken.None
        );

        // 关键位置：空白条码必须在入库查询前被拒绝，避免历史脏数据产生误登录。
        Assert.Null(session);
    }

    [Fact]
    public async Task BarcodeLoginAsync_按UserStore授权并返回权限快照()
    {
        await SeedStoreAsync("store-allowed", "S-ALLOWED");
        await SeedStoreAsync("store-legacy", "S-LEGACY");
        await SeedUserAsync("user-1", "Alice");
        await SeedUserStoreAsync("user-1", "store-allowed");
        await SeedCashierAsync("cashier-1", " user-1 ", "S-LEGACY", "BARCODE-1");
        await SeedRoleAsync("role-1", "Cashier");
        await SeedUserRoleAsync("user-1", "role-1");
        await SeedRolePermissionAsync("role-1", Permissions.PosTerminal.Sales.AddItem);

        var session = await CreateService().BarcodeLoginAsync(
            new CashierBarcodeLoginRequest("S-ALLOWED", "BARCODE-1", "POS-1"),
            CancellationToken.None
        );

        Assert.NotNull(session);
        Assert.Equal("cashier-1", session.CashierId);
        Assert.Equal("user-1", session.UserGuid);
        Assert.Equal("Alice", session.CashierName);
        Assert.Equal("S-ALLOWED", session.StoreCode);
        Assert.Equal("POS-1", session.DeviceCode);
        Assert.Contains(Permissions.PosTerminal.Sales.AddItem, session.PermissionCodes);
        Assert.Contains("S-ALLOWED", session.AllowedStoreCodes);
        Assert.False(session.IsSuperAdmin);
        Assert.False(session.IsOfflineCached);
        Assert.False(session.IsEmergencyOverride);
        Assert.Equal("test-ticket", session.AuthorizationToken);
        Assert.Equal(DateTimeOffset.Parse("2026-07-15T00:00:00Z"), session.AuthorizationExpiresAtUtc);
    }

    [Fact]
    public async Task BarcodeLoginAsync_管理员返回全部权限且拒绝未授权门店()
    {
        await SeedStoreAsync("store-allowed", "S-ALLOWED");
        await SeedStoreAsync("store-blocked", "S-BLOCKED");
        await SeedUserAsync("admin-1", "Admin User");
        await SeedUserStoreAsync("admin-1", "store-allowed");
        await SeedCashierAsync("cashier-admin", "admin-1", "S-OLD", "ADMIN-CODE");
        await SeedRoleAsync("role-admin", "Admin");
        await SeedUserRoleAsync("admin-1", "role-admin");
        await SeedPermissionAsync(Permissions.PosTerminal.Sales.AddItem);
        await SeedPermissionAsync(Permissions.PosTerminal.CashDrawer.Open);

        var blocked = await CreateService().BarcodeLoginAsync(
            new CashierBarcodeLoginRequest("S-BLOCKED", "ADMIN-CODE", "POS-1"),
            CancellationToken.None
        );
        var allowed = await CreateService().BarcodeLoginAsync(
            new CashierBarcodeLoginRequest("S-ALLOWED", "ADMIN-CODE", "POS-1"),
            CancellationToken.None
        );

        Assert.Null(blocked);
        Assert.NotNull(allowed);
        Assert.True(allowed.IsSuperAdmin);
        Assert.Contains(Permissions.PosTerminal.Sales.AddItem, allowed.PermissionCodes);
        Assert.Contains(Permissions.PosTerminal.CashDrawer.Open, allowed.PermissionCodes);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private CashierService CreateService() => new(CreateHbposContext(_db), new FakeTicketService());

    private sealed class FakeTicketService : ICashierAuthorizationTicketService
    {
        private static readonly DateTimeOffset ExpiresAtUtc = DateTimeOffset.Parse("2026-07-15T00:00:00Z");

        public (string Token, DateTimeOffset ExpiresAtUtc) Issue(
            string cashierId,
            string userGuid,
            string storeCode,
            string deviceCode) => ("test-ticket", ExpiresAtUtc);

        public CashierAuthorizationTicket? Validate(string? token) => null;
    }

    private async Task SeedStoreAsync(string storeGuid, string storeCode)
    {
        await _db.Insertable(new Store
        {
            StoreGUID = storeGuid,
            StoreCode = storeCode,
            StoreName = storeCode,
            IsActive = true,
        }).ExecuteCommandAsync();
    }

    private async Task SeedUserAsync(string userGuid, string username)
    {
        await _db.Insertable(new User
        {
            UserGUID = userGuid,
            Username = username,
            FullName = username,
            Email = $"{userGuid}@example.test",
            PasswordHash = "hash",
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedUserStoreAsync(string userGuid, string storeGuid)
    {
        await _db.Insertable(new UserStore
        {
            UserStoreGUID = $"{userGuid}-{storeGuid}",
            UserGUID = userGuid,
            StoreGUID = storeGuid,
            IsPrimary = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedCashierAsync(
        string hGuid,
        string userGuid,
        string legacyStoreCode,
        string barcode)
    {
        await _db.Insertable(new CashRegisterUser
        {
            HGUID = hGuid,
            UserGUID = userGuid,
            StoreCode = legacyStoreCode,
            OperatorUser = userGuid,
            UserBarcode = barcode,
            LoginRole = "legacy",
            Remark = string.Empty,
            Status = true,
            Creator = "seed",
            LastModifier = "seed",
            CreateDate = DateTime.UtcNow,
            LastModifyDate = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private async Task SeedRoleAsync(string roleGuid, string roleName)
    {
        await _db.Insertable(new Role
        {
            RoleGUID = roleGuid,
            RoleName = roleName,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedUserRoleAsync(string userGuid, string roleGuid)
    {
        await _db.Insertable(new UserRole
        {
            UserRoleGUID = $"{userGuid}-{roleGuid}",
            UserGUID = userGuid,
            RoleGUID = roleGuid,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedPermissionAsync(string code)
    {
        await _db.Insertable(new SysPermission
        {
            Id = code,
            Code = code,
            Name = code,
            Category = "test",
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedRolePermissionAsync(string roleGuid, string permissionCode)
    {
        await SeedPermissionAsync(permissionCode);
        await _db.Insertable(new SysRolePermission
        {
            Id = $"{roleGuid}-{permissionCode}",
            RoleGuid = roleGuid,
            PermissionCode = permissionCode,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private static HbposSqlSugarContext CreateHbposContext(ISqlSugarClient db)
    {
        var context = (HbposSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(HbposSqlSugarContext)
        );
        typeof(HbposSqlSugarContext)
            .GetField("<MainDb>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        typeof(HbposSqlSugarContext)
            .GetField("<PosmDb>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }
}
