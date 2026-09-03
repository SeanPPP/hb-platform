using System.Security.Claims;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services.MobileDeviceActivation;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.MobileDeviceActivation.Tests;

public sealed class MobileDeviceActivationCodeManagementServiceTests
{
    [Fact]
    public async Task GetManageableAccountsAsync_ReturnsActiveUsersAssignedToActiveStore()
    {
        var databaseName = $"mobile-activation-accounts-{Guid.NewGuid():N}";
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
        db.CodeFirst.InitTables<Store, User, UserStore>();

        const string storeGuid = "store-guid-1002";
        const string userGuid = "user-guid-1002";
        await db.Insertable(new Store
        {
            StoreGUID = storeGuid,
            StoreCode = "1002",
            StoreName = "Robinson Road",
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await db.Insertable(new User
        {
            UserGUID = userGuid,
            Username = "mobile.user",
            Email = "mobile.user@example.test",
            PasswordHash = "test-only",
            FullName = "Mobile User",
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await db.Insertable(new UserStore
        {
            UserStoreGUID = "user-store-guid-1002",
            UserGUID = userGuid,
            StoreGUID = storeGuid,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, Permissions.SuperAdminRoleNames.First())],
            "Test"));
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal },
        };
        var service = new MobileDeviceActivationCodeManagementService(
            db,
            db,
            new FakeStoreScopeService(new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAuthenticated = true,
                IsAdmin = true,
                StoreGuids = [storeGuid],
                StoreCodes = ["1002"],
            }),
            NullLogger<MobileDeviceActivationCodeManagementService>.Instance,
            httpContextAccessor: httpContextAccessor);

        var response = await service.GetManageableAccountsAsync("1002");

        Assert.True(response.Success, response.Message);
        var account = Assert.Single(response.Data!);
        Assert.Equal(userGuid, account.UserGuid);
        Assert.Equal("mobile.user", account.Username);
        Assert.Equal("Mobile User", account.FullName);
    }

    private sealed class FakeStoreScopeService(CurrentUserManageableStoreScope scope)
        : ICurrentUserManageableStoreScopeService
    {
        public Task<CurrentUserManageableStoreScope> GetScopeAsync() =>
            Task.FromResult(scope);

        public Task<IReadOnlyList<string>> GetAccessibleStoreCodesAsync() =>
            Task.FromResult(scope.StoreCodes);

        public Task<bool> CanAccessStoreCodeAsync(string storeCode) =>
            Task.FromResult(scope.CanAccessStoreCode(storeCode));

        public Task<bool> CanAccessOrderAsync(string orderGuid) => Task.FromResult(false);

        public Task<bool> CanManageStoreAsync(string storeGuid) => Task.FromResult(false);

        public Task<bool> CanManageUserAsync(string userGuid) => Task.FromResult(false);
    }
}
