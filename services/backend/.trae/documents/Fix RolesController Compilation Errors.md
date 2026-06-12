我已经找到了 `RolesController.cs` 编译报错的原因。`RoleService` 的方法已经返回了 `ApiResponse<T>` 对象，但在 Controller 中又尝试用 `ApiResponse<T>.OK(...)` 再次封装，导致了类型不匹配。

以下是修复计划：

# 修复 RolesController 编译错误

## 技术实现
### 修改 `BlazorApp.Api/Controllers/RolesController.cs`
1.  **修正 `GetSysPermissions` 方法**:
    -   将 `return Ok(ApiResponse<List<SysPermission>>.OK(result));` 修改为 `return Ok(result);`。
2.  **修正 `GetPermissionRoles` 方法**:
    -   将 `return Ok(ApiResponse<List<RoleDto>>.OK(result));` 修改为 `return Ok(result);`。
3.  **修正 `AssignRolesToPermission` 方法**:
    -   将 `return Ok(ApiResponse<bool>.OK(result));` 修改为 `return Ok(result);`。

## 验证
1.  执行 `dotnet build` 确保后端项目编译通过。
