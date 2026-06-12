# HB Platform 用户和角色管理系统 - 后端API文档

## 系统概述

**用户和角色管理系统** - 基于 StoresController 架构模式创建的完整用户权限管理体系
- 🧑‍💼 完整的用户生命周期管理
- 🔐 基于角色的权限控制 (RBAC)
- 📋 支持分页、搜索、筛选、批量操作
- 🏢 与分店管理系统无缝集成
- 📊 丰富的统计和报表功能

## 架构设计

### 1. 分层架构
```
Controllers (API层)
    ↓
Services (业务逻辑层)
    ↓
Models & DTOs (数据传输层)
    ↓
SqlSugar ORM (数据访问层)
```

### 2. 核心组件

#### 用户管理 (Users)
- **UsersController** - 用户管理API控制器
- **IUserService** - 用户服务接口
- **UserService** - 用户服务实现
- **UserDtos** - 用户相关数据传输对象

#### 角色管理 (Roles)
- **RolesController** - 角色管理API控制器 (待创建)
- **IRoleService** - 角色服务接口
- **RoleService** - 角色服务实现 (待创建)
- **RoleDtos** - 角色相关数据传输对象

### 3. 数据模型关系
```
User ←→ UserRole ←→ Role
User ←→ UserStore ←→ Store
User → RefreshToken
```

## 用户管理 API (UsersController)

### 基础用户操作

#### GET /api/users
**获取用户列表**
- 支持分页、搜索、筛选
- 查询参数: `UserQueryDto`
- 返回: `PagedResult<UserDto>`
- 权限: Admin, Manager

#### GET /api/users/guid/{guid}
**根据GUID获取用户详情**
- 包含角色和分店信息
- 返回: `UserDetailDto`
- 权限: Admin, Manager

#### GET /api/users/username/{username}
**根据用户名获取用户**
- 返回: `UserDto`
- 权限: Admin, Manager

#### GET /api/users/email/{email}
**根据邮箱获取用户**
- 返回: `UserDto`
- 权限: Admin, Manager

#### POST /api/users
**创建新用户**
- 参数: `CreateUserDto`
- 支持同时分配角色和分店
- 返回: `UserDto`
- 权限: 仅Admin

#### PUT /api/users/guid/{guid}
**更新用户信息**
- 参数: `UpdateUserDto`
- 返回: `UserDto`
- 权限: 仅Admin

#### DELETE /api/users/guid/{guid}
**删除用户**
- 级联删除相关数据
- 返回: `bool`
- 权限: 仅Admin

#### PUT /api/users/guid/{guid}/status
**更新用户状态**
- 参数: `UpdateUserStatusDto`
- 返回: `bool`
- 权限: 仅Admin

### 密码管理

#### PUT /api/users/guid/{guid}/password
**更新用户密码**
- 参数: `UpdateUserPasswordDto`
- 返回: `bool`
- 权限: 仅Admin

#### POST /api/users/guid/{guid}/reset-password
**重置用户密码**
- 生成随机密码
- 返回: `string` (新密码)
- 权限: 仅Admin

#### PUT /api/users/guid/{guid}/lock
**锁定/解锁用户**
- 参数: `bool isLocked`
- 返回: `bool`
- 权限: 仅Admin

### 角色管理

#### POST /api/users/guid/{guid}/roles
**为用户分配角色**
- 参数: `UserRoleAssignmentDto`
- 返回: `bool`
- 权限: 仅Admin

#### GET /api/users/guid/{guid}/roles
**获取用户角色列表**
- 返回: `List<RoleDto>`
- 权限: Admin, Manager

#### DELETE /api/users/guid/{guid}/roles/{roleGuid}
**从用户移除角色**
- 返回: `bool`
- 权限: 仅Admin

### 分店管理

#### POST /api/users/guid/{guid}/stores
**为用户分配分店**
- 参数: `UserStoreAssignmentDto`
- 返回: `bool`
- 权限: 仅Admin

#### GET /api/users/guid/{guid}/stores
**获取用户分店列表**
- 返回: `List<UserStoreDto>`
- 权限: Admin, Manager

#### DELETE /api/users/guid/{guid}/stores/{storeGuid}
**从用户移除分店**
- 返回: `bool`
- 权限: 仅Admin

### 批量操作

#### POST /api/users/batch
**批量管理用户**
- 参数: `BatchUserOperationDto`
- 支持: 激活、禁用、删除等操作
- 返回: `bool`
- 权限: 仅Admin

#### POST /api/users/import
**导入用户**
- 参数: `List<ImportUserDto>`
- 返回: `ImportUserResultDto`
- 权限: 仅Admin

#### GET /api/users/export
**导出用户**
- 查询参数: `UserQueryDto`
- 返回: `List<UserDto>`
- 权限: Admin, Manager

### 统计和验证

#### GET /api/users/statistics
**获取用户统计信息**
- 返回: `UserStatisticsDto`
- 权限: Admin, Manager

#### GET /api/users/check-username/{username}
**检查用户名可用性**
- 查询参数: `excludeUserGuid` (可选)
- 返回: `bool`
- 权限: 仅Admin

#### GET /api/users/check-email/{email}
**检查邮箱可用性**
- 查询参数: `excludeUserGuid` (可选)
- 返回: `bool`
- 权限: 仅Admin

## 数据传输对象 (DTOs)

### 用户相关 DTOs

#### UserQueryDto
```csharp
public class UserQueryDto
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? SearchKeyword { get; set; }
    public string? RoleGuid { get; set; }
    public string? StoreGuid { get; set; }
    public bool? IsActive { get; set; }
    public string? SortBy { get; set; } = "CreatedAt";
    public string? SortDirection { get; set; } = "desc";
}
```

#### UserDto
```csharp
public class UserDto
{
    public int Id { get; set; }
    public string UserGUID { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public string? FullName { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<string> RoleNames { get; set; }
    public List<string> StoreNames { get; set; }
}
```

#### CreateUserDto
```csharp
public class CreateUserDto
{
    [Required] public string Username { get; set; }
    [Required, EmailAddress] public string Email { get; set; }
    [Required] public string Password { get; set; }
    public string? FullName { get; set; }
    public bool IsActive { get; set; } = true;
    public List<string> RoleGuids { get; set; }
    public List<string> StoreGuids { get; set; }
}
```

### 角色相关 DTOs

#### RoleQueryDto
```csharp
public class RoleQueryDto
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? SearchKeyword { get; set; }
    public bool? IsActive { get; set; }
    public string? SortBy { get; set; } = "CreatedAt";
    public string? SortDirection { get; set; } = "desc";
}
```

#### RoleDto
```csharp
public class RoleDto
{
    public int Id { get; set; }
    public string RoleGUID { get; set; }
    public string RoleName { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int UserCount { get; set; }
}
```

## 技术特性

### 1. 错误处理
- 统一使用 `ApiResponse<T>` 包装返回结果
- 完整的异常捕获和日志记录
- 标准化的错误码和错误信息

### 2. 数据验证
- 使用 `DataAnnotations` 进行参数验证
- 返回详细的验证错误信息
- 业务逻辑验证（用户名、邮箱唯一性等）

### 3. 安全特性
- 密码哈希存储 (SHA256 + Salt)
- 角色基础访问控制
- API 端点授权验证
- 敏感操作日志记录

### 4. 性能优化
- 分页查询避免大数据量传输
- 使用 SqlSugar ORM 提高查询效率
- 批量操作支持
- 异步操作避免阻塞

### 5. 日志记录
- 使用 `ILogger<T>` 记录操作日志
- 包含详细的错误信息和上下文
- 操作成功和失败都有记录

## 权限设计

### 角色定义
- **Admin**: 系统管理员，拥有所有权限
- **Manager**: 管理员，可查看和管理分配的资源
- **User**: 普通用户，基础操作权限

### 权限矩阵
| 操作 | Admin | Manager | User |
|------|-------|---------|------|
| 查看用户列表 | ✅ | ✅ | ❌ |
| 查看用户详情 | ✅ | ✅ | ❌ |
| 创建用户 | ✅ | ❌ | ❌ |
| 修改用户 | ✅ | ❌ | ❌ |
| 删除用户 | ✅ | ❌ | ❌ |
| 分配角色 | ✅ | ❌ | ❌ |
| 分配分店 | ✅ | ❌ | ❌ |
| 批量操作 | ✅ | ❌ | ❌ |
| 导入/导出 | ✅ | ✅ | ❌ |
| 统计报表 | ✅ | ✅ | ❌ |

## 响应格式标准

### 成功响应
```json
{
  "success": true,
  "data": {},
  "message": "操作成功",
  "errorCode": null,
  "validationErrors": null
}
```

### 错误响应
```json
{
  "success": false,
  "data": null,
  "message": "错误描述",
  "errorCode": "ERROR_CODE",
  "validationErrors": {}
}
```

### 分页响应
```json
{
  "success": true,
  "data": {
    "items": [],
    "total": 100,
    "page": 1,
    "pageSize": 10
  },
  "message": "获取成功"
}
```

## 依赖注入配置

需要在 `Program.cs` 中注册服务：

```csharp
// 用户管理服务
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleService, RoleService>();
```

## 下一步开发计划

1. ✅ 创建 UserService 和 UsersController
2. ✅ 创建角色相关 DTOs
3. ✅ 创建 IRoleService 接口
4. ⏳ 创建 RoleService 实现
5. ⏳ 创建 RolesController
6. ⏳ 权限管理系统完善
7. ⏳ 单元测试编写
8. ⏳ API 文档生成
9. ⏳ 前端组件开发

## 总结

基于 StoresController 的成功架构模式，我们创建了完整的用户和角色管理系统后端代码：

1. **架构一致性**: 遵循相同的分层架构和命名规范
2. **功能完整性**: 覆盖用户生命周期的所有操作
3. **安全性**: 完整的权限控制和数据验证
4. **扩展性**: 支持批量操作、导入导出、统计分析
5. **维护性**: 清晰的代码结构和完整的日志记录

该系统为 HB Platform 多店铺管理系统提供了坚实的用户权限管理基础。