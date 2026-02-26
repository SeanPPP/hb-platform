# StoresController API 文档

## 控制器概述
**分店管理控制器** - 提供分店数据的CRUD操作和同步功能
- 🏢 完整的分店管理功能
- 🔐 完整的授权控制
- 📋 支持分页、搜索、筛选
- 🔄 支持从HQ总部同步数据

## 授权说明
- **Admin角色**: 完整权限 - 创建、查看、修改、删除分店，执行系统同步
- **Manager角色**: 查看权限 - 只能查看分配的分店数据
- **当前状态**: 🚧 调试期间暂时注释授权验证

## API端点列表

### 1. 分店基础管理

#### GET /api/stores
**获取分店列表**
- 支持分页、搜索、筛选
- 参数: `StoreQueryDto` (分页、搜索条件)
- 返回: `PagedResult<StoreDto>`
- 权限: Admin, Manager

#### GET /api/stores/guid/{guid}
**根据GUID获取分店详情**
- 参数: `guid` (分店唯一标识)
- 返回: `StoreDetailDto`
- 权限: Admin, Manager

#### POST /api/stores
**创建新分店**
- 参数: `CreateStoreDto`
- 返回: `StoreDto`
- 权限: 仅Admin

#### PUT /api/stores/guid/{guid}
**根据GUID更新分店**
- 参数: `guid`, `UpdateStoreDto`
- 返回: `StoreDto`
- 权限: 仅Admin

#### DELETE /api/stores/guid/{guid}
**根据GUID删除分店**
- 参数: `guid`
- 返回: `bool`
- 权限: 仅Admin

#### PUT /api/stores/guid/{guid}/status
**更新分店状态**
- 参数: `guid`, `UpdateStoreStatusDto`
- 返回: `bool`
- 权限: 仅Admin

### 2. 分店用户管理

#### GET /api/stores/guid/{guid}/users
**获取分店用户列表**
- 参数: `guid`, `UserQueryDto`
- 返回: `PagedResult<StoreUserDto>`
- 权限: Admin, Manager

#### POST /api/stores/guid/{guid}/users
**为分店添加用户**
- 参数: `guid`, `AddUserToStoreDto`
- 返回: `bool`
- 权限: 仅Admin

#### DELETE /api/stores/guid/{guid}/users/{userGuid}
**从分店移除用户**
- 参数: `guid`, `userGuid`
- 返回: `bool`
- 权限: 仅Admin

#### PUT /api/stores/guid/{guid}/users/{userGuid}/primary
**设置主要用户**
- 参数: `guid`, `userGuid`, `bool isPrimary`
- 返回: `bool`
- 权限: 仅Admin

#### POST /api/stores/guid/{guid}/users/batch
**批量管理用户**
- 参数: `guid`, `BatchUserOperationDto`
- 返回: `bool`
- 权限: 仅Admin

### 3. 数据同步

#### POST /api/stores/sync
**从HQ总部同步分店数据**
- 从总部数据库获取最新分店信息并更新本地数据库
- 返回: `SyncResult`
- 权限: 仅Admin
- ⚠️ 敏感操作，只有Admin可执行

#### GET /api/stores/sync/history
**获取同步历史记录**
- 参数: `pageSize` (默认10)
- 返回: `List<SyncHistory>`
- 权限: Admin, Manager

## 技术特性

### 错误处理
- 统一使用 `ApiResponse<T>` 包装返回结果
- 完整的异常捕获和日志记录
- 标准化的错误码和错误信息

### 数据验证
- 使用 `ModelState.IsValid` 进行参数验证
- 返回详细的验证错误信息

### 日志记录
- 使用 `ILogger<StoresController>` 记录操作日志
- 包含详细的错误信息和上下文

### 依赖注入
- `IStoreService` - 分店业务逻辑服务
- `StoreSyncService` - 分店同步服务
- `ILogger<StoresController>` - 日志服务

## 代码结构模式
```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize] // 全局授权
public class StoresController : ControllerBase
{
    // 依赖注入
    private readonly IStoreService _storeService;
    private readonly StoreSyncService _syncService;
    private readonly ILogger<StoresController> _logger;

    // 标准错误处理模式
    try
    {
        // 业务逻辑
        var result = await _service.MethodAsync();
        return Ok(result);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "操作失败");
        return StatusCode(500, ApiResponse<T>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
    }
}
```

## 响应格式标准
```json
{
  "success": true,
  "data": {},
  "message": "操作成功",
  "errorCode": null,
  "validationErrors": null
}
```