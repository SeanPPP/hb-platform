# 分店管理GUID规则文档

## 1. GUID使用规范

### 1.1 基本原则
- 所有实体都使用GUID作为主键
- GUID格式为标准的UUID v4格式
- 在API中统一使用字符串类型传递GUID
- 数据库存储为NVARCHAR(50)类型

### 1.2 实体GUID字段

#### 1.2.1 分店实体 (Store)
```csharp
public class Store : BaseEntity
{
    public string StoreGUID { get; set; } = string.Empty;  // 主GUID
    public string StoreName { get; set; } = string.Empty;
    public string StoreCode { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
}
```

#### 1.2.2 用户分店关联实体 (UserStore)
```csharp
public class UserStore : BaseEntity
{
    public string UserStoreGUID { get; set; } = string.Empty;  // 关联表主GUID
    public string UserGUID { get; set; } = string.Empty;       // 用户GUID
    public string StoreGUID { get; set; } = string.Empty;      // 分店GUID
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public string? AssignedByGUID { get; set; }                // 分配人GUID
}
```

#### 1.2.3 用户实体 (User)
```csharp
public class User : BaseEntity
{
    public string UserGUID { get; set; } = string.Empty;  // 用户主GUID
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
```

### 1.3 GUID生成规则

#### 1.3.1 创建新实体时
```csharp
// 创建分店
var store = new Store
{
    StoreGUID = Guid.NewGuid().ToString(),  // 生成新的GUID
    StoreName = "北京旗舰店",
    StoreCode = "BJ001",
    // ... 其他属性
};

// 创建用户分店关联
var userStore = new UserStore
{
    UserStoreGUID = Guid.NewGuid().ToString(),  // 生成新的GUID
    UserGUID = userGuid,                        // 使用传入的用户GUID
    StoreGUID = storeGuid,                      // 使用传入的分店GUID
    AssignedAt = DateTime.UtcNow
};
```

#### 1.3.2 GUID格式示例
```
550e8400-e29b-41d4-a716-446655440000  // 分店GUID
550e8400-e29b-41d4-a716-446655440001  // 用户GUID
550e8400-e29b-41d4-a716-446655440002  // 用户分店关联GUID
```

## 2. API接口GUID使用

### 2.1 URL路径中的GUID
```http
GET /api/stores/guid/{storeGUID}                    // 获取分店详情
PUT /api/stores/guid/{storeGUID}                    // 更新分店
DELETE /api/stores/guid/{storeGUID}                 // 删除分店
PUT /api/stores/guid/{storeGUID}/status             // 更新分店状态
GET /api/stores/guid/{storeGUID}/users              // 获取分店用户列表
POST /api/stores/guid/{storeGUID}/users             // 添加用户到分店
DELETE /api/stores/guid/{storeGUID}/users/{userGUID} // 从分店移除用户
PUT /api/stores/guid/{storeGUID}/users/{userGUID}/primary // 设置分店管理关系
POST /api/stores/guid/{storeGUID}/users/batch       // 批量管理用户
```

### 2.2 请求体中的GUID
```json
// 创建分店（不需要GUID，系统自动生成）
{
  "storeName": "上海分店",
  "storeCode": "SH001",
  "address": "上海市浦东新区陆家嘴金融区",
  "contactPhone": "021-12345678"
}

// 添加用户到分店
{
  "userGUID": "550e8400-e29b-41d4-a716-446655440002"
}

// 批量操作用户
{
  "action": "add",
  "userGUIDs": [
    "550e8400-e29b-41d4-a716-446655440002",
    "550e8400-e29b-41d4-a716-446655440003"
  ]
}
```

### 2.3 响应中的GUID
```json
{
  "success": true,
  "message": "获取分店列表成功",
  "data": {
    "items": [
      {
        "storeGUID": "550e8400-e29b-41d4-a716-446655440000",
        "storeName": "北京旗舰店",
        "storeCode": "BJ001",
        "address": "北京市朝阳区建国路88号",
        "phone": "010-12345678",
        "isActive": true,
        "totalUsers": 5,
        "activeUsers": 3,
        "createdAt": "2024-01-01T00:00:00Z"
      }
    ],
    "total": 10,
    "page": 1,
    "pageSize": 20
  }
}
```

## 3. 数据库表结构

### 3.1 分店表 (Store)
```sql
CREATE TABLE [Store] (
    [Id] INT PRIMARY KEY IDENTITY(1,1),
    [StoreGUID] NVARCHAR(50) NOT NULL UNIQUE,  -- 分店GUID
    [StoreName] NVARCHAR(100) NOT NULL,
    [StoreCode] NVARCHAR(20) NOT NULL UNIQUE,
    [Address] NVARCHAR(200),
    [Phone] NVARCHAR(20),
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL,
    [UpdatedAt] DATETIME2 NOT NULL,
    [CreatedBy] NVARCHAR(50),
    [UpdatedBy] NVARCHAR(50)
)
```

### 3.2 用户分店关联表 (UserStore)
```sql
CREATE TABLE [UserStore] (
    [Id] INT PRIMARY KEY IDENTITY(1,1),
    [UserStoreGUID] NVARCHAR(50) NOT NULL UNIQUE,  -- 关联表GUID
    [UserGUID] NVARCHAR(50) NOT NULL,              -- 用户GUID
    [StoreGUID] NVARCHAR(50) NOT NULL,             -- 分店GUID
    [AssignedAt] DATETIME2 NOT NULL,
    [AssignedByGUID] NVARCHAR(50),                 -- 分配人GUID
    FOREIGN KEY ([UserGUID]) REFERENCES [User]([UserGUID]),
    FOREIGN KEY ([StoreGUID]) REFERENCES [Store]([StoreGUID])
)
```

## 4. 代码实现示例

### 4.1 服务层GUID处理
```csharp
public class StoreService : IStoreService
{
    // 创建分店
    public async Task<ApiResponse<StoreDto>> CreateStoreAsync(CreateStoreDto dto)
    {
        var store = new Store
        {
            StoreGUID = Guid.NewGuid().ToString(),  // 生成新的GUID
            StoreName = dto.StoreName,
            StoreCode = dto.StoreCode,
            Address = dto.Address,
            Phone = dto.ContactPhone,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await db.Insertable(store).ExecuteCommandAsync();
        
        return ApiResponse<StoreDto>.OK(result, "创建分店成功");
    }

    // 根据GUID获取分店
    public async Task<ApiResponse<StoreDetailDto>> GetStoreByGuidAsync(string guid)
    {
        var store = await db.Queryable<Store>()
            .Where(s => s.StoreGUID == guid)  // 使用GUID查询
            .FirstAsync();

        if (store == null)
        {
            return ApiResponse<StoreDetailDto>.Error("分店不存在", "STORE_NOT_FOUND");
        }

        return await GetStoreDetailAsync(store);
    }

    // 添加用户到分店
    public async Task<ApiResponse<bool>> AddUserToStoreAsync(string storeGuid, AddUserToStoreDto dto)
    {
        var userStore = new UserStore
        {
            UserStoreGUID = Guid.NewGuid().ToString(),  // 生成新的关联GUID
            UserGUID = dto.UserGUID,                    // 使用传入的用户GUID
            StoreGUID = storeGuid,                      // 使用传入的分店GUID
            AssignedAt = DateTime.UtcNow
        };

        await db.Insertable(userStore).ExecuteCommandAsync();
        
        return ApiResponse<bool>.OK(true, "添加用户到分店成功");
    }
}
```

### 4.2 控制器层GUID处理
```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StoresController : ControllerBase
{
    // 根据GUID获取分店详情
    [HttpGet("guid/{guid}")]
    public async Task<IActionResult> GetStoreByGuid(string guid)
    {
        var result = await _storeService.GetStoreByGuidAsync(guid);
        return Ok(result);
    }

    // 根据GUID更新分店
    [HttpPut("guid/{guid}")]
    public async Task<IActionResult> UpdateStore(string guid, [FromBody] UpdateStoreDto dto)
    {
        var result = await _storeService.UpdateStoreByGuidAsync(guid, dto);
        return Ok(result);
    }

    // 根据GUID删除分店
    [HttpDelete("guid/{guid}")]
    public async Task<IActionResult> DeleteStore(string guid)
    {
        var result = await _storeService.DeleteStoreByGuidAsync(guid);
        return Ok(result);
    }

    // 获取分店用户列表
    [HttpGet("guid/{storeGuid}/users")]
    public async Task<IActionResult> GetStoreUsers(string storeGuid, [FromQuery] UserQueryDto query)
    {
        var result = await _storeService.GetStoreUsersAsync(storeGuid, query);
        return Ok(result);
    }

    // 添加用户到分店
    [HttpPost("guid/{storeGuid}/users")]
    public async Task<IActionResult> AddUserToStore(string storeGuid, [FromBody] AddUserToStoreDto dto)
    {
        var result = await _storeService.AddUserToStoreAsync(storeGuid, dto);
        return Ok(result);
    }

    // 从分店移除用户
    [HttpDelete("guid/{storeGuid}/users/{userGuid}")]
    public async Task<IActionResult> RemoveUserFromStore(string storeGuid, string userGuid)
    {
        var result = await _storeService.RemoveUserFromStoreAsync(storeGuid, userGuid);
        return Ok(result);
    }
}
```

## 5. 数据验证规则

### 5.1 GUID格式验证
```csharp
// GUID格式验证
public static bool IsValidGuid(string guid)
{
    return Guid.TryParse(guid, out _);
}

// 在DTO中使用
public class AddUserToStoreDto
{
    [Required]
    [RegularExpression(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", 
        ErrorMessage = "用户GUID格式不正确")]
    public string UserGUID { get; set; } = string.Empty;
}
```

### 5.2 业务规则验证
```csharp
// 检查分店是否存在
var store = await db.Queryable<Store>()
    .Where(s => s.StoreGUID == storeGuid)
    .FirstAsync();

if (store == null)
{
    return ApiResponse<bool>.Error("分店不存在", "STORE_NOT_FOUND");
}

// 检查用户是否存在
var user = await db.Queryable<User>()
    .Where(u => u.UserGUID == userGuid)
    .FirstAsync();

if (user == null)
{
    return ApiResponse<bool>.Error("用户不存在", "USER_NOT_FOUND");
}

// 检查用户是否已经关联到该分店
var existingUserStore = await db.Queryable<UserStore>()
    .Where(us => us.StoreGUID == storeGuid && us.UserGUID == userGuid)
    .FirstAsync();

if (existingUserStore != null)
{
    return ApiResponse<bool>.Error("用户已关联到该分店", "USER_ALREADY_ASSIGNED");
}
```

## 6. 错误处理

### 6.1 GUID相关错误
```csharp
// GUID格式错误
{
  "success": false,
  "message": "GUID格式不正确",
  "errorCode": "INVALID_GUID_FORMAT",
  "timestamp": "2024-01-01T00:00:00Z"
}

// 分店不存在
{
  "success": false,
  "message": "分店不存在",
  "errorCode": "STORE_NOT_FOUND",
  "timestamp": "2024-01-01T00:00:00Z"
}

// 用户不存在
{
  "success": false,
  "message": "用户不存在",
  "errorCode": "USER_NOT_FOUND",
  "timestamp": "2024-01-01T00:00:00Z"
}
```

### 6.2 日志记录
```csharp
// 记录GUID相关的操作日志
_logger.LogInformation("创建分店成功，StoreGUID: {StoreGUID}", store.StoreGUID);
_logger.LogError(ex, "删除分店失败，StoreGUID: {StoreGUID}", storeGuid);
_logger.LogWarning("用户已关联到分店，UserGUID: {UserGUID}, StoreGUID: {StoreGUID}", userGuid, storeGuid);
```

## 7. 最佳实践

### 7.1 GUID生成
- 使用 `Guid.NewGuid().ToString()` 生成新的GUID
- 确保GUID的唯一性
- 在创建实体时立即生成GUID

### 7.2 GUID传递
- API接口中统一使用字符串类型传递GUID
- 在URL路径中使用GUID参数
- 在请求体中使用GUID字段

### 7.3 GUID查询
- 使用GUID进行精确查询
- 建立GUID字段的数据库索引
- 避免使用GUID进行范围查询

### 7.4 GUID验证
- 在API接口中验证GUID格式
- 检查GUID对应的实体是否存在
- 记录GUID相关的操作日志

## 8. 注意事项

### 8.1 性能考虑
- GUID字段建立索引以提高查询性能
- 避免在WHERE子句中使用GUID进行范围查询
- 考虑使用GUID的哈希值进行分区

### 8.2 安全性
- 不要在前端暴露GUID的生成逻辑
- 验证GUID的格式和有效性
- 记录GUID相关的敏感操作

### 8.3 兼容性
- 确保所有客户端都能正确处理GUID字符串
- 保持GUID格式的一致性
- 考虑向后兼容性

---

**文档版本**: v1.0.0
**最后更新**: 2024-01-01
**状态**: ✅ 已完成，正在使用
