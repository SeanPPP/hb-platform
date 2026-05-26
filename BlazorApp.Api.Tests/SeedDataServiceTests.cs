using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
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

            _db.CodeFirst.InitTables<Role, SysPermission, SysRolePermission>();
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
        public void AllPermissionSeeds_IncludeExistingDatabasePermissionCodes()
        {
            var seeds = PermissionSeedData.AllPermissions.ToList();

            Assert.Contains(seeds, seed => seed.Code == "AustralianSuppliers" && seed.Category == "澳洲供应商管理");
            Assert.Contains(seeds, seed => seed.Code == "ChinaProduct.View" && seed.Category == "国内订货");
            Assert.Contains(seeds, seed => seed.Code == "LocalInvocie.Edit" && seed.Category == "澳洲进货单的管理");
            Assert.Contains(seeds, seed => seed.Code == "LocalPurchase" && seed.Category == "澳洲本地进货管理");
            Assert.Contains(seeds, seed => seed.Code == "OrderFront" && seed.Category == "前台订货");
            Assert.Contains(seeds, seed => seed.Code == "Promotions" && seed.Category == "促销管理");
            Assert.Contains(seeds, seed => seed.Code == "StoreProducts" && seed.Category == "分店商品管理");
            Assert.Equal(seeds.Count, seeds.Select(seed => seed.Code.ToLowerInvariant()).Distinct().Count());
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
        public async Task InitializePermissionSeedsAsync_InsertsMissingPermissionsAndAdminLinksWithoutDuplicates()
        {
            await _db.Insertable(new Role
            {
                RoleGUID = "role-admin",
                RoleName = "Admin",
                Description = "System Administrator",
                IsActive = true,
            }).ExecuteCommandAsync();

            var service = CreateService();

            await service.InitializePermissionSeedsAsync();
            await service.InitializePermissionSeedsAsync();

            var allPermissionRows = await _db.Queryable<SysPermission>().ToListAsync();
            var attendanceRows = allPermissionRows
                .Where(item => PermissionSeedData.AttendancePermissions.Any(seed => seed.Code == item.Code))
                .ToList();
            var adminPermissionCodes = await _db.Queryable<SysRolePermission>()
                .Where(item => item.RoleGuid == "role-admin")
                .Select(item => item.PermissionCode)
                .ToListAsync();

            Assert.Equal(PermissionSeedData.AllPermissions.Count(), allPermissionRows.Count);
            Assert.Equal(allPermissionRows.Count, allPermissionRows.Select(item => item.Code).Distinct().Count());
            Assert.Equal(16, attendanceRows.Count);
            Assert.All(PermissionSeedData.AttendancePermissions, seed =>
            {
                var row = Assert.Single(attendanceRows, item => item.Code == seed.Code);
                Assert.Equal(seed.Name, row.Name);
                Assert.Equal(seed.Category, row.Category);
                Assert.Equal(seed.Description, row.Description);
                Assert.Contains(seed.Code, adminPermissionCodes);
            });
            Assert.Equal(adminPermissionCodes.Count, adminPermissionCodes.Distinct().Count());
        }

        [Fact]
        public async Task InitializePermissionSeedsAsync_AddsStoreFinancePermissionsToAdmin()
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
            Assert.Contains(InstallmentOrdersPermission, adminPermissionCodes);
            Assert.Contains(StoreVouchersPermission, adminPermissionCodes);
        }

        [Fact]
        public async Task InitializePermissionSeedsAsync_WhenPermissionExists_DoesNotOverwriteMetadata()
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
            Assert.Equal("已有名称", row.Name);
            Assert.Equal("已有分类", row.Category);
            Assert.Equal("已有说明", row.Description);
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
