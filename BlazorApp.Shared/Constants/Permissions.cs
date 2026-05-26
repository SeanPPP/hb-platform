namespace BlazorApp.Shared.Constants
{
    /// <summary>
    /// 系统权限常量定义
    /// 命名规范: {模块}.{操作} (PascalCase)
    /// 前后端共享此规范，前端 hasPermission('Users.View') 对齐后端 [Authorize(Policy = "Users.View")]
    /// </summary>
    public static class Permissions
    {
        public static class Users
        {
            public const string View = "Users.View";
            public const string Create = "Users.Create";
            public const string Edit = "Users.Edit";
            public const string Delete = "Users.Delete";
            public const string ManageRoles = "Users.ManageRoles";
            public const string ManageStores = "Users.ManageStores";
            public const string ResetPassword = "Users.ResetPassword";
        }

        public static class EmployeeProfiles
        {
            public const string View = "EmployeeProfiles.View";
            public const string Edit = "EmployeeProfiles.Edit";
        }

        public static class Roles
        {
            public const string View = "Roles.View";
            public const string Create = "Roles.Create";
            public const string Edit = "Roles.Edit";
            public const string Delete = "Roles.Delete";
            public const string ManagePermissions = "Roles.ManagePermissions";
            public const string ManageUsers = "Roles.ManageUsers";
        }

        public static class Stores
        {
            public const string View = "Stores.View";
            public const string Create = "Stores.Create";
            public const string Edit = "Stores.Edit";
            public const string Delete = "Stores.Delete";
            public const string Sync = "Stores.Sync";
        }

        public static class Products
        {
            public const string View = "Products.View";
            public const string Create = "Products.Create";
            public const string Edit = "Products.Edit";
            public const string Delete = "Products.Delete";
        }

        public static class Orders
        {
            public const string View = "Orders.View";
            public const string Create = "Orders.Create";
            public const string Edit = "Orders.Edit";
            public const string Delete = "Orders.Delete";
        }

        public static class InstallmentOrders
        {
            public const string View = "InstallmentOrders.View";
        }

        public static class StoreVouchers
        {
            public const string View = "StoreVouchers.View";
        }

        public static class Container
        {
            public const string View = "Container.View";
            public const string Create = "Container.Create";
            public const string Edit = "Container.Edit";
            public const string Delete = "Container.Delete";
        }

        public static class Warehouse
        {
            public const string View = "Warehouse.View";
            public const string Manage = "Warehouse.Manage";
            public const string ManageProducts = "Warehouse.ManageProducts";
            public const string ManageCategories = "Warehouse.ManageCategories";
            public const string ManageLocations = "Warehouse.ManageLocations";
            public const string ManageOrders = "Warehouse.ManageOrders";
        }

        public static class DomesticPurchase
        {
            public const string View = "DomesticPurchase.View";
            public const string ManageSuppliers = "DomesticPurchase.ManageSuppliers";
            public const string ManageProducts = "DomesticPurchase.ManageProducts";
            public const string ManagePrefixCodes = "DomesticPurchase.ManagePrefixCodes";
        }

        public static class Prices
        {
            public const string View = "Prices.View";
            public const string Modify = "Prices.Modify";
            public const string Delete = "Prices.Delete";
        }

        public static class Reports
        {
            public const string View = "Reports.View";
            public const string Export = "Reports.Export";
        }

        public static class StoreProducts
        {
            public const string View = "StoreProducts.View";
            public const string Create = "StoreProducts.Create";
            public const string Edit = "StoreProducts.Edit";
        }

        public static class Promotions
        {
            public const string View = "Promotions.View";
            public const string Edit = "Promotions.Edit";
        }

        public static class PricingStrategy
        {
            public const string View = "PricingStrategy.View";
            public const string Edit = "PricingStrategy.Edit";
        }

        public static class LocalPurchase
        {
            public const string View = "LocalPurchase.View";
            public const string Edit = "LocalPurchase.Edit";
        }

        public static class AustralianSuppliers
        {
            public const string View = "AustralianSuppliers.View";
            public const string Edit = "AustralianSuppliers.Edit";
        }

        public static class Store
        {
            public const string ManageOperations = "Store.ManageOperations";
            public const string ManageInfo = "Store.ManageInfo";
        }

        public static class Attendance
        {
            public static class Schedule
            {
                public const string ViewSelf = "Attendance.Schedule.ViewSelf";
                public const string ViewStore = "Attendance.Schedule.ViewStore";
                public const string EditManagedStore = "Attendance.Schedule.EditManagedStore";
            }

            public static class Availability
            {
                public const string SubmitSelf = "Attendance.Availability.SubmitSelf";
                public const string ViewManagedStore = "Attendance.Availability.ViewManagedStore";
            }

            public static class Punch
            {
                public const string Self = "Attendance.Punch.Self";
                public const string ViewManagedStore = "Attendance.Punch.ViewManagedStore";
            }

            public static class Approval
            {
                public const string ViewManagedStore = "Attendance.Approval.ViewManagedStore";
                public const string ReviewManagedStore = "Attendance.Approval.ReviewManagedStore";
            }

            public static class Holiday
            {
                public const string ViewStore = "Attendance.Holiday.ViewStore";
                public const string EditManagedStore = "Attendance.Holiday.EditManagedStore";
            }

            public static class Leave
            {
                public const string ApplySelf = "Attendance.Leave.ApplySelf";
                public const string ViewManagedStore = "Attendance.Leave.ViewManagedStore";
                public const string ReviewManagedStore = "Attendance.Leave.ReviewManagedStore";
            }

            public static class Settings
            {
                public const string Edit = "Attendance.Settings.Edit";
            }

            public static class Admin
            {
                public const string View = "Attendance.Admin.View";
            }
        }

        public static class System
        {
            public const string ViewLogs = "System.ViewLogs";
            public const string ManageSettings = "System.ManageSettings";
        }

        public static class Dashboard
        {
            public const string View = "Dashboard";
        }

        private static readonly HashSet<string> WarehouseManagerGrantedPermissions = new(
            StringComparer.OrdinalIgnoreCase
        )
        {
            Stores.View,
            Stores.Create,
            Stores.Edit,
            Stores.Delete,
            Stores.Sync,
            Products.View,
            Products.Create,
            Products.Edit,
            Products.Delete,
            Orders.View,
            Orders.Create,
            Orders.Edit,
            Orders.Delete,
            Container.View,
            Container.Create,
            Container.Edit,
            Container.Delete,
            Warehouse.View,
            Warehouse.Manage,
            Warehouse.ManageProducts,
            Warehouse.ManageCategories,
            Warehouse.ManageLocations,
            Warehouse.ManageOrders,
            DomesticPurchase.View,
            DomesticPurchase.ManageSuppliers,
            DomesticPurchase.ManageProducts,
            DomesticPurchase.ManagePrefixCodes,
            Reports.View,
            Reports.Export,
            Dashboard.View,
        };

        private static readonly HashSet<string> AttendanceSelfServicePermissions = new(
            StringComparer.OrdinalIgnoreCase
        )
        {
            Attendance.Schedule.ViewSelf,
            Attendance.Availability.SubmitSelf,
            Attendance.Punch.Self,
            Attendance.Leave.ApplySelf,
        };

        private static readonly HashSet<string> StoreManagerGrantedPermissions = new(
            StringComparer.OrdinalIgnoreCase
        )
        {
            Attendance.Schedule.ViewSelf,
            Attendance.Schedule.ViewStore,
            Attendance.Schedule.EditManagedStore,
            Attendance.Availability.SubmitSelf,
            Attendance.Availability.ViewManagedStore,
            Attendance.Punch.Self,
            Attendance.Punch.ViewManagedStore,
            Attendance.Approval.ViewManagedStore,
            Attendance.Approval.ReviewManagedStore,
            Attendance.Holiday.ViewStore,
            Attendance.Holiday.EditManagedStore,
            Attendance.Leave.ApplySelf,
            Attendance.Leave.ViewManagedStore,
            Attendance.Leave.ReviewManagedStore,
        };

        /// <summary>
        /// WarehouseManager role-level permission grants.
        /// Keep this list in sync with hbweb_rv/src/types/permissions.ts.
        /// </summary>
        public static bool IsWarehouseManagerGranted(string? permission)
        {
            return !string.IsNullOrWhiteSpace(permission)
                && WarehouseManagerGrantedPermissions.Contains(permission);
        }

        public static bool IsAttendanceSelfServiceGranted(string? permission)
        {
            return !string.IsNullOrWhiteSpace(permission)
                && AttendanceSelfServicePermissions.Contains(permission);
        }

        public static bool IsStoreManagerGranted(string? permission)
        {
            return !string.IsNullOrWhiteSpace(permission)
                && StoreManagerGrantedPermissions.Contains(permission);
        }

        public static IEnumerable<(string Code, string Name, string Category)> GetAllPermissions() =>
            PermissionSeedData.AllPermissions.Select(seed => (seed.Code, seed.Name, seed.Category));
    }
}
