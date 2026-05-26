using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
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

            _db.CodeFirst.InitTables<User, Store, UserStore, Role, UserRole>();
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

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetUsers_WhenStoreManagerHasManagedStores_OnlyReturnsScopedUsers(
            bool useOptimized
        )
        {
            await SeedUsersRolesAndStoresForScopeTestsAsync();
            var service = CreateUserService(
                new FakeManageableStoreScopeService(
                    new CurrentUserManageableStoreScope
                    {
                        IsAllowed = true,
                        IsAuthenticated = true,
                        UserGuid = "manager-1",
                        StoreGuids = new[] { "store-1" },
                        StoreCodes = new[] { "S001" },
                    }
                )
            );

            var result = await GetUsersAsync(
                service,
                useOptimized,
                new UserQueryDto { Page = 1, PageSize = 20 }
            );

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal(3, result.Data!.Total);
            Assert.Equal(
                new[] { "dual-user", "manager-1", "scoped-user" },
                result.Data.Items!
                    .Select(item => item.UserGUID)
                    .OrderBy(item => item)
                    .ToArray()
            );
            Assert.DoesNotContain(
                result.Data.Items!,
                item => item.UserGUID == "soft-deleted-scoped-user"
            );

            var dualUser = Assert.Single(result.Data.Items, item => item.UserGUID == "dual-user");
            Assert.Equal(new[] { "Store 1" }, dualUser.StoreNames);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetUsers_WhenStoreManagerHasNoManagedStores_ReturnsEmptyPage(
            bool useOptimized
        )
        {
            await SeedUsersRolesAndStoresForScopeTestsAsync();
            var service = CreateUserService(
                new FakeManageableStoreScopeService(
                    new CurrentUserManageableStoreScope
                    {
                        IsAllowed = false,
                        IsAuthenticated = true,
                        UserGuid = "manager-no-store",
                        Message = "当前店长未分配任何可管理分店",
                    }
                )
            );

            var result = await GetUsersAsync(
                service,
                useOptimized,
                new UserQueryDto { Page = 1, PageSize = 20 }
            );

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Empty(result.Data!.Items!);
            Assert.Equal(0, result.Data.Total);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetUsers_WhenViewerIsNotStoreManager_RemainsUnrestricted(bool useOptimized)
        {
            await SeedUsersRolesAndStoresForScopeTestsAsync();
            var service = CreateUserService(
                new FakeManageableStoreScopeService(
                    new CurrentUserManageableStoreScope
                    {
                        IsAllowed = false,
                        IsAuthenticated = true,
                        UserGuid = "viewer-1",
                        Message = "当前账号没有店员管理权限",
                    }
                )
            );

            var result = await GetUsersAsync(
                service,
                useOptimized,
                new UserQueryDto { Page = 1, PageSize = 20 }
            );

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal(7, result.Data!.Total);
            Assert.Contains(result.Data.Items!, item => item.UserGUID == "foreign-user");
            Assert.Contains(result.Data.Items!, item => item.UserGUID == "viewer-1");
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

        private UserService CreateUserService(
            ICurrentUserManageableStoreScopeService? manageableStoreScopeService = null
        )
        {
            return new UserService(
                CreateSqlSugarContext(_db),
                NullLogger<UserService>.Instance,
                manageableStoreScopeService
            );
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

        private async Task SeedUsersRolesAndStoresForScopeTestsAsync()
        {
            await _db.Insertable(
                new[]
                {
                    CreateUser("manager-1", "manager1@example.com"),
                    CreateUser("manager-no-store", "manager2@example.com"),
                    CreateUser("viewer-1", "viewer@example.com"),
                    CreateUser("scoped-user", "scoped@example.com"),
                    CreateUser("soft-deleted-scoped-user", "deleted-scoped@example.com"),
                    CreateUser("foreign-user", "foreign@example.com"),
                    CreateUser("dual-user", "dual@example.com"),
                }
            ).ExecuteCommandAsync();

            await _db.Insertable(
                new[]
                {
                    new Role
                    {
                        RoleGUID = "role-store-manager",
                        RoleName = "StoreManager",
                        IsActive = true,
                    },
                    new Role
                    {
                        RoleGUID = "role-viewer",
                        RoleName = "Viewer",
                        IsActive = true,
                    },
                }
            ).ExecuteCommandAsync();

            await _db.Insertable(
                new[]
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
                }
            ).ExecuteCommandAsync();

            await _db.Insertable(
                new[]
                {
                    CreateUserRole("manager-1", "role-store-manager"),
                    CreateUserRole("manager-no-store", "role-store-manager"),
                    CreateUserRole("viewer-1", "role-viewer"),
                }
            ).ExecuteCommandAsync();

            await _db.Insertable(
                new[]
                {
                    CreateUserStore("manager-1", "store-1", true),
                    CreateUserStore("scoped-user", "store-1", false),
                    CreateUserStore("soft-deleted-scoped-user", "store-1", false, true),
                    CreateUserStore("foreign-user", "store-2", false),
                    CreateUserStore("dual-user", "store-1", false),
                    CreateUserStore("dual-user", "store-2", false),
                }
            ).ExecuteCommandAsync();
        }

        private static User CreateUser(string userGuid, string email)
        {
            return new User
            {
                UserGUID = userGuid,
                Username = userGuid,
                Email = email,
                PasswordHash = "hashed",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };
        }

        private static UserRole CreateUserRole(string userGuid, string roleGuid)
        {
            return new UserRole
            {
                UserRoleGUID = Guid.NewGuid().ToString(),
                UserGUID = userGuid,
                RoleGUID = roleGuid,
                AssignedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            };
        }

        private static UserStore CreateUserStore(
            string userGuid,
            string storeGuid,
            bool isPrimary,
            bool isDeleted = false
        )
        {
            return new UserStore
            {
                UserStoreGUID = Guid.NewGuid().ToString(),
                UserGUID = userGuid,
                StoreGUID = storeGuid,
                IsPrimary = isPrimary,
                IsDeleted = isDeleted,
                AssignedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
        }

        private static Task<ApiResponse<PagedResult<UserDto>>> GetUsersAsync(
            UserService service,
            bool useOptimized,
            UserQueryDto query
        )
        {
            return useOptimized ? service.GetUsersOptimizedAsync(query) : service.GetUsersAsync(query);
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

        private sealed class FakeManageableStoreScopeService
            : ICurrentUserManageableStoreScopeService
        {
            private readonly CurrentUserManageableStoreScope _scope;

            public FakeManageableStoreScopeService(CurrentUserManageableStoreScope scope)
            {
                _scope = scope;
            }

            public Task<CurrentUserManageableStoreScope> GetScopeAsync() => Task.FromResult(_scope);

            public Task<IReadOnlyList<string>> GetAccessibleStoreCodesAsync() =>
                Task.FromResult(_scope.StoreCodes);

            public Task<bool> CanAccessStoreCodeAsync(string storeCode) =>
                Task.FromResult(_scope.CanAccessStoreCode(storeCode));

            public Task<bool> CanAccessOrderAsync(string orderGuid) => Task.FromResult(false);

            public Task<bool> CanManageStoreAsync(string storeGuid) =>
                Task.FromResult(_scope.CanAccessStoreGuid(storeGuid));

            public Task<bool> CanManageUserAsync(string userGuid) => Task.FromResult(false);
        }
    }
}
