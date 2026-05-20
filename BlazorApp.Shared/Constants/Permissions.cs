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

        /// <summary>
        /// WarehouseManager role-level permission grants.
        /// Keep this list in sync with hbweb_rv/src/types/permissions.ts.
        /// </summary>
        public static bool IsWarehouseManagerGranted(string? permission)
        {
            return !string.IsNullOrWhiteSpace(permission)
                && WarehouseManagerGrantedPermissions.Contains(permission);
        }

        /// <summary>
        /// 获取所有权限列表 (用于种子数据初始化)
        /// </summary>
        public static IEnumerable<(string Code, string Name, string Category)> GetAllPermissions()
        {
            // 用户管理
            yield return (Users.View, "查看用户", "用户管理");
            yield return (Users.Create, "创建用户", "用户管理");
            yield return (Users.Edit, "编辑用户", "用户管理");
            yield return (Users.Delete, "删除用户", "用户管理");
            yield return (Users.ManageRoles, "管理用户角色", "用户管理");
            yield return (Users.ManageStores, "管理用户分店", "用户管理");
            yield return (Users.ResetPassword, "重置密码", "用户管理");
            yield return (EmployeeProfiles.View, "查看员工个人信息", "用户管理");
            yield return (EmployeeProfiles.Edit, "维护员工个人信息", "用户管理");

            // 角色管理
            yield return (Roles.View, "查看角色", "角色管理");
            yield return (Roles.Create, "创建角色", "角色管理");
            yield return (Roles.Edit, "编辑角色", "角色管理");
            yield return (Roles.Delete, "删除角色", "角色管理");
            yield return (Roles.ManagePermissions, "管理角色权限", "角色管理");
            yield return (Roles.ManageUsers, "管理角色用户", "角色管理");

            // 分店管理
            yield return (Stores.View, "查看分店", "分店管理");
            yield return (Stores.Create, "创建分店", "分店管理");
            yield return (Stores.Edit, "编辑分店", "分店管理");
            yield return (Stores.Delete, "删除分店", "分店管理");
            yield return (Stores.Sync, "同步分店数据", "分店管理");

            // 商品管理
            yield return (Products.View, "查看商品", "商品管理");
            yield return (Products.Create, "创建商品", "商品管理");
            yield return (Products.Edit, "编辑商品", "商品管理");
            yield return (Products.Delete, "删除商品", "商品管理");

            // 订单管理
            yield return (Orders.View, "查看订单", "订单管理");
            yield return (Orders.Create, "创建订单", "订单管理");
            yield return (Orders.Edit, "编辑订单", "订单管理");
            yield return (Orders.Delete, "删除订单", "订单管理");

            // 货柜管理
            yield return (Container.View, "查看货柜", "货柜管理");
            yield return (Container.Create, "创建货柜", "货柜管理");
            yield return (Container.Edit, "编辑货柜", "货柜管理");
            yield return (Container.Delete, "删除货柜", "货柜管理");

            // 仓库管理
            yield return (Warehouse.View, "查看仓库", "仓库管理");
            yield return (Warehouse.Manage, "管理仓库", "仓库管理");
            yield return (Warehouse.ManageProducts, "管理仓库商品", "仓库管理");
            yield return (Warehouse.ManageCategories, "管理仓库分类", "仓库管理");
            yield return (Warehouse.ManageLocations, "管理仓库标签", "仓库管理");
            yield return (Warehouse.ManageOrders, "管理仓库订货", "仓库管理");

            // 国内采购
            yield return (DomesticPurchase.View, "查看国内采购", "国内采购");
            yield return (DomesticPurchase.ManageSuppliers, "管理国内供应商", "国内采购");
            yield return (DomesticPurchase.ManageProducts, "管理国内商品", "国内采购");
            yield return (DomesticPurchase.ManagePrefixCodes, "管理前缀码", "国内采购");

            // 价格管理
            yield return (Prices.View, "查看价格", "价格管理");
            yield return (Prices.Modify, "修改价格", "价格管理");
            yield return (Prices.Delete, "删除价格", "价格管理");

            // 报表
            yield return (Reports.View, "查看报表", "报表");
            yield return (Reports.Export, "导出数据", "报表");

            // 分店商品管理
            yield return (StoreProducts.View, "查看分店商品", "分店商品管理");
            yield return (StoreProducts.Create, "创建分店商品", "分店商品管理");
            yield return (StoreProducts.Edit, "编辑分店商品", "分店商品管理");

            // 促销管理
            yield return (Promotions.View, "查看促销", "促销管理");
            yield return (Promotions.Edit, "编辑促销", "促销管理");

            // 定价策略
            yield return (PricingStrategy.View, "查看定价策略", "定价策略");
            yield return (PricingStrategy.Edit, "编辑定价策略", "定价策略");

            // 本地进货
            yield return (LocalPurchase.View, "查看本地进货", "本地进货管理");
            yield return (LocalPurchase.Edit, "编辑本地进货", "本地进货管理");

            // 澳洲供应商
            yield return (AustralianSuppliers.View, "查看澳洲供应商", "澳洲供应商");
            yield return (AustralianSuppliers.Edit, "编辑澳洲供应商", "澳洲供应商");

            // 分店运营
            yield return (Store.ManageOperations, "管理分店运营", "分店运营");
            yield return (Store.ManageInfo, "管理分店信息", "分店运营");

            // 系统管理
            yield return (System.ViewLogs, "查看日志", "系统管理");
            yield return (System.ManageSettings, "管理设置", "系统管理");

            // 后台管理
            yield return (Dashboard.View, "访问后台", "后台管理");
        }
    }
}
