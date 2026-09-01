using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.MobileDeviceActivation;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.MobileDeviceActivation.Tests;

public sealed class MobileDeviceActivationServiceSqlSugarTests
{
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
    public void JoinQueries_KeepSqlSugarAliasesConsistent()
    {
        var source = ReadServiceSource();

        Assert.Contains(".Select((user, userStore, store) =>", source, StringComparison.Ordinal);
        Assert.Contains(".Select((userStore, store) => userStore.StoreGUID)", source, StringComparison.Ordinal);
        Assert.Contains(".Select((userRole, role) => role.RoleName)", source, StringComparison.Ordinal);
        Assert.Contains(".OrderBy((userStore, store) => userStore.IsPrimary ? 0 : 1)", source, StringComparison.Ordinal);
        Assert.Contains(".OrderBy((userStore, store) => store.StoreCode)", source, StringComparison.Ordinal);

        Assert.DoesNotContain(".Select((user, _, store) =>", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Select((userStore, _) =>", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Select((_, role) =>", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".OrderBy((userStore, _) =>", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".OrderBy((_, store) =>", source, StringComparison.Ordinal);
    }

    private static MobileDeviceActivationService CreateService(ISqlSugarClient db) =>
        new(
            CreateContext<POSMSqlSugarContext>(db),
            CreateContext<SqlSugarContext>(db),
            new ThrowingTokenIssuer(),
            NullLogger<MobileDeviceActivationService>.Instance);

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

    private static string ReadServiceSource([CallerFilePath] string testSourcePath = "")
    {
        var testProject = Path.GetDirectoryName(testSourcePath)
            ?? throw new InvalidOperationException("Test source directory is unavailable.");
        var backendRoot = Directory.GetParent(testProject)?.FullName
            ?? throw new InvalidOperationException("Backend source directory is unavailable.");
        return File.ReadAllText(Path.Combine(
            backendRoot,
            "BlazorApp.Api",
            "Services",
            "MobileDeviceActivation",
            "MobileDeviceActivationService.cs"));
    }

    private sealed class ThrowingTokenIssuer : IMobileDeviceAccountTokenIssuer
    {
        public MobileDeviceIssuedToken Issue(MobileDeviceAccountTokenSubject subject) =>
            throw new InvalidOperationException("The assignment query test must not issue tokens.");
    }
}
