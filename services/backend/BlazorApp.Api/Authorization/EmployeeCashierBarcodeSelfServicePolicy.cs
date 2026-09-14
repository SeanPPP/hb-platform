namespace BlazorApp.Api.Authorization;

public static class EmployeeCashierBarcodeSelfServicePolicy
{
    // 本人个人码是账号自助能力，不加入可分配的员工资料管理权限目录。
    public const string Name = "EmployeeProfiles.SelfCashierBarcode";
}
