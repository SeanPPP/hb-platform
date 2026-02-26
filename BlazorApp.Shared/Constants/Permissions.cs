namespace BlazorApp.Shared.Constants
{
    public static class Permissions
    {
        public static class Users
        {
            public const string View = "Users.View";
            public const string Create = "Users.Create";
            public const string Edit = "Users.Edit";
            public const string Delete = "Users.Delete";
        }

        public static class Roles
        {
            public const string View = "Roles.View";
            public const string Create = "Roles.Create";
            public const string Edit = "Roles.Edit";
            public const string Delete = "Roles.Delete";
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

            // 角色管理
            yield return (Roles.View, "查看角色", "角色管理");
            yield return (Roles.Create, "创建角色", "角色管理");
            yield return (Roles.Edit, "编辑角色", "角色管理");
            yield return (Roles.Delete, "删除角色", "角色管理");

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
        }
    }
}
