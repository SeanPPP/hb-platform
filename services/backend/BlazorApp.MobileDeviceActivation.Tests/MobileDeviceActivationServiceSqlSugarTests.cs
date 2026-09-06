using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.MobileDeviceActivation;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.MobileDeviceActivation.Tests;

public sealed class MobileDeviceActivationServiceSqlSugarTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("1042", "1042")]
    public async Task LoadRegistrationStateAsync_MapsStoreWithoutProjectionError(
        string? storeCode,
        string expectedStoreCode)
    {
        var databaseName = $"mobile-registration-state-{Guid.NewGuid():N}";
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";
        await using var anchorConnection = new SqliteConnection(connectionString);
        await anchorConnection.OpenAsync();
        using var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        });
        db.CodeFirst.InitTables<POSM_设备注册信息表>();

        var registrationId = await db.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = "hardware-test-only",
            系统设备编号 = "device-test-only",
            分店代码 = storeCode,
            设备系统 = "Android",
            设备类型 = "Mobile",
            设备状态 = 1,
            设备授权码 = "authorization-test-only",
        }).ExecuteReturnIdentityAsync();

        var service = CreateService(db);
        var state = Assert.IsType<MobileDeviceRegistrationState>(
            await InvokePrivateAsync(
                service,
                "LoadRegistrationStateAsync",
                registrationId,
                CancellationToken.None));

        Assert.Equal(registrationId, state.DeviceRegistrationId);
        Assert.Equal("hardware-test-only", state.HardwareId);
        Assert.Equal("device-test-only", state.DeviceCode);
        Assert.Equal(expectedStoreCode, state.StoreCode);
        Assert.Equal("Android", state.DeviceSystem);
        Assert.Equal("Mobile", state.DeviceType);
        Assert.Equal(1, state.DeviceStatus);
    }

    [Fact]
    public async Task AssignmentQueries_ReturnActiveTargetRolesAndStores_WithoutAliasErrors()
    {
        var databaseName = $"mobile-activation-service-{Guid.NewGuid():N}";
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";
        await using var anchorConnection = new SqliteConnection(connectionString);
        await anchorConnection.OpenAsync();
        using var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        });
        db.CodeFirst.InitTables<User, UserStore, Store, UserRole, Role>();

        const string userGuid = "user-guid-activation";
        const string storeGuid = "store-guid-activation";
        const string roleGuid = "role-guid-activation";
        await db.Insertable(new User
        {
            UserGUID = userGuid,
            Username = "activation.user",
            Email = "activation.user@example.test",
            PasswordHash = "test-only",
            FullName = "Activation User",
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await db.Insertable(new Store
        {
            StoreGUID = storeGuid,
            StoreCode = "1042",
            StoreName = "Test Store",
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await db.Insertable(new UserStore
        {
            UserStoreGUID = "user-store-guid-activation",
            UserGUID = userGuid,
            StoreGUID = storeGuid,
            IsPrimary = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await db.Insertable(new[]
        {
            new Store
            {
                StoreGUID = "store-guid-secondary-a",
                StoreCode = "1001",
                StoreName = "Secondary Store A",
                IsActive = true,
                IsDeleted = false,
            },
            new Store
            {
                StoreGUID = "store-guid-secondary-b",
                StoreCode = "1050",
                StoreName = "Secondary Store B",
                IsActive = true,
                IsDeleted = false,
            },
        }).ExecuteCommandAsync();
        await db.Insertable(new[]
        {
            new UserStore
            {
                UserStoreGUID = "user-store-guid-secondary-a",
                UserGUID = userGuid,
                StoreGUID = "store-guid-secondary-a",
                IsPrimary = false,
                IsDeleted = false,
            },
            new UserStore
            {
                UserStoreGUID = "user-store-guid-secondary-b",
                UserGUID = userGuid,
                StoreGUID = "store-guid-secondary-b",
                IsPrimary = false,
                IsDeleted = false,
            },
        }).ExecuteCommandAsync();
        await db.Insertable(new Role
        {
            RoleGUID = roleGuid,
            RoleName = "OrderUser",
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await db.Insertable(new UserRole
        {
            UserRoleGUID = "user-role-guid-activation",
            UserGUID = userGuid,
            RoleGUID = roleGuid,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        var service = CreateService(db);

        var target = await InvokePrivateAsync(
            service,
            "LoadActiveTargetAsync",
            "1042",
            userGuid,
            CancellationToken.None);
        Assert.NotNull(target);
        Assert.Equal(userGuid, ReadProperty<string>(target, "UserGuid"));
        Assert.Equal("1042", ReadProperty<string>(target, "StoreCode"));
        Assert.Equal("Test Store", ReadProperty<string>(target, "StoreName"));

        var storeCount = await InvokePrivateAsync(
            service,
            "CountAssignedStoresAsync",
            userGuid,
            CancellationToken.None);
        Assert.Equal(3, Assert.IsType<int>(storeCount));

        var roles = Assert.IsAssignableFrom<IReadOnlyList<string>>(
            await InvokePrivateAsync(
                service,
                "LoadActiveRolesAsync",
                userGuid,
                CancellationToken.None));
        Assert.Equal(["OrderUser"], roles);

        var stores = Assert.IsAssignableFrom<IReadOnlyList<MobileDeviceSessionStoreDto>>(
            await InvokePrivateAsync(
                service,
                "LoadAssignedStoresAsync",
                userGuid,
                CancellationToken.None));
        Assert.Equal(["1042", "1001", "1050"], stores.Select(store => store.StoreCode));
        Assert.Equal([true, false, false], stores.Select(store => store.IsPrimary));
        Assert.Equal(storeGuid, stores[0].StoreGuid);
    }

    [Fact]
    public void SqlServerRegistrationProjection_UsesExplicitColumnAliases()
    {
        using var db = CreateSqlServerClient();
        var sql = MobileDeviceActivationQueries
            .BuildRegistrationStateQuery(db, 42)
            .ToSql().Key;

        AssertSqlContains(
            sql,
            "[ID] AS [DeviceRegistrationId]",
            "[设备硬件识别码] AS [HardwareId]",
            "[系统设备编号] AS [DeviceCode]",
            "[分店代码] AS [StoreCode]",
            "[设备系统] AS [DeviceSystem]",
            "[设备类型] AS [DeviceType]",
            "[设备状态] AS [DeviceStatus]");
    }

    [Fact]
    public void SqlServerTwoTableSelects_KeepJoinAliasesConsistent()
    {
        using var db = CreateSqlServerClient();

        var manageableAccountsSql = MobileDeviceActivationQueries
            .BuildManageableAccountsQuery(db, "store-guid")
            .ToSql().Key;
        var assignedStoreIdsSql = MobileDeviceActivationQueries
            .BuildAssignedStoreCountQuery(db, "user-guid")
            .ToSql().Key;
        var activeRolesSql = MobileDeviceActivationQueries
            .BuildActiveRolesQuery(db, "user-guid")
            .ToSql().Key;

        AssertSqlContains(
            manageableAccountsSql,
            "[User] [user]",
            "JOIN [UserStore] [userStore]",
            "[user].[UserGUID] AS [UserGUID]",
            "[user].[Username] AS [Username]",
            "[user].[FullName] AS [FullName]");
        AssertSqlContains(
            assignedStoreIdsSql,
            "[UserStore] [userStore]",
            "JOIN [Store] [store]",
            "DISTINCT",
            "[userStore].[StoreGUID]");
        AssertSqlContains(
            activeRolesSql,
            "[UserRole] [userRole]",
            "JOIN [Role] [role]",
            "DISTINCT",
            "[role].[RoleName]");
    }

    [Fact]
    public void SqlServerThreeTableSelect_KeepsJoinAliasesConsistent()
    {
        using var db = CreateSqlServerClient();
        var sql = MobileDeviceActivationQueries
            .BuildActiveTargetQuery(db, "1042", "user-guid")
            .ToSql().Key;

        AssertSqlContains(
            sql,
            "[User] [user]",
            "JOIN [UserStore] [userStore]",
            "JOIN [Store] [store]",
            "[user].[UserGUID] AS [UserGuid]",
            "[user].[Username] AS [Username]",
            "[user].[Email] AS [Email]",
            "[user].[FullName] AS [FullName]",
            "[store].[StoreCode] AS [StoreCode]",
            "[store].[StoreName] AS [StoreName]");
    }

    [Fact]
    public void SqlServerAssignedStoreOrderingAndProjection_KeepJoinAliasesConsistent()
    {
        using var db = CreateSqlServerClient();
        var sql = MobileDeviceActivationQueries
            .BuildAssignedStoresQuery(db, "user-guid")
            .ToSql().Key;

        AssertSqlContains(
            sql,
            "[UserStore] [userStore]",
            "JOIN [Store] [store]",
            "[store].[StoreGUID] AS [StoreGuid]",
            "[store].[StoreCode] AS [StoreCode]",
            "[store].[StoreName] AS [StoreName]",
            "[userStore].[IsPrimary] AS [IsPrimary]",
            "ORDER BY",
            "[userStore].[IsPrimary]",
            "[store].[StoreCode] ASC");
    }

    private static MobileDeviceActivationService CreateService(ISqlSugarClient db) =>
        new(
            CreateContext<POSMSqlSugarContext>(db),
            CreateContext<SqlSugarContext>(db),
            new ThrowingTokenIssuer(),
            NullLogger<MobileDeviceActivationService>.Instance);

    private static SqlSugarClient CreateSqlServerClient() =>
        new(new ConnectionConfig
        {
            ConnectionString =
                "Server=127.0.0.1;Database=hb_platform_sql_generation;"
                + "User Id=test;Password=test;TrustServerCertificate=True;",
            DbType = DbType.SqlServer,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        });

    private static TContext CreateContext<TContext>(ISqlSugarClient db)
        where TContext : class
    {
        var context = (TContext)RuntimeHelpers.GetUninitializedObject(typeof(TContext));
        typeof(TContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private static async Task<object?> InvokePrivateAsync(
        MobileDeviceActivationService service,
        string methodName,
        params object[] arguments)
    {
        var method = typeof(MobileDeviceActivationService).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method.Invoke(service, arguments));
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private static T ReadProperty<T>(object instance, string propertyName) =>
        Assert.IsType<T>(instance.GetType().GetProperty(propertyName)!.GetValue(instance));

    private static void AssertSqlContains(string sql, params string[] expectedFragments)
    {
        foreach (var fragment in expectedFragments)
        {
            Assert.Contains(fragment, sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class ThrowingTokenIssuer : IMobileDeviceAccountTokenIssuer
    {
        public MobileDeviceIssuedToken Issue(MobileDeviceAccountTokenSubject subject) =>
            throw new InvalidOperationException("The assignment query test must not issue tokens.");
    }
}
