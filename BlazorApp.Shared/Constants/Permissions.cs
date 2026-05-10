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

        public static class System
        {
            public const string ViewLogs = "System.ViewLogs";
            public const string ManageSettings = "System.ManageSettings";
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

            // 系统管理
            yield return (System.ViewLogs, "查看日志", "系统管理");
            yield return (System.ManageSettings, "管理设置", "系统管理");
        }
    }
}
