namespace BlazorApp.Shared.Constants
{
    public sealed record PermissionSeedDefinition(
        string Code,
        string Name,
        string Category,
        string Description
    );

    public sealed record RolePermissionTemplateDefinition(
        string RoleName,
        IReadOnlyList<string> PermissionCodes
    );

    public static class PermissionSeedData
    {
        public static IReadOnlyList<PermissionSeedDefinition> AttendancePermissions { get; } =
            new List<PermissionSeedDefinition>
            {
                new(Permissions.Attendance.Schedule.ViewSelf, "查看自己的排班", "排班考勤", "页面 /pos-admin/schedule-attendance - 查看自己的排班"),
                new(Permissions.Attendance.Schedule.ViewStore, "查看相关分店排班", "排班考勤", "页面 /pos-admin/schedule-attendance - 查看相关分店排班"),
                new(Permissions.Attendance.Schedule.EditManagedStore, "编辑管理分店排班", "排班考勤", "页面 /pos-admin/schedule-attendance - 编辑管理分店排班"),
                new(Permissions.Attendance.Availability.SubmitSelf, "上报自己的可上班时间", "排班考勤", "页面 /pos-admin/schedule-attendance - 上报自己的可上班时间"),
                new(Permissions.Attendance.Availability.ViewManagedStore, "查看管理分店可上班时间", "排班考勤", "页面 /pos-admin/schedule-attendance - 查看管理分店可上班时间"),
                new(Permissions.Attendance.Punch.Self, "本人打卡", "排班考勤", "页面 /pos-admin/schedule-attendance - 本人打卡"),
                new(Permissions.Attendance.Punch.ViewManagedStore, "查看管理分店打卡记录", "排班考勤", "页面 /pos-admin/schedule-attendance - 查看管理分店打卡记录"),
                new(Permissions.Attendance.Approval.ViewManagedStore, "查看管理分店审核记录", "排班考勤", "页面 /pos-admin/schedule-attendance - 查看管理分店审核记录"),
                new(Permissions.Attendance.Approval.ReviewManagedStore, "审核管理分店考勤", "排班考勤", "页面 /pos-admin/schedule-attendance - 审核管理分店考勤"),
                new(Permissions.Attendance.Holiday.ViewStore, "查看分店公共假期", "排班考勤", "页面 /pos-admin/schedule-attendance - 查看分店公共假期"),
                new(Permissions.Attendance.Holiday.EditManagedStore, "编辑管理分店公共假期", "排班考勤", "页面 /pos-admin/schedule-attendance - 编辑管理分店公共假期"),
                new(Permissions.Attendance.Leave.ApplySelf, "本人提交请假申请", "排班考勤", "页面 /pos-admin/schedule-attendance - 本人提交请假申请"),
                new(Permissions.Attendance.Leave.ViewManagedStore, "查看管理分店请假申请", "排班考勤", "页面 /pos-admin/schedule-attendance - 查看管理分店请假申请"),
                new(Permissions.Attendance.Leave.ReviewManagedStore, "审核管理分店请假申请", "排班考勤", "页面 /pos-admin/schedule-attendance - 审核管理分店请假申请"),
                new(Permissions.Attendance.Settings.Edit, "编辑考勤设置", "排班考勤", "页面 /pos-admin/schedule-attendance - 编辑考勤设置"),
                new(Permissions.Attendance.Admin.View, "查看全部考勤管理", "排班考勤", "页面 /pos-admin/schedule-attendance - 查看全部考勤管理"),
            };

        public static IReadOnlyList<PermissionSeedDefinition> SeasonalCardPermissions { get; } =
            new List<PermissionSeedDefinition>
            {
                new(
                    Permissions.SeasonalCards.Remaining.ViewManagedStore,
                    "查看管理分店季节卡剩余",
                    "季节卡片",
                    "页面 /seasonal-cards - 查看管理分店季节卡剩余上报记录"
                ),
                new(
                    Permissions.SeasonalCards.Remaining.SubmitManagedStore,
                    "提交管理分店季节卡剩余",
                    "季节卡片",
                    "页面 /seasonal-cards - 提交管理分店季节卡剩余"
                ),
            };

        private static IReadOnlyList<string> AttendanceSelfServicePermissionCodes { get; } =
            new[]
            {
                Permissions.Attendance.Schedule.ViewSelf,
                Permissions.Attendance.Availability.SubmitSelf,
                Permissions.Attendance.Punch.Self,
                Permissions.Attendance.Leave.ApplySelf,
            };

        public static IReadOnlySet<string> DeprecatedPermissionCodes { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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

        private static IReadOnlyList<PermissionSeedDefinition> SharedPermissionSeeds { get; } =
            new List<PermissionSeedDefinition>
            {
                new(Permissions.Users.View, "查看用户", "用户管理", "页面 /system/users - 查看用户列表与详情"),
                new(Permissions.Users.Create, "创建用户", "用户管理", "页面 /system/users - 创建后台用户"),
                new(Permissions.Users.Edit, "编辑用户", "用户管理", "页面 /system/users - 编辑用户基础信息"),
                new(Permissions.Users.Delete, "删除用户", "用户管理", "页面 /system/users - 删除或停用用户"),
                new(Permissions.Users.ManageRoles, "管理用户角色", "用户管理", "页面 /system/users - 分配或移除用户角色"),
                new(Permissions.Users.ManageStores, "管理用户分店", "用户管理", "页面 /system/users - 维护用户关联分店"),
                new(Permissions.Users.ResetPassword, "重置密码", "用户管理", "页面 /system/users - 重置用户登录密码"),
                new(Permissions.EmployeeProfiles.View, "查看员工个人信息", "用户管理", "页面 /system/employee-profiles - 查看员工个人信息维护列表与详情"),
                new(Permissions.EmployeeProfiles.Edit, "维护员工个人信息", "用户管理", "页面 /system/employee-profiles - 编辑员工身份、银行、养老金、地址等资料"),
                new(Permissions.Roles.View, "查看角色", "角色管理", "页面 /system/roles 与 /system/permissions - 查看角色和权限配置"),
                new(Permissions.Roles.Create, "创建角色", "角色管理", "页面 /system/roles - 创建角色"),
                new(Permissions.Roles.Edit, "编辑角色", "角色管理", "页面 /system/roles - 编辑角色基础信息"),
                new(Permissions.Roles.Delete, "删除角色", "角色管理", "页面 /system/roles - 删除角色"),
                new(Permissions.Roles.ManagePermissions, "管理角色权限", "角色管理", "页面 /system/permissions - 分配或移除角色权限"),
                new(Permissions.Roles.ManageUsers, "管理角色用户", "角色管理", "页面 /system/roles - 管理角色关联用户"),
                new(Permissions.Stores.View, "查看分店", "分店管理", "页面 /system/stores - 查看分店列表与详情"),
                new(Permissions.Stores.Create, "创建分店", "分店管理", "页面 /system/stores - 创建分店"),
                new(Permissions.Stores.Edit, "编辑分店", "分店管理", "页面 /system/stores - 编辑分店资料"),
                new(Permissions.Stores.Delete, "删除分店", "分店管理", "页面 /system/stores - 删除分店"),
                new(Permissions.Stores.Sync, "同步分店数据", "分店管理", "页面 /system/stores - 同步分店数据"),
                new(Permissions.Products.View, "查看商品", "商品管理", "页面 /domestic-purchase/domestic-products - 查看国内商品"),
                new(Permissions.Products.Create, "创建商品", "商品管理", "商品管理 - 创建商品"),
                new(Permissions.Products.Edit, "编辑商品", "商品管理", "商品管理 - 编辑商品"),
                new(Permissions.Products.Delete, "删除商品", "商品管理", "商品管理 - 删除商品"),
                new(Permissions.Orders.View, "查看订单", "订单管理", "页面 /pos-admin/sales-orders - 查看收银记录"),
                new(Permissions.Orders.Create, "创建订单", "订单管理", "订单管理 - 创建订单"),
                new(Permissions.Orders.Edit, "编辑订单", "订单管理", "订单管理 - 编辑订单"),
                new(Permissions.Orders.Delete, "删除订单", "订单管理", "订单管理 - 删除订单"),
                new(Permissions.InstallmentOrders.View, "查看分期付款订单", "分店财务", "分店财务 - 查看分店分期付款订单与支付记录"),
                new(Permissions.StoreVouchers.View, "查看分店代金券", "分店财务", "分店财务 - 查看分店代金券使用情况与关联订单"),
                new(Permissions.Container.View, "查看货柜", "货柜管理", "页面 /warehouse/containers - 查看货柜列表与明细"),
                new(Permissions.Container.Create, "创建货柜", "货柜管理", "页面 /warehouse/containers - 创建货柜"),
                new(Permissions.Container.Edit, "编辑货柜", "货柜管理", "页面 /warehouse/containers - 编辑货柜"),
                new(Permissions.Container.Delete, "删除货柜", "货柜管理", "页面 /warehouse/containers - 删除货柜"),
                new(Permissions.Warehouse.View, "查看仓库", "仓库管理", "页面 /warehouse - 查看仓库模块"),
                new(Permissions.Warehouse.Manage, "管理仓库", "仓库管理", "页面 /warehouse - 管理仓库模块"),
                new(Permissions.Warehouse.ManageProducts, "管理仓库商品", "仓库管理", "页面 /warehouse/products 与 /warehouse/product-grade-management - 管理仓库商品和等级"),
                new(Permissions.Warehouse.ManageCategories, "管理仓库分类", "仓库管理", "页面 /warehouse/categories - 管理仓库分类"),
                new(Permissions.Warehouse.ManageLocations, "管理仓库标签", "仓库管理", "页面 /warehouse/locations - 管理仓库标签"),
                new(Permissions.Warehouse.ManageOrders, "管理仓库订货", "仓库管理", "页面 /warehouse/store-orders - 管理分店订货、明细、配货单和发票"),
                new(Permissions.DomesticPurchase.View, "查看国内采购", "国内采购", "页面 /domestic-purchase - 查看国内采购模块"),
                new(Permissions.DomesticPurchase.ManageSuppliers, "管理国内供应商", "国内采购", "页面 /domestic-purchase/china-suppliers - 管理国内供应商"),
                new(Permissions.DomesticPurchase.ManageProducts, "管理国内商品", "国内采购", "页面 /domestic-purchase/product-creation 与 /product-import - 创建和导入商品"),
                new(Permissions.DomesticPurchase.ManagePrefixCodes, "管理前缀码", "国内采购", "页面 /domestic-purchase/prefix-code-management - 管理商品前缀码"),
                new(Permissions.Prices.View, "查看价格", "价格管理", "价格管理 - 查看价格"),
                new(Permissions.Prices.Modify, "修改价格", "价格管理", "价格管理 - 修改价格"),
                new(Permissions.Prices.Delete, "删除价格", "价格管理", "价格管理 - 删除价格"),
                new(Permissions.Reports.View, "查看报表", "报表", "页面 /executive-sales-intelligence - 查看销售看板和销售明细"),
                new(Permissions.Reports.Export, "导出数据", "报表", "页面 /executive-sales-intelligence - 导出销售报表数据"),
                new(Permissions.StoreProducts.View, "查看分店商品", "分店商品管理", "页面 /pos-admin/store-product-price - 查看分店商品价格"),
                new(Permissions.StoreProducts.Create, "创建分店商品", "分店商品管理", "页面 /pos-admin/store-product-price - 创建分店商品价格"),
                new(Permissions.StoreProducts.Edit, "编辑分店商品", "分店商品管理", "页面 /pos-admin/store-product-price - 编辑分店商品价格"),
                new(Permissions.PosProducts.View, "查看 POS 商品管理", "POS 管理", "页面 /pos-admin/products - 查看 POS 商品、分类、套装码、同步和完整性检查入口"),
                new(Permissions.PosProducts.Manage, "管理 POS 商品", "POS 管理", "页面 /pos-admin/products - 编辑 POS 商品、批量改价、同步总部/分店、维护分类/套装码、执行完整性修复"),
                new(Permissions.Promotions.View, "查看促销", "促销管理", "页面 /pos-admin/promotions - 查看促销活动"),
                new(Permissions.Promotions.Edit, "编辑促销", "促销管理", "页面 /pos-admin/promotions - 编辑促销活动"),
                new(Permissions.Advertisements.View, "查看广告素材", "广告管理", "页面 /pos-admin/advertisements - 查看广告素材列表与详情"),
                new(Permissions.Advertisements.Edit, "编辑广告素材", "广告管理", "页面 /pos-admin/advertisements - 新增、编辑、删除、启用与上传广告素材"),
                new(Permissions.PricingStrategy.View, "查看定价策略", "定价策略", "页面 /pos-admin/pricing-strategies - 查看自动价格策略"),
                new(Permissions.PricingStrategy.Edit, "编辑定价策略", "定价策略", "页面 /pos-admin/pricing-strategies - 编辑自动价格策略"),
                new(Permissions.DeviceRegistration.View, "查看设备注册", "系统管理", "页面 /system/device-registration - 查看 POS 设备注册列表与状态"),
                new(Permissions.DeviceRegistration.Manage, "管理设备注册", "系统管理", "页面 /system/device-registration - 审核、维护或管理设备注册"),
                new(Permissions.LocalPurchase.View, "查看本地进货", "本地进货管理", "页面 /pos-admin/local-supplier-invoices - 查看分店进货单列表与详情"),
                new(Permissions.LocalPurchase.Edit, "编辑本地进货", "本地进货管理", "页面 /pos-admin/local-supplier-invoices - 新增、编辑、提交和维护分店进货单"),
                new(Permissions.LocalPurchase.PushToHq, "推送本地进货到 HQ", "本地进货管理", "页面 /pos-admin/local-supplier-invoices - 推送本地进货单到 HQ"),
                new(Permissions.AustralianSuppliers.View, "查看澳洲供应商", "澳洲供应商", "页面 /pos-admin/suppliers - 查看供应商列表与详情"),
                new(Permissions.AustralianSuppliers.Edit, "编辑澳洲供应商", "澳洲供应商", "页面 /pos-admin/suppliers - 编辑供应商资料"),
                new(Permissions.Store.ManageOperations, "管理分店运营", "分店运营", "页面 /pos-admin/cash-register-users - 管理收银用户条码"),
                new(Permissions.Store.ManageInfo, "管理分店信息", "分店运营", "分店运营 - 管理分店信息"),
                new(Permissions.System.ViewLogs, "查看日志", "系统管理", "系统管理 - 查看日志"),
                new(Permissions.System.ManageSettings, "管理设置", "系统管理", "系统管理 - 管理设置"),
                new(Permissions.Dashboard.View, "访问后台", "后台管理", "页面 /dashboard - 访问后台工作台"),
                new(Permissions.OrderFront.View, "前台订货", "前台订货", "前台订货 - 前台订货"),
            };

        public static IReadOnlyList<RolePermissionTemplateDefinition> RolePermissionTemplates { get; } =
            new List<RolePermissionTemplateDefinition>
            {
                new("Admin", Array.Empty<string>()),
                new(
                    "WarehouseManager",
                    new[]
                    {
                        Permissions.Stores.View,
                        Permissions.Stores.Create,
                        Permissions.Stores.Edit,
                        Permissions.Stores.Delete,
                        Permissions.Stores.Sync,
                        Permissions.Products.View,
                        Permissions.Products.Create,
                        Permissions.Products.Edit,
                        Permissions.Products.Delete,
                        Permissions.Orders.View,
                        Permissions.Orders.Create,
                        Permissions.Orders.Edit,
                        Permissions.Orders.Delete,
                        Permissions.Container.View,
                        Permissions.Container.Create,
                        Permissions.Container.Edit,
                        Permissions.Container.Delete,
                        Permissions.Warehouse.View,
                        Permissions.Warehouse.Manage,
                        Permissions.Warehouse.ManageProducts,
                        Permissions.Warehouse.ManageCategories,
                        Permissions.Warehouse.ManageLocations,
                        Permissions.Warehouse.ManageOrders,
                        Permissions.DomesticPurchase.View,
                        Permissions.DomesticPurchase.ManageSuppliers,
                        Permissions.DomesticPurchase.ManageProducts,
                        Permissions.DomesticPurchase.ManagePrefixCodes,
                        Permissions.Reports.View,
                        Permissions.Reports.Export,
                        Permissions.Dashboard.View,
                    }
                ),
                new(
                    "StoreManager",
                    new[]
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
                        Permissions.SeasonalCards.Remaining.ViewManagedStore,
                        Permissions.SeasonalCards.Remaining.SubmitManagedStore,
                        Permissions.DeviceRegistration.View,
                        Permissions.DeviceRegistration.Manage,
                    }
                ),
                new("Manager", AttendanceSelfServicePermissionCodes),
                new("User", AttendanceSelfServicePermissionCodes),
                new("StoreStaff", AttendanceSelfServicePermissionCodes),
                new("Order", AttendanceSelfServicePermissionCodes),
            };

        public static IReadOnlyList<PermissionSeedDefinition> AllPermissions { get; } =
            SharedPermissionSeeds
            .Concat(AttendancePermissions)
            .Concat(SeasonalCardPermissions)
            .GroupBy(seed => seed.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
    }
}
