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
            typeof(CashierBarcodeReservation),
            typeof(EmployeeCashierBarcode),
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
    public async Task CreateAsync_启用Legacy条码时停用同用户个人条码()
    {
        await SeedActiveUserAsync("user-1", "Alice");
        await SeedStoreAsync("store-1", "S1");
        await SeedEmployeeCashierAsync("employee-1", "user-1", "2900000000001");

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

        Assert.True(result.Success);
        Assert.False((await _db.Queryable<EmployeeCashierBarcode>()
            .FirstAsync(item => item.HGUID == "employee-1")).Status);
    }

    [Fact]
    public async Task CreateAsync_拒绝复用个人或历史已占用条码()
    {
        await SeedActiveUserAsync("user-1", "Alice");
        await SeedStoreAsync("store-1", "S1");
        await _db.Insertable(new CashierBarcodeReservation
        {
            Barcode = "RESERVED-CODE",
            CreatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();

        var result = await CreateService().CreateAsync(
            new CreateCashRegisterUserDto
            {
                StoreCode = "S1",
                UserGUID = "user-1",
                OperatorUser = "Alice",
                UserBarcode = "RESERVED-CODE",
                LoginRole = "2",
                Status = true,
            },
            "tester"
        );

        Assert.False(result.Success);
        Assert.False(await _db.Queryable<CashRegisterUser>()
            .AnyAsync(item => item.UserBarcode == "RESERVED-CODE"));
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
    public async Task UpdateAsync_启用Legacy条码时停用同用户个人条码()
    {
        await SeedActiveUserAsync("user-1", "Alice");
        await SeedCashierAsync("cashier-1", "user-1", "CODE-1", status: false);
        await SeedEmployeeCashierAsync("employee-1", "user-1", "2900000000001");

        var result = await CreateService().UpdateAsync(
            "cashier-1",
            new UpdateCashRegisterUserDto
            {
                StoreCode = "S1",
                UserGUID = "user-1",
                OperatorUser = "Alice",
                UserBarcode = "CODE-1",
                LoginRole = "2",
                Status = true,
            },
            "tester"
        );

        Assert.True(result.Success);
        Assert.False((await _db.Queryable<EmployeeCashierBarcode>()
            .FirstAsync(item => item.HGUID == "employee-1")).Status);
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

    [Fact]
    public async Task GetGridDataAsync_StoreManager_显示所管分店员工和本人条码()
    {
        await SeedActiveUserAsync("manager-1", "Manager");
        await SeedActiveUserAsync("user-allowed", "Allowed");
        await SeedActiveUserAsync("user-blocked", "Blocked");
        await SeedStoreAsync("store-allowed", "S1");
        await SeedStoreAsync("store-blocked", "S2");
        // 店长本人只挂在非主分店上：本人条码仍需可见，但该分店其他员工不可见。
        await SeedUserStoreAsync("manager-1", "store-allowed");
        await SeedUserStoreAsync("manager-1", "store-blocked", isPrimary: false);
        await SeedUserStoreAsync("user-allowed", "store-allowed");
        await SeedUserStoreAsync("user-blocked", "store-blocked");
        await SeedCashierAsync("cashier-self", "manager-1", "SELF-CODE", status: true);
        await SeedCashierAsync("cashier-allowed", "user-allowed", "ALLOWED-CODE", status: true);
        await SeedCashierAsync("cashier-blocked", "user-blocked", "BLOCKED-CODE", status: true);

        var result = await CreateService("StoreManager", "manager-1")
            .GetGridDataAsync(new GridRequestDto { StartRow = 0, PageSize = 20 });

        Assert.NotNull(result.Items);
        Assert.Equal(
            new[] { "cashier-allowed", "cashier-self" },
            result.Items.Select(item => item.HGUID).OrderBy(id => id).ToArray()
        );
    }

    [Fact]
    public async Task GetGridDataAsync_没有主分店的店长_只显示本人条码()
    {
        await SeedActiveUserAsync("manager-1", "Manager");
        await SeedActiveUserAsync("user-other", "Other");
        await SeedStoreAsync("store-1", "S1");
        await SeedUserStoreAsync("manager-1", "store-1", isPrimary: false);
        await SeedUserStoreAsync("user-other", "store-1");
        await SeedCashierAsync("cashier-self", "manager-1", "SELF-CODE", status: true);
        await SeedCashierAsync("cashier-other", "user-other", "OTHER-CODE", status: true);

        var service = CreateService("StoreManager", "manager-1");
        var grid = await service.GetGridDataAsync(new GridRequestDto { StartRow = 0, PageSize = 20 });
        var print = await service.ConfirmPrintAsync(
            "cashier-self", new ConfirmCashRegisterUserPrintDto { UserBarcode = "SELF-CODE" }, "manager");

        Assert.Equal("cashier-self", Assert.Single(grid.Items!).HGUID);
        Assert.True(print.Success, "店长可以打印本人条码");
    }

    [Fact]
    public async Task GetGridDataAsync_StoreManager_未关联用户的历史条码按旧StoreCode归属主分店()
    {
        await SeedActiveUserAsync("manager-1", "Manager");
        await SeedStoreAsync("store-allowed", "S1");
        await SeedStoreAsync("store-blocked", "S2");
        await SeedUserStoreAsync("manager-1", "store-allowed");
        // HQ 同步来的历史条码全部没有关联后台用户，只能按旧 StoreCode 判断归属。
        await SeedCashierAsync("legacy-allowed", null, "LEGACY-1", status: true, storeCode: "S1");
        await SeedCashierAsync("legacy-blocked", null, "LEGACY-2", status: true, storeCode: "S2");

        var result = await CreateService("StoreManager", "manager-1")
            .GetGridDataAsync(new GridRequestDto { StartRow = 0, PageSize = 20 });

        var item = Assert.Single(result.Items!);
        Assert.Equal("legacy-allowed", item.HGUID);
        Assert.Equal("S1", item.StoreCode);
        Assert.Equal("S1", item.StoreName);
    }

    [Fact]
    public async Task GetGridDataAsync_分店筛选同时命中关联用户和未关联历史条码()
    {
        await SeedActiveUserAsync("user-1", "Alice");
        await SeedStoreAsync("store-1", "S1");
        await SeedStoreAsync("store-2", "S2");
        await SeedUserStoreAsync("user-1", "store-1");
        await SeedCashierAsync("linked-s1", "user-1", "CODE-1", status: true, storeCode: "S2");
        await SeedCashierAsync("legacy-s1", null, "CODE-2", status: true, storeCode: "S1");
        await SeedCashierAsync("legacy-s2", null, "CODE-3", status: true, storeCode: "S2");

        var result = await CreateService().GetGridDataAsync(new GridRequestDto
        {
            StartRow = 0,
            PageSize = 20,
            FilterModel = new Dictionary<string, FilterModelDto>
            {
                ["storeCode"] = new FilterModelDto { FilterType = "text", Type = "equals", Filter = "S1" },
            },
        });

        Assert.Equal(
            new[] { "legacy-s1", "linked-s1" },
            result.Items!.Select(item => item.HGUID).OrderBy(id => id).ToArray()
        );
    }

    [Fact]
    public async Task GetScopeAsync_按后端判定返回可管理分店()
    {
        await SeedActiveUserAsync("manager-1", "Manager");
        await SeedActiveUserAsync("staff-1", "Staff");
        await SeedStoreAsync("store-1", "S1");
        await SeedStoreAsync("store-2", "S2");
        await SeedUserStoreAsync("manager-1", "store-1");
        await SeedUserStoreAsync("manager-1", "store-2", isPrimary: false);
        await SeedUserStoreAsync("staff-1", "store-2", isPrimary: false);

        var manager = await CreateService("StoreManager", "manager-1").GetScopeAsync();
        var staff = await CreateService("StoreStaff", "staff-1").GetScopeAsync();
        var admin = await CreateService().GetScopeAsync();

        Assert.False(manager.Data!.IsAdmin);
        Assert.Equal(new[] { "S1" }, manager.Data.ManageableStores.Select(s => s.StoreCode).ToArray());
        Assert.Empty(staff.Data!.ManageableStores);
        Assert.True(admin.Data!.IsAdmin);
        Assert.Equal(new[] { "S1", "S2" }, admin.Data.ManageableStores.Select(s => s.StoreCode).ToArray());
    }

    [Fact]
    public async Task ConfirmPrintAsync_条码与状态匹配时原子累加打印次数()
    {
        await SeedActiveUserAsync("user-1", "Alice");
        await SeedCashierAsync("cashier-1", "user-1", "CODE-1", status: true);

        var service = CreateService();
        var first = await service.ConfirmPrintAsync(
            "cashier-1", new ConfirmCashRegisterUserPrintDto { UserBarcode = " CODE-1 " }, "printer");
        var second = await service.ConfirmPrintAsync(
            "cashier-1", new ConfirmCashRegisterUserPrintDto { UserBarcode = "CODE-1" }, "printer");

        Assert.True(first.Success);
        Assert.Equal(1, first.Data!.PrintCount);
        Assert.True(second.Success);
        Assert.Equal(2, second.Data!.PrintCount);
        var entity = await _db.Queryable<CashRegisterUser>().FirstAsync(item => item.HGUID == "cashier-1");
        Assert.Equal(2, entity.PrintCount);
        Assert.Equal("printer", entity.LastModifier);
    }

    [Fact]
    public async Task ConfirmPrintAsync_换码后旧码确认不计数()
    {
        await SeedActiveUserAsync("user-1", "Alice");
        await SeedCashierAsync("cashier-1", "user-1", "NEW-CODE", status: true);

        var result = await CreateService().ConfirmPrintAsync(
            "cashier-1", new ConfirmCashRegisterUserPrintDto { UserBarcode = "OLD-CODE" }, "printer");

        Assert.False(result.Success);
        Assert.Equal(0, (await _db.Queryable<CashRegisterUser>()
            .FirstAsync(item => item.HGUID == "cashier-1")).PrintCount);
    }

    [Fact]
    public async Task ConfirmPrintAsync_已停用条码不计数()
    {
        await SeedActiveUserAsync("user-1", "Alice");
        await SeedCashierAsync("cashier-1", "user-1", "CODE-1", status: false);

        var result = await CreateService().ConfirmPrintAsync(
            "cashier-1", new ConfirmCashRegisterUserPrintDto { UserBarcode = "CODE-1" }, "printer");

        Assert.False(result.Success);
        Assert.Equal(0, (await _db.Queryable<CashRegisterUser>()
            .FirstAsync(item => item.HGUID == "cashier-1")).PrintCount);
    }

    [Fact]
    public async Task ConfirmPrintAsync_非管理员不能确认无权管理分店的条码()
    {
        await SeedActiveUserAsync("manager-1", "Manager");
        await SeedActiveUserAsync("user-blocked", "Blocked");
        await SeedStoreAsync("store-allowed", "S1");
        await SeedStoreAsync("store-blocked", "S2");
        await SeedUserStoreAsync("manager-1", "store-allowed");
        await SeedUserStoreAsync("user-blocked", "store-blocked");
        await SeedCashierAsync("cashier-blocked", "user-blocked", "BLOCKED-CODE", status: true, storeCode: "S1");

        var result = await CreateService("StoreManager", "manager-1").ConfirmPrintAsync(
            "cashier-blocked", new ConfirmCashRegisterUserPrintDto { UserBarcode = "BLOCKED-CODE" }, "printer");

        Assert.False(result.Success);
        Assert.Equal(0, (await _db.Queryable<CashRegisterUser>()
            .FirstAsync(item => item.HGUID == "cashier-blocked")).PrintCount);
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
        string? userGuid,
        string barcode,
        bool status,
        string storeCode = "LEGACY")
    {
        await _db.Insertable(new CashRegisterUser
        {
            HGUID = hGuid,
            StoreCode = storeCode,
            UserGUID = userGuid,
            OperatorUser = userGuid ?? hGuid,
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

    private async Task SeedEmployeeCashierAsync(string hGuid, string userGuid, string barcode)
    {
        await _db.Insertable(new EmployeeCashierBarcode
        {
            HGUID = hGuid,
            UserGUID = userGuid,
            Barcode = barcode,
            Status = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
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
