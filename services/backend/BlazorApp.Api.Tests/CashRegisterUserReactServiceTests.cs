using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class CashRegisterUserReactServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;

    public CashRegisterUserReactServiceTests()
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
            typeof(UserStore)
        );
    }

    [Fact]
    public async Task CreateAsync_启用同一后台用户新条码时停用旧有效条码()
    {
        await SeedActiveUserAsync("user-1", "Alice");
        await SeedStoreAsync("store-1", "S1");
        await SeedCashierAsync("old-cashier", "user-1", "OLD-CODE", status: true);

        var result = await CreateService().CreateAsync(
            new CreateCashRegisterUserDto
            {
                StoreCode = "S1",
                UserGUID = "user-1",
                OperatorUser = "Alice",
                UserBarcode = "NEW-CODE",
                LoginRole = "2",
                Status = true,
            },
            "tester"
        );

        var oldCashier = await _db.Queryable<CashRegisterUser>()
            .FirstAsync(item => item.HGUID == "old-cashier");
        var newCashier = await _db.Queryable<CashRegisterUser>()
            .FirstAsync(item => item.UserBarcode == "NEW-CODE");

        Assert.NotNull(result.Data);
        Assert.Equal("user-1", result.Data.UserGUID);
        Assert.False(oldCashier.Status);
        Assert.True(newCashier.Status);
        Assert.Equal("user-1", newCashier.UserGUID);
    }

    [Fact]
    public async Task UpdateAsync_拒绝启用其他用户已启用条码()
    {
        await SeedActiveUserAsync("user-1", "Alice");
        await SeedActiveUserAsync("user-2", "Bob");
        await SeedCashierAsync("cashier-1", "user-1", "CODE-1", status: true);
        await SeedCashierAsync("cashier-2", "user-2", "CODE-2", status: true);

        var result = await CreateService().UpdateAsync(
            "cashier-1",
            new UpdateCashRegisterUserDto
            {
                StoreCode = "S1",
                UserGUID = "user-1",
                OperatorUser = "Alice",
                UserBarcode = "CODE-2",
                LoginRole = "2",
                Status = true,
            },
            "tester"
        );

        Assert.False(result.Success);
    }

    [Fact]
    public async Task CreateAsync_规范化条码并按Trim后值查重()
    {
        await SeedActiveUserAsync("user-1", "Alice");
        await SeedActiveUserAsync("user-2", "Bob");
        await SeedStoreAsync("store-1", "S1");

        var first = await CreateService().CreateAsync(
            new CreateCashRegisterUserDto
            {
                StoreCode = "S1",
                UserGUID = "user-1",
                OperatorUser = "Alice",
                UserBarcode = "  CODE-1  ",
                LoginRole = "2",
                Status = true,
            },
            "tester"
        );
        var duplicate = await CreateService().CreateAsync(
            new CreateCashRegisterUserDto
            {
                StoreCode = "S1",
                UserGUID = "user-2",
                OperatorUser = "Bob",
                UserBarcode = "CODE-1",
                LoginRole = "2",
                Status = true,
            },
            "tester"
        );

        Assert.True(first.Success);
        Assert.Equal("CODE-1", first.Data!.UserBarcode);
        Assert.False(duplicate.Success);
    }

    [Fact]
    public async Task GetGridDataAsync_按关联后台用户分店过滤而不是旧StoreCode()
    {
        await SeedActiveUserAsync("manager-1", "Manager");
        await SeedActiveUserAsync("user-allowed", "Allowed");
        await SeedActiveUserAsync("user-blocked", "Blocked");
        await SeedStoreAsync("store-allowed", "S1");
        await SeedStoreAsync("store-blocked", "S2");
        await SeedUserStoreAsync("manager-1", "store-allowed");
        await SeedUserStoreAsync("manager-1", "store-blocked", isPrimary: false);
        await SeedUserStoreAsync("user-allowed", "store-allowed");
        await SeedUserStoreAsync("user-blocked", "store-blocked");
        await SeedCashierAsync("cashier-allowed", "user-allowed", "ALLOWED-CODE", status: true, storeCode: "S2");
        await SeedCashierAsync("cashier-blocked", "user-blocked", "BLOCKED-CODE", status: true, storeCode: "S1");

        var result = await CreateService("StoreManager", "manager-1")
            .GetGridDataAsync(new GridRequestDto { StartRow = 0, PageSize = 20 });

        Assert.NotNull(result.Items);
        var item = Assert.Single(result.Items);
        Assert.Equal("cashier-allowed", item.HGUID);
        Assert.Equal("S1", item.StoreCode);
        Assert.Equal("S1", item.StoreName);
        Assert.Equal("S2", item.LegacyStoreCode);
    }

    [Fact]
    public async Task GetGridDataAsync_WarehouseManager_仍按关联后台用户分店过滤()
    {
        await SeedActiveUserAsync("manager-1", "Manager");
        await SeedActiveUserAsync("user-allowed", "Allowed");
        await SeedActiveUserAsync("user-blocked", "Blocked");
        await SeedStoreAsync("store-allowed", "S1");
        await SeedStoreAsync("store-blocked", "S2");
        await SeedUserStoreAsync("manager-1", "store-allowed");
        await SeedUserStoreAsync("user-allowed", "store-allowed");
        await SeedUserStoreAsync("user-blocked", "store-blocked");
        await SeedCashierAsync("cashier-allowed", "user-allowed", "ALLOWED-CODE", status: true, storeCode: "S2");
        await SeedCashierAsync("cashier-blocked", "user-blocked", "BLOCKED-CODE", status: true, storeCode: "S1");

        var result = await CreateService("WarehouseManager", "manager-1")
            .GetGridDataAsync(new GridRequestDto { StartRow = 0, PageSize = 20 });

        Assert.NotNull(result.Items);
        var item = Assert.Single(result.Items);
        Assert.Equal("cashier-allowed", item.HGUID);
    }

    [Fact]
    public async Task UpdateAsync_非管理员不能改绑到无权管理分店用户()
    {
        await SeedActiveUserAsync("manager-1", "Manager");
        await SeedActiveUserAsync("user-allowed", "Allowed");
        await SeedActiveUserAsync("user-blocked", "Blocked");
        await SeedStoreAsync("store-allowed", "S1");
        await SeedStoreAsync("store-blocked", "S2");
        await SeedUserStoreAsync("manager-1", "store-allowed");
        await SeedUserStoreAsync("user-allowed", "store-allowed");
        await SeedUserStoreAsync("user-blocked", "store-blocked");
        await SeedCashierAsync("cashier-allowed", "user-allowed", "ALLOWED-CODE", status: true, storeCode: "S1");

        var result = await CreateService("StoreManager", "manager-1").UpdateAsync(
            "cashier-allowed",
            new UpdateCashRegisterUserDto
            {
                StoreCode = "S1",
                UserGUID = "user-blocked",
                OperatorUser = "Blocked",
                UserBarcode = "ALLOWED-CODE",
                LoginRole = "2",
                Status = true,
            },
            "tester"
        );

        Assert.False(result.Success);
    }

    [Fact]
    public async Task GetUserOptionsAsync_StoreManager_按可管理门店返回启用用户()
    {
        await SeedActiveUserAsync("manager-1", "Manager");
        await SeedActiveUserAsync("user-allowed", "Allowed");
        await SeedActiveUserAsync("user-blocked", "Blocked");
        await SeedActiveUserAsync("user-inactive", "Inactive", isActive: false);
        await SeedStoreAsync("store-allowed", "S1");
        await SeedStoreAsync("store-blocked", "S2");
        await SeedUserStoreAsync("manager-1", "store-allowed");
        await SeedUserStoreAsync("manager-1", "store-blocked", isPrimary: false);
        await SeedUserStoreAsync("user-allowed", "store-allowed");
        await SeedUserStoreAsync("user-inactive", "store-allowed");
        await SeedUserStoreAsync("user-blocked", "store-blocked");

        var result = await CreateService("StoreManager", "manager-1").GetUserOptionsAsync();

        Assert.True(result.Success);
        Assert.Contains(result.Data!, option => option.UserGUID == "user-allowed");
        Assert.Contains(result.Data!, option => option.UserGUID == "manager-1");
        Assert.DoesNotContain(result.Data!, option => option.UserGUID == "user-blocked");
        Assert.DoesNotContain(result.Data!, option => option.UserGUID == "user-inactive");
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private async Task SeedActiveUserAsync(string userGuid, string username, bool isActive = true)
    {
        await _db.Insertable(new User
        {
            UserGUID = userGuid,
            Username = username,
            Email = $"{userGuid}@example.test",
            PasswordHash = "hash",
            FullName = $"{username} User",
            IsActive = isActive,
            IsDeleted = false,
        }).ExecuteCommandAsync();
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

    private async Task SeedUserStoreAsync(string userGuid, string storeGuid, bool isPrimary = true)
    {
        await _db.Insertable(new UserStore
        {
            UserStoreGUID = Guid.NewGuid().ToString("N"),
            UserGUID = userGuid,
            StoreGUID = storeGuid,
            AssignedAt = DateTime.UtcNow,
            IsPrimary = isPrimary,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedCashierAsync(
        string hGuid,
        string userGuid,
        string barcode,
        bool status,
        string storeCode = "LEGACY")
    {
        await _db.Insertable(new CashRegisterUser
        {
            HGUID = hGuid,
            StoreCode = storeCode,
            UserGUID = userGuid,
            OperatorUser = userGuid,
            UserBarcode = barcode,
            LoginRole = "2",
            Remark = string.Empty,
            Status = status,
            Creator = "seed",
            LastModifier = "seed",
            CreateDate = DateTime.UtcNow,
            LastModifyDate = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private CashRegisterUserReactService CreateService(string role = "Admin", string? userGuid = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (!string.IsNullOrWhiteSpace(userGuid))
        {
            claims.Add(new Claim("userGuid", userGuid));
        }

        var identity = new ClaimsIdentity(claims, "test");
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };

        return new CashRegisterUserReactService(
            CreateSqlSugarContext(_db),
            NullLogger<CashRegisterUserReactService>.Instance,
            accessor
        );
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(SqlSugarContext)
        );
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }
}
