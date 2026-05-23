using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public sealed class UserStoreManagementRelationTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _sqliteConnection;
        private readonly SqlSugarClient _db;

        public UserStoreManagementRelationTests()
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

            _db.CodeFirst.InitTables<User, Store, UserStore>();
        }

        [Fact]
        public async Task AssignStoresToUserAsync_PreservesExplicitManagementFlags()
        {
            await SeedUsersAndStoresAsync();
            var service = CreateUserService();

            var result = await service.AssignStoresToUserAsync(
                "user-1",
                new List<UserStoreAssignmentDto>
                {
                    new() { StoreGUID = "store-1", IsPrimary = false },
                    new() { StoreGUID = "store-2", IsPrimary = true },
                }
            );

            Assert.True(result.Success);
            var store1 = await FindUserStoreAsync("user-1", "store-1");
            var store2 = await FindUserStoreAsync("user-1", "store-2");
            Assert.False(store1.IsPrimary);
            Assert.True(store2.IsPrimary);
        }

        [Fact]
        public async Task AddUserToStoreAsync_PersistsManagementFlag()
        {
            await SeedUsersAndStoresAsync();
            var service = CreateStoreService();

            var result = await service.AddUserToStoreAsync(
                "store-1",
                new AddUserToStoreDto { UserGUID = "user-1", IsPrimary = true }
            );

            Assert.True(result.Success);
            var userStore = await FindUserStoreAsync("user-1", "store-1");
            Assert.True(userStore.IsPrimary);
        }

        [Fact]
        public async Task SetPrimaryUserAsync_UpdatesOnlyTargetStoreManagementFlag()
        {
            await SeedUsersAndStoresAsync();
            await InsertUserStoreAsync("user-1", "store-1", false);
            await InsertUserStoreAsync("user-1", "store-2", true);
            var service = CreateStoreService();

            var enableResult = await service.SetPrimaryUserAsync("store-1", "user-1", true);

            Assert.True(enableResult.Success);
            Assert.True((await FindUserStoreAsync("user-1", "store-1")).IsPrimary);
            Assert.True((await FindUserStoreAsync("user-1", "store-2")).IsPrimary);

            var disableResult = await service.SetPrimaryUserAsync("store-2", "user-1", false);

            Assert.True(disableResult.Success);
            Assert.True((await FindUserStoreAsync("user-1", "store-1")).IsPrimary);
            Assert.False((await FindUserStoreAsync("user-1", "store-2")).IsPrimary);
        }

        [Fact]
        public async Task GetStoreUsersAsync_ReturnsManagementFlag()
        {
            await SeedUsersAndStoresAsync();
            await InsertUserStoreAsync("user-1", "store-1", true);
            var service = CreateStoreService();

            var result = await service.GetStoreUsersAsync(
                "store-1",
                new UserQueryDto { Page = 1, PageSize = 20 }
            );

            Assert.True(result.Success);
            var data = result.Data;
            Assert.NotNull(data);
            var items = data!.Items;
            Assert.NotNull(items);
            Assert.Single(items);
            Assert.True(items![0].IsPrimary);
        }

        [Fact]
        public async Task GetStoreByGuidAsync_ReturnsManagementFlagForLinkedUsers()
        {
            await SeedUsersAndStoresAsync();
            await InsertUserStoreAsync("user-1", "store-1", true);
            var service = CreateStoreService();

            var result = await service.GetStoreByGuidAsync("store-1");

            Assert.True(result.Success);
            Assert.Single(result.Data!.Users!);
            Assert.True(result.Data.Users[0].IsPrimary);
        }

        public void Dispose()
        {
            _db.Dispose();
            _sqliteConnection.Dispose();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }

        private UserService CreateUserService()
        {
            return new UserService(CreateSqlSugarContext(_db), NullLogger<UserService>.Instance);
        }

        private StoreService CreateStoreService()
        {
            return new StoreService(CreateSqlSugarContext(_db), NullLogger<StoreService>.Instance);
        }

        private async Task SeedUsersAndStoresAsync()
        {
            await _db.Insertable(new[]
            {
                new User
                {
                    UserGUID = "user-1",
                    Username = "user_1",
                    Email = "user1@example.com",
                    PasswordHash = "hashed",
                    IsActive = true,
                },
            }).ExecuteCommandAsync();

            await _db.Insertable(new[]
            {
                new Store
                {
                    StoreGUID = "store-1",
                    StoreCode = "S001",
                    StoreName = "Store 1",
                    IsActive = true,
                },
                new Store
                {
                    StoreGUID = "store-2",
                    StoreCode = "S002",
                    StoreName = "Store 2",
                    IsActive = true,
                },
            }).ExecuteCommandAsync();
        }

        private async Task InsertUserStoreAsync(string userGuid, string storeGuid, bool isPrimary)
        {
            await _db.Insertable(new UserStore
            {
                UserStoreGUID = Guid.NewGuid().ToString(),
                UserGUID = userGuid,
                StoreGUID = storeGuid,
                IsPrimary = isPrimary,
                AssignedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
        }

        private async Task<UserStore> FindUserStoreAsync(string userGuid, string storeGuid)
        {
            var userStore = await _db.Queryable<UserStore>()
                .FirstAsync(item => item.UserGUID == userGuid && item.StoreGUID == storeGuid);
            Assert.NotNull(userStore);
            return userStore!;
        }

        private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
        {
            var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(
                typeof(SqlSugarContext)
            );

            var dbField = typeof(SqlSugarContext).GetField(
                "_db",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            dbField!.SetValue(context, db);

            return context;
        }
    }
}
