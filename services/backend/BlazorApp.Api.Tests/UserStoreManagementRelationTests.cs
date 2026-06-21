using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading.Tasks;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
            _db.CodeFirst.InitTables<RefreshToken>();
        }

        [Fact]
        public void UsersController_HasSinglePublicConstructor_ForDependencyInjection()
        {
            var constructors = typeof(UsersController).GetConstructors();

            Assert.Single(constructors);
        }

        [Fact]
        public void UsersController_GetUserLoginRecords_UsesUsersViewPermission()
        {
            var method = typeof(UsersController).GetMethod(nameof(UsersController.GetUserLoginRecords));

            var authorize = method?.GetCustomAttribute<AuthorizeAttribute>();

            Assert.NotNull(authorize);
            Assert.Equal(Permissions.Users.View, authorize!.Policy);
        }

        [Fact]
        public async Task GetUserLoginRecordsAsync_ReturnsSelectedUserRecordsInCreatedAtDescendingOrder()
        {
            await SeedLoginRecordUsersAsync();
            await SeedRefreshTokenAsync(
                "session-old",
                "login-user",
                DateTime.UtcNow.AddHours(-3),
                "203.0.113.1"
            );
            await SeedRefreshTokenAsync(
                "session-new",
                "login-user",
                DateTime.UtcNow.AddHours(-1),
                "203.0.113.2"
            );
            await SeedRefreshTokenAsync(
                "session-other-user",
                "other-user",
                DateTime.UtcNow,
                "198.51.100.9"
            );
            var service = CreateUserService();

            var result = await service.GetUserLoginRecordsAsync(
                "login-user",
                new UserLoginRecordQueryDto { Page = 1, PageSize = 1 }
            );

            Assert.True(result.Success);
            Assert.Equal(2, result.Data!.Total);
            var item = Assert.Single(result.Data.Items!);
            Assert.Equal("session-new", item.SessionId);
            Assert.Equal("203.0.113.2", item.IpAddress);
        }

        [Fact]
        public async Task GetUserLoginRecordsAsync_ComputesActiveRevokedAndExpiredStatuses()
        {
            await SeedLoginRecordUsersAsync();
            await SeedRefreshTokenAsync(
                "session-active",
                "login-user",
                DateTime.UtcNow.AddMinutes(-3),
                "203.0.113.1"
            );
            await SeedRefreshTokenAsync(
                "session-revoked",
                "login-user",
                DateTime.UtcNow.AddMinutes(-2),
                "203.0.113.2",
                isRevoked: true
            );
            await SeedRefreshTokenAsync(
                "session-expired",
                "login-user",
                DateTime.UtcNow.AddMinutes(-1),
                "203.0.113.3",
                expiresAt: DateTime.UtcNow.AddMinutes(-1)
            );
            var service = CreateUserService();

            var result = await service.GetUserLoginRecordsAsync(
                "login-user",
                new UserLoginRecordQueryDto { Page = 1, PageSize = 10 }
            );

            Assert.True(result.Success);
            var items = result.Data!.Items!.ToDictionary(item => item.SessionId);
            Assert.Equal("active", items["session-active"].Status);
            Assert.Equal("revoked", items["session-revoked"].Status);
            Assert.Equal("expired", items["session-expired"].Status);
            Assert.False(items["session-active"].IsExpired);
            Assert.True(items["session-revoked"].IsRevoked);
            Assert.True(items["session-expired"].IsExpired);
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
        public async Task CreateStoreAsync_WhenIsActiveOmitted_DefaultsCashRegisterDisabled()
        {
            var service = CreateStoreService();

            var result = await service.CreateStoreAsync(new CreateStoreDto
            {
                StoreName = "New Store",
                StoreCode = "1999",
            });

            Assert.True(result.Success);
            Assert.False(result.Data!.IsActive);
            var createdStore = await _db.Queryable<Store>()
                .FirstAsync(store => store.StoreCode == "1999");
            Assert.NotNull(createdStore);
            Assert.False(createdStore!.IsActive);
        }

        [Fact]
        public async Task CreateStoreAsync_WhenIsActiveTrue_PersistsCashRegisterEnabled()
        {
            var service = CreateStoreService();

            var result = await service.CreateStoreAsync(new CreateStoreDto
            {
                StoreName = "Cash Store",
                StoreCode = "1888",
                IsActive = true,
            });

            Assert.True(result.Success);
            Assert.True(result.Data!.IsActive);
            var createdStore = await _db.Queryable<Store>()
                .FirstAsync(store => store.StoreCode == "1888");
            Assert.NotNull(createdStore);
            Assert.True(createdStore!.IsActive);
        }

        [Fact]
        public async Task GetNextStoreCodeAsync_WhenNoNumericCodes_ReturnsDefaultStartCode()
        {
            await _db.Insertable(new Store
            {
                StoreGUID = "store-alpha",
                StoreName = "Alpha Store",
                StoreCode = "S001",
                IsActive = true,
                IsDeleted = false,
            }).ExecuteCommandAsync();
            var service = CreateStoreService();

            var result = await service.GetNextStoreCodeAsync();

            Assert.True(result.Success);
            Assert.Equal("1001", result.Data);
        }

        [Fact]
        public async Task GetNextStoreCodeAsync_UsesMaxNumericCodePlusOne()
        {
            await _db.Insertable(new[]
            {
                new Store
                {
                    StoreGUID = "store-1001",
                    StoreName = "Store 1001",
                    StoreCode = "1001",
                    IsActive = true,
                    IsDeleted = false,
                },
                new Store
                {
                    StoreGUID = "store-1042",
                    StoreName = "Store 1042",
                    StoreCode = "1042",
                    IsActive = false,
                    IsDeleted = false,
                },
                new Store
                {
                    StoreGUID = "store-s001",
                    StoreName = "Store S001",
                    StoreCode = "S001",
                    IsActive = true,
                    IsDeleted = false,
                },
            }).ExecuteCommandAsync();
            var service = CreateStoreService();

            var result = await service.GetNextStoreCodeAsync();

            Assert.True(result.Success);
            Assert.Equal("1043", result.Data);
        }

        [Fact]
        public async Task CreateStoreAsync_WhenStoreCodeExists_ReturnsDuplicateAndDoesNotInsert()
        {
            var service = CreateStoreService();
            var first = await service.CreateStoreAsync(new CreateStoreDto
            {
                StoreName = "First Store",
                StoreCode = "1043",
            });

            var duplicate = await service.CreateStoreAsync(new CreateStoreDto
            {
                StoreName = "Duplicate Store",
                StoreCode = "1043",
            });

            Assert.True(first.Success);
            Assert.False(duplicate.Success);
            Assert.Equal("DUPLICATE_STORE_CODE", duplicate.ErrorCode);
            var count = await _db.Queryable<Store>()
                .Where(store => store.StoreCode == "1043")
                .CountAsync();
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task CreateStoreAsync_TrimsStoreCodeBeforeDuplicateCheckAndInsert()
        {
            var service = CreateStoreService();
            var first = await service.CreateStoreAsync(new CreateStoreDto
            {
                StoreName = "Trimmed Store",
                StoreCode = " 1044 ",
            });

            var duplicate = await service.CreateStoreAsync(new CreateStoreDto
            {
                StoreName = "Duplicate Trimmed Store",
                StoreCode = "1044",
            });

            Assert.True(first.Success);
            Assert.Equal("1044", first.Data!.StoreCode);
            Assert.False(duplicate.Success);
            Assert.Equal("DUPLICATE_STORE_CODE", duplicate.ErrorCode);
            var count = await _db.Queryable<Store>()
                .Where(store => store.StoreCode == "1044")
                .CountAsync();
            Assert.Equal(1, count);
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

        [Fact]
        public async Task GetStoresAsync_FiltersByBrandNameAndSortsStoreColumns()
        {
            await SeedStoresForListQueryAsync();
            await _db.Insertable(new[]
            {
                CreateUserStore("user-a", "store-hot-a", false),
                CreateUserStore("user-b", "store-hot-b", false),
                CreateUserStore("user-c", "store-hot-b", false),
            }).ExecuteCommandAsync();
            var service = CreateStoreService();

            var filtered = await service.GetStoresAsync(
                new StoreQueryDto { BrandName = "Hot Bargain", Page = 1, PageSize = 20 }
            );

            Assert.True(filtered.Success);
            var filteredData = filtered.Data!;
            var filteredItems = filteredData.Items!;
            Assert.Equal(2, filteredData.Total);
            Assert.All(filteredItems, item => Assert.Equal("Hot Bargain", item.BrandName));

            var brandSorted = await service.GetStoresAsync(
                new StoreQueryDto
                {
                    Page = 1,
                    PageSize = 20,
                    SortField = "BrandName",
                    SortOrder = "desc",
                }
            );

            Assert.True(brandSorted.Success);
            var brandSortedItems = brandSorted.Data!.Items!;
            Assert.Equal("Other Brand", brandSortedItems[0].BrandName);

            var userCountSorted = await service.GetStoresAsync(
                new StoreQueryDto
                {
                    Page = 1,
                    PageSize = 20,
                    SortField = "totalUsers",
                    SortOrder = "desc",
                }
            );

            Assert.True(userCountSorted.Success);
            var userCountSortedItems = userCountSorted.Data!.Items!;
            Assert.Equal(
                new[] { "store-hot-b", "store-hot-a", "store-other" },
                userCountSortedItems.Select(item => item.StoreGUID).ToArray()
            );
            Assert.Equal(new[] { 2, 1, 0 }, userCountSortedItems.Select(item => item.TotalUsers).ToArray());
        }

        [Fact]
        public async Task GetUsersOptimizedAsync_WhenLinkedStoreInactive_IncludesStoreName()
        {
            await SeedInactiveStoreUserAsync();
            var service = CreateUserService();

            var result = await service.GetUsersOptimizedAsync(
                new UserQueryDto { Page = 1, PageSize = 20 }
            );

            Assert.True(result.Success);
            var user = Assert.Single(result.Data!.Items!, item => item.UserGUID == "inactive-store-user");
            Assert.Contains("Inactive Store", user.StoreNames);
        }

        [Fact]
        public async Task GetUserStoresAsync_WhenLinkedStoreInactive_ReturnsStoreWithInactiveStatus()
        {
            await SeedInactiveStoreUserAsync();
            var service = CreateUserService();

            var result = await service.GetUserStoresAsync("inactive-store-user");

            Assert.True(result.Success);
            var store = Assert.Single(result.Data!);
            Assert.Equal("inactive-store", store.StoreGUID);
            Assert.False(store.IsActive);
        }

        [Theory]
        [InlineData("店长", false)]
        [InlineData("店长", true)]
        [InlineData("经理", false)]
        [InlineData("经理", true)]
        public async Task GetUsers_WhenChineseStoreManagerHasManagedStores_OnlyReturnsScopedUsers(
            string roleName,
            bool useOptimized
        )
        {
            await SeedUsersRolesAndStoresForScopeTestsAsync();
            await SeedAliasedStoreManagerAsync(
                "manager-cn-1",
                "role-store-manager-cn",
                roleName,
                true
            );

            var service = CreateUserService(
                CreateCurrentUserManageableStoreScopeService("manager-cn-1", roleName)
            );

            var result = await GetUsersAsync(
                service,
                useOptimized,
                new UserQueryDto { Page = 1, PageSize = 20 }
            );

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal(4, result.Data!.Total);
            Assert.Equal(
                new[] { "dual-user", "manager-1", "manager-cn-1", "scoped-user" },
                result.Data.Items!
                    .Select(item => item.UserGUID)
                    .OrderBy(item => item)
                    .ToArray()
            );
            Assert.DoesNotContain(result.Data.Items!, item => item.UserGUID == "foreign-user");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetUsers_WhenChineseStoreManagerHasNoManagedStores_ReturnsEmptyPage(
            bool useOptimized
        )
        {
            await SeedUsersRolesAndStoresForScopeTestsAsync();
            await SeedAliasedStoreManagerAsync(
                "manager-cn-no-store",
                "role-store-manager-manager",
                "经理",
                false
            );

            var service = CreateUserService(
                CreateCurrentUserManageableStoreScopeService("manager-cn-no-store", "经理")
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
        [InlineData("Admin")]
        [InlineData("管理员")]
        [InlineData("WarehouseManager")]
        [InlineData("仓库经理")]
        public async Task GetScopeAsync_WhenCurrentUserIsUnrestrictedAlias_ReturnsAdminScope(
            string roleName
        )
        {
            await SeedUsersRolesAndStoresForScopeTestsAsync();
            await _db.Insertable(CreateUser("admin-1", "admin@example.com")).ExecuteCommandAsync();
            var service = CreateCurrentUserManageableStoreScopeService("admin-1", roleName);

            var scope = await service.GetScopeAsync();

            Assert.True(scope.IsAllowed);
            Assert.True(scope.IsAuthenticated);
            Assert.True(scope.IsAdmin);
            Assert.Equal("admin-1", scope.UserGuid);
        }

        [Theory]
        [InlineData("管理员")]
        [InlineData("WarehouseManager")]
        [InlineData("仓库经理")]
        public async Task GetUserStores_WhenPrivilegedAliasViewsOtherUser_ReturnsStores(
            string roleName
        )
        {
            await SeedUsersRolesAndStoresForScopeTestsAsync();
            var controller = new UsersController(
                CreateUserService(),
                Mock.Of<IRoleService>(),
                NullLogger<UsersController>.Instance
            )
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = CreatePrincipal("operator-1", roleName),
                    },
                },
            };

            var result = await controller.GetUserStores(
                "scoped-user",
                new FakeCurrentUserService("operator-1")
            );

            Assert.IsType<OkObjectResult>(result);
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

            var dualUser = Assert.Single(result.Data.Items!, item => item.UserGUID == "dual-user");
            Assert.Equal(new[] { "Store 1" }, dualUser.StoreNames);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetUsers_WhenStoreManagerRequestsOutOfScopeStore_ReturnsEmptyPage(
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
                new UserQueryDto { Page = 1, PageSize = 20, StoreGuid = "store-2" }
            );

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Empty(result.Data!.Items!);
            Assert.Equal(0, result.Data.Total);
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

        [Fact]
        public async Task AssignStoresToUser_WhenScopedManagerAddsOutOfScopeStore_ReturnsError()
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

            var result = await service.AssignStoresToUserAsync(
                "scoped-user",
                new List<UserStoreAssignmentDto>
                {
                    new() { StoreGUID = "store-2", IsPrimary = true },
                }
            );

            Assert.False(result.Success);
            Assert.Equal("STORE_SCOPE_DENIED", result.ErrorCode);
            Assert.False(await _db.Queryable<UserStore>().AnyAsync(item =>
                item.UserGUID == "scoped-user" && item.StoreGUID == "store-2"
            ));
        }

        [Fact]
        public async Task CreateUser_WhenScopedManagerAssignsOutOfScopeStore_ReturnsError()
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

            var result = await service.CreateUserAsync(
                new CreateUserDto
                {
                    Username = "new-scoped-user",
                    Email = "new-scoped@example.com",
                    Password = "secret1",
                    IsActive = true,
                    StoreGuids = new List<string> { "store-2" },
                }
            );

            Assert.False(result.Success);
            Assert.Equal("STORE_SCOPE_DENIED", result.ErrorCode);
            Assert.False(await _db.Queryable<User>().AnyAsync(item =>
                item.Username == "new-scoped-user"
            ));
        }

        [Fact]
        public async Task AssignStoresToUser_WhenChineseStoreManagerAddsOutOfScopeStore_ReturnsError()
        {
            await SeedUsersRolesAndStoresForScopeTestsAsync();
            await SeedAliasedStoreManagerAsync(
                "manager-cn-1",
                "role-store-manager-cn",
                "店长",
                true
            );
            var service = CreateUserService(
                CreateCurrentUserManageableStoreScopeService("manager-cn-1", "店长")
            );

            var result = await service.AssignStoresToUserAsync(
                "scoped-user",
                new List<UserStoreAssignmentDto>
                {
                    new() { StoreGUID = "store-2", IsPrimary = true },
                }
            );

            Assert.False(result.Success);
            Assert.Equal("STORE_SCOPE_DENIED", result.ErrorCode);
        }

        [Fact]
        public async Task AssignStoresToUser_WhenScopedManagerReceivesHiddenExistingStore_PreservesIt()
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

            var result = await service.AssignStoresToUserAsync(
                "dual-user",
                new List<UserStoreAssignmentDto>
                {
                    new() { StoreGUID = "store-1", IsPrimary = true },
                    new() { StoreGUID = "store-2", IsPrimary = true },
                }
            );

            Assert.True(result.Success);
            Assert.True((await FindUserStoreAsync("dual-user", "store-1")).IsPrimary);
            Assert.False((await FindUserStoreAsync("dual-user", "store-2")).IsPrimary);
        }

        public void Dispose()
        {
            _db.Dispose();
            _sqliteConnection.Dispose();
            if (File.Exists(_dbPath))
            {
                SqliteTempFileCleanup.DeleteIfExists(_dbPath);
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

        private async Task SeedStoresForListQueryAsync()
        {
            await _db.Insertable(new[]
            {
                new Store
                {
                    StoreGUID = "store-hot-a",
                    StoreCode = "S001",
                    StoreName = "Hot Store A",
                    BrandName = "Hot Bargain",
                    Phone = "0731000001",
                    IsActive = true,
                },
                new Store
                {
                    StoreGUID = "store-hot-b",
                    StoreCode = "S002",
                    StoreName = "Hot Store B",
                    BrandName = "Hot Bargain",
                    Phone = "0731000002",
                    IsActive = false,
                },
                new Store
                {
                    StoreGUID = "store-other",
                    StoreCode = "S003",
                    StoreName = "Other Store",
                    BrandName = "Other Brand",
                    Phone = "0731000003",
                    IsActive = true,
                },
            }).ExecuteCommandAsync();
        }

        private async Task SeedInactiveStoreUserAsync()
        {
            await _db.Insertable(CreateUser("inactive-store-user", "inactive-store-user@example.com"))
                .ExecuteCommandAsync();

            await _db.Insertable(
                new Store
                {
                    StoreGUID = "inactive-store",
                    StoreCode = "INACTIVE",
                    StoreName = "Inactive Store",
                    IsActive = false,
                    IsDeleted = false,
                    CreatedAt = DateTime.UtcNow,
                }
            ).ExecuteCommandAsync();

            await _db.Insertable(CreateUserStore("inactive-store-user", "inactive-store", false))
                .ExecuteCommandAsync();
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

        private async Task SeedAliasedStoreManagerAsync(
            string userGuid,
            string roleGuid,
            string roleName,
            bool assignManagedStore
        )
        {
            await _db.Insertable(CreateUser(userGuid, $"{userGuid}@example.com")).ExecuteCommandAsync();
            await _db.Insertable(
                new Role
                {
                    RoleGUID = roleGuid,
                    RoleName = roleName,
                    IsActive = true,
                }
            ).ExecuteCommandAsync();
            await _db.Insertable(CreateUserRole(userGuid, roleGuid)).ExecuteCommandAsync();

            if (assignManagedStore)
            {
                await _db.Insertable(CreateUserStore(userGuid, "store-1", true)).ExecuteCommandAsync();
            }
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

        private async Task SeedLoginRecordUsersAsync()
        {
            await _db.Insertable(new[]
            {
                new User
                {
                    UserGUID = "login-user",
                    Username = "login-user",
                    Email = "login-user@example.com",
                    PasswordHash = "hashed",
                    IsActive = true,
                    IsDeleted = false,
                },
                new User
                {
                    UserGUID = "other-user",
                    Username = "other-user",
                    Email = "other-user@example.com",
                    PasswordHash = "hashed",
                    IsActive = true,
                    IsDeleted = false,
                },
            }).ExecuteCommandAsync();
        }

        private Task SeedRefreshTokenAsync(
            string sessionId,
            string userGuid,
            DateTime createdAt,
            string ipAddress,
            bool isRevoked = false,
            DateTime? expiresAt = null
        )
        {
            return _db.Insertable(new RefreshToken
            {
                RefreshTokenGUID = sessionId,
                UserGUID = userGuid,
                Token = $"{sessionId}-token",
                IpAddress = ipAddress,
                UserAgent = $"{sessionId}-agent",
                ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(1),
                IsRevoked = isRevoked,
                IsDeleted = false,
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
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

        private CurrentUserManageableStoreScopeService CreateCurrentUserManageableStoreScopeService(
            string userGuid,
            string roleName
        )
        {
            var httpContextAccessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = CreatePrincipal(userGuid, roleName),
                },
            };

            return new CurrentUserManageableStoreScopeService(
                CreateSqlSugarContext(_db),
                new FakeCurrentUserService(userGuid),
                httpContextAccessor
            );
        }

        private static ClaimsPrincipal CreatePrincipal(string userGuid, string roleName)
        {
            var claims = new List<Claim>
            {
                new("userGuid", userGuid),
                new(ClaimTypes.NameIdentifier, userGuid),
                new(ClaimTypes.Name, userGuid),
                new(ClaimTypes.Role, roleName),
            };

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthType"));
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

        private sealed class FakeCurrentUserService : ICurrentUserService
        {
            private readonly string _userGuid;

            public FakeCurrentUserService(string userGuid)
            {
                _userGuid = userGuid;
            }

            public string GetCurrentUsername() => _userGuid;

            public string GetCurrentUserGuid() => _userGuid;
        }
    }
}
