using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public sealed class SeedDataServiceTests : IDisposable
    {
        private const string InstallmentOrdersPermission = "InstallmentOrders.View";
        private const string StoreVouchersPermission = "StoreVouchers.View";
        private const string DeviceRegistrationViewPermission = "DeviceRegistration.View";
        private const string DeviceRegistrationManagePermission = "DeviceRegistration.Manage";
        private const string PosProductsViewPermission = "PosProducts.View";
        private const string PosProductsManagePermission = "PosProducts.Manage";
        private const string AdvertisementsViewPermission = "Advertisements.View";
        private const string AdvertisementsEditPermission = "Advertisements.Edit";
        private const string SeasonalCardsViewManagedStorePermission =
            "SeasonalCards.Remaining.ViewManagedStore";
        private const string SeasonalCardsSubmitManagedStorePermission =
            "SeasonalCards.Remaining.SubmitManagedStore";

        private readonly string _dbPath;
        private readonly SqliteConnection _sqliteConnection;
        private readonly SqlSugarClient _db;

        public SeedDataServiceTests()
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

            _db.CodeFirst.InitTables<Role, SysPermission, SysRolePermission, SeasonalCardCatalog>();
        }

        [Fact]
        public void AttendancePermissionSeeds_UseEnglishCodesAndChineseMetadata()
        {
            var expectedCodes = new[]
            {
                Permissions.Attendance.Schedule.ViewSelf,
                Permissions.Attendance.Schedule.ViewStore,
                Permissions.Attendance.Schedule.EditManagedStore,
                Permissions.Attendance.Availability.SubmitSelf,
                Permissions.Attendance.Availability.ViewManagedStore,
                Permissions.Attendance.Punch.Self,
                Permissions.Attendance.Punch.ViewManagedStore,
                Permissions.Attendance.Approval.ViewManagedStore,
                Permissions.Attendance.Approval.ReviewManagedStore,
                Permissions.Attendance.Holiday.ViewStore,
                Permissions.Attendance.Holiday.EditManagedStore,
                Permissions.Attendance.Leave.ApplySelf,
                Permissions.Attendance.Leave.ViewManagedStore,
                Permissions.Attendance.Leave.ReviewManagedStore,
                Permissions.Attendance.Settings.Edit,
                Permissions.Attendance.Admin.View,
            };

            var seeds = PermissionSeedData.AttendancePermissions.ToList();

            Assert.Equal(expectedCodes.OrderBy(code => code), seeds.Select(seed => seed.Code).OrderBy(code => code));
            Assert.All(seeds, seed =>
            {
                Assert.Matches("^[A-Za-z.]+$", seed.Code);
                Assert.Equal("排班考勤", seed.Category);
                Assert.False(string.IsNullOrWhiteSpace(seed.Name));
                Assert.False(string.IsNullOrWhiteSpace(seed.Description));
                Assert.Matches(@"[\u4e00-\u9fff]", seed.Name);
                Assert.Matches(@"[\u4e00-\u9fff]", seed.Category);
                Assert.Matches(@"[\u4e00-\u9fff]", seed.Description);
            });
        }

        [Fact]
        public void AllPermissionSeeds_OnlyIncludeCanonicalPermissionCodes()
        {
            var seeds = PermissionSeedData.AllPermissions.ToList();
            var seedCodes = seeds.Select(seed => seed.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var permissionConstants = GetPermissionConstantCodes();

            Assert.Subset(permissionConstants, seedCodes);
            Assert.Subset(seedCodes, permissionConstants);
            Assert.Contains(seeds, seed => seed.Code == Permissions.OrderFront.View && seed.Category == "前台订货");
            Assert.Equal(seeds.Count, seeds.Select(seed => seed.Code.ToLowerInvariant()).Distinct().Count());
        }

        [Fact]
        public void DeprecatedPermissionCodes_AreNotActiveSeeds()
        {
            var activeSeedCodes = PermissionSeedData.AllPermissions
                .Select(seed => seed.Code)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var deprecatedCodes = PermissionSeedData.DeprecatedPermissionCodes;
            var expectedDeprecatedCodes = new[]
            {
                "AustralianSuppliers",
                "LocalInvocie",
                "LocalInvocie.View",
                "LocalInvocie.Create",
                "LocalInvocie.Edit",
                "LocalInvocie.Delete",
                "LocalPurchase",
                "StoreProducts",
                "Promotions",
                "PricingStrategy",
                "ChinaProduct.View",
                "ChinaProduct.Create",
                "ChinaProduct.Edit",
                "ChinaProduct.Delete",
            };

            Assert.All(expectedDeprecatedCodes, code =>
            {
                Assert.Contains(code, deprecatedCodes, StringComparer.OrdinalIgnoreCase);
                Assert.DoesNotContain(code, activeSeedCodes, StringComparer.OrdinalIgnoreCase);
            });
        }

        [Fact]
        public void LocalPurchasePushToHqPermission_IsCanonicalSeed()
        {
            var seed = Assert.Single(
                PermissionSeedData.AllPermissions,
                item => item.Code == Permissions.LocalPurchase.PushToHq
            );

            Assert.Equal("推送本地进货到 HQ", seed.Name);
            Assert.Equal("本地进货管理", seed.Category);
            Assert.Equal("页面 /pos-admin/local-supplier-invoices - 推送本地进货单到 HQ", seed.Description);
        }

        [Fact]
        public void StoreFinancePermissionSeeds_UseEnglishCodesAndChineseMetadata()
        {
            var seeds = PermissionSeedData.AllPermissions.ToList();

            var installmentOrders = Assert.Single(seeds, seed => seed.Code == InstallmentOrdersPermission);
            Assert.Equal("查看分期付款订单", installmentOrders.Name);
            Assert.Equal("分店财务", installmentOrders.Category);
            Assert.Equal("分店财务 - 查看分店分期付款订单与支付记录", installmentOrders.Description);

            var storeVouchers = Assert.Single(seeds, seed => seed.Code == StoreVouchersPermission);
            Assert.Equal("查看分店代金券", storeVouchers.Name);
            Assert.Equal("分店财务", storeVouchers.Category);
            Assert.Equal("分店财务 - 查看分店代金券使用情况与关联订单", storeVouchers.Description);
        }

        [Fact]
        public void PagePermissionSeeds_IncludeDeviceRegistrationAndPosProductPages()
        {
            var seeds = PermissionSeedData.AllPermissions.ToList();

            var deviceRegistrationView = Assert.Single(seeds, seed => seed.Code == DeviceRegistrationViewPermission);
            Assert.Equal("查看设备注册", deviceRegistrationView.Name);
            Assert.Equal("系统管理", deviceRegistrationView.Category);
            Assert.Contains("/system/device-registration", deviceRegistrationView.Description);
            Assert.Contains("查看 POS 设备注册列表与状态", deviceRegistrationView.Description);

            var deviceRegistrationManage = Assert.Single(seeds, seed => seed.Code == DeviceRegistrationManagePermission);
            Assert.Equal("管理设备注册", deviceRegistrationManage.Name);
            Assert.Equal("系统管理", deviceRegistrationManage.Category);
            Assert.Contains("/system/device-registration", deviceRegistrationManage.Description);
            Assert.Contains("审核、维护或管理设备注册", deviceRegistrationManage.Description);

            var posProductsView = Assert.Single(seeds, seed => seed.Code == PosProductsViewPermission);
            Assert.Equal("查看 POS 商品管理", posProductsView.Name);
            Assert.Equal("POS 管理", posProductsView.Category);
            Assert.Contains("/pos-admin/products", posProductsView.Description);
            Assert.Contains("查看 POS 商品、分类、套装码、同步和完整性检查入口", posProductsView.Description);

            var posProductsManage = Assert.Single(seeds, seed => seed.Code == PosProductsManagePermission);
            Assert.Equal("管理 POS 商品", posProductsManage.Name);
            Assert.Equal("POS 管理", posProductsManage.Category);
            Assert.Contains("/pos-admin/products", posProductsManage.Description);
            Assert.Contains("编辑 POS 商品、批量改价、同步总部/分店、维护分类/套装码、执行完整性修复", posProductsManage.Description);

            var advertisementsView = Assert.Single(seeds, seed => seed.Code == AdvertisementsViewPermission);
            Assert.Equal("查看广告素材", advertisementsView.Name);
            Assert.Equal("广告管理", advertisementsView.Category);
            Assert.Contains("/pos-admin/advertisements", advertisementsView.Description);

            var advertisementsEdit = Assert.Single(seeds, seed => seed.Code == AdvertisementsEditPermission);
            Assert.Equal("编辑广告素材", advertisementsEdit.Name);
            Assert.Equal("广告管理", advertisementsEdit.Category);
            Assert.Contains("/pos-admin/advertisements", advertisementsEdit.Description);
        }

        [Fact]
        public void SeasonalCardPermissionSeeds_UseSeasonalCardsCategoryAndGrantStoreManagerTemplate()
        {
            var seeds = PermissionSeedData.AllPermissions.ToList();

            var viewSeed = Assert.Single(
                seeds,
                seed => seed.Code == SeasonalCardsViewManagedStorePermission
            );
            Assert.Equal("查看管理分店季节卡剩余", viewSeed.Name);
            Assert.Equal("季节卡片", viewSeed.Category);
            Assert.Contains("/seasonal-cards", viewSeed.Description);

            var submitSeed = Assert.Single(
                seeds,
                seed => seed.Code == SeasonalCardsSubmitManagedStorePermission
            );
            Assert.Equal("提交管理分店季节卡剩余", submitSeed.Name);
            Assert.Equal("季节卡片", submitSeed.Category);
            Assert.Contains("/seasonal-cards", submitSeed.Description);

            var storeManagerTemplate = Assert.Single(
                PermissionSeedData.RolePermissionTemplates,
                template => template.RoleName == "StoreManager"
            );
            Assert.Contains(
                SeasonalCardsViewManagedStorePermission,
                storeManagerTemplate.PermissionCodes
            );
            Assert.Contains(
                SeasonalCardsSubmitManagedStorePermission,
                storeManagerTemplate.PermissionCodes
            );
        }

        [Fact]
        public async Task InitializePermissionSeedsAsync_SyncsPermissions_ClearsAdminLinks_AndSeedsRoleTemplatesIdempotently()
        {
            var adminRole = CreateRole("role-admin", "Admin", "System Administrator");
            var warehouseManagerRole = CreateRole("role-warehouse-manager", "WarehouseManager", "Warehouse manager");
            var storeManagerRole = CreateRole("role-store-manager", "StoreManager", "Store manager");
            var managerRole = CreateRole("role-manager", "Manager", "Manager");
            var userRole = CreateRole("role-user", "User", "User");
            var storeStaffRole = CreateRole("role-store-staff", "StoreStaff", "Store staff");
            var orderRole = CreateRole("role-order", "Order", "Order");

            await _db.Insertable(
                new[]
                {
                    adminRole,
                    warehouseManagerRole,
                    storeManagerRole,
                    managerRole,
                    userRole,
                    storeStaffRole,
                    orderRole,
                }
            ).ExecuteCommandAsync();

            await _db.Insertable(
                new[]
                {
                    new SysRolePermission
                    {
                        Id = "admin-legacy-dashboard",
                        RoleGuid = adminRole.RoleGUID,
                        PermissionCode = Permissions.Dashboard.View,
                    },
                    new SysRolePermission
                    {
                        Id = "store-manager-existing-extra",
                        RoleGuid = storeManagerRole.RoleGUID,
                        PermissionCode = Permissions.Users.Delete,
                    },
                    new SysRolePermission
                    {
                        Id = "store-manager-existing-template",
                        RoleGuid = storeManagerRole.RoleGUID,
                        PermissionCode = Permissions.Attendance.Schedule.ViewSelf,
                    },
                }
            ).ExecuteCommandAsync();

            var service = CreateService();

            await service.InitializePermissionSeedsAsync();
            await service.InitializePermissionSeedsAsync();

            var allPermissionRows = await _db.Queryable<SysPermission>().ToListAsync();
            var attendanceRows = allPermissionRows
                .Where(item => PermissionSeedData.AttendancePermissions.Any(seed => seed.Code == item.Code))
                .ToList();
            var allRolePermissions = await _db.Queryable<SysRolePermission>().ToListAsync();
            var adminPermissionCodes = allRolePermissions
                .Where(item => item.RoleGuid == adminRole.RoleGUID)
                .Select(item => item.PermissionCode)
                .ToList();

            Assert.Equal(PermissionSeedData.AllPermissions.Count(), allPermissionRows.Count);
            Assert.Equal(allPermissionRows.Count, allPermissionRows.Select(item => item.Code).Distinct().Count());
            Assert.Equal(16, attendanceRows.Count);
            Assert.Empty(adminPermissionCodes);
            Assert.All(PermissionSeedData.AttendancePermissions, seed =>
            {
                var row = Assert.Single(attendanceRows, item => item.Code == seed.Code);
                Assert.Equal(seed.Name, row.Name);
                Assert.Equal(seed.Category, row.Category);
                Assert.Equal(seed.Description, row.Description);
            });

            AssertRolePermissionsMatchTemplate(
                allRolePermissions,
                warehouseManagerRole.RoleGUID,
                "WarehouseManager"
            );
            AssertRolePermissionsMatchTemplate(
                allRolePermissions,
                managerRole.RoleGUID,
                "Manager"
            );
            AssertRolePermissionsMatchTemplate(
                allRolePermissions,
                userRole.RoleGUID,
                "User"
            );
            AssertRolePermissionsMatchTemplate(
                allRolePermissions,
                storeStaffRole.RoleGUID,
                "StoreStaff"
            );
            AssertRolePermissionsMatchTemplate(
                allRolePermissions,
                orderRole.RoleGUID,
                "Order"
            );

            var expectedStoreManagerPermissions = PermissionSeedData
                .RolePermissionTemplates.Single(template => template.RoleName == "StoreManager")
                .PermissionCodes
                .Append(Permissions.Users.Delete)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code);
            var actualStoreManagerPermissions = GetRolePermissionCodes(
                allRolePermissions,
                storeManagerRole.RoleGUID
            ).OrderBy(code => code);

            Assert.Equal(expectedStoreManagerPermissions, actualStoreManagerPermissions);
            Assert.Equal(
                allRolePermissions.Count,
                allRolePermissions
                    .Select(item => $"{item.RoleGuid}:{item.PermissionCode}".ToLowerInvariant())
                    .Distinct()
                    .Count()
            );
        }

        [Fact]
        public async Task InitializePermissionSeedsAsync_PersistsStoreFinancePermissionsWithoutAdminLinks()
        {
            await _db.Insertable(new Role
            {
                RoleGUID = "role-admin",
                RoleName = "Admin",
                Description = "System Administrator",
                IsActive = true,
            }).ExecuteCommandAsync();

            await CreateService().InitializePermissionSeedsAsync();

            var permissionCodes = await _db.Queryable<SysPermission>()
                .Where(item => item.Code == InstallmentOrdersPermission || item.Code == StoreVouchersPermission)
                .Select(item => item.Code)
                .ToListAsync();
            var adminPermissionCodes = await _db.Queryable<SysRolePermission>()
                .Where(item => item.RoleGuid == "role-admin")
                .Select(item => item.PermissionCode)
                .ToListAsync();

            Assert.Contains(InstallmentOrdersPermission, permissionCodes);
            Assert.Contains(StoreVouchersPermission, permissionCodes);
            Assert.Empty(adminPermissionCodes);
        }

        [Fact]
        public async Task InitializePermissionSeedsAsync_SeedsSeasonalCardCatalogIdempotently()
        {
            await _db.Insertable(new Role
            {
                RoleGUID = "role-store-manager",
                RoleName = "StoreManager",
                Description = "Store manager",
                IsActive = true,
            }).ExecuteCommandAsync();

            await CreateService().InitializePermissionSeedsAsync();
            await CreateService().InitializePermissionSeedsAsync();

            var catalogs = await _db.Queryable<SeasonalCardCatalog>()
                .OrderBy(item => item.SortOrder)
                .ToListAsync();

            Assert.Equal(20, catalogs.Count);
            Assert.Equal(20, catalogs.Select(item => item.CatalogCode).Distinct().Count());
            Assert.Equal(5, catalogs.Select(item => item.CardType).Distinct().Count());
            Assert.Equal(15, catalogs.Count(item => !item.AllowsCustomUnitPrice));
            Assert.Equal(5, catalogs.Count(item => item.AllowsCustomUnitPrice));
            Assert.All(catalogs, item => Assert.True(item.IsEnabled));
            Assert.Equal(
                new decimal?[] { 1m, 2m, 3m, null },
                catalogs
                    .Where(item => item.CardType == SeasonalCardType.Christmas)
                    .OrderBy(item => item.SortOrder)
                    .Select(item => item.FixedUnitPrice)
                    .ToArray()
            );
        }

        [Fact]
        public async Task InitializePermissionSeedsAsync_WhenPermissionExists_OverwritesMetadataFromSeed()
        {
            await _db.Insertable(new Role
            {
                RoleGUID = "role-admin",
                RoleName = "Admin",
                Description = "System Administrator",
                IsActive = true,
            }).ExecuteCommandAsync();
            await _db.Insertable(new SysPermission
            {
                Id = "existing-attendance-punch",
                Code = Permissions.Attendance.Punch.Self,
                Name = "已有名称",
                Category = "已有分类",
                Description = "已有说明",
            }).ExecuteCommandAsync();

            await CreateService().InitializePermissionSeedsAsync();

            var rows = await _db.Queryable<SysPermission>()
                .Where(item => item.Code == Permissions.Attendance.Punch.Self)
                .ToListAsync();

            var row = Assert.Single(rows);
            var seed = Assert.Single(
                PermissionSeedData.AllPermissions,
                item => item.Code == Permissions.Attendance.Punch.Self
            );
            Assert.Equal(seed.Name, row.Name);
            Assert.Equal(seed.Category, row.Category);
            Assert.Equal(seed.Description, row.Description);
        }

        [Fact]
        public async Task InitializePermissionSeedsAsync_NormalizesPermissionsWithoutDeletingCustomPermissions()
        {
            var role = CreateRole("role-store", "StoreStaff", "Store staff");
            await _db.Insertable(role).ExecuteCommandAsync();
            await _db.Insertable(
                new[]
                {
                    new SysPermission
                    {
                        Id = "soft-deleted-push-to-hq",
                        Code = Permissions.LocalPurchase.PushToHq,
                        Name = "旧名称",
                        Category = "旧分类",
                        Description = "旧说明",
                        IsDeleted = true,
                    },
                    new SysPermission
                    {
                        Id = "deprecated-pricing-root",
                        Code = "PricingStrategy",
                        Name = "定价策略",
                        Category = "定价策略管理",
                        Description = string.Empty,
                    },
                    new SysPermission
                    {
                        Id = "deprecated-local-invoice-view",
                        Code = "LocalInvocie.View",
                        Name = "澳洲进货单的管理 - 查看",
                        Category = "澳洲进货单的管理",
                        Description = "澳洲进货单的管理 - 查看",
                    },
                    new SysPermission
                    {
                        Id = "custom-permission",
                        Code = "Custom.Report.View",
                        Name = "自定义报表",
                        Category = "自定义",
                        Description = "后台手工创建的自定义权限",
                    },
                }
            ).ExecuteCommandAsync();
            await _db.Insertable(new SysRolePermission
            {
                Id = "legacy-role-permission",
                RoleGuid = role.RoleGUID,
                PermissionCode = "LocalInvocie.View",
            }).ExecuteCommandAsync();

            await CreateService().InitializePermissionSeedsAsync();

            var rows = await _db.Queryable<SysPermission>().ToListAsync();
            var pushToHq = Assert.Single(rows, item => item.Code == Permissions.LocalPurchase.PushToHq);
            var pushToHqSeed = Assert.Single(
                PermissionSeedData.AllPermissions,
                item => item.Code == Permissions.LocalPurchase.PushToHq
            );
            var rolePermission = await _db.Queryable<SysRolePermission>()
                .SingleAsync(item => item.Id == "legacy-role-permission");

            Assert.False(pushToHq.IsDeleted);
            Assert.Equal(pushToHqSeed.Name, pushToHq.Name);
            Assert.Equal(pushToHqSeed.Category, pushToHq.Category);
            Assert.Equal(pushToHqSeed.Description, pushToHq.Description);
            Assert.True(Assert.Single(rows, item => item.Code == "PricingStrategy").IsDeleted);
            Assert.True(Assert.Single(rows, item => item.Code == "LocalInvocie.View").IsDeleted);
            Assert.False(Assert.Single(rows, item => item.Code == "Custom.Report.View").IsDeleted);
            Assert.Equal("LocalInvocie.View", rolePermission.PermissionCode);
        }

        [Fact]
        public async Task InitializePermissionSeedsAsync_CollapsesDuplicatePermissionRowsIdempotently()
        {
            await _db.Insertable(
                new[]
                {
                    new SysPermission
                    {
                        Id = "dashboard-active",
                        Code = Permissions.Dashboard.View,
                        Name = "旧后台",
                        Category = "旧分类",
                        Description = "旧说明",
                    },
                    new SysPermission
                    {
                        Id = "dashboard-active-duplicate",
                        Code = Permissions.Dashboard.View,
                        Name = "重复后台",
                        Category = "重复分类",
                        Description = "重复说明",
                    },
                    new SysPermission
                    {
                        Id = "dashboard-deleted",
                        Code = Permissions.Dashboard.View,
                        Name = "后台管理",
                        Category = "后台管理",
                        Description = string.Empty,
                        IsDeleted = true,
                    },
                }
            ).ExecuteCommandAsync();

            await CreateService().InitializePermissionSeedsAsync();
            var dashboardRowsAfterFirstRun = await _db.Queryable<SysPermission>()
                .Where(item => item.Code == Permissions.Dashboard.View)
                .ToListAsync();
            Assert.Equal(2, dashboardRowsAfterFirstRun.Count);
            Assert.Single(dashboardRowsAfterFirstRun, item => !item.IsDeleted);
            Assert.Single(dashboardRowsAfterFirstRun, item => item.IsDeleted);

            await CreateService().InitializePermissionSeedsAsync();

            var dashboardRowsAfterSecondRun = await _db.Queryable<SysPermission>()
                .Where(item => item.Code == Permissions.Dashboard.View)
                .ToListAsync();
            var seed = Assert.Single(
                PermissionSeedData.AllPermissions,
                item => item.Code == Permissions.Dashboard.View
            );
            var dashboard = Assert.Single(dashboardRowsAfterSecondRun);

            Assert.False(dashboard.IsDeleted);
            Assert.Equal(seed.Name, dashboard.Name);
            Assert.Equal(seed.Category, dashboard.Category);
            Assert.Equal(seed.Description, dashboard.Description);
        }

        [Fact]
        public async Task CreateRoleSeedDataAsync_CreatesWarehouseManager_WithoutOverwritingExistingRoles()
        {
            await _db.Insertable(
                new[]
                {
                    CreateRole("existing-admin", "Admin", "custom admin", isActive: false),
                    CreateRole("existing-manager", "Manager", "custom manager", isActive: false),
                }
            ).ExecuteCommandAsync();

            await InvokePrivateAsync("CreateRoleSeedDataAsync");

            var roles = await _db.Queryable<Role>().ToListAsync();
            var roleNames = roles.Select(role => role.RoleName).ToList();
            var adminRole = Assert.Single(roles, role => role.RoleName == "Admin");
            var managerRole = Assert.Single(roles, role => role.RoleName == "Manager");
            var warehouseManagerRole = Assert.Single(
                roles,
                role => role.RoleName == "WarehouseManager"
            );

            Assert.Contains("User", roleNames);
            Assert.Contains("Order", roleNames);
            Assert.Contains("StoreManager", roleNames);
            Assert.Contains("StoreStaff", roleNames);
            Assert.Equal("custom admin", adminRole.Description);
            Assert.False(adminRole.IsActive);
            Assert.Equal("custom manager", managerRole.Description);
            Assert.False(managerRole.IsActive);
            Assert.True(warehouseManagerRole.IsActive);
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

        private SeedDataService CreateService()
        {
            return new SeedDataService(
                CreateSqlSugarContext(_db),
                NullLogger<SeedDataService>.Instance
            );
        }

        private static Role CreateRole(
            string roleGuid,
            string roleName,
            string? description,
            bool isActive = true
        )
        {
            return new Role
            {
                RoleGUID = roleGuid,
                RoleName = roleName,
                Description = description,
                IsActive = isActive,
            };
        }

        private static List<string> GetRolePermissionCodes(
            IEnumerable<SysRolePermission> allRolePermissions,
            string roleGuid
        )
        {
            return allRolePermissions
                .Where(item => item.RoleGuid == roleGuid)
                .Select(item => item.PermissionCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AssertRolePermissionsMatchTemplate(
            IEnumerable<SysRolePermission> allRolePermissions,
            string roleGuid,
            string roleName
        )
        {
            var expectedPermissions = PermissionSeedData
                .RolePermissionTemplates.Single(template => template.RoleName == roleName)
                .PermissionCodes
                .OrderBy(code => code);
            var actualPermissions = GetRolePermissionCodes(allRolePermissions, roleGuid).OrderBy(
                code => code
            );

            Assert.Equal(expectedPermissions, actualPermissions);
        }

        private static HashSet<string> GetPermissionConstantCodes()
        {
            return GetPermissionConstantCodes(typeof(Permissions))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> GetPermissionConstantCodes(Type type)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType == typeof(string) && field.IsLiteral && !field.IsInitOnly)
                {
                    yield return (string)field.GetRawConstantValue()!;
                }
            }

            foreach (var nestedType in type.GetNestedTypes(BindingFlags.Public))
            {
                foreach (var code in GetPermissionConstantCodes(nestedType))
                {
                    yield return code;
                }
            }
        }

        private async Task InvokePrivateAsync(string methodName)
        {
            var service = CreateService();
            var method = typeof(SeedDataService).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic
            );

            Assert.NotNull(method);

            var task = method!.Invoke(service, null) as Task;
            Assert.NotNull(task);
            await task!;
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
